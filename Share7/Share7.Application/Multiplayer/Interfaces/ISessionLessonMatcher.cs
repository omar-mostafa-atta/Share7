using Share7.Application.Common.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>One lesson a player could play right now, with what the pick needs to order it by.</summary>
public readonly record struct EligibleLesson(
    Guid LessonId, int BestPercent, int ChapterOrder, int LessonOrder);

/// <summary>
/// Works out which lesson two or more children can actually play together.
/// <para>
/// <b>The problem it exists for:</b> matchmaking used to demand that both players had chosen the same
/// lesson, which in practice meant a child could only ever match with somebody at exactly their own
/// point in the curriculum — and usually matched with nobody at all. Players now choose a
/// <i>subject</i>, and the server finds a lesson they have in common: unlocked by both, with
/// questions in the language each of them is playing in.
/// </para>
/// <para>
/// The set is an intersection that only ever narrows as players are seated, and the lesson is stamped
/// once the match has the players it needs. A lesson that left the set because a third player had not
/// unlocked it must never come back, or a match would start on a lesson somebody was already told
/// they could not play.
/// </para>
/// </summary>
public interface ISessionLessonMatcher
{
    /// <summary>
    /// Every lesson in one subject this player could play in this game, ordered by where it sits in
    /// the curriculum. Empty when the subject has nothing unlocked, or nothing with questions in
    /// their language — both of which are ordinary, and neither of which is an error.
    /// </summary>
    Task<IReadOnlyList<EligibleLesson>> EligibleLessonsAsync(
        Guid userId, Guid gameId, Guid subjectId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Narrows a session's candidate set to what a newly seated player can also play, and stamps the
    /// match's lesson once the roster is big enough.
    /// <para>
    /// Refuses with <c>PC_NO_SHARED_LESSON</c> when there is nothing left in common — which is the
    /// signal for matchmaking to unseat this player and try the next session rather than starting a
    /// match somebody cannot play.
    /// </para>
    /// </summary>
    Task<ServiceResult> NarrowForSeatAsync(
        Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
}
