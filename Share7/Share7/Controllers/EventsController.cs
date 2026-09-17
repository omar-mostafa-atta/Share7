using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Play.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// Events as an entrant sees them: what is running, what it pays, and what they have won.
/// <para>
/// <b>Never decide from device time.</b> Every response carries <c>serverTimeUtc</c>, and each event
/// carries the state of its own ladder — a tablet with a wrong clock must not be able to show a
/// competition as open when it has finished, or refuse one that is running.
/// </para>
/// </summary>
[ApiController]
[Route("api/events")]
[Authorize]
public class EventsController : ControllerBase
{
    private readonly IPlayEventService _events;
    private readonly ICurrentUserService _currentUser;

    public EventsController(IPlayEventService events, ICurrentUserService currentUser)
    {
        _events = events;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Live events, with this caller's eligibility, entries used and standing filled in. Finished
    /// events stay listed for a week so their winners can still be read.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? gameKey,
        [FromQuery] bool includeScheduled = true,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        return Ok(await _events.ListAsync(userId, gameKey, includeScheduled, cancellationToken));
    }

    /// <summary>One event in full: rules, prize table, standing.</summary>
    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> Get(Guid eventId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _events.GetAsync(userId, eventId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// What this account has won. <c>unseenOnly</c> is what a "you won" surface polls — an award
    /// nobody has been shown is different from one already celebrated.
    /// </summary>
    [HttpGet("me/awards")]
    public async Task<IActionResult> GetAwards(
        [FromQuery] bool unseenOnly = false, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        return Ok(await _events.GetAwardsAsync(userId, unseenOnly, cancellationToken));
    }

    /// <summary>
    /// Marks an award as shown. Idempotent — a retry does not move the moment the child first saw it.
    /// </summary>
    [HttpPost("me/awards/{awardId:guid}/seen")]
    public async Task<IActionResult> MarkSeen(Guid awardId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _events.MarkAwardSeenAsync(userId, awardId, cancellationToken);

        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
