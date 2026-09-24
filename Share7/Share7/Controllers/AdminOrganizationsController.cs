using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Organizations.Models;
using Share7.Domain.Constants;
using Share7.Domain.Organizations;

namespace Share7.API.Controllers;

/// <summary>
/// Organizations, cohorts, rosters, overlays and guardian links, for the platform console.
///
/// <para><b>This is the superadmin surface, not a school portal.</b> A school's own people reach
/// the same data through the org-scoped endpoints, which resolve every read through
/// <see cref="IEducationalAccessService"/>. The console exists because somebody has to provision
/// the first organization and the first admin, and because support needs to be able to see why a
/// teacher cannot see a class.</para>
///
/// <para><b>Reads here are platform-admin reads and are wide by construction.</b>
/// <c>Docs/EducationalArchitecture.md</c> §9.3 calls that route audited and never routine, which is
/// why the reporting endpoints below pass the platform-admin flag explicitly rather than having it
/// inferred — an unaudited wide read should be hard to write by accident.</para>
/// </summary>
[ApiController]
[Route("api/admin/organizations")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminOrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;
    private readonly ICohortService _cohorts;
    private readonly IGuardianService _guardians;
    private readonly IEducationalReportingService _reporting;
    private readonly ICurriculumOverlayService _overlays;
    private readonly ICurrentUserService _currentUser;
    private readonly ILanguageService _languages;

    public AdminOrganizationsController(
        IOrganizationService organizations,
        ICohortService cohorts,
        IGuardianService guardians,
        IEducationalReportingService reporting,
        ICurriculumOverlayService overlays,
        ICurrentUserService currentUser,
        ILanguageService languages)
    {
        _organizations = organizations;
        _cohorts = cohorts;
        _guardians = guardians;
        _reporting = reporting;
        _overlays = overlays;
        _currentUser = currentUser;
        _languages = languages;
    }

    // ──────────────────────────────────────────────────────── organizations

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? parentOrgId, CancellationToken cancellationToken) =>
        Ok(await _organizations.ListAsync(parentOrgId, cancellationToken));

    [HttpGet("{orgId:guid}")]
    public async Task<IActionResult> Get(Guid orgId, CancellationToken cancellationToken) =>
        await _organizations.GetAsync(orgId, cancellationToken) is { } org
            ? Ok(org)
            : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _organizations.CreateAsync(request, userId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("{orgId:guid}/status")]
    public async Task<IActionResult> SetStatus(
        Guid orgId, [FromBody] SetOrgStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _organizations.SetStatusAsync(orgId, request.Status, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    /// <summary>
    /// The published curriculum versions a cohort can be attached to.
    ///
    /// <para>Here rather than on the curriculum controller because this is the only place that asks
    /// the question, and because a cohort without one enrols nobody — the enrolment is what gives
    /// the organization sight of any work at all.</para>
    /// </summary>
    [HttpGet("curriculum-versions")]
    public async Task<IActionResult> CurriculumVersions(
        [FromServices] Share7.Infrastructure.Persistence.ApplicationDbContext dbContext,
        CancellationToken cancellationToken) =>
        Ok(await dbContext.CurriculumVersions
            .AsNoTracking()
            .OrderByDescending(v => v.PublishedAtUtc)
            .Select(v => new
            {
                id = v.Id,
                name = v.Curriculum!.Name + " — " + v.VersionLabel,
                isPublished = v.PublishedAtUtc != null
            })
            .ToListAsync(cancellationToken));

    // ────────────────────────────────────────────────────────── membership

    [HttpGet("{orgId:guid}/members")]
    public async Task<IActionResult> Members(Guid orgId, CancellationToken cancellationToken) =>
        Ok(await _organizations.ListMembersAsync(orgId, cancellationToken));

    [HttpPost("{orgId:guid}/members")]
    public async Task<IActionResult> Grant(
        Guid orgId, [FromBody] GrantMembershipRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _organizations.GrantAsync(orgId, request, userId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("members/{membershipId:guid}")]
    public async Task<IActionResult> Revoke(Guid membershipId, CancellationToken cancellationToken)
    {
        try
        {
            await _organizations.RevokeAsync(membershipId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    // ────────────────────────────────────────────────────────────── cohorts

    [HttpGet("{orgId:guid}/cohorts")]
    public async Task<IActionResult> Cohorts(
        Guid orgId, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);
        return Ok(await _cohorts.ListAsync(orgId, langId, includeArchived, cancellationToken));
    }

    [HttpPost("cohorts")]
    public async Task<IActionResult> CreateCohort(
        [FromBody] CreateCohortRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _cohorts.CreateAsync(request, langId, userId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("cohorts/{cohortId:guid}/status")]
    public async Task<IActionResult> SetCohortStatus(
        Guid cohortId, [FromBody] SetCohortStatusRequest request, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _cohorts.SetStatusAsync(cohortId, request.Status, langId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("cohorts/{cohortId:guid}/members")]
    public async Task<IActionResult> CohortMembers(
        Guid cohortId, [FromQuery] bool includeLeft, CancellationToken cancellationToken) =>
        Ok(await _cohorts.ListMembersAsync(cohortId, includeLeft, cancellationToken));

    /// <summary>
    /// Adds somebody to a cohort. For a learner this provisions the org-owned enrolment, which is
    /// the act that gives the organization visibility of their work from that point on — not
    /// retroactively, and not of anything they do outside it (§9.2).
    /// </summary>
    [HttpPost("cohorts/{cohortId:guid}/members")]
    public async Task<IActionResult> AddCohortMember(
        Guid cohortId, [FromBody] AddCohortMemberRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _cohorts.AddMemberAsync(cohortId, request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("cohorts/members/{cohortMembershipId:guid}")]
    public async Task<IActionResult> RemoveCohortMember(
        Guid cohortMembershipId, CancellationToken cancellationToken)
    {
        try
        {
            await _cohorts.RemoveMemberAsync(cohortMembershipId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    // ────────────────────────────────────────────────────────── reporting

    /// <summary>
    /// How a cohort stands, target by target. Small cells are suppressed with the reason stated
    /// rather than blanked, so a reader never learns that blank means zero (§19.2).
    /// </summary>
    [HttpGet("cohorts/{cohortId:guid}/report")]
    public async Task<IActionResult> CohortReport(Guid cohortId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _reporting.GetCohortReportAsync(
                cohortId, userId, langId, viewerIsPlatformAdmin: true, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("cohorts/{cohortId:guid}/learners")]
    public async Task<IActionResult> CohortLearners(Guid cohortId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _reporting.GetCohortLearnersAsync(
                cohortId, userId, viewerIsPlatformAdmin: true, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    // ───────────────────────────────────────────────────────── assignments

    [HttpGet("cohorts/{cohortId:guid}/assignments")]
    public async Task<IActionResult> Assignments(Guid cohortId, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);
        return Ok(await _cohorts.ListAssignmentsAsync(cohortId, langId, cancellationToken));
    }

    [HttpPost("assignments")]
    public async Task<IActionResult> CreateAssignment(
        [FromBody] CreateAssignmentRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _cohorts.CreateAssignmentAsync(request, langId, userId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("assignments/{assignmentId:guid}")]
    public async Task<IActionResult> WithdrawAssignment(
        Guid assignmentId, CancellationToken cancellationToken)
    {
        try
        {
            await _cohorts.WithdrawAssignmentAsync(assignmentId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    // ──────────────────────────────────────────────────────────── guardians

    [HttpGet("guardians/learner/{learnerUserId:guid}")]
    public async Task<IActionResult> GuardiansOf(
        Guid learnerUserId, CancellationToken cancellationToken) =>
        Ok(await _guardians.ListForLearnerAsync(learnerUserId, cancellationToken));

    [HttpPost("guardians")]
    public async Task<IActionResult> CreateGuardianLink(
        [FromBody] CreateGuardianLinkRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _guardians.CreateAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    /// <summary>
    /// Confirms the relationship is real. Separate from creation on purpose: anyone can type a
    /// child's user name, and an unverified link grants nothing (§18.3).
    /// </summary>
    [HttpPost("guardians/{linkId:guid}/verify")]
    public async Task<IActionResult> VerifyGuardianLink(
        Guid linkId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _guardians.VerifyAsync(linkId, userId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("guardians/{linkId:guid}/consent")]
    public async Task<IActionResult> SetGuardianConsent(
        Guid linkId, [FromBody] SetConsentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _guardians.SetConsentAsync(linkId, request.Scope, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("guardians/{linkId:guid}")]
    public async Task<IActionResult> RevokeGuardianLink(
        Guid linkId, CancellationToken cancellationToken)
    {
        await _guardians.RevokeAsync(linkId, cancellationToken);
        return NoContent();
    }

    // ───────────────────────────────────────────────────────────── overlays

    [HttpGet("{orgId:guid}/overlays")]
    public async Task<IActionResult> Overlays(Guid orgId, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);
        return Ok(await _overlays.ListAsync(orgId, langId, cancellationToken));
    }

    [HttpPost("overlays")]
    public async Task<IActionResult> CreateOverlay(
        [FromBody] CreateOverlayRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _overlays.CreateAsync(request, langId, userId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("overlays/{overlayId:guid}/edits")]
    public async Task<IActionResult> AddOverlayEdit(
        Guid overlayId, [FromBody] AddOverlayEditRequest request, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _overlays.AddEditAsync(overlayId, request, langId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("overlays/{overlayId:guid}/edits/{editId:guid}")]
    public async Task<IActionResult> RemoveOverlayEdit(
        Guid overlayId, Guid editId, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _overlays.RemoveEditAsync(overlayId, editId, langId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("overlays/{overlayId:guid}/publish")]
    public async Task<IActionResult> PublishOverlay(
        Guid overlayId, CancellationToken cancellationToken)
    {
        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _overlays.PublishAsync(overlayId, langId, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}

public sealed record SetOrgStatusRequest(OrganizationStatus Status);
public sealed record SetCohortStatusRequest(CohortStatus Status);
public sealed record SetConsentRequest(GuardianConsentScope Scope);
