using Share7.Application.Progress.Models;

namespace Share7.Application.Progress.Interfaces;

/// <summary>
/// The unlock ladder. Every grant is permanent — nothing here ever removes a row, so a student
/// who completes a lesson and later replays it badly keeps whatever that completion opened.
/// <para>
/// The ladder runs Term → Chapter → Lesson. <b>Subjects are not a rung</b>: all of a term's
/// subjects open with the term, so a student is free to pick Science before finishing Maths.
/// </para>
/// </summary>
public interface IUnlockService
{
    /// <summary>
    /// Ensures a student's entry points into a game exist. With no unlocks at all they are given
    /// the first term of <paramref name="gradeId"/> by <c>Order</c>; otherwise the terms they
    /// already hold are topped up. Either way every subject of every unlocked term ends up open,
    /// each down to its first chapter and that chapter's first lesson.
    /// <para>
    /// Idempotent, and it is the repair path: a student who started while subjects still gated
    /// each other, or a term an author has since added a subject to, is corrected on the next
    /// call rather than by a data migration.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<UnlockedNodeDto>> EnsureSeededAsync(
        Guid userId, Guid gameId, Guid gradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-evaluates the ladder around a lesson that was just attempted and grants anything now
    /// earned:
    /// <list type="bullet">
    /// <item>the next lesson opens once this one is Completed or Aced;</item>
    /// <item>the next chapter (and its first lesson) opens once every lesson in this chapter is;</item>
    /// <item>the next term (and all of its subjects) opens once every lesson in this term is.</item>
    /// </list>
    /// There is no subject rule — see the interface remarks.
    /// <para>
    /// Lessons with no question set in the student's language count as satisfied — otherwise one
    /// missing sheet would freeze a whole chapter for every student reading that language.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<UnlockedNodeDto>> EvaluateAfterAttemptAsync(
        Guid userId, Guid gameId, Guid lessonId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>Every node this student has unlocked in this game, for snapshot reads.</summary>
    Task<HashSet<Guid>> GetUnlockedNodeIdsAsync(
        Guid userId, Guid gameId, CancellationToken cancellationToken = default);
}
