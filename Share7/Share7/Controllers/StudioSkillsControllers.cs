using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Models;
using Share7.Application.Recovery.Interfaces;
using Share7.Application.Studio.Interfaces;
using Share7.Application.Workspace;
using Share7.Domain.Competency;
using Share7.Domain.Measurement;

namespace Share7.API.Controllers;

// ===========================================================================
// Phase 5: /api/studio/{skills,quality,recovery} and the game's one opt-in read
//
// Nothing on these boards publishes anything. Skills and quality change what a child's answers
// MEAN — which target they are evidence about, and whether they are allowed to be evidence at all
// — and so they take effect at once and leave a row in the permanent record. Recovery rules change
// what a child is SHOWN, so they are drafted, reviewed and released like a lesson's questions, and
// the only thing here that touches them is a read.
// ===========================================================================

/// <summary>Skills: importing the official outcomes, editing them, and saying what measures them.</summary>
[Route("api/studio/skills")]
public class StudioSkillsController : StudioWorkspaceControllerBase
{
    private readonly IStudioSkillsService _skills;

    public StudioSkillsController(IStudioSkillsService skills) => _skills = skills;

    [HttpGet("frameworks")]
    public async Task<IActionResult> Frameworks(CancellationToken cancellationToken) =>
        Ok(await _skills.FrameworksAsync(cancellationToken));

    [HttpPost("frameworks")]
    public Task<IActionResult> CreateFramework(
        [FromBody] CreateFrameworkRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _skills.CreateFrameworkAsync(member, request, cancellationToken)), cancellationToken);

    /// <summary>The blank sheet, with its columns, two worked rows and a page explaining both.</summary>
    [HttpGet("template")]
    public async Task<IActionResult> Template(CancellationToken cancellationToken) =>
        File(await _skills.TemplateAsync(cancellationToken),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "learning-outcomes-template.xlsx");

    /// <summary>
    /// Reads a filled sheet. <c>dryRun</c> reports what would happen and writes nothing — which is
    /// what the board does first, every time, because an import is not a thing to find out about
    /// afterwards.
    /// </summary>
    [HttpPost("frameworks/{frameworkId:guid}/import")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public Task<IActionResult> Import(
        Guid frameworkId, IFormFile? file, [FromQuery] bool dryRun = true, CancellationToken cancellationToken = default) =>
        AsMember(async member =>
        {
            if (RefuseSheet(file) is { } refusal) return refusal;

            // ClosedXML needs a seekable stream; the request body stream is not reliably seekable.
            using var buffer = new MemoryStream();
            await file!.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            return Reply(await _skills.ImportAsync(member, frameworkId, buffer, dryRun, cancellationToken));
        }, cancellationToken);

    [HttpGet("frameworks/{frameworkId:guid}/skills")]
    public Task<IActionResult> Skills(
        Guid frameworkId, [FromQuery] Guid langId, [FromQuery] string? search, [FromQuery] string? reviewState,
        [FromQuery] int take = 100, [FromQuery] int skip = 0, CancellationToken cancellationToken = default) =>
        AsMember(async _ => Ok(await _skills.SkillsAsync(frameworkId, langId, search, reviewState, take, skip, cancellationToken)),
            cancellationToken);

    [HttpGet("{targetId:guid}")]
    public Task<IActionResult> Skill(Guid targetId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async _ => Reply(await _skills.SkillAsync(targetId, langId, cancellationToken)), cancellationToken);

    [HttpPut("{targetId:guid}")]
    public Task<IActionResult> Edit(
        Guid targetId, [FromBody] EditSkillRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _skills.EditSkillAsync(member, targetId, request, cancellationToken)), cancellationToken);

    /// <summary>A subject specialist confirming a claim, or taking that back.</summary>
    [HttpPost("{targetId:guid}/review-state")]
    public Task<IActionResult> ReviewState(
        Guid targetId, [FromBody] SetSkillReviewStateRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _skills.SetReviewStateAsync(member, targetId, request.State, cancellationToken)), cancellationToken);

    [HttpGet("questions/{itemId:guid}")]
    public Task<IActionResult> ItemSkills(Guid itemId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async _ => Reply(await _skills.ItemSkillsAsync(itemId, langId, cancellationToken)), cancellationToken);

    [HttpPut("questions/{itemId:guid}")]
    public Task<IActionResult> MapItem(
        Guid itemId, [FromBody] MapItemRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _skills.MapItemAsync(member, itemId, request, cancellationToken)), cancellationToken);

    /// <summary>How far one subject is from being mapped — the Phase 5 gate, as a number.</summary>
    [HttpGet("subjects/{subjectNodeId:guid}/progress")]
    public Task<IActionResult> SubjectProgress(
        Guid subjectNodeId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async _ => Reply(await _skills.SubjectProgressAsync(subjectNodeId, langId, cancellationToken)), cancellationToken);

    /// <summary>A subject's questions, worst first: mapped to nothing, then on a stand-in, then done.</summary>
    [HttpGet("subjects/{subjectNodeId:guid}/questions")]
    public Task<IActionResult> QuestionsToMap(
        Guid subjectNodeId, [FromQuery] Guid langId, [FromQuery] string? state,
        [FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken cancellationToken = default) =>
        AsMember(async _ => Reply(await _skills.QuestionsToMapAsync(subjectNodeId, langId, state, take, skip, cancellationToken)),
            cancellationToken);

    /// <summary>The stand-ins under one place, heaviest first — most evidence, most questions.</summary>
    [HttpGet("nodes/{nodeId:guid}/stand-ins")]
    public Task<IActionResult> StandIns(Guid nodeId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async _ => Reply(await _skills.StandInsAsync(nodeId, langId, cancellationToken)), cancellationToken);

    /// <summary>
    /// Replaces stand-ins with a real skill — either one already imported, or a new one minted
    /// where the official document has nothing to say. Every answer already given against the
    /// stand-ins is re-read against the real claim.
    /// </summary>
    [HttpPost("promote")]
    public Task<IActionResult> Promote([FromBody] PromoteRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _skills.PromoteAsync(member, request, cancellationToken)), cancellationToken);
}

