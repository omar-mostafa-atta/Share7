using Microsoft.EntityFrameworkCore;
using Share7.Application.Structure.Interfaces;
using Share7.Domain.Constants;
using Share7.Domain.Structure;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Persistence.Configurations;

namespace Share7.Infrastructure.Structure;

/// <inheritdoc cref="ICurriculumProjector"/>
public class CurriculumProjector : ICurriculumProjector
{
    private readonly ApplicationDbContext _dbContext;

    public CurriculumProjector(ApplicationDbContext dbContext) => _dbContext = dbContext;

    /// <summary>One row of the flattened legacy tree, before it becomes a node.</summary>
    private readonly record struct Source(
        Guid Id, Guid? ParentId, string KindKey, int Order, int Depth, string LegacySource);

    public async Task<CurriculumProjectionReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        var versionId = EducationIds.EgyptianNationalAsMigrated;
        var now = DateTime.UtcNow;

        var sources = await ReadLegacyTreeAsync(cancellationToken);
        var paths = BuildPaths(sources);

        var existing = await _dbContext.CurriculumNodes
            .Where(n => n.CurriculumVersionId == versionId)
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        var kindIds = CurriculumNodeKindConfiguration.SeededKindIds;
        var added = 0;
        var updated = 0;
        var restored = 0;

        foreach (var source in sources)
        {
            var path = paths[source.Id];

            if (!existing.TryGetValue(source.Id, out var node))
            {
                _dbContext.CurriculumNodes.Add(new CurriculumNode
                {
                    Id = source.Id,
                    CurriculumVersionId = versionId,
                    ParentNodeId = source.ParentId,
                    NodeKindId = kindIds[source.KindKey],
                    KindKey = source.KindKey,
                    Order = source.Order,
                    Depth = source.Depth,
                    Path = path,
                    IsPlayable = source.KindKey == "lesson",
                    LegacySource = source.LegacySource,
                    CreatedAtUtc = now
                });
                added++;
                continue;
            }

            var changed = node.ParentNodeId != source.ParentId
                || node.Order != source.Order
                || node.Depth != source.Depth
                || node.Path != path
                || node.KindKey != source.KindKey;

            if (changed)
            {
                node.ParentNodeId = source.ParentId;
                node.NodeKindId = kindIds[source.KindKey];
                node.KindKey = source.KindKey;
                node.Order = source.Order;
                node.Depth = source.Depth;
                node.Path = path;
                node.IsPlayable = source.KindKey == "lesson";
                updated++;
            }

            // A node whose legacy row came back — an admin re-adding a chapter they deleted. The
            // node keeps its id, so the evidence collected under it reattaches rather than being
            // stranded under a retired position.
            if (node.RetiredAtUtc is not null)
            {
                node.RetiredAtUtc = null;
                restored++;
            }
        }

        var live = sources.Select(s => s.Id).ToHashSet();
        var retired = 0;

        foreach (var (id, node) in existing)
        {
            if (live.Contains(id) || node.RetiredAtUtc is not null) continue;

            // **Retired, never deleted.** Responses, item mappings and target mappings name this
            // id; removing the row would strand all of them and destroy the only record of what a
            // child was studying when they studied it.
            node.RetiredAtUtc = now;
            retired++;
        }

        var translations = await SyncTranslationsAsync(live, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CurriculumProjectionReport
        {
            Added = added,
            Updated = updated,
            Retired = retired,
            Restored = restored,
            TranslationsWritten = translations,
            Total = sources.Count
        };
    }

