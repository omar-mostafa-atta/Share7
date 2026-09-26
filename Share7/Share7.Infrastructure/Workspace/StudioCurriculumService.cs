using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Structure;

namespace Share7.Infrastructure.Workspace;

/// <inheritdoc cref="IStudioCurriculumService"/>
public sealed class StudioCurriculumService : IStudioCurriculumService
{
    private readonly ApplicationDbContext _db;
    private readonly IContentLanguages _languages;
    private readonly ILessonContentReader _reader;
    private readonly NodeTrails _trails;
    private readonly DraftService _drafts;

    public StudioCurriculumService(
        ApplicationDbContext db, IContentLanguages languages, ILessonContentReader reader, NodeTrails trails, DraftService drafts)
    {
        _db = db;
        _languages = languages;
        _reader = reader;
        _trails = trails;
        _drafts = drafts;
    }

    public Task<IReadOnlyList<ContentLanguage>> LanguagesAsync(CancellationToken cancellationToken = default) =>
        _languages.GetAsync(cancellationToken);

    public async Task<IReadOnlyList<StudioNodeDto>> ChildrenAsync(
        StudioMember member, Guid? parentId, bool includeRetired, CancellationToken cancellationToken = default)
    {
        // No parent means the top of the tree the game serves: the fourteen grades. Every other
        // curriculum is reached through its own root node, and a parent belongs to one curriculum
        // only, so naming it is enough.
        var nodes = parentId is null
            ? _db.CurriculumNodes.AsNoTracking()
                .Where(n => n.CurriculumVersionId == EducationIds.EgyptianNationalAsMigrated && n.ParentNodeId == null)
            : _db.CurriculumNodes.AsNoTracking().Where(n => n.ParentNodeId == parentId);

        if (!includeRetired)
            nodes = nodes.Where(n => n.RetiredAtUtc == null);

        var rows = await nodes.OrderBy(n => n.RetiredAtUtc != null).ThenBy(n => n.Order).ThenBy(n => n.Id)
            .Select(n => n.Id).ToListAsync(cancellationToken);

        return await DescribeAsync(member, rows, cancellationToken);
    }

    public async Task<ServiceResult<StudioNodeDetailDto>> NodeAsync(StudioMember member, Guid nodeId, CancellationToken cancellationToken = default)
    {
        var described = await DescribeAsync(member, [nodeId], cancellationToken);
        if (described.Count == 0)
            return ServiceResult<StudioNodeDetailDto>.Failure(WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "Not found.");

        return ServiceResult<StudioNodeDetailDto>.Success(new StudioNodeDetailDto(
            described[0],
            await _trails.ForAsync(nodeId, cancellationToken),
            await ChildrenAsync(member, nodeId, includeRetired: true, cancellationToken)));
    }

    public async Task<ServiceResult<LessonWorkspaceDto>> LessonAsync(StudioMember member, Guid lessonId, CancellationToken cancellationToken = default)
    {
        var live = await _reader.ReadAsync(lessonId, cancellationToken);
        var described = await DescribeAsync(member, [lessonId], cancellationToken);

        if (live is null || described.Count == 0)
            return ServiceResult<LessonWorkspaceDto>.Failure(WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "Lesson not found.");

        var open = await _db.Drafts.AsNoTracking()
            .Where(d => d.NodeId == lessonId && d.Kind == DraftKind.LessonContent && d.IsOpen && !d.IsPractice)
            .ToListAsync(cancellationToken);

        return ServiceResult<LessonWorkspaceDto>.Success(new LessonWorkspaceDto(
            described[0],
            await _trails.ForAsync(lessonId, cancellationToken),
            live,
            open.Count == 0 ? null : (await _drafts.SummariesAsync(open, cancellationToken))[0]));
    }

    /// <summary>Nodes with everything the Studio's tree colours by: open drafts, missing languages, question counts.</summary>
    private async Task<IReadOnlyList<StudioNodeDto>> DescribeAsync(StudioMember member, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];

