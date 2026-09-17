using Share7.Application.Common.Models;
using Share7.Application.Runs.Models;

namespace Share7.Application.Runs.Interfaces;

/// <summary>
/// Opens runs and settles them. **The authority boundary for the pickup economy**: the client reports
/// what it collected, this decides what that was worth.
/// <para>
/// There is no overload taking an amount, no way to reach the wallet from a mini-game, and no route
/// that settles a run the server did not open. A 3D coin is a gameplay signal; currency is what this
/// grants in answer to one.
/// </para>
/// </summary>
public interface IRunService
{
    /// <summary>
    /// Opens a run and issues its seed.
    /// <para>
    /// Idempotent on <c>requestId</c>: a retried start returns the same run and the same seed rather
    /// than opening a second one. That is not just tidiness — the client generates its track from the
    /// seed, and two seeds for one run means the track on screen is not the track the server can
    /// later check.
    /// </para>
    /// </summary>
    Task<ServiceResult<StartRunResponse>> StartAsync(
        Guid userId,
        StartRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-values a finished run and pays it.
    /// <para>
    /// Refuses a run that was never started, one belonging to somebody else, and one past its expiry.
    /// A run that is **already settled returns its stored settlement** rather than paying twice —
    /// the client's offline queue retries on reconnect by design, so a replay is the ordinary path,
    /// not an edge case.
    /// </para>
    /// <para>
    /// An implausible run is capped, flagged and paid — never discarded. A child on a device with a
    /// bad clock, or one whose session dropped and resumed, must not lose a legitimate run with no way
    /// to explain why.
    /// </para>
    /// </summary>
    Task<ServiceResult<RunSettlementDto>> SettleAsync(
        Guid userId,
        Guid runId,
        SubmitRunResultRequest request,
        CancellationToken cancellationToken = default);
}
