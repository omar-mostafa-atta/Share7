using Microsoft.EntityFrameworkCore;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Progress.Models;
using Share7.Domain.Progress;
using Share7.Infrastructure.Engine.Reads;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Progress;

/// <summary>
/// Grants unlocks and never revokes them. The asymmetry is deliberate: completion follows the
/// last attempt and can drop, but what that completion opened stays open — re-locking content a
/// student already earned is the kind of thing that generates support tickets.
/// <para>
/// That asymmetry is also why unlocks are stored rather than derived. Current state cannot tell
/// you what was true at the moment a lesson was passed.
/// </para>
/// <para>
/// <b>Subjects do not gate.</b> Every subject of an unlocked term opens with the term, so a
/// student may start Science without having finished Maths. Subjects are a parallel split of a
/// term's content, not a sequence through it — <c>Order</c> on a subject is a display order.
/// Terms, chapters and lessons remain sequential.
/// </para>
/// </summary>
public class UnlockService : IUnlockService
{
    private readonly ApplicationDbContext _dbContext;

    /// <summary>The shape of the tree — typed tables or node tree per Curriculum:ReadModel.</summary>
    private readonly ICurriculumReads _reads;

    public UnlockService(ApplicationDbContext dbContext, ICurriculumReads reads)
    {
        _dbContext = dbContext;
        _reads = reads;
    }

    public async Task<IReadOnlyList<UnlockedNodeDto>> EnsureSeededAsync(
        Guid userId, Guid gameId, Guid gradeId, CancellationToken cancellationToken = default)
    {
        var pass = await BeginPassAsync(userId, gameId, cancellationToken);
        var termIds = pass.HeldOfType(CurriculumNodeType.Term);

        if (termIds.Count == 0)
        {
            if (await _reads.FirstTermAsync(gradeId, cancellationToken) is not { } firstTerm)
                return [];

            termIds = [firstTerm];
        }

        // Re-walking the terms this student already holds is what makes this a top-up rather than
        // a one-shot seed. It costs one extra id query in the steady state, and it is how two
        // things heal themselves: a student who started while subjects were still sequential and
        // holds a term with only its first subject open, and a subject an author adds to a term
        // that students are already inside. Both simply appear on the next game-open.
        await GrantTermsAsync(pass, termIds, cancellationToken);

        return await CommitAsync(pass, cancellationToken);
    }

    public async Task<IReadOnlyList<UnlockedNodeDto>> EvaluateAfterAttemptAsync(
        Guid userId, Guid gameId, Guid lessonId, Guid langId, CancellationToken cancellationToken = default)
    {
        var location = await _reads.LessonLocationAsync(lessonId, cancellationToken);

        if (location is null)
            return [];

        // Every lesson under the same term, with whether it is playable in this language. One
        // query covers the chapter and term checks below, and supplies the first lesson of
        // whatever chapter opens without a second round trip.
        var lessons = await _reads.LessonsInTermAsync(location.TermId, langId, cancellationToken);

        var lessonIds = lessons.Select(l => l.Id).ToList();

        var states = await _dbContext.UserLessonProgress
            .Where(p => p.UserId == userId && p.GameId == gameId && lessonIds.Contains(p.LessonId))
            .Select(p => new { p.LessonId, p.CompletionState })
            .ToDictionaryAsync(p => p.LessonId, p => p.CompletionState, cancellationToken);

        // A lesson with no sheet in this language counts as satisfied. Otherwise one missing
        // Arabic upload would freeze the whole chapter for every Arabic-reading student.
        bool Satisfied(Guid id, bool playable) =>
            !playable ||
            (states.TryGetValue(id, out var state) &&
             state is CompletionState.Completed or CompletionState.Aced);

        var pass = await BeginPassAsync(userId, gameId, cancellationToken);

        // The next lesson opens as soon as this one is passed.
        var attempted = lessons.FirstOrDefault(l => l.Id == lessonId);
        if (attempted is not null && Satisfied(attempted.Id, attempted.Playable))
        {
            var nextLesson = lessons
                .Where(l => l.ChapterId == location.ChapterId && l.Order > location.LessonOrder)
                .OrderBy(l => l.Order)
                .FirstOrDefault();

            if (nextLesson is not null)
                pass.Grant(CurriculumNodeType.Lesson, nextLesson.Id);
        }

        // Each level is checked independently rather than only when the level below runs out of
        // siblings — completion can drop, so "finished the last lesson" does not imply the
        // earlier ones are still passed. All() over an empty set is true, so an empty chapter
        // is vacuously complete and never blocks the chain.
        if (lessons.Where(l => l.ChapterId == location.ChapterId).All(l => Satisfied(l.Id, l.Playable)))
        {
            var nextChapter = await _reads.NextChapterAsync(location.SubjectId, location.ChapterOrder, cancellationToken)
                              ?? Guid.Empty;

            if (nextChapter != Guid.Empty && pass.Grant(CurriculumNodeType.Chapter, nextChapter))
            {
                var firstLesson = lessons
                    .Where(l => l.ChapterId == nextChapter)
                    .OrderBy(l => l.Order)
                    .FirstOrDefault();

                if (firstLesson is not null)
                    pass.Grant(CurriculumNodeType.Lesson, firstLesson.Id);
            }
        }

        // There is deliberately no subject rule here: the sibling subjects of this one were
        // opened by the term that contains them. See the class remarks.

        // A term is complete when every lesson under it is. With subjects ungated that means the
        // student has cleared all of them, not merely the one branch they were sent down.
        if (lessons.All(l => Satisfied(l.Id, l.Playable)))
        {
            if (await _reads.NextTermAsync(location.GradeId, location.TermOrder, cancellationToken) is { } nextTerm)
                await GrantTermsAsync(pass, [nextTerm], cancellationToken);
        }

        return await CommitAsync(pass, cancellationToken);
    }