public sealed record SetSkillReviewStateRequest(TargetReviewState State);

/// <summary>What the answers say about the questions: <c>/api/studio/quality</c>.</summary>
[Route("api/studio/quality")]
public class StudioQualityController : StudioWorkspaceControllerBase
{
    private readonly IStudioQualityService _quality;

    public StudioQualityController(IStudioQualityService quality) => _quality = quality;

    [HttpGet("summary")]
    public Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        AsMember(async _ => Ok(await _quality.SummaryAsync(cancellationToken)), cancellationToken);

    [HttpGet("questions")]
    public Task<IActionResult> Flagged(
        [FromQuery] Guid langId, [FromQuery] string? flag, [FromQuery] Guid? nodeId,
        [FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken cancellationToken = default) =>
        AsMember(async member => Ok(await _quality.FlaggedAsync(member, langId, flag, nodeId, take, skip, cancellationToken)),
            cancellationToken);

    [HttpGet("questions/{itemId:guid}")]
    public Task<IActionResult> Question(Guid itemId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _quality.QuestionAsync(member, itemId, langId, cancellationToken)), cancellationToken);

    /// <summary>A Lead's act, at once, with a reason kept beside it for ever.</summary>
    [HttpPost("questions/{itemId:guid}/anchor")]
    public Task<IActionResult> Anchor(
        Guid itemId, [FromBody] StudioAnchorRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _quality.SetAnchorAsync(member, itemId, request.IsAnchor, request.Reason, cancellationToken)),
            cancellationToken);

    /// <summary>Stops one version's answers counting. The answers themselves are never touched.</summary>
    [HttpPost("versions/{itemVersionId:guid}/exclude")]
    public Task<IActionResult> Exclude(
        Guid itemVersionId, [FromBody] StudioExcludeRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _quality.ExcludeAsync(member, itemVersionId, request.Reason, request.Note, cancellationToken)),
            cancellationToken);
}

/// <summary>Marking an anchor from the Studio, where saying why is not optional.</summary>
public sealed record StudioAnchorRequest(bool IsAnchor, string Reason);

/// <summary>Stopping a version's answers counting, with the note that is kept beside the numbers.</summary>
public sealed record StudioExcludeRequest(ObservationExclusionReason Reason, string Note);

