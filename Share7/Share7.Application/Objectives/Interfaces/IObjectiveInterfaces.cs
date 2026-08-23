using Share7.Application.Common.Models;
using Share7.Application.Objectives.Models;

namespace Share7.Application.Objectives.Interfaces;

/// <summary>
/// Folds new <c>GameResult</c> rows into player objective counters.
/// <para>
/// **A second projection of the same stream leaderboards read**, which is what makes objectives
/// rebuildable and backfillable: an achievement authored today can be replayed over results that
/// already happened, so launching one does not require every child to play their history again.
/// </para>
/// </summary>
public interface IObjectiveProjector
{
    /// <summary>
    /// Projects everything not yet counted for **one player**, called inline on the request that
    /// produced their results.
    /// <para>
    /// Inline rather than deferred because a child who finishes their third lesson expects the
    /// quest to complete *now*; a background-only projector makes that a race with the results
    /// screen. It is the same code the batch pass runs — one implementation, two callers — so
    /// immediacy costs no second code path to keep correct.
    /// </para>
    /// <para>
    /// **Must be called inside the caller's open transaction**, so progress and the objectives it
    /// advanced commit together.
    /// </para>
    /// </summary>
    Task ProjectForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains the stream for every player, from the <c>objectives</c> checkpoint forward.
    /// <para>
    /// Backfill and repair only — the inline pass keeps live play current. This is what a newly
    /// authored objective is caught up with, and what recovers a window where the inline pass
    /// failed.
    /// </para>
    /// </summary>
    /// <returns>How many results were folded in.</returns>
    Task<int> ProjectPendingAsync(int batchSize = 500, CancellationToken cancellationToken = default);
}

/// <summary>
/// A player's own objectives: what they are working on, and collecting what they finished.
/// </summary>
public interface IObjectiveService
{
    /// <summary>
    /// Every objective currently offered to this player, with their progress in the current cycle.
    /// <para>
    /// Includes objectives never started — a quest a child has not touched still has to appear, or
    /// the list is empty until they happen to do the right thing by accident.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ObjectiveDto>> GetForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays a completed objective and marks it claimed.
    /// <para>
    /// Idempotent on <paramref name="requestId"/>, and independently on the claim itself: a second
    /// claim of an already-claimed objective reports what it paid the first time rather than paying
    /// again. Refuses an objective that is not <c>Completed</c>.
    /// </para>
    /// </summary>
    Task<ServiceResult<ObjectiveClaimResultDto>> ClaimAsync(
        Guid userId,
        string objectiveKey,
        string? requestId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Authoring. Admin only — an objective decides what gets paid.</summary>
public interface IObjectiveAdminService
{
    Task<IReadOnlyList<ObjectiveAdminDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<ObjectiveAdminDto>> CreateAsync(
        CreateObjectiveRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<ObjectiveAdminDto>> UpdateAsync(
        Guid objectiveId, UpdateObjectiveRequest request, CancellationToken cancellationToken = default);
}
