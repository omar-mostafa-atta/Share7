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
}
