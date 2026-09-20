using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Application.Competency.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Domain.Competency;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Authoring the two things Phase 3 cannot generate: what an examination covers, and what a lesson
/// actually teaches.
/// <para>
/// **Both are human work and neither is seeded.** A blueprint is a claim about a real paper, and a
/// plausible one shipped in a migration would wear the schema's authority without anybody having
/// read a syllabus. A learning target is a claim about what a child can do, and the placeholders
/// the migration minted are lessons wearing targets' clothes — honest as far as they go, and
/// incapable of saying what an examination covers. This controller is where a subject specialist
/// replaces both (§20.5, §6.6).
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/assessment")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminAssessmentController : ControllerBase
{
    private readonly IBlueprintAuthoringService _blueprints;
    private readonly IExamCoverageService _coverage;
    private readonly IExamOutcomeService _outcomes;
    private readonly IAssessmentService _assessments;
    private readonly ITargetAuthoringService _targets;
    private readonly ILanguageService _languages;

    public AdminAssessmentController(
        IBlueprintAuthoringService blueprints,
        IExamCoverageService coverage,
        IExamOutcomeService outcomes,
        IAssessmentService assessments,
        ITargetAuthoringService targets,
        ILanguageService languages)
    {
        _blueprints = blueprints;
        _coverage = coverage;
        _outcomes = outcomes;
        _assessments = assessments;
        _targets = targets;
        _languages = languages;
    }

    // ------------------------------------------------------------------------ blueprints

    /// <summary>
    /// Every blueprint, with the two numbers that decide whether it is usable:
    /// <c>placeholderLines</c> (lines naming a lesson rather than a competency, which block
    /// publication) and <c>unservableLines</c> (lines no item in the bank can satisfy, which is the
    /// orphan a syllabus change produces).
    /// </summary>
    [HttpGet("blueprints")]
    public async Task<IActionResult> Blueprints(
        [FromQuery] Guid? langId, CancellationToken cancellationToken) =>
        Ok(await _blueprints.ListBlueprintsAsync(
            langId ?? await _languages.ResolveCurrentAsync(cancellationToken), cancellationToken));

    [HttpGet("blueprints/{blueprintId:guid}")]
    public async Task<IActionResult> Blueprint(
        Guid blueprintId, [FromQuery] Guid? langId, CancellationToken cancellationToken)
    {
        var language = langId ?? await _languages.ResolveCurrentAsync(cancellationToken);
        var blueprint = await _blueprints.GetBlueprintAsync(blueprintId, language, cancellationToken);

        return blueprint is null ? NotFound() : Ok(blueprint);
    }

    /// <summary>
    /// Writes a blueprint whole. A draft is edited in place; **a published one is never edited**,
    /// and the write creates the next version instead — every coverage figure ever computed named
    /// the version it was computed against, and changing what that version says would retroactively
    /// change what those figures meant.
    /// </summary>
    [HttpPost("blueprints")]
    public async Task<IActionResult> SaveBlueprint(
        [FromBody] SaveBlueprintRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _blueprints.SaveBlueprintAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Publishes a blueprint. **Refused while any line names a lesson placeholder** — a placeholder
    /// cannot say what a paper examines, so publishing would put a completion percentage behind an
    /// exam-readiness claim.
    /// </summary>
    [HttpPost("blueprints/{blueprintId:guid}/publish")]
    public async Task<IActionResult> PublishBlueprint(
        Guid blueprintId, CancellationToken cancellationToken)
    {
        try
        {
            return await _blueprints.PublishBlueprintAsync(blueprintId, cancellationToken)
                ? Ok(new { blueprintId, published = true })
                : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ------------------------------------------------------------------ exam specifications

    [HttpGet("exams")]
    public async Task<IActionResult> Exams(CancellationToken cancellationToken) =>
        Ok(await _coverage.ListAsync(includeUnpublished: true, cancellationToken));

    [HttpPost("exams")]
    public async Task<IActionResult> SaveExam(
        [FromBody] SaveExamSpecificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _blueprints.SaveExamSpecificationAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("exams/{versionId:guid}/publish")]
    public async Task<IActionResult> PublishExam(Guid versionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _blueprints.PublishExamVersionAsync(versionId, cancellationToken)
                ? Ok(new { versionId, published = true })
                : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Builds the worked example: a Share7 benchmark over one subject, chapters as areas.
    /// <para>
    /// Exists so coverage has something real to run against before anybody has authored a
    /// syllabus. Its source note says exactly what it is and its authority is Share7's own — a
    /// structural derivation from the platform's own content is a legitimate benchmark and an
    /// illegitimate national paper, and that difference has to be visible to whoever reads the
    /// report.
    /// </para>
    /// </summary>
    [HttpPost("exams/benchmark")]
    public async Task<IActionResult> GenerateBenchmark(
        [FromBody] GenerateBenchmarkRequest request,
        [FromQuery] Guid? langId,
        CancellationToken cancellationToken)
    {
        var language = langId ?? await _languages.ResolveCurrentAsync(cancellationToken);

        try
        {
            return Ok(await _blueprints.GenerateBenchmarkAsync(request, language, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// How far each examination is from a calibration that would let a predicted outcome render.
    /// The honest answer today is "nowhere near", and saying it with a number attached is better
    /// than saying nothing.
    /// </summary>
    [HttpGet("calibration")]
    public async Task<IActionResult> Calibration(CancellationToken cancellationToken) =>
        Ok(await _outcomes.GetCalibrationStatusAsync(cancellationToken));

    // ----------------------------------------------------------------------------- forms

    /// <summary>
    /// Generates a form from a blueprint. Reports the lines the bank could not fill rather than
    /// quietly producing a shorter paper that no longer matches its own specification.
    /// </summary>
    [HttpPost("assessments/{assessmentId:guid}/forms")]
    public async Task<IActionResult> GenerateForm(
        Guid assessmentId, [FromQuery] Guid blueprintId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _assessments.GenerateFormAsync(assessmentId, blueprintId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // --------------------------------------------------------------------------- targets

    /// <summary>
    /// The lesson placeholders under one node, with how much evidence each carries — so an author
    /// works on the claims that matter rather than alphabetically.
    /// </summary>
    [HttpGet("targets/placeholders")]
    public async Task<IActionResult> Placeholders(
        [FromQuery] Guid nodeId, [FromQuery] Guid? langId, CancellationToken cancellationToken)
    {
        var language = langId ?? await _languages.ResolveCurrentAsync(cancellationToken);
        return Ok(await _targets.GetPlaceholdersAsync(nodeId, language, cancellationToken));
    }

    /// <summary>Authored targets, for picking blueprint lines.</summary>
    [HttpGet("targets")]
    public async Task<IActionResult> Targets(
        [FromQuery] Guid? frameworkId,
        [FromQuery] Guid? langId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var language = langId ?? await _languages.ResolveCurrentAsync(cancellationToken);

        return Ok(await _targets.ListAuthoredAsync(
            frameworkId ?? AssessmentIds.Share7CoreFramework, language, search, cancellationToken));
    }

    /// <summary>
    /// Mints a real learning target over one or more lesson placeholders, moves their content onto
    /// it, and **rebuilds every affected observation from the immutable response log**.
    /// <para>
    /// This is the recompute line paying for itself, and it is the first time it does: a child who
    /// answered forty questions last March has those forty answers re-interpreted against the real
    /// claim they were always about, without anybody replaying a lesson. In a system that stored
    /// progress as current state the operation does not exist, because the evidence it needs was
    /// overwritten when it was recorded (§20.5).
    /// </para>
    /// <para>
    /// The placeholders are deprecated, never deleted — a measurement taken against one last month
    /// was a real measurement and its row still has to resolve.
    /// </para>
    /// </summary>
    [HttpPost("targets/promote")]
    public async Task<IActionResult> Promote(
        [FromBody] PromoteTargetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _targets.PromoteAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Marks an authored target as reviewed by a specialist, or back to unreviewed.</summary>
    [HttpPut("targets/{targetId:guid}/review-state")]
    public async Task<IActionResult> SetReviewState(
        Guid targetId, [FromBody] SetReviewStateRequest request, CancellationToken cancellationToken) =>
        await _targets.SetReviewStateAsync(targetId, request.State, cancellationToken)
            ? Ok(new { targetId, request.State })
            : NotFound();
}

public sealed record SetReviewStateRequest
{
    public TargetReviewState State { get; init; }
}
