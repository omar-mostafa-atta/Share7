using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Queued matchmaking — ranked (solo, matched by skill) and parties (placed together). The answer
/// arrives on the player feed as <c>multiplayer.matchmaking.match_found</c>.
/// </summary>
[ApiController]
[Route("api/multiplayer/tickets")]
[Authorize]
public class MatchmakingTicketsController : ControllerBase
{
    private readonly IMatchmakingTicketService _tickets;
    private readonly ICurrentUserService _currentUser;

    public MatchmakingTicketsController(IMatchmakingTicketService tickets, ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Start searching.
    /// <code>{ "gameId": "…", "modeKey": "…", "ranked": true, "protocolVersion": 1, "curriculumPath": { "subjectId": "…" }, "requestId": "…" }</code>
    /// Add <c>"partyId"</c> (as its leader) to queue your party — casual only. Answers with the ticket;
    /// keep polling your feed. Searching again returns the same ticket.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Enqueue([FromBody] EnqueueTicketRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tickets.EnqueueAsync(userId, request, cancellationToken));

    /// <summary>Your ticket — searching, or matched in the last few minutes (to recover a match after a reconnect).</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) =>
        await Run(userId => _tickets.CurrentAsync(userId, cancellationToken));

    /// <summary>Stop searching. Any player on the ticket may.</summary>
    [HttpPost("{ticketId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Cancel(Guid ticketId, CancellationToken cancellationToken) =>
        await Run(userId => _tickets.CancelAsync(userId, ticketId, cancellationToken));

    private async Task<IActionResult> Run<T>(Func<Guid, Task<Application.Common.Models.ServiceResult<T>>> action)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await action(userId);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}

/// <summary>A player's visible ranked standing. The hidden rating behind it is never sent.</summary>
[ApiController]
[Route("api/multiplayer/ranked")]
[Authorize]
public class RankedController : ControllerBase
{
    private readonly IRatingService _ratings;
    private readonly ICurrentUserService _currentUser;

    public RankedController(IRatingService ratings, ICurrentUserService currentUser)
    {
        _ratings = ratings;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Your rank in a ranked mode this season:
    /// <code>{ "seasonKey": "2026-10", "isPlacement": false, "tier": "silver", "division": 2, "matchesPlayed": 9, "wins": 5, "seasonEndsAtUtc": "…Z" }</code>
    /// While placing, <c>tier</c> is null and <c>placementMatchesLeft</c> counts down. The tier is the
    /// season's best and never drops before the season ends.
    /// </summary>
    [HttpGet("{modeId:guid}/standing")]
    public async Task<IActionResult> Standing(Guid modeId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _ratings.StandingAsync(userId, modeId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