    public async Task<HashSet<Guid>> GetUnlockedNodeIdsAsync(
        Guid userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        var ids = await _dbContext.UserNodeUnlocks
            .Where(u => u.UserId == userId && u.GameId == gameId)
            .Select(u => u.NodeId)
            .ToListAsync(cancellationToken);

        return [.. ids];
    }

    // ------------------------------------------------------------- grants

    /// <summary>
    /// Opens the given terms and <b>every</b> subject inside them, each walked down to its first
    /// chapter and that chapter's first lesson by <c>Order</c>. A container on its own is useless
    /// — a student handed an unlocked subject with no unlocked lesson in it has nowhere to go.
    /// <para>
    /// Three queries regardless of how many terms or subjects are involved, and the last two are
    /// skipped entirely when no subject was newly opened, which is the common case.
    /// </para>
    /// </summary>
    private async Task GrantTermsAsync(
        GrantPass pass, IReadOnlyCollection<Guid> termIds, CancellationToken cancellationToken)
    {
        foreach (var termId in termIds)
            pass.Grant(CurriculumNodeType.Term, termId);

        var subjectIds = await _reads.SubjectsOfTermsAsync(termIds.ToList(), cancellationToken);

        var opened = subjectIds.Where(id => pass.Grant(CurriculumNodeType.Subject, id)).ToList();

        if (opened.Count == 0)
            return;

        var chapters = await _reads.ChaptersOfSubjectsAsync(opened, cancellationToken);

        var firstChapterIds = opened
            .Select(subjectId => chapters
                .Where(c => c.ParentId == subjectId)
                .OrderBy(c => c.Order)
                .Select(c => c.Id)
                .FirstOrDefault())
            .Where(id => id != Guid.Empty)
            .ToList();

        if (firstChapterIds.Count == 0)
            return;

        var lessons = await _reads.LessonsOfChaptersAsync(firstChapterIds, cancellationToken);

        foreach (var chapterId in firstChapterIds)
        {
            pass.Grant(CurriculumNodeType.Chapter, chapterId);

            var firstLesson = lessons
                .Where(l => l.ParentId == chapterId)
                .OrderBy(l => l.Order)
                .FirstOrDefault();

            if (firstLesson is not null)
                pass.Grant(CurriculumNodeType.Lesson, firstLesson.Id);
        }
    }

    private async Task<GrantPass> BeginPassAsync(Guid userId, Guid gameId, CancellationToken cancellationToken)
    {
        var held = await _dbContext.UserNodeUnlocks
            .Where(u => u.UserId == userId && u.GameId == gameId)
            .Select(u => new { u.NodeType, u.NodeId })
            .ToListAsync(cancellationToken);

        return new GrantPass(userId, gameId, held.Select(h => (h.NodeType, h.NodeId)));
    }

