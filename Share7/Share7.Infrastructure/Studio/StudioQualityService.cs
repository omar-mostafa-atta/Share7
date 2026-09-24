using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Measurement.Interfaces;
using Share7.Application.Measurement.Models;
using Share7.Application.Studio.Interfaces;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Measurement;
using Share7.Domain.Content;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Workspace;

namespace Share7.Infrastructure.Studio;

/// <inheritdoc cref="IStudioQualityService"/>
public sealed class StudioQualityService : IStudioQualityService
{
    private readonly ApplicationDbContext _db;
    private readonly IContentQualityService _quality;
    private readonly NodeTrails _trails;
    private readonly StudioScope _scope;
    private readonly IAuditLog _audit;

    public StudioQualityService(
        ApplicationDbContext db, IContentQualityService quality, NodeTrails trails, StudioScope scope, IAuditLog audit)
    {
        _db = db;
        _quality = quality;
        _trails = trails;
        _scope = scope;
        _audit = audit;
    }

    public async Task<StudioQualitySummaryDto> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var floor = ItemStatisticsPopulations.MinimumForReporting;

        // Five counted queries, each one indexed and each one about the team's own questions.
        // The platform-wide summary counts every response and observation in the database; that is
        // the Admin Console's question, it takes half a minute on real data, and putting it at the
        // top of this board would mean the board could not be read until it came back.
        var questions = await _db.Items.CountAsync(i => i.RetiredAtUtc == null, cancellationToken);

        var anchors = await _db.Items.CountAsync(
            i => i.RetiredAtUtc == null && i.IsAnchor, cancellationToken);

        var unmapped = await _db.Items.CountAsync(
            i => i.RetiredAtUtc == null && !_db.ItemTargetMappings.Any(m => m.ItemId == i.Id), cancellationToken);

        var answered = await _db.ItemStatistics.CountAsync(
            s => s.Population == ItemStatisticsPopulations.Global && s.NTotal > 0, cancellationToken);

        var enough = await _db.ItemStatistics.CountAsync(
            s => s.Population == ItemStatisticsPopulations.Global && s.NFirstEncounter >= floor, cancellationToken);

