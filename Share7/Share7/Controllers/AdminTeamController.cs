using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Authorization;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Team &amp; Access: <c>/api/admin/team</c>. SuperAdmins only — the only way a content-team
/// account is created, changed, suspended or closed. Every write lands in the audit trail.
/// </summary>
[ApiController]
[Route("api/admin/team")]
[Authorize(Policy = Policies.ManageStaff)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminTeamController : ControllerBase
{
    private readonly ITeamAdminService _team;

    public AdminTeamController(ITeamAdminService team) => _team = team;

    /// <summary>Every member, the content-team accounts still waiting for a Studio profile, and the counts.</summary>
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken) =>
        Ok(await _team.GetOverviewAsync(cancellationToken));

    /// <summary>The curriculum (down to chapters) and the languages a scope is built from.</summary>
    [HttpGet("scope-options")]
    public async Task<IActionResult> ScopeOptions(CancellationToken cancellationToken) =>
        Ok(await _team.GetScopeOptionsAsync(cancellationToken));

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _team.GetMemberAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>Creates a member and returns their one-time setup link. The link is never shown again.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(CreateTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.CreateMemberAsync(request, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { userId = result.Value!.Member.UserId }, result.Value)
            : result.ToErrorResult();
    }

    /// <summary>Gives a content-team account made before Team &amp; Access its Studio profile.</summary>
    [HttpPost("legacy/{userId:guid}/adopt")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Adopt(Guid userId, AdoptLegacyAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.AdoptLegacyAccountAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    [HttpPut("{userId:guid}/profile")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> UpdateProfile(Guid userId, UpdateTeamMemberProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.UpdateProfileAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>Studio role and scope. Applies to the member's next request.</summary>
    [HttpPut("{userId:guid}/access")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> UpdateAccess(Guid userId, UpdateTeamMemberAccessRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.UpdateAccessAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    [HttpPut("{userId:guid}/notes")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> UpdateNotes(Guid userId, UpdateTeamMemberNotesRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.UpdateNotesAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    [HttpPost("{userId:guid}/suspend")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Suspend(Guid userId, SuspendTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.SuspendAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    [HttpPost("{userId:guid}/reactivate")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _team.ReactivateAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>Closes the account for good. The request retypes the username to confirm.</summary>
    [HttpPost("{userId:guid}/deactivate")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Deactivate(Guid userId, DeactivateTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.DeactivateAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>Clears the password (and optionally 2-step), signs out everywhere, and returns a new setup link.</summary>
    [HttpPost("{userId:guid}/reset-access")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> ResetAccess(Guid userId, ResetTeamMemberAccessRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.ResetAccessAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    [HttpDelete("{userId:guid}/setup-link")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> RevokeSetupLink(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _team.RevokeSetupLinkAsync(userId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToErrorResult();
    }

    [HttpPost("{userId:guid}/sign-out-everywhere")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SignOutEverywhere(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _team.SignOutEverywhereAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    [HttpDelete("{userId:guid}/sessions/{sessionId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> RevokeSession(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _team.RevokeSessionAsync(userId, sessionId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToErrorResult();
    }

    [HttpGet("security")]
    public async Task<IActionResult> Security(CancellationToken cancellationToken) =>
        Ok(await _team.GetSecurityAsync(cancellationToken));

    [HttpPut("security")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> UpdateSecurity(UpdateStaffSecuritySettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _team.UpdateSecurityAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }
}

/// <summary>
/// The audit trail, read back: <c>/api/admin/audit</c>. SuperAdmins only.
/// </summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = Policies.ManageStaff)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminAuditController : ControllerBase
{
    /// <summary>The most rows one export returns. A wider need is a database query, not a spreadsheet.</summary>
    public const int ExportLimit = 50_000;

    private readonly IAuditQueryService _audit;

    public AdminAuditController(IAuditQueryService audit) => _audit = audit;

    [HttpGet]
    public async Task<IActionResult> Query([FromQuery] AuditQuery query, CancellationToken cancellationToken) =>
        Ok(await _audit.QueryAsync(query, cancellationToken));

    /// <summary>The areas, actions and people that appear in the trail — for the filters.</summary>
    [HttpGet("facets")]
    public async Task<IActionResult> Facets(CancellationToken cancellationToken) =>
        Ok(await _audit.GetFacetsAsync(cancellationToken));

    /// <summary>The filtered trail as CSV, newest first, up to 50,000 rows.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        var csv = await _audit.ExportCsvAsync(query, ExportLimit, cancellationToken);

        // With a byte-order mark, so Excel reads Arabic summaries as UTF-8 rather than mojibake.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"share7-audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
