using Microsoft.EntityFrameworkCore;
using Share7.Application.Staff.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// Turns stored scopes (node ids, language ids) into what people read: "Primary 4 › First term ›
/// Science", in English and Arabic. One query per concern for any number of members, so the team
/// list does not issue a query per row.
/// </summary>
public class StaffScopeReader
{
    /// <summary>The kinds a scope can be set at. Lessons are too fine to hand someone as a remit.</summary>
    public static readonly string[] ScopeKinds = ["grade", "term", "subject", "chapter"];

    private readonly ApplicationDbContext _db;

    public StaffScopeReader(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, StaffScopeDto>> ReadAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, StaffScopeDto>();

        var profiles = await _db.StaffProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.AllNodes, p.AllLanguages })
            .ToListAsync(cancellationToken);

        var nodeRows = await _db.StaffScopeNodes.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .ToListAsync(cancellationToken);

        var languageRows = await _db.StaffScopeLanguages.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .ToListAsync(cancellationToken);

        var nodes = await DescribeNodesAsync(nodeRows.Select(n => n.NodeId).Distinct().ToList(), cancellationToken);
        var languages = await LanguagesAsync(cancellationToken);

        return profiles.ToDictionary(
            p => p.UserId,
            p => new StaffScopeDto(
                p.AllNodes,
                p.AllNodes
                    ? []
                    : nodeRows.Where(n => n.UserId == p.UserId).Select(n => nodes[n.NodeId]).OrderBy(n => n.Trail.Count).ThenBy(n => n.Trail[^1].En).ToList(),
                p.AllLanguages,
                p.AllLanguages
                    ? []
                    : languageRows.Where(l => l.UserId == p.UserId)
                        .Select(l => languages.FirstOrDefault(x => x.Id == l.LanguageId))
                        .OfType<ScopeLanguageDto>()
                        .OrderBy(l => l.Name)
                        .ToList()));
    }

    public async Task<StaffScopeDto> ReadAsync(Guid userId, CancellationToken cancellationToken) =>
        (await ReadAsync([userId], cancellationToken)).TryGetValue(userId, out var scope)
            ? scope
            : new StaffScopeDto(false, [], false, []);

    public async Task<IReadOnlyList<ScopeLanguageDto>> LanguagesAsync(CancellationToken cancellationToken) =>
        await _db.Languages.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new ScopeLanguageDto(l.Id, l.Code, l.Name))
            .ToListAsync(cancellationToken);

    /// <summary>The curriculum down to chapters, for the scope picker.</summary>
    public async Task<IReadOnlyList<ScopeTreeNodeDto>> TreeAsync(CancellationToken cancellationToken)
    {
        var nodes = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.RetiredAtUtc == null && ScopeKinds.Contains(n.KindKey))
            .Select(n => new { n.Id, n.ParentNodeId, n.KindKey, n.Depth, n.Order })
            .ToListAsync(cancellationToken);

        var titles = await TitlesAsync(nodes.Select(n => n.Id).ToList(), cancellationToken);

        return nodes
            .OrderBy(n => n.Depth).ThenBy(n => n.Order)
            .Select(n => new ScopeTreeNodeDto(n.Id, n.ParentNodeId, n.KindKey, TitleFor(titles, n.Id), n.Depth, n.Order))
            .ToList();
    }

    /// <summary>Which of these ids are nodes a scope may name — for validating a scope change.</summary>
    public async Task<HashSet<Guid>> ValidScopeNodeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await _db.CurriculumNodes.AsNoTracking()
            .Where(n => ids.Contains(n.Id) && n.RetiredAtUtc == null && ScopeKinds.Contains(n.KindKey))
            .Select(n => n.Id)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    private async Task<Dictionary<Guid, ScopeNodeDto>> DescribeNodesAsync(IReadOnlyList<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ScopeNodeDto>();
        if (nodeIds.Count == 0)
            return result;

        var found = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => nodeIds.Contains(n.Id))
            .Select(n => new { n.Id, n.KindKey, n.Path, n.RetiredAtUtc })
            .ToListAsync(cancellationToken);

        // Each node's ancestors come from its materialised path, so the whole trail is one query.
        var trailIds = found
            .SelectMany(n => PathIds(n.Path).Append(n.Id))
            .Distinct()
            .ToList();

        var titles = await TitlesAsync(trailIds, cancellationToken);

        foreach (var node in found)
        {
            var trail = PathIds(node.Path).Append(node.Id).Distinct().Select(id => TitleFor(titles, id)).ToList();
            result[node.Id] = new ScopeNodeDto(node.Id, node.KindKey, trail, node.RetiredAtUtc is null);
        }

        foreach (var missing in nodeIds.Where(id => !result.ContainsKey(id)))
            result[missing] = new ScopeNodeDto(missing, "removed", [new LocalizedTitleDto("Removed from the curriculum", "حُذف من المنهج")], false);

        return result;
    }

    private async Task<Dictionary<Guid, (string? En, string? Ar)>> TitlesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await _db.CurriculumNodeTranslations.AsNoTracking()
            .Where(t => ids.Contains(t.NodeId))
            .Select(t => new { t.NodeId, t.LangId, t.Title })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.NodeId)
            .ToDictionary(
                g => g.Key,
                g => (
                    g.FirstOrDefault(r => r.LangId == LanguageIds.English)?.Title,
                    g.FirstOrDefault(r => r.LangId == LanguageIds.Arabic)?.Title));
    }

    private static LocalizedTitleDto TitleFor(Dictionary<Guid, (string? En, string? Ar)> titles, Guid id)
    {
        if (!titles.TryGetValue(id, out var t))
            return new LocalizedTitleDto("Untitled", null);

        // A node with only an Arabic title still has to read as something in the English interface.
        return new LocalizedTitleDto(t.En ?? t.Ar ?? "Untitled", t.Ar);
    }

    private static IEnumerable<Guid> PathIds(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Guid.TryParse(part, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty);
}
