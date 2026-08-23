using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Where a player stands. **Every route here is a read.**
/// <para>
/// There is deliberately no submit endpoint. Ranking is projected from results the server itself
/// graded — a route that accepted a client-stated score would make a modified build the author of
/// its own rank, exactly as a purchase route taking a price rather than an <c>OfferId</c> would
/// have made it the author of its own prices. If a future game measures something the attempt path
/// cannot produce, the answer is an authoritative result route, never a leaderboard write.
/// </para>
/// <para>
/// Cohort is sent as a *kind* (<c>?cohort=grade</c>) and never as a value. The server resolves
/// which grade, school or class the caller is in from their own identity, because a client that
/// could name the value could rank itself inside a cohort it does not belong to.
/// </para>
/// </summary>
[ApiController]
[Route("api/leaderboards")]
[Authorize]
public class LeaderboardsController : ControllerBase
{
    private readonly ILeaderboardService _leaderboards;
    private readonly ICurrentUserService _currentUser;

    public LeaderboardsController(ILeaderboardService leaderboards, ICurrentUserService currentUser)
    {
        _leaderboards = leaderboards;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Boards on offer, each with its live cycle.
    /// <para>
    /// <c>supportedCohorts</c> is filtered to what this caller can actually use — a grade cohort is
    /// omitted for a child with no grade on their profile, rather than offered as a button that
    /// only ever produces an error.
    /// </para>
    /// </summary>
    [HttpGet]
    public Task<IActionResult> GetBoards([FromQuery] Guid? gameId, CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.GetBoardsAsync(userId, gameId, cancellationToken));

    /// <summary>A board's cycle history, newest first. Backs the "last week's results" switcher.</summary>
    [HttpGet("{boardId:guid}/cycles")]
    public async Task<IActionResult> GetCycles(
        Guid boardId, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        var result = await _leaderboards.GetCyclesAsync(boardId, limit, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// One page of a board.
    /// <para>
    /// Paged by opaque cursor rather than by offset, because the board is written to while it is
    /// read: under <c>OFFSET</c>, one player overtaking another between pages silently skips a row
    /// and repeats another, which on a leaderboard reads as a child's name vanishing.
    /// </para>
    /// <para>
    /// Hidden players are omitted but keep their rank, so the numbers on a page can have gaps.
    /// That is correct — rank 4 being absent does not promote rank 5.
    /// </para>
    /// </summary>
    [HttpGet("cycles/{cycleId:guid}/entries")]
    public Task<IActionResult> GetEntries(
        Guid cycleId,
        [FromQuery] string? cohort,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.GetPageAsync(userId, cycleId, cohort, cursor, limit, cancellationToken));

    /// <summary>
    /// The rows around the caller — the interesting view for anyone outside the top ten, which is
    /// nearly everyone. A caller with no entry gets an empty window and a null rank, not a 404:
    /// "you have not played yet" is an ordinary state.
    /// </summary>
    [HttpGet("cycles/{cycleId:guid}/around-me")]
    public Task<IActionResult> GetAroundMe(
        Guid cycleId,
        [FromQuery] string? cohort,
        [FromQuery] int? window,
        CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.GetAroundMeAsync(userId, cycleId, cohort, window, cancellationToken));

    /// <summary>The caller's own standing. The cheapest read — results screens, home badge, HUD.</summary>
    [HttpGet("cycles/{cycleId:guid}/me")]
    public Task<IActionResult> GetStanding(
        Guid cycleId, [FromQuery] string? cohort, CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.GetStandingAsync(userId, cycleId, cohort, cancellationToken));

    /// <summary>
    /// Where the caller finished a settled cycle, and what it paid.
    /// <para>
    /// Explains a payout rather than delivering one — the currency itself arrives through the
    /// ordinary balance path, so there is never a second way for money to reach a player. Refused
    /// with a 404 while the cycle is still running: a rank that can still move is not a result,
    /// and congratulating a child on a third place they are about to lose is worse than waiting.
    /// </para>
    /// </summary>
    [HttpGet("cycles/{cycleId:guid}/settlement/me")]
    public Task<IActionResult> GetSettlement(
        Guid cycleId, [FromQuery] string? cohort, CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.GetSettlementAsync(userId, cycleId, cohort, cancellationToken));

    /// <summary>
    /// How the caller appears on public boards: their generated handle, and whether they are
    /// listed at all.
    /// </summary>
    [HttpGet("visibility")]
    public Task<IActionResult> GetVisibility(CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.GetVisibilityAsync(userId, cancellationToken));

    /// <summary>
    /// Opt in or out of public listing. Affects listings only — the caller keeps their rank on
    /// <c>me</c> and <c>around-me</c>, because hiding is not forfeiting.
    /// <para>
    /// Refused with <c>403</c> when a guardian has forced the account unlisted. A child must not be
    /// able to undo that by toggling their own setting.
    /// </para>
    /// </summary>
    [HttpPut("visibility")]
    public Task<IActionResult> SetVisibility(
        [FromBody] LeaderboardVisibilityRequest request, CancellationToken cancellationToken) =>
        Run(userId => _leaderboards.SetVisibilityAsync(userId, request.IsHidden, cancellationToken));

    /// <summary>
    /// Resolves the caller and maps the result, so every route above stays one expression.
    /// <para>
    /// Refusals go through <c>ToApiErrorResult</c> — the <c>{ code, messageKey, details }</c>
    /// envelope commerce and account already use — rather than the older
    /// <c>{ errors: ["sentence"] }</c> shape. The backend never returns UI prose: Unity owns the
    /// localized text and looks it up by <c>messageKey</c>, which matters more here than anywhere
    /// because these strings are read by children in two languages.
    /// </para>
    /// </summary>
    private async Task<IActionResult> Run<T>(
        Func<Guid, Task<Application.Common.Models.ServiceResult<T>>> operation)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await operation(userId);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