/// <summary>Second chances, as the Studio reads them: <c>/api/studio/recovery</c>.</summary>
[Route("api/studio/recovery")]
public class StudioRecoveryController : StudioWorkspaceControllerBase
{
    private readonly IStudioRecoveryService _recovery;

    public StudioRecoveryController(IStudioRecoveryService recovery) => _recovery = recovery;

    /// <summary>What happens at one place, where it was decided, and what changing it would reach.</summary>
    [HttpGet("nodes/{nodeId:guid}")]
    public Task<IActionResult> AtNode(Guid nodeId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _recovery.AtNodeAsync(member, nodeId, cancellationToken)), cancellationToken);

    /// <summary>Every place a rule has actually been written — what the team has decided anywhere.</summary>
    [HttpGet("written")]
    public Task<IActionResult> Written([FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async member => Ok(await _recovery.WrittenAsync(member, langId, cancellationToken)), cancellationToken);
}

/// <summary>
/// The game's one read: <c>/api/recovery/lessons/{lessonId}</c>.
/// <para>
/// **Opt-in, on purpose.** Nothing the Studio releases changes what the game does until the game
/// asks this and acts on the answer, which is what lets a team write, review and release recovery
/// rules for a whole curriculum without a client release and without a day where half the players
/// are on one set of rules and half on another. A client that never calls this behaves exactly as
/// it does today.
/// </para>
/// <para>
/// It always answers. Where nobody has written a rule, the platform's own defaults come back with
/// <c>isDefault</c> set, because a game that has to handle "no answer" is a game that will handle
/// it differently from the one next to it.
/// </para>
/// </summary>
[ApiController]
[Route("api/recovery")]
[Authorize]
public class RecoveryController : ControllerBase
{
    private readonly IRecoveryRuleReader _recovery;

    public RecoveryController(IRecoveryRuleReader recovery) => _recovery = recovery;

    [HttpGet("lessons/{lessonId:guid}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> ForLesson(Guid lessonId, CancellationToken cancellationToken)
    {
        var rule = await _recovery.InForceAsync(lessonId, cancellationToken);

        return Ok(new
        {
            lessonId,
            afterWrongAnswers = rule.AfterWrongAnswers,
            questionsToServe = rule.QuestionsToServe,
            allowRepeats = rule.AllowRepeats,
            isDefault = rule.RuleId is null,
            fromNodeId = rule.FromNodeId
        });
    }
}

/// <summary>
/// Blueprints, as the team reads them: <c>/api/studio/exams</c>.
/// <para>
/// A blueprint is a claim about what a real paper contains, so nothing here invents one. The team
/// reads what exists, sees whether the bank could serve it, and a Lead either freezes it or builds
/// Share7's own benchmark where nobody has authored a syllabus at all.
/// </para>
/// </summary>
[Route("api/studio/exams")]
public class StudioExamsController : StudioWorkspaceControllerBase
{
    private readonly IStudioExamsService _exams;

    public StudioExamsController(IStudioExamsService exams) => _exams = exams;

    [HttpGet("blueprints")]
    public Task<IActionResult> Blueprints([FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async _ => Ok(await _exams.BlueprintsAsync(langId, cancellationToken)), cancellationToken);

    [HttpGet("blueprints/{blueprintId:guid}")]
    public Task<IActionResult> Blueprint(Guid blueprintId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async _ => Reply(await _exams.BlueprintAsync(blueprintId, langId, cancellationToken)), cancellationToken);

    /// <summary>Share7's own benchmark over one subject. Its source note says what it is.</summary>
    [HttpPost("benchmark")]
    public Task<IActionResult> Benchmark([FromBody] BuildBenchmarkRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _exams.GenerateBenchmarkAsync(
            member, request.SubjectNodeId, request.VersionLabel, request.LangId, cancellationToken)), cancellationToken);

    /// <summary>Freezes a blueprint. Refused while any line still names a stand-in.</summary>
    [HttpPost("blueprints/{blueprintId:guid}/publish")]
    public Task<IActionResult> Publish(Guid blueprintId, [FromQuery] Guid langId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _exams.PublishAsync(member, blueprintId, langId, cancellationToken)), cancellationToken);
}

public sealed record BuildBenchmarkRequest(Guid SubjectNodeId, string? VersionLabel, Guid LangId);
