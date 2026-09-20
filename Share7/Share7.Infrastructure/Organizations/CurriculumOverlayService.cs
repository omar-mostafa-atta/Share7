using Microsoft.EntityFrameworkCore;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Organizations.Models;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Organizations;

/// <inheritdoc cref="ICurriculumOverlayService"/>
public class CurriculumOverlayService : ICurriculumOverlayService
{
    private readonly ApplicationDbContext _dbContext;

    public CurriculumOverlayService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // ───────────────────────────────────────────────────────────── reading

    public async Task<IReadOnlyList<OverlayDto>> ListAsync(
        Guid orgId, Guid langId, CancellationToken cancellationToken = default)
    {
        var overlays = await _dbContext.CurriculumOverlays
            .AsNoTracking()
            .Include(o => o.Edits)
            .Where(o => o.OrgId == orgId)
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);

        var result = new List<OverlayDto>(overlays.Count);
        foreach (var overlay in overlays) result.Add(await DescribeAsync(overlay, langId, cancellationToken));

        return result;
    }

    public async Task<OverlayDto?> GetAsync(
        Guid overlayId, Guid langId, CancellationToken cancellationToken = default)
    {
        var overlay = await _dbContext.CurriculumOverlays
            .AsNoTracking()
            .Include(o => o.Edits)
            .FirstOrDefaultAsync(o => o.Id == overlayId, cancellationToken);

        return overlay is null ? null : await DescribeAsync(overlay, langId, cancellationToken);
    }

    private async Task<OverlayDto> DescribeAsync(
        CurriculumOverlay overlay, Guid langId, CancellationToken cancellationToken)
    {
        var nodeIds = overlay.Edits
            .SelectMany(e => e.ReplacementNodeId is { } r ? new[] { e.TargetNodeId, r } : [e.TargetNodeId])
            .Distinct()
            .ToList();

        var titles = await _dbContext.CurriculumNodeTranslations
            .AsNoTracking()
            .Where(t => nodeIds.Contains(t.NodeId) && t.LangId == langId)
            .ToDictionaryAsync(t => t.NodeId, t => t.Title, cancellationToken);

        return new OverlayDto(
            overlay.Id,
            overlay.OverlayKey,
            overlay.OrgId,
            overlay.CurriculumVersionId,
            overlay.Name,
            overlay.Status,
            overlay.SourceNote,
            overlay.CreatedAtUtc,
            overlay.PublishedAtUtc,
            overlay.Edits
                .OrderBy(e => e.ApplyOrder)
                .Select(e => new OverlayEditDto(
                    e.Id,
                    e.Kind,
                    e.TargetNodeId,
                    titles.GetValueOrDefault(e.TargetNodeId),
                    e.ReplacementNodeId,
                    e.ReplacementNodeId is { } r ? titles.GetValueOrDefault(r) : null,
                    e.NewOrder,
                    e.ScheduledFromUtc,
                    e.ScheduledToUtc,
                    e.ApplyOrder,
                    e.Note))
                .ToList());
    }

    // ───────────────────────────────────────────────────────────── writing

    public async Task<OverlayDto> CreateAsync(
        CreateOverlayRequest request, Guid langId, Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var key = (request.OverlayKey ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("An overlay needs a key.");

        if (await _dbContext.CurriculumOverlays.AnyAsync(o => o.OverlayKey == key, cancellationToken))
            throw new InvalidOperationException($"Overlay key '{key}' is already taken.");

        if (!await _dbContext.Organizations.AnyAsync(o => o.Id == request.OrgId, cancellationToken))
            throw new InvalidOperationException("Organization not found.");

        if (!await _dbContext.CurriculumVersions.AnyAsync(
                v => v.Id == request.CurriculumVersionId, cancellationToken))
        {
            throw new InvalidOperationException("Curriculum version not found.");
        }

        var overlay = new CurriculumOverlay
        {
            Id = Guid.NewGuid(),
            OverlayKey = key,
            OrgId = request.OrgId,
            CurriculumVersionId = request.CurriculumVersionId,
            Name = request.Name,
            Status = OverlayStatus.Draft,
            SourceNote = request.SourceNote,
            CreatedByUserId = actingUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.CurriculumOverlays.Add(overlay);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(overlay, langId, cancellationToken);
    }

    public async Task<OverlayDto> AddEditAsync(
        Guid overlayId, AddOverlayEditRequest request, Guid langId,
        CancellationToken cancellationToken = default)
    {
        var overlay = await Load(overlayId, cancellationToken);

        // A published overlay is what a cohort reads. Editing one in place would change what a
        // class was shown last week without anything recording that it had happened — the same
        // rule a published blueprint follows.
        if (overlay.Status == OverlayStatus.Published)
            throw new InvalidOperationException("A published overlay cannot be edited; withdraw it first.");

        var target = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == request.TargetNodeId, cancellationToken)
            ?? throw new InvalidOperationException("The target node does not exist.");

        if (target.CurriculumVersionId != overlay.CurriculumVersionId)
            throw new InvalidOperationException("The target node belongs to a different curriculum version.");

        var needsReplacement = request.Kind is OverlayEditKind.Insert or OverlayEditKind.Substitute;

        if (needsReplacement && request.ReplacementNodeId is null)
            throw new InvalidOperationException($"{request.Kind} needs a replacement node.");

        if (!needsReplacement && request.ReplacementNodeId is not null)
            throw new InvalidOperationException($"{request.Kind} takes no replacement node.");

        if (request.Kind == OverlayEditKind.Reorder && request.NewOrder is null)
            throw new InvalidOperationException("Reorder needs a position.");

        var edit = new CurriculumOverlayEdit
        {
            Id = Guid.NewGuid(),
            OverlayId = overlayId,
            Kind = request.Kind,
            TargetNodeId = request.TargetNodeId,
            ReplacementNodeId = request.ReplacementNodeId,
            NewOrder = request.NewOrder,
            ScheduledFromUtc = request.ScheduledFromUtc,
            ScheduledToUtc = request.ScheduledToUtc,
            ApplyOrder = overlay.Edits.Count == 0 ? 1 : overlay.Edits.Max(e => e.ApplyOrder) + 1,
            Note = request.Note
        };

        _dbContext.CurriculumOverlayEdits.Add(edit);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetAsync(overlayId, langId, cancellationToken))!;
    }

    public async Task<OverlayDto> RemoveEditAsync(
        Guid overlayId, Guid editId, Guid langId, CancellationToken cancellationToken = default)
    {
        var overlay = await Load(overlayId, cancellationToken);

        if (overlay.Status == OverlayStatus.Published)
            throw new InvalidOperationException("A published overlay cannot be edited; withdraw it first.");

        var edit = overlay.Edits.FirstOrDefault(e => e.Id == editId);
        if (edit is not null) _dbContext.CurriculumOverlayEdits.Remove(edit);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetAsync(overlayId, langId, cancellationToken))!;
    }

    public async Task<OverlayDto> PublishAsync(
        Guid overlayId, Guid langId, CancellationToken cancellationToken = default)
    {
        var overlay = await Load(overlayId, cancellationToken);

        if (overlay.Edits.Count == 0)
            throw new InvalidOperationException("An overlay with no edits changes nothing.");

        // A substitution is the one edit that can put content under somebody else's heading, so the
        // replacement has to be the organization's own. Refused at publish rather than warned
        // about, because a published overlay is what a class actually sees.
        var substitutions = overlay.Edits
            .Where(e => e.Kind is OverlayEditKind.Insert or OverlayEditKind.Substitute
                        && e.ReplacementNodeId is not null)
            .Select(e => e.ReplacementNodeId!.Value)
            .Distinct()
            .ToList();

        if (substitutions.Count > 0)
        {
            var known = await _dbContext.CurriculumNodes
                .AsNoTracking()
                .Where(n => substitutions.Contains(n.Id))
                .Select(n => n.Id)
                .ToListAsync(cancellationToken);

            var missing = substitutions.Except(known).ToList();

            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"{missing.Count} replacement node(s) do not exist.");
        }

        overlay.Status = OverlayStatus.Published;
        overlay.PublishedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetAsync(overlayId, langId, cancellationToken))!;
    }

    // ─────────────────────────────────────────────────────────── resolution

    public async Task<IReadOnlyList<ResolvedNodeDto>> ResolveChildrenAsync(
        Guid cohortId, Guid parentNodeId, Guid langId, CancellationToken cancellationToken = default)
    {
        var overlayId = await _dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Select(c => c.OverlayId)
            .FirstOrDefaultAsync(cancellationToken);

        var official = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.ParentNodeId == parentNodeId && n.RetiredAtUtc == null)
            .OrderBy(n => n.Order)
            .Select(n => new { n.Id, n.KindKey, n.Order, n.IsPlayable })
            .ToListAsync(cancellationToken);

        var edits = overlayId is { } id
            ? await _dbContext.CurriculumOverlayEdits
                .AsNoTracking()
                .Where(e => e.OverlayId == id)
                .OrderBy(e => e.ApplyOrder)
                .ToListAsync(cancellationToken)
            : [];

        var rows = official.Select(n => new ResolvedNodeDto(
            n.Id, n.KindKey, string.Empty, n.Order, n.IsPlayable, false, null, null, null)).ToList();

        // Applied in ApplyOrder, in one pass. Two edits touching the same node have to resolve
        // predictably, and "last one wins" is only predictable if the order is stored.
        foreach (var edit in edits)
        {
            var index = rows.FindIndex(r => r.NodeId == edit.TargetNodeId);

            switch (edit.Kind)
            {
                case OverlayEditKind.Hide when index >= 0:
                    rows.RemoveAt(index);
                    break;

                case OverlayEditKind.Reorder when index >= 0 && edit.NewOrder is { } order:
                    rows[index] = rows[index] with { Order = order, IsOverlaid = true };
                    break;

                case OverlayEditKind.Substitute when index >= 0 && edit.ReplacementNodeId is { } replacement:
                    rows[index] = rows[index] with
                    {
                        NodeId = replacement,
                        IsOverlaid = true,
                        SubstitutesNodeId = edit.TargetNodeId
                    };
                    break;

                case OverlayEditKind.Insert when edit.ReplacementNodeId is { } inserted:
                    // The target of an insert is the parent, so it only applies to this listing
                    // when that parent is the one being read.
                    if (edit.TargetNodeId != parentNodeId) break;

                    rows.Add(new ResolvedNodeDto(
                        inserted, string.Empty, string.Empty,
                        edit.NewOrder ?? rows.Count + 1, true, true, null,
                        edit.ScheduledFromUtc, edit.ScheduledToUtc));
                    break;

                case OverlayEditKind.Repace when index >= 0:
                    // Changes when, never what. The tree is identical; only the dates move.
                    rows[index] = rows[index] with
                    {
                        IsOverlaid = true,
                        ScheduledFromUtc = edit.ScheduledFromUtc,
                        ScheduledToUtc = edit.ScheduledToUtc
                    };
                    break;
            }
        }

        var ids = rows.Select(r => r.NodeId).ToList();

        var titles = await _dbContext.CurriculumNodeTranslations
            .AsNoTracking()
            .Where(t => ids.Contains(t.NodeId) && t.LangId == langId)
            .ToDictionaryAsync(t => t.NodeId, t => t.Title, cancellationToken);

        var kinds = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.KindKey, cancellationToken);

        return rows
            .Select(r => r with
            {
                Title = titles.GetValueOrDefault(r.NodeId) ?? string.Empty,
                KindKey = string.IsNullOrEmpty(r.KindKey) ? kinds.GetValueOrDefault(r.NodeId) ?? string.Empty : r.KindKey
            })
            .OrderBy(r => r.Order)
            .ToList();
    }

    private async Task<CurriculumOverlay> Load(Guid overlayId, CancellationToken cancellationToken) =>
        await _dbContext.CurriculumOverlays
            .Include(o => o.Edits)
            .FirstOrDefaultAsync(o => o.Id == overlayId, cancellationToken)
        ?? throw new InvalidOperationException("Overlay not found.");
}
