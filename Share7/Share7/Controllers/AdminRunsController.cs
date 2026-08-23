using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Runs.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Application.Runs.Models.Admin;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// What a pickup is worth, and the runs that tripped a bound.
/// <para>
/// **Admin only, without exception** — a player who could author a valuation could set their own
/// payout, which is precisely what the server-authoritative economy exists to prevent. Nothing here
/// is reachable by the game client.
/// </para>
/// <para>
/// Together these two surfaces are what makes the economy tunable without a deploy, and what stops
/// "flagged for review" from being a column nobody reads.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminRunsController : ControllerBase
{
    private readonly IRunAdminService _runAdmin;
    private readonly ICurrentUserService _currentUser;

    public AdminRunsController(IRunAdminService runAdmin, ICurrentUserService currentUser)
    {
        _runAdmin = runAdmin;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Every price, retired ones included. <c>currencyEnabled</c> is worth checking when a kind has
    /// stopped paying: a row referencing a retired currency is skipped whole at settlement.
    /// </summary>
    [HttpGet("pickup-valuations")]
    public async Task<IActionResult> GetValuations(CancellationToken cancellationToken)
    {
        var valuations = await _runAdmin.GetValuationsAsync(cancellationToken);
        return Ok(new { valuations });
    }

    /// <summary>
    /// Prices a pickup kind.
    /// <code>
    /// { "gameId": null, "pickupKind": "coin", "currencyId": "…", "unitValue": 1, "maxPerRun": 500 }
    /// </code>
    /// <para>
    /// Leave <c>gameId</c> null for the platform default every unconfigured mini-game resolves
    /// through, or set it to price the same kind differently in one harder game. Exact match wins
    /// over the default; a kind with neither pays nothing, which is a design oversight to notice in
    /// the payout data rather than a reason to fail a child's run.
    /// </para>
    /// <para>
    /// <c>maxPerDay</c> is optional for a soft currency and **mandatory for a hard one**. That is
    /// refused here rather than clamped at settlement because a missing bound on something people
    /// paid real money for cannot be corrected after the fact — the currency is already in
    /// circulation and can never be rebalanced downward.
    /// </para>
    /// </summary>
    /// <response code="400">Illegal kind token, or a hard currency without <c>maxPerDay</c>.</response>
    /// <response code="404">No such currency, or no such game.</response>
    /// <response code="409">That game already prices that kind in that currency.</response>
    [HttpPost("pickup-valuations")]
    public async Task<IActionResult> CreateValuation(
        CreatePickupValuationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _runAdmin.CreateValuationAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Retunes a price — a weekend double-value event, a nerf after launch. **This is the whole
    /// economy-tuning surface**, and it takes effect on the next run with no deploy and no client
    /// release.
    /// <para>
    /// What the row *prices* cannot move: <c>gameId</c>, <c>pickupKind</c> and <c>currencyId</c> are
    /// not accepted, because changing them would strand the payout rows recorded against it. Retire
    /// it with <c>enabled: false</c> and author a replacement.
    /// </para>
    /// </summary>
    [HttpPut("pickup-valuations/{valuationId:guid}")]
    public async Task<IActionResult> UpdateValuation(
        Guid valuationId,
        UpdatePickupValuationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _runAdmin.UpdateValuationAsync(valuationId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Runs that tripped a bound and are still waiting on a human, newest first.
    /// <para>
    /// An implausible run is capped, flagged and **paid** — never discarded, because a child on a
    /// device with a bad clock trips the same bounds a farming script does and must not lose a
    /// legitimate run with no way to explain why. This queue is the other half of that bargain:
    /// without somebody reading it, the two cases are recorded identically and neither is noticed.
    /// </para>
    /// </summary>
    [HttpGet("runs/flagged")]
    public async Task<IActionResult> GetFlagged(
        CancellationToken cancellationToken,
        int take = 50,
        bool includeReviewed = false)
    {
        var runs = await _runAdmin.GetFlaggedRunsAsync(take, includeReviewed, cancellationToken);
        return Ok(new { runs });
    }

    /// <summary>
    /// One run in full: what was claimed, what was paid, and the gross/net difference that answers
    /// "why did they only get 20?".
    /// </summary>
    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _runAdmin.GetRunAsync(runId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Records that somebody looked at a flagged run, and what they concluded.
    /// <para>
    /// **The flag is not cleared and the payout is not changed.** Both are what actually happened,
    /// and a settled payout has to stay explicable; a review records a judgement about history rather
    /// than editing it. Reviewed runs drop out of the default queue and can be listed with
    /// <c>includeReviewed=true</c>.
    /// </para>
    /// </summary>
    [HttpPost("runs/{runId:guid}/review")]
    public async Task<IActionResult> ReviewRun(
        Guid runId,
        ReviewRunRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } reviewerId)
            return Unauthorized();

        var result = await _runAdmin.ReviewRunAsync(runId, reviewerId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
