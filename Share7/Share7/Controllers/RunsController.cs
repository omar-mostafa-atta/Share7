using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Runs.Interfaces;
using Share7.Application.Runs.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Runs of a mini-game, and the currency they earn.
/// <para>
/// **A 3D coin is a gameplay signal, not currency.** The client collects pickups and reports counts;
/// the server re-values the run and grants what it decides. There is no field in any request here in
/// which a client can name a currency, an amount or a balance — that absence is the contract, and a
/// test enforces it by reflection so a later convenience cannot erode it.
/// </para>
/// <para>
/// Both routes operate on the caller's own runs. A run must be started before it can settle, which
/// is what makes the reported duration checkable and what refuses a fabricated result outright.
/// </para>
/// </summary>
[ApiController]
[Route("api/runs")]
[Authorize]
public class RunsController : ControllerBase
{
    private readonly IRunService _runs;
    private readonly ICurrentUserService _currentUser;

    public RunsController(IRunService runs, ICurrentUserService currentUser)
    {
        _runs = runs;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Opens a run and issues its seed.
    /// <code>
    /// { "gameId": "…", "sessionId": null, "requestId": "one-id-per-run" }
    /// </code>
    /// <para>
    /// The response carries <c>seed</c> — generate the track from it rather than from a local RNG.
    /// It is the server's copy of the layout, and the only thing that lets a later phase check a
    /// claim exactly rather than guess at it.
    /// </para>
    /// <para>
    /// Idempotent on <c>requestId</c>: a retry returns the **same** <c>runId</c> and the **same**
    /// <c>seed</c>. Reuse one id for every retry of a start — two seeds for one run means the track
    /// on screen is not the track the server can check.
    /// </para>
    /// </summary>
    /// <response code="404">No such game.</response>
    /// <response code="409">The game is retired and cannot start new runs.</response>
    [HttpPost("start")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Start(StartRunRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _runs.StartAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Settles a finished run. **Report what was collected; the server decides what it was worth.**
    /// <code>
    /// {
    ///   "pickups":   [ { "kind": "coin", "count": 47 } ],
    ///   "modifiers": [ { "kind": "double_reward", "durationSeconds": 12.0 } ],
    ///   "durationMs": 94500,
    ///   "outcome": "Completed",
    ///   "requestId": "one-id-per-result"
    /// }
    /// </code>
    /// <para>
    /// The response is deliberately the same shape as <c>POST /api/progress/attempts</c>:
    /// <c>rewards</c> are **deltas** to animate, <c>balances</c> are **absolute** totals that already
    /// include them. Assign the balances over the local wallet; do not add the rewards to it. One
    /// reconciler serves both routes.
    /// </para>
    /// <para>
    /// <c>capReached</c> and <c>capMessage</c> are how a shortfall is explained. A run that shows 47
    /// collected and pays 20 has to be able to say why — paying less in silence is how a child learns
    /// the game is unfair.
    /// </para>
    /// <para>
    /// Idempotent twice over: a second result for a settled run returns the **stored settlement**
    /// rather than paying again, and a <c>requestId</c> already spent on a different run is refused.
    /// The offline queue retries on reconnect by design, so a replay is the ordinary path.
    /// </para>
    /// </summary>
    /// <response code="404">No such run, or it belongs to another account — including a run that was never started.</response>
    /// <response code="409">The run expired, is in a state that cannot settle, or the request id already settled a different run.</response>
    [HttpPost("{runId:guid}/result")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Result(
        Guid runId,
        SubmitRunResultRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _runs.SettleAsync(userId, runId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
