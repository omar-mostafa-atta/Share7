using Microsoft.EntityFrameworkCore;
using Share7.Application.Engine.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Structure;

/// <summary>
/// Reads a curriculum's <see cref="CurriculumShape"/> — its declared levels — for a version, a node
/// or a path, and remembers what it read for the life of the unit of work.
/// <para>
/// Constructed over the caller's own context rather than injected, so the services that need it
/// keep their constructors and a shape is always read inside the same transaction as the change
/// it governs.
/// </para>
/// </summary>
public sealed class CurriculumShapes
{
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<Guid, CurriculumShape?> _byVersion = new();
    private readonly Dictionary<Guid, Guid?> _versionOfNode = new();

    public CurriculumShapes(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// The one version the game serves. **Only this version's nodes are ever copied into the legacy
    /// typed tables**, which is what keeps every other curriculum out of the game while it still
    /// reads them.
    /// </summary>
    public static bool IsServed(Guid versionId) => versionId == EducationIds.EgyptianNationalAsMigrated;

    public async Task<CurriculumShape?> ForVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        if (_byVersion.TryGetValue(versionId, out var cached))
            return cached;

        var version = await _db.CurriculumVersions.AsNoTracking()
            .Where(v => v.Id == versionId)
            .Select(v => new { v.Id, v.CurriculumId })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
            return _byVersion[versionId] = null;

        var kinds = await _db.CurriculumNodeKinds.AsNoTracking()
            .Where(k => k.CurriculumVersionId == versionId)
            .OrderBy(k => k.Depth)
            .ThenBy(k => k.Order)
            .Select(k => new
            {
                k.Id,
                k.KindKey,
                k.Depth,
                k.ParentKindKey,
                k.IsPlayable,
                Names = k.Translations.Select(t => new { t.LangId, t.Name }).ToList()
            })
            .ToListAsync(cancellationToken);

        return _byVersion[versionId] = new CurriculumShape
        {
            VersionId = version.Id,
            CurriculumId = version.CurriculumId,
            IsServed = IsServed(version.Id),
            Levels = kinds
                .Select(k => new CurriculumLevel(
                    k.Id, k.KindKey, k.Depth, k.ParentKindKey, k.IsPlayable,
                    k.Names.Select(n => new NodeTitle(n.LangId, n.Name)).ToList()))
                .ToList()
        };
    }

    public async Task<CurriculumShape?> ForNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        if (!_versionOfNode.TryGetValue(nodeId, out var versionId))
        {
            versionId = await _db.CurriculumNodes.AsNoTracking()
                .Where(n => n.Id == nodeId)
                .Select(n => (Guid?)n.CurriculumVersionId)
                .FirstOrDefaultAsync(cancellationToken);

            _versionOfNode[nodeId] = versionId;
        }

        return versionId is { } id ? await ForVersionAsync(id, cancellationToken) : null;
    }

    /// <summary>
    /// For a materialised path. Its first segment is the top of the tree it belongs to — a grade, or
    /// a declared curriculum's root — which is enough to find the curriculum even when the rest of
    /// the path runs through nodes that exist only in open drafts.
    /// </summary>
    public Task<CurriculumShape?> ForPathAsync(string path, CancellationToken cancellationToken)
    {
        var top = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Guid.TryParse(top, out var id) ? ForNodeAsync(id, cancellationToken) : Task.FromResult<CurriculumShape?>(null);
    }
}