        return new StudioQualitySummaryDto
        {
            Questions = questions,
            Answered = answered,
            EnoughToSay = enough,
            Unmapped = unmapped,
            Anchors = anchors,
            ReportingFloor = floor
        };
    }

    public async Task<IReadOnlyList<FlaggedQuestionDto>> FlaggedAsync(
        StudioMember member, Guid langId, string? flag = null, Guid? nodeId = null,
        int take = 50, int skip = 0, CancellationToken cancellationToken = default)
    {
        var items = await _quality.GetItemsAsync(langId, flag, nodeId, Math.Clamp(take, 1, 200), Math.Max(0, skip), cancellationToken);
        return await DecorateAsync(member, items, cancellationToken);
    }

    public async Task<ServiceResult<FlaggedQuestionDto>> QuestionAsync(
        StudioMember member, Guid itemId, Guid langId, CancellationToken cancellationToken = default)
    {
        var item = await _quality.GetItemAsync(itemId, langId, cancellationToken);
        if (item is null)
            return ServiceResult<FlaggedQuestionDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no question with that id.");

        var decorated = await DecorateAsync(member, [item], cancellationToken);
        return ServiceResult<FlaggedQuestionDto>.Success(decorated[0]);
    }

    /// <summary>
    /// Adds the way to the lesson and whether this member could act on it. Scope is a statement
    /// here rather than a filter: a member seeing a flagged question they cannot fix is how work
    /// gets handed to whoever can, and hiding it would make the board lie about how much is wrong.
    /// </summary>
    private async Task<IReadOnlyList<FlaggedQuestionDto>> DecorateAsync(
        StudioMember member, IReadOnlyList<ItemQualityDto> items, CancellationToken cancellationToken)
    {
        var nodeIds = items.Where(i => i.NodeId is not null).Select(i => i.NodeId!.Value).Distinct().ToList();

        var trails = nodeIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<TrailStepDto>>()
            : await _trails.ForAsync(nodeIds, cancellationToken);

        var paths = nodeIds.Count == 0
            ? []
            : await _db.CurriculumNodes.AsNoTracking()
                .Where(n => nodeIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Path })
                .ToListAsync(cancellationToken);

        var drafts = nodeIds.Count == 0
            ? []
            : await _db.Drafts.AsNoTracking()
                .Where(d => d.NodeId != null && nodeIds.Contains(d.NodeId!.Value)
                            && d.Kind == DraftKind.LessonContent && d.IsOpen && !d.IsPractice)
                .Select(d => new { d.Id, NodeId = d.NodeId!.Value })
                .ToListAsync(cancellationToken);

        return items.Select(item =>
        {
            var path = item.NodeId is { } id ? paths.FirstOrDefault(p => p.Id == id)?.Path : null;

            return new FlaggedQuestionDto
            {
                Quality = item,
                Trail = item.NodeId is { } nodeId && trails.TryGetValue(nodeId, out var trail) ? trail : [],
                OpenDraftId = item.NodeId is { } draftNode
                    ? drafts.FirstOrDefault(d => d.NodeId == draftNode)?.Id
                    : null,
                CanFix = path is not null && member.CoversPath(path)
            };
        }).ToList();
    }

    // =====================================================================================
    // The two acts
    // =====================================================================================

    public async Task<ServiceResult<FlaggedQuestionDto>> SetAnchorAsync(
        StudioMember member, Guid itemId, bool isAnchor, string reason, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return ServiceResult<FlaggedQuestionDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "Only a Lead marks an anchor.");

        var said = (reason ?? string.Empty).Trim();
        if (said.Length is < 10 or > 500)
            return ServiceResult<FlaggedQuestionDto>.Failure(
                WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation,
                "Say why, in a sentence somebody reading this in a year will understand.",
                new Dictionary<string, object?> { ["field"] = "reason" });

        // Being a Lead is not being a Lead of everything. An anchor is the yardstick the whole
        // subject's mastery is read against, and this member may not be the person who decides that
        // for this part of the curriculum — see StudioScope.
        if (!await _scope.CoversItemAsync(member, itemId, cancellationToken))
            return ServiceResult<FlaggedQuestionDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden,
                "This question is outside the part of the curriculum you work in.",
                new Dictionary<string, object?> { ["reason"] = "node" });

        if (!await _quality.SetAnchorAsync(itemId, isAnchor, cancellationToken))
            return ServiceResult<FlaggedQuestionDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no question with that id.");

        _audit.Record(new AuditEntry(
            AuditActions.ItemAnchored, AuditAreas.Workspace,
            isAnchor ? "Made a question an anchor." : "Stopped a question being an anchor.",
            "item", itemId.ToString(),
            new { isAnchor, reason = said }));

        await _db.SaveChangesAsync(cancellationToken);
        return await QuestionAsync(member, itemId, Guid.Empty, cancellationToken);
    }

    public async Task<ServiceResult<ExclusionReportDto>> ExcludeAsync(
        StudioMember member, Guid itemVersionId, ObservationExclusionReason reason, string note,
        CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return ServiceResult<ExclusionReportDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "Only a Lead stops answers counting.");

        var said = (note ?? string.Empty).Trim();
        if (said.Length is < 10 or > 500)
            return ServiceResult<ExclusionReportDto>.Failure(
                WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation,
                "Say why. This changes numbers other people will read, and the reason is kept with them for ever.",
                new Dictionary<string, object?> { ["field"] = "note" });

        var itemId = await _db.ItemVersions.AsNoTracking()
            .Where(v => v.Id == itemVersionId)
            .Select(v => (Guid?)v.ItemId)
            .FirstOrDefaultAsync(cancellationToken);

        if (itemId is null)
            return ServiceResult<ExclusionReportDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no version of a question with that id.");

        // Withdrawing answers changes what the platform says children know. A Lead does it for the
        // curriculum they are responsible for, not for somebody else's.
        if (!await _scope.CoversItemAsync(member, itemId.Value, cancellationToken))
            return ServiceResult<ExclusionReportDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden,
                "This question is outside the part of the curriculum you work in.",
                new Dictionary<string, object?> { ["reason"] = "node" });

        var excluded = await _quality.ExcludeItemObservationsAsync(
            itemVersionId, reason, member.UserId, said, cancellationToken);

        _audit.Record(new AuditEntry(
            AuditActions.ObservationsExcluded, AuditAreas.Workspace,
            $"Stopped {excluded} answers counting towards what children know.",
            "itemVersion", itemVersionId.ToString(),
            new { excluded, reason = reason.ToString(), note = said }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<ExclusionReportDto>.Success(
            new ExclusionReportDto(itemVersionId, excluded, reason.ToString()));
    }
}