        var nodes = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => ids.Contains(n.Id))
            .Select(n => new
            {
                n.Id,
                n.KindKey,
                n.ParentNodeId,
                n.Order,
                n.Revision,
                n.Path,
                n.RetiredAtUtc,
                n.IsPlayable,
                n.CurriculumVersionId,
                n.CurriculumVersion!.CurriculumId,
                Titles = n.Translations.Select(t => new NodeTitle(t.LangId, t.Title)).ToList(),
                Children = _db.CurriculumNodes.Count(c => c.ParentNodeId == n.Id && c.RetiredAtUtc == null)
            })
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        var drafts = await _db.Drafts.AsNoTracking()
            .Where(d => d.IsOpen && !d.IsPractice && d.NodeId != null && ids.Contains(d.NodeId!.Value))
            .Select(d => new { d.NodeId, d.Kind })
            .ToListAsync(cancellationToken);

        // New nodes waiting in drafts show on their parent, so the tree can say "2 new lessons here".
        var incoming = await _db.Drafts.AsNoTracking()
            .Where(d => d.IsOpen && !d.IsPractice && d.Kind == DraftKind.NewNode && d.ParentNodeId != null && ids.Contains(d.ParentNodeId!.Value))
            .Select(d => d.ParentNodeId)
            .ToListAsync(cancellationToken);

        // Where questions are counted depends on whose they are: the game's own table for the
        // curriculum it serves, the node-keyed one for any other.
        var servedIds = nodes.Values.Where(n => n.IsPlayable && CurriculumShapes.IsServed(n.CurriculumVersionId)).Select(n => n.Id).ToList();
        var declaredIds = nodes.Values.Where(n => n.IsPlayable && !CurriculumShapes.IsServed(n.CurriculumVersionId)).Select(n => n.Id).ToList();

        var counts = servedIds.Count == 0
            ? []
            : await _db.Questions.AsNoTracking()
                .Where(q => servedIds.Contains(q.LessonId) && q.IsActive)
                .GroupBy(q => new { q.LessonId, q.LangId })
                .Select(g => new { g.Key.LessonId, g.Key.LangId, Count = g.Count() })
                .ToListAsync(cancellationToken);

        if (declaredIds.Count > 0)
        {
            counts.AddRange(await _db.NodeItemRenderings.AsNoTracking()
                .Where(r => declaredIds.Contains(r.NodeId) && r.IsActive && r.Role == NodeItemRole.Core)
                .GroupBy(r => new { r.NodeId, r.LangId })
                .Select(g => new { LessonId = g.Key.NodeId, g.Key.LangId, Count = g.Count() })
                .ToListAsync(cancellationToken));
        }

        var required = (await _languages.GetAsync(cancellationToken))
            .Where(l => l.IsContentLanguage && l.RequiredToPublish)
            .Select(l => l.Id)
            .ToList();

        return ids.Where(nodes.ContainsKey).Select(id =>
        {
            var n = nodes[id];
            var isLesson = n.IsPlayable;
            var perLanguage = counts.Where(c => c.LessonId == id).ToDictionary(c => c.LangId, c => c.Count);

            return new StudioNodeDto
            {
                Id = n.Id,
                Kind = n.KindKey,
                ParentId = n.ParentNodeId,
                Order = n.Order,
                Revision = n.Revision,
                Titles = n.Titles,
                IsRetired = n.RetiredAtUtc is not null,
                ChildCount = n.Children,
                OpenDrafts = drafts.Where(d => d.NodeId == id).Select(d => d.Kind)
                    .Concat(incoming.Where(p => p == id).Select(_ => DraftKind.NewNode))
                    .ToList(),
                MissingLanguages = isLesson ? required.Where(l => !perLanguage.ContainsKey(l)).ToList() : null,
                QuestionCounts = isLesson ? perLanguage : null,
                InScope = member.CoversPath(n.Path),
                CurriculumId = n.CurriculumId,
                IsPlayable = n.IsPlayable
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<QuestionSearchHitDto>> SearchAsync(
        string text, Guid? underNodeId, Guid? langId, int take, CancellationToken cancellationToken = default)
    {
        var term = text?.Trim() ?? string.Empty;
        if (term.Length < 2) return [];

        var rows = _db.ItemLocalizations.AsNoTracking()
            .Where(q => q.IsActive && (q.Role == NodeItemRole.Core || q.Role == NodeItemRole.Recovery) && q.Text.Contains(term));

        if (langId is { } lang) rows = rows.Where(q => q.LangId == lang);

        if (underNodeId is { } under)
        {
            var path = await _db.CurriculumNodes.AsNoTracking().Where(n => n.Id == under).Select(n => n.Path).FirstOrDefaultAsync(cancellationToken);
            if (path is null) return [];

            var prefix = path + "/";
            rows = rows.Where(q => _db.CurriculumNodes.Any(n => n.Id == q.LessonId && (n.Path == path || n.Path.StartsWith(prefix))));
        }

        var hits = await rows
            .Where(q => _db.CurriculumNodes.Any(n => n.Id == q.LessonId && n.RetiredAtUtc == null))
            .OrderBy(q => q.LessonId).ThenBy(q => q.Role).ThenBy(q => q.RowNumber)
            .Take(Math.Clamp(take, 1, 200))
            .Select(q => new { q.LessonId, ItemId = q.ItemVersion!.ItemId, q.Role, q.RowNumber, q.LangId, q.Id, q.Text })
            .ToListAsync(cancellationToken);

        var trails = await _trails.ForAsync(hits.Select(h => h.LessonId), cancellationToken);

        return hits.Select(h => new QuestionSearchHitDto(
            h.LessonId, trails.TryGetValue(h.LessonId, out var trail) ? trail : [], h.ItemId, h.Role, h.RowNumber, h.LangId, h.Id, h.Text)).ToList();
    }
}
