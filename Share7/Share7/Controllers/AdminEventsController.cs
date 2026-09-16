using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Authoring competitions.
/// <para>
/// Creating an event creates its ladder in the same transaction — the board, its single cycle, and
/// the prize table. What may then be edited narrows as the event runs: everything while it is
/// scheduled, the end date and presentation while it is open, presentation alone once it has closed.
/// Entrants competed under the rules they were shown.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/events")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminEventsController : ControllerBase
{
    private readonly IPlayEventAdminService _events;
    private readonly ICurrentUserService _currentUser;

    public AdminEventsController(IPlayEventAdminService events, ICurrentUserService currentUser)
    {
        _events = events;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? gameId,
        [FromQuery] bool includeFinished = true,
        CancellationToken cancellationToken = default) =>
        Ok(await _events.ListForAuthoringAsync(gameId, includeFinished, cancellationToken));

    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> GetForAuthoring(Guid eventId, CancellationToken cancellationToken)
    {
        var playEvent = await _events.GetForAuthoringAsync(eventId, cancellationToken);

        return playEvent is null ? NotFound(new { errors = new[] { "Event not found." } }) : Ok(playEvent);
    }

    /// <summary>Creates an event, its board, its cycle and its prize table in one transaction.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(SavePlayEventRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _events.CreateAsync(request, userId, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(GetForAuthoring), new { eventId = result.Value!.EventId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>Updates an event, within whatever its ladder's state still allows to move.</summary>
    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> Update(
        Guid eventId, SavePlayEventRequest request, CancellationToken cancellationToken)
    {
        var result = await _events.UpdateAsync(eventId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Calls an event off: entries stop, the ladder closes, and no prize is ever awarded — including
    /// if its cycle settles afterwards.
    /// </summary>
    [HttpPost("{eventId:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid eventId, CancelPlayEventRequest request, CancellationToken cancellationToken)
    {
        var result = await _events.CancelAsync(eventId, request?.Reason, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Copies an event into the next window, inactive, so an operator running a weekly competition
    /// does not retype it. The copy is published by setting <c>isActive</c>.
    /// </summary>
    [HttpPost("{eventId:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid eventId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _events.DuplicateAsync(eventId, userId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>What the event paid out, once its cycle settled.</summary>
    [HttpGet("{eventId:guid}/awards")]
    public async Task<IActionResult> GetAwards(Guid eventId, CancellationToken cancellationToken) =>
        Ok(await _events.GetAwardsAsync(eventId, cancellationToken));
}

/// <summary>
/// The real-world prize queue. Every transition is a person's decision, recorded with a note.
/// </summary>
[ApiController]
[Route("api/admin/prize-claims")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminPrizeClaimsController : ControllerBase
{
    private readonly IPrizeClaimAdminService _claims;
    private readonly ICurrentUserService _currentUser;

    public AdminPrizeClaimsController(IPrizeClaimAdminService claims, ICurrentUserService currentUser)
    {
        _claims = claims;
        _currentUser = currentUser;
    }

    /// <summary>The queue, oldest first within each state. Filterable by state and by event.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? state,
        [FromQuery] Guid? eventId,
        CancellationToken cancellationToken) =>
        Ok(await _claims.ListAsync(state, eventId, cancellationToken));

    /// <summary>
    /// Moves a claim along: approved and with a guardian, delivered, forfeited or refused. The
    /// award's own state follows it, so the winner's screen says what this queue says.
    /// </summary>
    [HttpPut("{claimId:guid}")]
    public async Task<IActionResult> Update(
        Guid claimId, UpdatePrizeClaimRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _claims.UpdateAsync(claimId, request, userId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