    private async Task<List<Source>> ReadLegacyTreeAsync(CancellationToken cancellationToken)
    {
        var grades = await _dbContext.Grades.AsNoTracking()
            .Select(g => new { g.Id, g.Order }).ToListAsync(cancellationToken);
        var terms = await _dbContext.Terms.AsNoTracking()
            .Select(t => new { t.Id, t.GradeId, t.Order }).ToListAsync(cancellationToken);
        var subjects = await _dbContext.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.TermId, s.Order }).ToListAsync(cancellationToken);
        var chapters = await _dbContext.Chapters.AsNoTracking()
            .Select(c => new { c.Id, c.SubjectId, c.Order }).ToListAsync(cancellationToken);
        var lessons = await _dbContext.Lessons.AsNoTracking()
            .Select(l => new { l.Id, l.ChapterId, l.Order }).ToListAsync(cancellationToken);

        var sources = new List<Source>(
            grades.Count + terms.Count + subjects.Count + chapters.Count + lessons.Count);

        sources.AddRange(grades.Select(g => new Source(g.Id, null, "grade", g.Order, 0, "Grades")));
        sources.AddRange(terms.Select(t => new Source(t.Id, t.GradeId, "term", t.Order, 1, "Terms")));
        sources.AddRange(subjects.Select(s => new Source(s.Id, s.TermId, "subject", s.Order, 2, "Subjects")));
        sources.AddRange(chapters.Select(c => new Source(c.Id, c.SubjectId, "chapter", c.Order, 3, "Chapters")));
        sources.AddRange(lessons.Select(l => new Source(l.Id, l.ChapterId, "lesson", l.Order, 4, "Lessons")));

        return sources;
    }

    /// <summary>
    /// Materialises each node's ancestry, so "everything under this subject" is one indexed prefix
    /// scan rather than a recursive query per read. Built shallow-first, so a parent's path is
    /// always known before its children need it.
    /// </summary>
    private static Dictionary<Guid, string> BuildPaths(List<Source> sources)
    {
        var paths = new Dictionary<Guid, string>(sources.Count);

        foreach (var source in sources.OrderBy(s => s.Depth))
        {
            var parent = source.ParentId is { } id && paths.TryGetValue(id, out var above) ? above : string.Empty;
            paths[source.Id] = $"{parent}/{source.Id:D}";
        }

        return paths;
    }

    /// <summary>
    /// Gathers titles from the five legacy translation tables into one.
    /// <para>
    /// Duplicated deliberately: the point of a generic node is that a reader never has to know
    /// which of five tables a title lives in, and a five-way union on every tree read would put
    /// that knowledge back into every query instead of into this one method.
    /// </para>
    /// </summary>
    private async Task<int> SyncTranslationsAsync(
        HashSet<Guid> live, CancellationToken cancellationToken)
    {
        var incoming = new List<(Guid NodeId, Guid LangId, string Title)>();

        incoming.AddRange((await _dbContext.GradeTranslations.AsNoTracking()
            .Select(t => new { NodeId = t.GradeId, t.LangId, t.Name }).ToListAsync(cancellationToken))
            .Select(t => (t.NodeId, t.LangId, t.Name)));
        incoming.AddRange((await _dbContext.TermTranslations.AsNoTracking()
            .Select(t => new { NodeId = t.TermId, t.LangId, t.Name }).ToListAsync(cancellationToken))
            .Select(t => (t.NodeId, t.LangId, t.Name)));
        incoming.AddRange((await _dbContext.SubjectTranslations.AsNoTracking()
            .Select(t => new { NodeId = t.SubjectId, t.LangId, t.Name }).ToListAsync(cancellationToken))
            .Select(t => (t.NodeId, t.LangId, t.Name)));
        incoming.AddRange((await _dbContext.ChapterTranslations.AsNoTracking()
            .Select(t => new { NodeId = t.ChapterId, t.LangId, t.Name }).ToListAsync(cancellationToken))
            .Select(t => (t.NodeId, t.LangId, t.Name)));
        incoming.AddRange((await _dbContext.LessonTranslations.AsNoTracking()
            .Select(t => new { NodeId = t.LessonId, t.LangId, t.Name }).ToListAsync(cancellationToken))
            .Select(t => (t.NodeId, t.LangId, t.Name)));

        var existing = await _dbContext.CurriculumNodeTranslations
            .ToDictionaryAsync(t => (t.NodeId, t.LangId), cancellationToken);

        var written = 0;

        foreach (var (nodeId, langId, title) in incoming)
        {
            if (!live.Contains(nodeId)) continue;

            if (existing.TryGetValue((nodeId, langId), out var row))
            {
                if (row.Title == title) continue;
                row.Title = title;
            }
            else
            {
                _dbContext.CurriculumNodeTranslations.Add(new CurriculumNodeTranslation
                {
                    NodeId = nodeId,
                    LangId = langId,
                    Title = title
                });
            }

            written++;
        }

        return written;
    }
}
