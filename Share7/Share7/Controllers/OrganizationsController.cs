using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Organizations.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// What the caller can see because of who they are to somebody: their organizations, their
/// cohorts, the children they are responsible for, and the work set for them.
///
/// <para><b>Every read here resolves through <see cref="IEducationalAccessService"/>, and none of
/// them takes a role claim as an answer.</b> A global "Teacher" role says nothing about
/// <i>whose</i> children a person may look at, which is the whole reason org membership exists
/// beside it (<c>Docs/EducationalArchitecture.md</c> §9.1, §9.3).</para>
///
/// <para><b>This is not a portal.</b> The teacher, parent and school portals are their own surfaces
/// and are not built here. What this is, is the API those portals will read — deliberately
/// finished and testable before any of them exists, because the boundary is where the child-safety
/// rules live and a portal built against a half-specified boundary bakes its own interpretation of
/// them.</para>
/// </summary>
[ApiController]
[Route("api/orgs")]
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;
    private readonly ICohortService _cohorts;
    private readonly IGuardianService _guardians;
    private readonly IEducationalReportingService _reporting;
    private readonly IEducationalAccessService _access;
    private readonly ICurriculumOverlayService _overlays;
    private readonly ICurrentUserService _currentUser;
    private readonly ILanguageService _languages;

    public OrganizationsController(
        IOrganizationService organizations,
        ICohortService cohorts,
        IGuardianService guardians,
        IEducationalReportingService reporting,
        IEducationalAccessService access,
        ICurriculumOverlayService overlays,
        ICurrentUserService currentUser,
        ILanguageService languages)
    {
        _organizations = organizations;
        _cohorts = cohorts;
        _guardians = guardians;
        _reporting = reporting;
        _access = access;
        _overlays = overlays;
        _currentUser = currentUser;
        _languages = languages;
    }

    /// <summary>The organizations the caller belongs to, and in what role.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return Ok(await _organizations.ListForUserAsync(userId, cancellationToken));
    }

    /// <summary>Every cohort the caller is in, as learner or as teacher.</summary>
    [HttpGet("cohorts/mine")]
    public async Task<IActionResult> MyCohorts(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);
        return Ok(await _cohorts.ListForUserAsync(userId, langId, cancellationToken));
    }

    /// <summary>
    /// The open work set for the caller, across every cohort they are in, soonest deadline first.
    /// Empty for a learner in no cohort, which is every B2C learner.
    /// </summary>
    [HttpGet("assignments/mine")]
    public async Task<IActionResult> MyAssignments(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);
        return Ok(await _cohorts.ListForLearnerAsync(userId, langId, cancellationToken));
    }

    /// <summary>
    /// The cohort's roster, when the caller teaches or administers it.
    /// Returns an empty list rather than a 403 when they do not: being unrelated to a class is the
    /// normal case, not an error.
    /// </summary>
    [HttpGet("cohorts/{cohortId:guid}/learners")]
    public async Task<IActionResult> CohortLearners(Guid cohortId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _reporting.GetCohortLearnersAsync(cohortId, userId, false, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    /// <summary>
    /// How the cohort stands, target by target.
    ///
    /// <para><b>Computed over the evidence this organization owns, not over the learners' stored
    /// verdicts.</b> A verdict is computed across a child's whole history — personal play, a
    /// previous school — and reporting those to a school would hand it a summary of work it has no
    /// right to. Small cells are suppressed with the reason stated (§9.2, §19.2).</para>
    /// </summary>
    [HttpGet("cohorts/{cohortId:guid}/report")]
    public async Task<IActionResult> CohortReport(Guid cohortId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _reporting.GetCohortReportAsync(cohortId, userId, langId, false, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    /// <summary>The children the caller may see at all, through every relationship they hold.</summary>
    [HttpGet("learners/visible")]
    public async Task<IActionResult> VisibleLearners(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return Ok(await _access.ListVisibleLearnersAsync(userId, cancellationToken));
    }

    /// <summary>
    /// What relationship, if any, the caller has to one learner, and what it lets them see.
    ///
    /// <para>Exposed because a portal has to be able to render "you cannot see this" without
    /// guessing, and because a support conversation about "why can't this teacher see that class"
    /// should have one authoritative answer.</para>
    /// </summary>
    [HttpGet("learners/{learnerUserId:guid}/access")]
    public async Task<IActionResult> AccessTo(Guid learnerUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return Ok(await _access.ResolveAsync(userId, learnerUserId, false, cancellationToken));
    }

    // ──────────────────────────────────────────────────────────── guardian

    /// <summary>The learners the caller is a verified guardian of.</summary>
    [HttpGet("guardian/links")]
    public async Task<IActionResult> MyWards(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return Ok(await _guardians.ListForGuardianAsync(userId, cancellationToken));
    }

    /// <summary>
    /// What a guardian may read about one child: strengths, gaps, and how much evidence stands
    /// behind each, in plain language.
    ///
    /// <para><b>There is no overall percentage in this response and no field one could be written
    /// into.</b> "Ahmed is 72% at mathematics" is uninterpretable and will be read as a school
    /// grade; "has shown he can do 8 of the 11 things this term covers, and fractions with unlike
    /// denominators is the gap" is actionable and true (§18.2).</para>
    ///
    /// <para>404 when the caller is not this child's verified guardian — which is the same answer a
    /// stranger gets, deliberately: "you may not" and "there is nothing" must be distinguishable,
    /// and the existence of a child's record is not something to leak by status code.</para>
    /// </summary>
    [HttpGet("guardian/learners/{learnerUserId:guid}/report")]
    public async Task<IActionResult> GuardianReport(
        Guid learnerUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        var report = await _reporting.GetGuardianReportAsync(
            learnerUserId, userId, langId, false, cancellationToken);

        return report is null ? NotFound() : Ok(report);
    }

    // ───────────────────────────────────────────────────────────── overlay

    /// <summary>
    /// The children of one node as a cohort sees them, with its organization's overlay applied.
    /// Identical to the official tree for a cohort with no overlay, so a client has one code path.
    /// </summary>
    [HttpGet("cohorts/{cohortId:guid}/tree/{parentNodeId:guid}")]
    public async Task<IActionResult> ResolvedTree(
        Guid cohortId, Guid parentNodeId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        return Ok(await _overlays.ResolveChildrenAsync(cohortId, parentNodeId, langId, cancellationToken));
    }
}
