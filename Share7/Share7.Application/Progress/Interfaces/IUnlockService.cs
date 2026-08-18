using Share7.Application.Progress.Models;

namespace Share7.Application.Progress.Interfaces;

/// <summary>
/// The unlock ladder. Every grant is permanent — nothing here ever removes a row, so a student
/// who completes a lesson and later replays it badly keeps whatever that completion opened.
/// </summary>
public interface IUnlockService
{
    /// <summary>
    /// Gives a student their starting point in a game if they have none: the first lesson of the
    /// first chapter of the first subject of the first term of <paramref name="gradeId"/>, by
    /// <c>Order</c>, along with the ancestors that contain it. Idempotent.
    /// </summary>
    Task<IReadOnlyList<UnlockedNodeDto>> EnsureSeededAsync(
        Guid userId, Guid gameId, Guid gradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-evaluates the ladder around a lesson that was just attempted and grants anything now
    /// earned. Rules, applied at every level:
    /// <list type="bullet">
    /// <item>the next lesson opens once this one is Completed or Aced;</item>
    /// <item>the next chapter (and its first lesson) opens once every lesson in this chapter is;</item>
    /// <item>likewise for subjects and terms, each from its own children.</item>
    /// </list>
    /// Lessons with no question set in the student's language count as satisfied — otherwise one
    /// missing sheet would freeze a whole chapter for every student reading that language.
    /// </summary>
    Task<IReadOnlyList<UnlockedNodeDto>> EvaluateAfterAttemptAsync(
        Guid userId, Guid gameId, Guid lessonId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>Every node this student has unlocked in this game, for snapshot reads.</summary>
    Task<HashSet<Guid>> GetUnlockedNodeIdsAsync(
        Guid userId, Guid gameId, CancellationToken cancellationToken = default);
}