    /// <summary>
    /// Writes the pass's new unlocks.
    /// <para>
    /// <b>Two requests for the same student can grant the same node at once</b> — a client retrying
    /// a timed-out attempt while the original is still running, or a game-open and an attempt
    /// arriving together on first contact. Both read the same held set and both insert the same
    /// rows, and the loser used to surface as a primary-key violation and a 500. Unlocks only ever
    /// grow, so "somebody else already granted it" is the state this pass wanted anyway: the rows
    /// that now exist are dropped and only the ones still missing are written. The loser reports
    /// only what it opened itself, which leaves the unlock animation to the request that won.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<UnlockedNodeDto>> CommitAsync(
        GrantPass pass, CancellationToken cancellationToken)
    {
        var pending = pass.Rows.ToList();

        for (var attempt = 0; pending.Count > 0; attempt++)
        {
            _dbContext.UserNodeUnlocks.AddRange(pending);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception) && attempt < MaxGrantRetries)
            {
                foreach (var row in pending)
                    _dbContext.Entry(row).State = EntityState.Detached;

                var nodeIds = pending.Select(r => r.NodeId).ToList();
                var userId = pending[0].UserId;
                var gameId = pending[0].GameId;

                var nowHeld = (await _dbContext.UserNodeUnlocks
                        .AsNoTracking()
                        .Where(u => u.UserId == userId && u.GameId == gameId && nodeIds.Contains(u.NodeId))
                        .Select(u => new { u.NodeType, u.NodeId })
                        .ToListAsync(cancellationToken))
                    .Select(u => (u.NodeType, u.NodeId))
                    .ToHashSet();

                pending = pending.Where(r => !nowHeld.Contains((r.NodeType, r.NodeId))).ToList();
                pass.Concede(nowHeld);
            }
        }

        return pass.Granted;
    }

    /// <summary>
    /// How many times a pass re-reads and retries after losing a race. One retry settles the
    /// two-request case; the headroom is for a burst, and past it the error is real.
    /// </summary>
    private const int MaxGrantRetries = 3;

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// One grant pass. The student's existing unlocks are read once up front, so walking the tree
    /// costs no round trip per node, and a node already held — or already added earlier in the
    /// same pass — is silently skipped. Nothing here ever removes; see the class remarks.
    /// </summary>
    private sealed class GrantPass
    {
        private readonly Guid _userId;
        private readonly Guid _gameId;
        private readonly HashSet<(CurriculumNodeType Type, Guid Id)> _held;
        private readonly List<UserNodeUnlock> _rows = [];
        private readonly List<UnlockedNodeDto> _granted = [];

        public GrantPass(Guid userId, Guid gameId, IEnumerable<(CurriculumNodeType Type, Guid Id)> held)
        {
            _userId = userId;
            _gameId = gameId;
            _held = [.. held];
        }

        /// <summary>Rows to insert, in the order they were opened.</summary>
        public IReadOnlyList<UserNodeUnlock> Rows => _rows;

        /// <summary>What this pass opened, for the client's unlock animation.</summary>
        public IReadOnlyList<UnlockedNodeDto> Granted => _granted;

        /// <summary>
        /// Forgets the grants another request committed first, so this pass reports only the
        /// nodes it actually opened.
        /// </summary>
        public void Concede(IReadOnlySet<(CurriculumNodeType Type, Guid Id)> grantedElsewhere)
        {
            _rows.RemoveAll(r => grantedElsewhere.Contains((r.NodeType, r.NodeId)));
            _granted.RemoveAll(g =>
                Enum.TryParse<CurriculumNodeType>(g.NodeType, out var type)
                && grantedElsewhere.Contains((type, g.NodeId)));
        }

        public List<Guid> HeldOfType(CurriculumNodeType type) =>
            _held.Where(h => h.Type == type).Select(h => h.Id).ToList();

        /// <returns><c>true</c> when this pass newly opened the node, <c>false</c> when it was already held.</returns>
        public bool Grant(CurriculumNodeType type, Guid id)
        {
            if (!_held.Add((type, id)))
                return false;

            _rows.Add(new UserNodeUnlock
            {
                UserId = _userId,
                GameId = _gameId,
                NodeType = type,
                NodeId = id,
                UnlockedAt = DateTime.UtcNow
            });

            _granted.Add(new UnlockedNodeDto { NodeType = type.ToString(), NodeId = id });
            return true;
        }
    }
}
