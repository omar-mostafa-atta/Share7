using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Authorization;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Measurement.Interfaces;
using Share7.Application.Structure.Interfaces;
using Share7.Domain.Constants;
using Share7.Domain.Measurement;

namespace Share7.API.Controllers;

/// <summary>
/// What children's answers say about the content.
/// <para>
/// **The first surface the evidence layer pays for**, and it pays an admin rather than a learner.
/// Long before the platform can say how good a child is at mathematics, it can say which questions
/// are mis-keyed, which distractors nobody picks, which items are answered too fast to have been
/// read, and which content is mapped to no learning target and therefore cannot be measured at all.
/// Every one of those is actionable today and none needs a psychometric model —
/// <c>Docs/EducationalArchitecture.md</c> §16.4.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/education")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminEducationController : ControllerBase
{
    private readonly IContentQualityService _quality;
    private readonly IObservationProjector _projector;
    private readonly ICurriculumProjector _nodes;
    private readonly ILanguageService _languages;
    private readonly ICurrentUserService _currentUser;

    public AdminEducationController(
        IContentQualityService quality,
        IObservationProjector projector,
        ICurriculumProjector nodes,
        ILanguageService languages,
        ICurrentUserService currentUser)
    {
        _quality = quality;
        _projector = projector;
        _nodes = nodes;
        _languages = languages;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The headline numbers: how much content exists, how much of it has enough answers to say
    /// anything about, how much is unmeasurable, and how far behind the projector is.
    /// </summary>
    [HttpGet("quality/summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Ok(await _quality.GetSummaryAsync(cancellationToken));

    /// <summary>
    /// Items, worst first. Filter by <c>flag</c> to see one problem at a time, or by
    /// <c>nodeId</c> to scope to a lesson.
    /// </summary>
    [HttpGet("quality/items")]
    public async Task<IActionResult> Items(
        [FromQuery] string? flag,
        [FromQuery] Guid? nodeId,
        [FromQuery] Guid? langId,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var language = langId ?? await _languages.ResolveCurrentAsync(cancellationToken);

        return Ok(await _quality.GetItemsAsync(
            language, flag, nodeId, Math.Clamp(take, 1, 200), Math.Max(0, skip), cancellationToken));
    }

    [HttpGet("quality/items/{itemId:guid}")]
    public async Task<IActionResult> Item(
        Guid itemId, [FromQuery] Guid? langId, CancellationToken cancellationToken)
    {
        var language = langId ?? await _languages.ResolveCurrentAsync(cancellationToken);
        var item = await _quality.GetItemAsync(itemId, language, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>
    /// Marks an item as an anchor — administered broadly and on purpose, so measurements taken
    /// apart can later be put on one scale.
    /// <para>
    /// **Cheap now, impossible retroactively.** Deciding in two years that a question should have
    /// been an anchor does not produce the responses that would have made it one.
    /// </para>
    /// </summary>
    [HttpPut("items/{itemId:guid}/anchor")]
    [ClosedAtCutover("Marking a question as an anchor", Now = "Answers")]
    public async Task<IActionResult> SetAnchor(
        Guid itemId, [FromBody] SetAnchorRequest request, CancellationToken cancellationToken)
    {
        var updated = await _quality.SetAnchorAsync(itemId, request.IsAnchor, cancellationToken);
        return updated ? Ok(new { itemId, request.IsAnchor }) : NotFound();
    }

    /// <summary>
    /// Stops every observation of one item version counting, with a stated reason.
    /// <para>
    /// **The responses are not touched.** The child did answer; that is a fact and it stays. What
    /// changes is whether the answer informs a measurement — and the reason is recorded, so the
    /// audit trail explains why the numbers moved. Measurements are rebuilt from what is left the
    /// next time each learner is read.
    /// </para>
    /// </summary>
    [HttpPost("items/{itemVersionId:guid}/exclude-observations")]
    [ClosedAtCutover("Excluding a question's answers from measurement", Now = "Answers")]
    public async Task<IActionResult> ExcludeObservations(
        Guid itemVersionId, [FromBody] ExcludeObservationsRequest request, CancellationToken cancellationToken)
    {
        var affected = await _quality.ExcludeItemObservationsAsync(
            itemVersionId, request.Reason, _currentUser.UserId, request.Note, cancellationToken);

        return Ok(new { itemVersionId, excluded = affected, request.Reason });
    }

    /// <summary>
    /// Folds pending responses into observations and item statistics. Idempotent — running it
    /// twice does the second half of nothing.
    /// </summary>
    [HttpPost("projection/observations")]
    public async Task<IActionResult> ProjectObservations(
        [FromQuery] int max = 5000, CancellationToken cancellationToken = default) =>
        Ok(await _projector.ProjectPendingAsync(Math.Clamp(max, 1, 50000), cancellationToken));

    /// <summary>
    /// Brings the node tree in line with the typed tree. **A repair tool since the engine rebuild**:
    /// every tree edit now writes the node first and the typed row beside it, so this only matters
    /// when somebody changed the typed tables directly. Harmless to run — it changes nothing that
    /// already agrees.
    /// </summary>
    [HttpPost("projection/curriculum")]
    public async Task<IActionResult> ProjectCurriculum(CancellationToken cancellationToken) =>
        Ok(await _nodes.SyncAsync(cancellationToken));
}

public sealed record SetAnchorRequest
{
    public bool IsAnchor { get; init; }
}

public sealed record ExcludeObservationsRequest
{
    public ObservationExclusionReason Reason { get; init; } = ObservationExclusionReason.MisKeyedItem;
    public string? Note { get; init; }
}
