using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Guidance.Interfaces;
using Share7.Application.Guidance.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Remote guidance management console API.
/// Allows SuperAdmin/Admin to author, version, publish, audit, and emergency-kill guidance flows,
/// and administratively reset player guidance state.
/// </summary>
[ApiController]
[Route("api/admin/guidance")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminGuidanceController : ControllerBase
{
    private readonly IGuidanceAdminService _adminService;
    private readonly IGuidanceStateService _stateService;
    private readonly ICurrentUserService _currentUserService;

    public AdminGuidanceController(
        IGuidanceAdminService adminService,
        IGuidanceStateService stateService,
        ICurrentUserService currentUserService)
    {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    /// <summary>
    /// Returns all authored guidance flows including version status and kill-switch state.
    /// </summary>
    [HttpGet("flows")]
    public async Task<IActionResult> ListFlows(
        [FromQuery] bool includeKillSwitched = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.ListFlowsAsync(includeKillSwitched, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns one guidance flow in full detail, with draft and published versions.
    /// </summary>
    [HttpGet("flows/{id:guid}")]
    public async Task<IActionResult> GetFlow(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetFlowAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Creates a new guidance flow aggregate with an initial Draft version.
    /// </summary>
    [HttpPost("flows")]
    public async Task<IActionResult> CreateFlow(
        [FromBody] CreateGuidanceFlowRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var userEmail = _currentUserService.Email;

        var result = await _adminService.CreateFlowAsync(request, userId, userEmail, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetFlow), new { id = result.Value!.Id }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Updates the draft steps or flow configuration. If no draft exists, creates a new draft version.
    /// </summary>
    [HttpPut("flows/{id:guid}/draft")]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        [FromBody] UpdateGuidanceFlowDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var userEmail = _currentUserService.Email;

        var result = await _adminService.UpdateDraftAsync(id, request, userId, userEmail, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Publishes the current draft into an immutable published version, making it the active version.
    /// </summary>
    [HttpPost("flows/{id:guid}/publish")]
    public async Task<IActionResult> PublishVersion(
        Guid id,
        [FromBody] PublishGuidanceFlowRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var userEmail = _currentUserService.Email;

        var result = await _adminService.PublishVersionAsync(id, request, userId, userEmail, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Toggles the emergency kill switch for a flow, instantly removing or restoring it on clients.
    /// </summary>
    [HttpPost("flows/{id:guid}/kill-switch")]
    public async Task<IActionResult> ToggleKillSwitch(
        Guid id,
        [FromBody] ToggleKillSwitchRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var userEmail = _currentUserService.Email;

        var result = await _adminService.ToggleKillSwitchAsync(id, request.IsKillSwitched, request.Reason, userId, userEmail, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns the target user's current server-backed guidance state.
    /// </summary>
    [HttpGet("users/{userId:guid}/state")]
    public async Task<IActionResult> GetUserGuidanceState(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = await _stateService.GetStateAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Administratively resets a target user's guidance journal state by incrementing their generation.
    /// Replays onboarding and wipes flow completion on all of their devices upon their next sync.
    /// </summary>
    [HttpPost("users/{userId:guid}/reset")]
    public async Task<IActionResult> ResetUserGuidance(
        Guid userId,
        [FromBody] ResetUserGuidanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var adminUserId = _currentUserService.UserId ?? Guid.Empty;
        var adminEmail = _currentUserService.Email;

        var result = await _adminService.ResetUserGuidanceAsync(userId, request.Reason, adminUserId, adminEmail, cancellationToken);
        return result.Succeeded ? Ok(new { reset = true, targetUserId = userId }) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns recent audit logs for guidance changes.
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] Guid? flowId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetAuditLogsAsync(flowId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns aggregated funnel, drop-off, and dwell duration analytics for a specific guidance flow.
    /// </summary>
    [HttpGet("flows/{id:guid}/funnel")]
    public async Task<IActionResult> GetFlowFunnel(
        Guid id,
        [FromQuery] int? version = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetFlowFunnelAsync(id, version, from, to, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns diagnostic report of missing scene anchors reported by clients.
    /// </summary>
    [HttpGet("missing-anchors")]
    public async Task<IActionResult> GetMissingAnchors(
        [FromQuery] string? flowKey = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetMissingAnchorsAsync(flowKey, from, to, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Returns high-level performance metrics (completion rates, started counts, missing anchors) for all flows.
    /// </summary>
    [HttpGet("flows-summary")]
    public async Task<IActionResult> GetFlowsSummary(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetFlowsSummaryStatsAsync(from, to, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
