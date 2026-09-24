using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Guidance.Interfaces;
using Share7.Application.Guidance.Models;
using Share7.Domain.Guidance;

namespace Share7.API.Controllers;

/// <summary>
/// Carries an account's guidance journal state to and from the server.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GuidanceController : ControllerBase
{
    private readonly IGuidanceStateService _guidanceStateService;
    private readonly IGuidanceCatalogService _guidanceCatalogService;
    private readonly ICurrentUserService _currentUserService;

    public GuidanceController(
        IGuidanceStateService guidanceStateService,
        IGuidanceCatalogService guidanceCatalogService,
        ICurrentUserService currentUserService)
    {
        _guidanceStateService = guidanceStateService ?? throw new ArgumentNullException(nameof(guidanceStateService));
        _guidanceCatalogService = guidanceCatalogService ?? throw new ArgumentNullException(nameof(guidanceCatalogService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    /// <summary>
    /// Returns the active remote guidance catalog containing all published, non-kill-switched flows.
    /// Accessible anonymously so client can load catalog before or after signing in.
    /// </summary>
    [HttpGet("catalog")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
    {
        var result = await _guidanceCatalogService.GetPublishedCatalogAsync(cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns the calling player's authoritative guidance state snapshot.
    /// Always succeeds (200), returning an empty snapshot if never synced before.
    /// </summary>
    [HttpGet("state")]
    public async Task<IActionResult> GetState(CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _guidanceStateService.GetStateAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Pushes a local guidance state snapshot to be merged with the server's state.
    /// The merge is a mathematically proven CRDT union (grow-only, commutative, associative, idempotent).
    /// Returns the merged snapshot.
    /// </summary>
    [HttpPost("state")]
    public async Task<IActionResult> PushState(
        [FromBody] GuidanceStateSnapshot request,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _guidanceStateService.PushStateAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
