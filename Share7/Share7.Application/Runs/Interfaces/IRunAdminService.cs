using Share7.Application.Common.Models;
using Share7.Application.Runs.Models;
using Share7.Application.Runs.Models.Admin;

namespace Share7.Application.Runs.Interfaces;

/// <summary>
/// Authoring for the valuation table, and the review queue for runs that tripped a bound.
/// <para>
/// Admin-only without exception: a player who could author a valuation could set their own payout,
/// which is precisely what the server-authoritative economy exists to prevent.
/// </para>
/// <para>
/// **There is no delete for a valuation.** A row that has priced a payout has to stay resolvable for
/// the <c>RunPayout</c> rows recorded against it to remain explicable, so retiring with
/// <c>enabled: false</c> is the supported way to take a price out of circulation.
/// </para>
/// </summary>
public interface IRunAdminService
{
    Task<IReadOnlyList<PickupValuationDto>> GetValuationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a price. Refuses a row that could never price safely — an illegal kind token, a missing
    /// per-run bound, or a **hard currency without a per-day cap**. Refused at creation rather than
    /// clamped at runtime, because a missing bound discovered later is currency already spent.
    /// </summary>
    Task<ServiceResult<PickupValuationDto>> CreateValuationAsync(
        CreatePickupValuationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retunes a price. What the row *prices* cannot move — see
    /// <see cref="UpdatePickupValuationRequest"/>.
    /// </summary>
    Task<ServiceResult<PickupValuationDto>> UpdateValuationAsync(
        Guid valuationId,
        UpdatePickupValuationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs that tripped a bound and are still waiting on a human, newest first.
    /// <para>
    /// This queue is the other half of "cap and flag, never discard". Capping without anyone ever
    /// reading the flags means a farming pattern and a child with a broken clock are recorded
    /// identically and neither is ever noticed.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RunAdminDto>> GetFlaggedRunsAsync(
        int take = 50,
        bool includeReviewed = false,
        CancellationToken cancellationToken = default);

    /// <summary>One run in full, flagged or not — what was claimed, what was paid, and why they differ.</summary>
    Task<ServiceResult<RunAdminDto>> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a run as looked at, recording who and what they concluded.
    /// <para>
    /// **The flag itself is not cleared.** It is what actually happened to the run, and a settled
    /// payout has to stay explicable — reviewing records a judgement about history, it does not edit
    /// history.
    /// </para>
    /// </summary>
    Task<ServiceResult<RunAdminDto>> ReviewRunAsync(
        Guid runId,
        Guid reviewerUserId,
        ReviewRunRequest request,
        CancellationToken cancellationToken = default);
}
