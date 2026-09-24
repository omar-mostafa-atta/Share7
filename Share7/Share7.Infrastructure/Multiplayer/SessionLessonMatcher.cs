using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Progress.Interfaces;
using Share7.Domain.Multiplayer;
using Share7.Domain.Progress;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Finds the lesson a group of children can play together.
/// <para>
/// <b>Eligible means three things at once</b>, and leaving any of them out produces a match that
/// cannot start: the lesson is unlocked for that player in that game, it has active questions in the
/// language they are playing in, and it belongs to the subject they chose. The second is the one that
/// is easy to forget — the curriculum tree is shared across languages but its questions are not, so
/// two children can share an unlock and still have nothing to answer.
/// </para>
/// </summary>
public class SessionLessonMatcher : ISessionLessonMatcher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IUnlockService _unlocks;
    private readonly Engine.Reads.ICurriculumReads _reads;

    public SessionLessonMatcher(ApplicationDbContext dbContext, IUnlockService unlocks, Engine.Reads.ICurriculumReads reads)
    {
        _dbContext = dbContext;
        _unlocks = unlocks;
        _reads = reads;
    }

    public async Task<IReadOnlyList<EligibleLesson>> EligibleLessonsAsync(
        Guid userId, Guid gameId, Guid subjectId, Guid langId, CancellationToken cancellationToken = default)
    {
        // A player's very first contact with a game has no unlock rows at all, so seeding first is
        // what stops "nobody can ever matchmake into their first session" — the same call the attempt
        // path makes before it checks a lesson is open.
        if (await _reads.GradeOfSubjectAsync(subjectId, cancellationToken) is { } gradeId)
            await _unlocks.EnsureSeededAsync(userId, gameId, gradeId, cancellationToken);

        // Unlocked for this player in this game, and answerable: the tree is shared across
        // languages, the questions are not.
        return await _reads.EligibleLessonsAsync(userId, gameId, subjectId, langId, cancellationToken);
    }

    public async Task<ServiceResult> NarrowForSeatAsync(
        Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.MultiplayerSessions
            .Include(s => s.EligibleLessons)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        // Not subject-scoped: an older client, or a direct invite, that named its lesson outright.
        // Nothing to intersect, and the exact-lesson behaviour is unchanged.
        if (session?.SubjectId is not { } subjectId || session.LangId is not { } langId)
            return ServiceResult.Success();

        var mine = await EligibleLessonsAsync(userId, session.GameId, subjectId, langId, cancellationToken);
        var mineById = mine.ToDictionary(l => l.LessonId);

        // The lesson is already decided — this player is joining a match in progress, or one whose
        // roster filled while they were being seated. They can only stay if they can play it.
        if (session.LessonId is { } stamped)
        {
            return mineById.ContainsKey(stamped)
                ? ServiceResult.Success()
                : NoSharedLesson();
        }

        var shared = session.EligibleLessons
            .Where(l => mineById.ContainsKey(l.LessonId))
            .ToList();

        if (shared.Count == 0)
            return NoSharedLesson();

        // Narrow, never widen. Rows this player cannot play leave the set for good, so a lesson
        // ruled out by somebody who has since left cannot come back and start a match they were told
        // they could not play.
        foreach (var gone in session.EligibleLessons.Where(l => !mineById.ContainsKey(l.LessonId)).ToList())
        {
            session.EligibleLessons.Remove(gone);
            _dbContext.Remove(gone);
        }

        // The pick is "what is this group collectively worst at", so every player's record adds to
        // the total as they are seated.
        foreach (var row in shared)
            row.SummedBestPercent += mineById[row.LessonId].BestPercent;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await TryStampLessonAsync(session, mineById, cancellationToken);

        return ServiceResult.Success();
    }

    /// <summary>
    /// Chooses the match's lesson once enough players are seated, and writes it exactly once.
    /// <para>
    /// The write is conditional on the column still being null, so two clients seating simultaneously
    /// cannot stamp two different lessons — one wins, and the loser's own seat check has already
    /// confirmed it can play whatever won.
    /// </para>
    /// </summary>
    private async Task TryStampLessonAsync(
        MultiplayerSession session,
        Dictionary<Guid, EligibleLesson> joinerLessons,
        CancellationToken cancellationToken)
    {
        if (session.LessonId is not null) return;
        if (session.CurrentPlayerCount < Math.Max(2, session.MinPlayers)) return;

        var remaining = await _dbContext.Set<MultiplayerSessionEligibleLesson>()
            .AsNoTracking()
            .Where(l => l.SessionId == session.Id)
            .ToListAsync(cancellationToken);

        if (remaining.Count == 0) return;

        // Least-practised first, then the earliest lesson in the curriculum — so a tie resolves to
        // the one the group would reach first anyway rather than to whatever the database returned.
        var pick = remaining
            .OrderBy(l => l.SummedBestPercent)
            .ThenBy(l => joinerLessons.TryGetValue(l.LessonId, out var lesson) ? lesson.ChapterOrder : int.MaxValue)
            .ThenBy(l => joinerLessons.TryGetValue(l.LessonId, out var lesson) ? lesson.LessonOrder : int.MaxValue)
            .ThenBy(l => l.LessonId)
            .First();

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [MultiplayerSessions]
            SET [LessonId] = {1}
            WHERE [Id] = {0} AND [LessonId] IS NULL
            """,
            [session.Id, pick.LessonId],
            cancellationToken);

        // Kept in step with the row that was just written, so the DTO this seat returns names the
        // lesson rather than a null the client would have to poll for.
        //
        // **Re-read, never assign.** The UPDATE above moved the row's RowVersion. Setting LessonId
        // on the tracked copy by hand marked it modified with the *old* version, so the next
        // SaveChanges in this request — the matchmaking request log, for one — failed with a
        // concurrency exception, and the player whose seat completed the match got a 500 at the
        // exact moment it formed. Reloading takes the new LessonId and RowVersion together and
        // leaves the entity unchanged.
        await _dbContext.Entry(session).ReloadAsync(cancellationToken);
    }

    private static ServiceResult NoSharedLesson() =>
        ServiceResult.Failure(
            ApiErrors.PlayNoSharedLesson,
            ServiceErrorKind.Conflict,
            "These players have no lesson in common in this subject.");
}
