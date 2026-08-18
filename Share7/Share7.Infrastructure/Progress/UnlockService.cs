using Microsoft.EntityFrameworkCore;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Progress.Models;
using Share7.Domain.Progress;
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
/// </summary>
public class UnlockService : IUnlockService
{
    private readonly ApplicationDbContext _dbContext;

    public UnlockService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UnlockedNodeDto>> EnsureSeededAsync(
        Guid userId, Guid gameId, Guid gradeId, CancellationToken cancellationToken = default)
    {
        // Any unlock at all means this student has already started this game.
        if (await _dbContext.UserNodeUnlocks.AnyAsync(u => u.UserId == userId && u.GameId == gameId, cancellationToken))
            return [];

        var firstTerm = await _dbContext.Terms
            .Where(t => t.GradeId == gradeId)
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstTerm == Guid.Empty)
            return [];

        var granted = new List<UnlockedNodeDto>();
        await UnlockTermChainAsync(userId, gameId, firstTerm, granted, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return granted;
    }

    public async Task<IReadOnlyList<UnlockedNodeDto>> EvaluateAfterAttemptAsync(
        Guid userId, Guid gameId, Guid lessonId, Guid langId, CancellationToken cancellationToken = default)
    {
        var location = await _dbContext.Lessons
            .Where(l => l.Id == lessonId)
            .Select(l => new
            {
                LessonOrder = l.Order,
                l.ChapterId,
                ChapterOrder = l.Chapter!.Order,
                SubjectId = l.Chapter!.SubjectId,
                SubjectOrder = l.Chapter!.Subject!.Order,
                TermId = l.Chapter!.Subject!.TermId,
                TermOrder = l.Chapter!.Subject!.Term!.Order,
                GradeId = l.Chapter!.Subject!.Term!.GradeId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (location is null)
            return [];

        // Every lesson under the same term, with whether it is playable in this language.
        // One query covers the chapter, subject and term checks below.
        var lessons = await _dbContext.Lessons
            .Where(l => l.Chapter!.Subject!.TermId == location.TermId)
            .Select(l => new
            {
                l.Id,
                l.Order,
                l.ChapterId,
                SubjectId = l.Chapter!.SubjectId,
                Playable = l.QuestionSets.Any(s => s.LangId == langId && s.Version > 0)
            })
            .ToListAsync(cancellationToken);

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

        var granted = new List<UnlockedNodeDto>();

        // The next lesson opens as soon as this one is passed.
        var attempted = lessons.FirstOrDefault(l => l.Id == lessonId);
        if (attempted is not null && Satisfied(attempted.Id, attempted.Playable))
        {
            var nextLesson = lessons
                .Where(l => l.ChapterId == location.ChapterId && l.Order > location.LessonOrder)
                .OrderBy(l => l.Order)
                .FirstOrDefault();

            if (nextLesson is not null)
                await GrantAsync(userId, gameId, CurriculumNodeType.Lesson, nextLesson.Id, granted, cancellationToken);
        }

        // Each level is checked independently rather than only when the level below runs out of
        // siblings — completion can drop, so "finished the last lesson" does not imply the
        // earlier ones are still passed. All() over an empty set is true, so an empty chapter
        // is vacuously complete and never blocks the chain.
        if (lessons.Where(l => l.ChapterId == location.ChapterId).All(l => Satisfied(l.Id, l.Playable)))
        {
            var nextChapter = await _dbContext.Chapters
                .Where(c => c.SubjectId == location.SubjectId && c.Order > location.ChapterOrder)
                .OrderBy(c => c.Order)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextChapter != Guid.Empty)
                await UnlockChapterChainAsync(userId, gameId, nextChapter, granted, cancellationToken);
        }

        if (lessons.Where(l => l.SubjectId == location.SubjectId).All(l => Satisfied(l.Id, l.Playable)))
        {
            var nextSubject = await _dbContext.Subjects
                .Where(s => s.TermId == location.TermId && s.Order > location.SubjectOrder)
                .OrderBy(s => s.Order)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextSubject != Guid.Empty)
                await UnlockSubjectChainAsync(userId, gameId, nextSubject, granted, cancellationToken);
        }

        if (lessons.All(l => Satisfied(l.Id, l.Playable)))
        {
            var nextTerm = await _dbContext.Terms
                .Where(t => t.GradeId == location.GradeId && t.Order > location.TermOrder)
                .OrderBy(t => t.Order)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextTerm != Guid.Empty)
                await UnlockTermChainAsync(userId, gameId, nextTerm, granted, cancellationToken);
        }

        if (granted.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return granted;
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

    // ------------------------------------------------------------- chains
    //
    // Opening a container is useless on its own — a student handed an unlocked chapter with no
    // unlocked lesson inside it has nowhere to go. Each grant walks down to the first playable
    // entry point by Order.

    private async Task UnlockTermChainAsync(
        Guid userId, Guid gameId, Guid termId, List<UnlockedNodeDto> granted, CancellationToken cancellationToken)
    {
        await GrantAsync(userId, gameId, CurriculumNodeType.Term, termId, granted, cancellationToken);

        var firstSubject = await _dbContext.Subjects
            .Where(s => s.TermId == termId)
            .OrderBy(s => s.Order)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstSubject != Guid.Empty)
            await UnlockSubjectChainAsync(userId, gameId, firstSubject, granted, cancellationToken);
    }

    private async Task UnlockSubjectChainAsync(
        Guid userId, Guid gameId, Guid subjectId, List<UnlockedNodeDto> granted, CancellationToken cancellationToken)
    {
        await GrantAsync(userId, gameId, CurriculumNodeType.Subject, subjectId, granted, cancellationToken);

        var firstChapter = await _dbContext.Chapters
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.Order)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstChapter != Guid.Empty)
            await UnlockChapterChainAsync(userId, gameId, firstChapter, granted, cancellationToken);
    }

    private async Task UnlockChapterChainAsync(
        Guid userId, Guid gameId, Guid chapterId, List<UnlockedNodeDto> granted, CancellationToken cancellationToken)
    {
        await GrantAsync(userId, gameId, CurriculumNodeType.Chapter, chapterId, granted, cancellationToken);

        var firstLesson = await _dbContext.Lessons
            .Where(l => l.ChapterId == chapterId)
            .OrderBy(l => l.Order)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstLesson != Guid.Empty)
            await GrantAsync(userId, gameId, CurriculumNodeType.Lesson, firstLesson, granted, cancellationToken);
    }

    /// <summary>
    /// Adds an unlock unless it is already held or already queued in this call. Never removes —
    /// see the class remarks.
    /// </summary>
    private async Task GrantAsync(
        Guid userId, Guid gameId, CurriculumNodeType nodeType, Guid nodeId,
        List<UnlockedNodeDto> granted, CancellationToken cancellationToken)
    {
        var typeName = nodeType.ToString();

        if (granted.Any(g => g.NodeId == nodeId && g.NodeType == typeName))
            return;

        var alreadyHeld = await _dbContext.UserNodeUnlocks.AnyAsync(
            u => u.UserId == userId && u.GameId == gameId && u.NodeType == nodeType && u.NodeId == nodeId,
            cancellationToken);

        if (alreadyHeld)
            return;

        _dbContext.UserNodeUnlocks.Add(new UserNodeUnlock
        {
            UserId = userId,
            GameId = gameId,
            NodeType = nodeType,
            NodeId = nodeId,
            UnlockedAt = DateTime.UtcNow
        });

        granted.Add(new UnlockedNodeDto { NodeType = typeName, NodeId = nodeId });
    }
}
