using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Board authoring and recovery. Not called by the game client.
/// <para>
/// **Adding a leaderboard is an INSERT here** — no migration, no deploy, no client release. That
/// is the point of boards being data: a seasonal event should not be a release.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/leaderboards")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminLeaderboardsController : ControllerBase
{
    private readonly ILeaderboardAdminService _admin;

    public AdminLeaderboardsController(ILeaderboardAdminService admin) => _admin = admin;

    [HttpGet("boards")]
    public async Task<IActionResult> GetBoards(CancellationToken cancellationToken)
    {
        var result = await _admin.GetBoardsAsync(cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Creates a board and opens its current window.
    /// <para>
    /// Refuses a metric nothing raises and a cohort the schema cannot resolve. Both would produce
    /// a board that never fills, which is indistinguishable from an unpopular one and is therefore
    /// far harder to notice than a refusal.
    /// </para>
    /// </summary>
    [HttpPost("boards")]
    public async Task<IActionResult> CreateBoard(
        [FromBody] SaveLeaderboardBoardRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.CreateBoardAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Updates presentation and policy. Key, metric and aggregation are immutable — retire the
    /// board and author a replacement instead of changing what its existing numbers mean.
    /// </summary>
    [HttpPut("boards/{boardId:guid}")]
    public async Task<IActionResult> UpdateBoard(
        Guid boardId,
        [FromBody] SaveLeaderboardBoardRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _admin.UpdateBoardAsync(boardId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// A board's windows, newest first.
    /// <para>
    /// The player-facing listing answers this too, but refuses while <c>Leaderboards:Enabled</c> is
    /// off — exactly the window in which boards are authored. Rebuild and settle below are
    /// addressed by cycle id, so without this an operator has no way to name the cycle they mean.
    /// </para>
    /// </summary>
    [HttpGet("boards/{boardId:guid}/cycles")]
    public async Task<IActionResult> GetCycles(
        Guid boardId, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var result = await _admin.GetCyclesAsync(boardId, limit, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Authors an event window by hand.</summary>
    [HttpPost("boards/{boardId:guid}/cycles")]
    public async Task<IActionResult> CreateCycle(
        Guid boardId,
        [FromBody] CreateLeaderboardCycleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _admin.CreateEventCycleAsync(boardId, request, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>
    /// Rebuilds a cycle's ranking from <c>GameResults</c>.
    /// <para>
    /// The recovery path. Because entries are purely derived, this is safe to run against live
    /// data — and if it ever produced different ranks, that would be a defect in the projector
    /// rather than a reason not to run it.
    /// </para>
    /// </summary>
    [HttpPost("cycles/{cycleId:guid}/rebuild")]
    public async Task<IActionResult> RebuildCycle(Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _admin.RebuildCycleAsync(cycleId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>
    /// Settles a closed cycle now instead of waiting for its scheduled job. Idempotent — a cycle
    /// already settled succeeds without paying anything a second time.
    /// </summary>
    [HttpPost("cycles/{cycleId:guid}/settle")]
    public async Task<IActionResult> SettleCycle(Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _admin.SettleCycleAsync(cycleId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    // ---- anti-cheat ------------------------------------------------------------------------

    /// <summary>What currently counts as a believable result, per game and metric.</summary>
    [HttpGet("bounds")]
    public async Task<IActionResult> GetBounds(CancellationToken cancellationToken)
    {
        var result = await _admin.GetBoundsAsync(cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Authors or replaces a bound.
    /// <para>
    /// Bounds are data so that tightening one after a live exploit is a row edit rather than a
    /// release — which matters, because the answer key is client-visible by necessity (the quiz
    /// grades locally to show right or wrong the instant a child taps) and this is the compensating
    /// control.
    /// </para>
    /// </summary>
    [HttpPut("bounds")]
    public async Task<IActionResult> SaveBound(
        [FromBody] SaveMetricBoundRequest request, CancellationToken cancellationToken)
    {
        var result = await _admin.SaveBoundAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The review queue: results held out of ranking. Players are identified by public handle only
    /// — nothing about judging whether a score is real requires knowing which child earned it.
    /// </summary>
    [HttpGet("flagged")]
    public async Task<IActionResult> GetFlagged(
        [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var result = await _admin.GetFlaggedAsync(limit, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Records a decision. Clearing a flag re-queues the result so the player takes the rank they
    /// should have had; upholding one leaves it excluded. The row survives either way.
    /// </summary>
    [HttpPost("flagged/{resultId:guid}/resolve")]
    public async Task<IActionResult> ResolveFlag(
        Guid resultId,
        [FromBody] ResolveFlagRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _admin.ResolveFlagAsync(resultId, request.Legitimate, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
