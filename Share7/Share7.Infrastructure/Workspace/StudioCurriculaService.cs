using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Engine;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Staff;
using Share7.Domain.Structure;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Structure;
using CurriculumEntity = Share7.Domain.Structure.Curriculum;

namespace Share7.Infrastructure.Workspace;

/// <inheritdoc cref="IStudioCurriculaService"/>
/// <remarks>
/// A declared curriculum is four kinds of row written together: the curriculum, its first version,
/// a level for its root and one per declared level, and the root node that carries its name. The
/// root is a real node so that everything the Studio already does — trails, scope by path, one
/// parent for every top-level node, locks on a parent's children — works inside it unchanged.
/// <para>
/// **Level keys are positional and can never be an Egyptian key**: the top declared level is
/// <c>level1</c>, the next <c>level2</c>. A curriculum that calls its top level "Grade" still stores
/// it as <c>level1</c>, so no reader that looks for <c>grade</c> nodes can ever find one of its nodes,
/// even a reader that forgot to ask which curriculum it was reading.
/// </para>
/// </remarks>
public sealed class StudioCurriculaService : IStudioCurriculaService
{
    public const int MaxLevels = 8;
    public const int NameMaxLength = 200;
    public const int LevelNameMaxLength = 64;

    private readonly ApplicationDbContext _db;
    private readonly IContentLanguages _languages;
    private readonly IAuditLog _audit;

    public StudioCurriculaService(ApplicationDbContext db, IContentLanguages languages, IAuditLog audit)
    {
        _db = db;
        _languages = languages;
        _audit = audit;
    }

    // =====================================================================================
    // Read
    // =====================================================================================

    public async Task<IReadOnlyList<StudioCurriculumDto>> ListAsync(StudioMember member, CancellationToken cancellationToken = default)
    {
        var ids = await _db.Curricula.AsNoTracking()
            .Where(c => c.Versions.Any())
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var described = new List<StudioCurriculumDto>();
        foreach (var id in ids)
        {
            if (await DescribeAsync(member, id, cancellationToken) is { } one)
                described.Add(one);
        }

        // The one the game serves first: it is where nearly all of the work is.
        return described.OrderByDescending(c => c.IsServed).ToList();
    }

    public async Task<ServiceResult<StudioCurriculumDto>> GetAsync(
        StudioMember member, Guid curriculumId, CancellationToken cancellationToken = default) =>
        await DescribeAsync(member, curriculumId, cancellationToken) is { } one
            ? ServiceResult<StudioCurriculumDto>.Success(one)
            : Missing();

    private async Task<StudioCurriculumDto?> DescribeAsync(StudioMember member, Guid curriculumId, CancellationToken cancellationToken)
    {
        var curriculum = await _db.Curricula.AsNoTracking()
            .Where(c => c.Id == curriculumId)
            .Select(c => new { c.Id, c.CurriculumKey, c.CreatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (curriculum is null) return null;

        // Each curriculum has one version today. The newest is the one being worked on.
        var versionId = await _db.CurriculumVersions.AsNoTracking()
            .Where(v => v.CurriculumId == curriculumId)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (versionId is not { } version) return null;

        var shape = await new CurriculumShapes(_db).ForVersionAsync(version, cancellationToken);
        if (shape is null) return null;

        var live = _db.CurriculumNodes.AsNoTracking().Where(n => n.CurriculumVersionId == version && n.RetiredAtUtc == null);
        var playableCount = await live.CountAsync(n => n.IsPlayable, cancellationToken);

        if (shape.IsServed)
        {
            return new StudioCurriculumDto
            {
                Id = curriculum.Id,
                VersionId = version,
                RootNodeId = null,
                Key = curriculum.CurriculumKey,
                Titles = [],
                IsServed = true,
                FixedLevels = shape.Levels.Count,
                LevelsLocked = true,
                Levels = shape.Levels.Select(Level).ToList(),
                TopCount = await live.CountAsync(n => n.ParentNodeId == null, cancellationToken),
                PlayableCount = playableCount,
                InScope = member.AllNodes || member.NodePaths.Count > 0,
                CanManage = false,
                CreatedAtUtc = curriculum.CreatedAtUtc
            };
        }

        var root = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.CurriculumVersionId == version && n.KindKey == NodeKinds.CurriculumRoot)
            .Select(n => new
            {
                n.Id,
                n.Path,
                Titles = n.Translations.Select(t => new NodeTitle(t.LangId, t.Title)).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (root is null) return null;

        var levels = shape.Levels.Where(l => l.Key != NodeKinds.CurriculumRoot).Select(Level).ToList();
        var fixedLevels = await FixedLevelsAsync(version, root.Path, cancellationToken);

        return new StudioCurriculumDto
        {
            Id = curriculum.Id,
            VersionId = version,
            RootNodeId = root.Id,
            Key = curriculum.CurriculumKey,
            Titles = root.Titles,
            IsServed = false,
            FixedLevels = fixedLevels,
            LevelsLocked = fixedLevels >= levels.Count,
            Levels = levels,
            TopCount = await live.CountAsync(n => n.ParentNodeId == root.Id, cancellationToken),
            PlayableCount = playableCount,
            InScope = member.CoversPath(root.Path),
            CanManage = MayManage(member),
            CreatedAtUtc = curriculum.CreatedAtUtc
        };
    }

    private static StudioLevelDto Level(CurriculumLevel level) => new(level.Key, level.Depth, level.IsPlayable, level.Names);

    /// <summary>
    /// The deepest level anything sits at — a node, live or retired (it can come back), or a new one
    /// proposed in an open draft — and so how many levels from the top are fixed in place. Taking
    /// away or moving a level at or above it would leave something at a level that is no longer
    /// there; the levels below it hold nothing yet, so they are free.
    /// </summary>
    private async Task<int> FixedLevelsAsync(Guid versionId, string rootPath, CancellationToken cancellationToken)
    {
        var deepestNode = await _db.CurriculumNodes
            .Where(n => n.CurriculumVersionId == versionId && n.KindKey != NodeKinds.CurriculumRoot)
            .Select(n => (int?)n.Depth)
            .MaxAsync(cancellationToken) ?? 0;

        var proposed = await _db.Drafts
            .Where(d => d.IsOpen && d.Kind == DraftKind.NewNode && (d.ScopePath == rootPath || d.ScopePath.StartsWith(rootPath + "/")))
            .Select(d => d.NodeKind)
            .ToListAsync(cancellationToken);

        // A proposed node's level is its key: level3 sits at depth 3.
        var deepestProposed = proposed
            .Select(key => key is not null && key.StartsWith("level", StringComparison.Ordinal) && int.TryParse(key[5..], out var depth) ? depth : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(deepestNode, deepestProposed);
    }

    /// <summary>A Lead whose scope is the whole curriculum: the same people who can reach every part of it.</summary>
    private static bool MayManage(StudioMember member) => member.IsAtLeast(StudioRole.Lead) && member.AllNodes;

    // =====================================================================================
    // Create
    // =====================================================================================

    public async Task<ServiceResult<StudioCurriculumDto>> CreateAsync(
        StudioMember member, CreateCurriculumRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<StudioCurriculumDto>("role");
        if (!member.AllNodes)
            return DraftService.OutOfScope<StudioCurriculumDto>("node");

        var languages = await RequiredLanguagesAsync(cancellationToken);
        var problems = new List<object>();

        var titles = CleanTitles(request.Titles, languages, NameMaxLength, "title", problems);
        var levels = CleanLevels(request.Levels, languages, problems);
        problems.AddRange(await NameClashesAsync(titles, null, cancellationToken));

        if (problems.Count > 0)
            return Invalid(problems);

        var now = DateTime.UtcNow;
        var curriculumId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var rootKindId = Guid.NewGuid();
        var rootId = Guid.NewGuid();

        var english = titles.FirstOrDefault(t => languages.First(l => l.Id == t.LangId).Code == "en")?.Title ?? titles[0].Title;

        _db.Curricula.Add(new CurriculumEntity
        {
            Id = curriculumId,
            CurriculumKey = $"studio.{curriculumId:N}",
            Name = english,
            CreatedAtUtc = now
        });

        _db.CurriculumVersions.Add(new CurriculumVersion
        {
            Id = versionId,
            CurriculumId = curriculumId,
            VersionLabel = "1",

            // Its nodes are the only record of its structure there is — no legacy table stands
            // behind them — so this version is its own source of truth from the start.
            IsAuthoritative = true,
            CreatedAtUtc = now
        });

        _db.CurriculumNodeKinds.Add(new CurriculumNodeKind
        {
            Id = rootKindId,
            CurriculumVersionId = versionId,
            KindKey = NodeKinds.CurriculumRoot,
            DisplayName = "Curriculum",
            Depth = 0,
            ParentKindKey = null,
            IsPlayable = false,
            Order = 0
        });

        AddLevels(versionId, levels, languages);

        _db.CurriculumNodes.Add(new CurriculumNode
        {
            Id = rootId,
            CurriculumVersionId = versionId,
            ParentNodeId = null,
            NodeKindId = rootKindId,
            KindKey = NodeKinds.CurriculumRoot,
            Order = 1,
            Depth = 0,
            Path = $"/{rootId:D}",
            IsPlayable = false,
            LegacySource = null,
            CreatedAtUtc = now,
            Revision = 1,
            UpdatedAtUtc = now,
            UpdatedByUserId = member.UserId,
            Translations = titles.Select(t => new CurriculumNodeTranslation { NodeId = rootId, LangId = t.LangId, Title = t.Title }).ToList()
        });

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumCreated,
            AuditAreas.Curriculum,
            $"Declared a new curriculum, '{english}', with {levels.Count} level(s). Not played by the game.",
            "curriculum",
            curriculumId.ToString(),
            new
            {
                versionId,
                rootNodeId = rootId,
                titles = titles.ToDictionary(t => t.LangId, t => t.Title),
                levels = levels.Select(l => l.ToDictionary(n => n.LangId, n => n.Title))
            }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<StudioCurriculumDto>.Success((await DescribeAsync(member, curriculumId, cancellationToken))!);
    }

    // =====================================================================================
    // Update
    // =====================================================================================

    public async Task<ServiceResult<StudioCurriculumDto>> UpdateAsync(
        StudioMember member, Guid curriculumId, UpdateCurriculumRequest request, CancellationToken cancellationToken = default)
    {
        var current = await DescribeAsync(member, curriculumId, cancellationToken);
        if (current is null)
            return Missing();

        if (current.IsServed)
            return ServiceResult<StudioCurriculumDto>.Failure(WorkspaceErrors.CurriculumServed, ServiceErrorKind.Validation,
                "The curriculum the game serves is not changed from here.");

        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<StudioCurriculumDto>("role");
        if (!member.AllNodes)
            return DraftService.OutOfScope<StudioCurriculumDto>("node");

        var languages = await RequiredLanguagesAsync(cancellationToken);
        var problems = new List<object>();

        var titles = CleanTitles(request.Titles, languages, NameMaxLength, "title", problems);
        var levels = request.Levels is null ? null : CleanLevels(request.Levels, languages, problems);
        problems.AddRange(await NameClashesAsync(titles, curriculumId, cancellationToken));

        if (problems.Count > 0)
            return Invalid(problems);

        // Renaming a level is always safe — every node keeps its level, only what it is called
        // changes. The levels above and at the deepest one that holds something keep their places;
        // below it they can change, as long as one stays below — what holds something is never made
        // the played level, and what is played never stops being played.
        var fixedLevels = current.FixedLevels;
        var reshaped = levels is not null && levels.Count != current.Levels.Count;
        if (reshaped && fixedLevels > 0 && (fixedLevels >= current.Levels.Count || levels!.Count <= fixedLevels))
            return ServiceResult<StudioCurriculumDto>.Failure(WorkspaceErrors.CurriculumLocked, ServiceErrorKind.Conflict,
                "Something sits at these levels, so they can be renamed but not taken out, and at least one level has to stay below them.",
                new Dictionary<string, object?> { ["fixedLevels"] = fixedLevels });

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var root = await _db.CurriculumNodes.Include(n => n.Translations).FirstAsync(n => n.Id == current.RootNodeId, cancellationToken);
        var before = new
        {
            titles = root.Translations.ToDictionary(t => t.LangId, t => t.Title),
            levels = current.Levels.Select(l => l.Names.ToDictionary(n => n.LangId, n => n.Title)).ToList()
        };

        foreach (var title in titles)
        {
            var existing = root.Translations.FirstOrDefault(t => t.LangId == title.LangId);
            if (existing is null)
                root.Translations.Add(new CurriculumNodeTranslation { NodeId = root.Id, LangId = title.LangId, Title = title.Title });
            else
                existing.Title = title.Title;
        }

        foreach (var dropped in root.Translations.Where(t => titles.All(n => n.LangId != t.LangId)).ToList())
            root.Translations.Remove(dropped);

        root.Revision += 1;
        root.UpdatedAtUtc = DateTime.UtcNow;
        root.UpdatedByUserId = member.UserId;

        var entity = await _db.Curricula.FirstAsync(c => c.Id == curriculumId, cancellationToken);
        entity.Name = titles.FirstOrDefault(t => languages.First(l => l.Id == t.LangId).Code == "en")?.Title ?? titles[0].Title;

        if (levels is not null)
        {
            var declared = await _db.CurriculumNodeKinds
                .Include(k => k.Translations)
                .Where(k => k.CurriculumVersionId == current.VersionId && k.KindKey != NodeKinds.CurriculumRoot)
                .OrderBy(k => k.Depth)
                .ToListAsync(cancellationToken);

            // The levels that keep their rows: all of them when the count stays, otherwise the fixed
            // ones. Renamed where they stand — the same rows, the same keys, only the words change.
            var kept = levels.Count == declared.Count ? declared.Count : fixedLevels;

            for (var i = 0; i < kept; i++)
            {
                var kind = declared[i];
                kind.DisplayName = NameIn(levels[i], languages, "en");

                foreach (var name in levels[i])
                {
                    var existing = kind.Translations.FirstOrDefault(t => t.LangId == name.LangId);
                    if (existing is null)
                        kind.Translations.Add(new CurriculumNodeKindTranslation { NodeKindId = kind.Id, LangId = name.LangId, Name = name.Title });
                    else
                        existing.Name = name.Title;
                }

                foreach (var dropped in kind.Translations.Where(t => levels[i].All(n => n.LangId != t.LangId)).ToList())
                    kind.Translations.Remove(dropped);
            }

            if (kept < declared.Count)
            {
                // Nothing sits below the kept levels, so those are simply declared again. The old rows
                // go first, in their own statement, so the one-key-per-level index never sees two
                // level3s at once. A kept level is never the played one here: at least one follows it.
                _db.CurriculumNodeKinds.RemoveRange(declared.Skip(kept));
                await _db.SaveChangesAsync(cancellationToken);
                AddLevels(current.VersionId, levels, languages, from: kept);
            }
        }

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumUpdated,
            AuditAreas.Curriculum,
            levels is null
                ? $"Renamed the curriculum '{entity.Name}'."
                : $"Changed the name or levels of the curriculum '{entity.Name}'.",
            "curriculum",
            curriculumId.ToString(),
            new
            {
                before,
                after = new
                {
                    titles = titles.ToDictionary(t => t.LangId, t => t.Title),
                    levels = levels?.Select(l => l.ToDictionary(n => n.LangId, n => n.Title)).ToList()
                }
            }));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult<StudioCurriculumDto>.Success((await DescribeAsync(member, curriculumId, cancellationToken))!);
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    /// <summary>
    /// The declared levels, top first: <c>level1</c> under the root, each under the one before, and
    /// the last one played. The played level is always the deepest, because the game plays leaves —
    /// a level under the played one would hold nodes nothing could ever reach.
    /// </summary>
    private void AddLevels(Guid versionId, List<List<NodeTitle>> levels, IReadOnlyList<ContentLanguage> languages, int from = 0)
    {
        for (var i = from; i < levels.Count; i++)
        {
            var depth = i + 1;
            var id = Guid.NewGuid();

            _db.CurriculumNodeKinds.Add(new CurriculumNodeKind
            {
                Id = id,
                CurriculumVersionId = versionId,
                KindKey = $"level{depth}",
                DisplayName = NameIn(levels[i], languages, "en"),
                Depth = depth,
                ParentKindKey = depth == 1 ? NodeKinds.CurriculumRoot : $"level{depth - 1}",
                IsPlayable = depth == levels.Count,
                Order = depth,
                Translations = levels[i].Select(n => new CurriculumNodeKindTranslation { NodeKindId = id, LangId = n.LangId, Name = n.Title }).ToList()
            });
        }
    }

    private async Task<IReadOnlyList<ContentLanguage>> RequiredLanguagesAsync(CancellationToken cancellationToken) =>
        (await _languages.GetAsync(cancellationToken)).Where(l => l.IsContentLanguage).ToList();

    /// <summary>
    /// Trimmed names, one per language: every language content must be published in is required, the
    /// others optional; unknown languages, blanks, repeats and over-long names are problems.
    /// </summary>
    private static List<NodeTitle> CleanTitles(
        IReadOnlyList<NodeTitle>? given, IReadOnlyList<ContentLanguage> languages, int max, string field, List<object> problems, int? level = null)
    {
        var clean = new List<NodeTitle>();

        foreach (var title in given ?? [])
        {
            var text = (title.Title ?? string.Empty).Trim();

            if (languages.All(l => l.Id != title.LangId))
                problems.Add(new { code = "unknownLanguage", field, level, langId = title.LangId });
            else if (clean.Any(c => c.LangId == title.LangId))
                problems.Add(new { code = "duplicateLanguage", field, level, langId = title.LangId });
            else if (text.Length > max)
                problems.Add(new { code = "tooLong", field, level, langId = title.LangId, max });
            else if (text.Length > 0)
                clean.Add(new NodeTitle(title.LangId, text));
        }

        var requiredMissing = false;
        foreach (var required in languages.Where(l => l.RequiredToPublish))
        {
            if (clean.All(c => c.LangId != required.Id))
            {
                problems.Add(new { code = "missing", field, level, langId = required.Id });
                requiredMissing = true;
            }
        }

        // With no language required to publish, a name in none of them is still no name at all.
        if (clean.Count == 0 && !requiredMissing)
            problems.Add(new { code = "missing", field, level, langId = (Guid?)null });

        return clean;
    }

    private static List<List<NodeTitle>> CleanLevels(
        IReadOnlyList<CurriculumLevelRequest>? given, IReadOnlyList<ContentLanguage> languages, List<object> problems)
    {
        var levels = (given ?? []).ToList();

        if (levels.Count == 0)
            problems.Add(new { code = "noLevels", field = "levels" });
        if (levels.Count > MaxLevels)
            problems.Add(new { code = "tooManyLevels", field = "levels", max = MaxLevels });

        var clean = levels.Select((level, i) => CleanTitles(level.Names, languages, LevelNameMaxLength, "level", problems, i + 1)).ToList();

        // Two levels with one name would make "Add — Unit" ambiguous on every board.
        foreach (var language in languages)
        {
            var repeated = clean
                .Select((names, i) => (Name: names.FirstOrDefault(n => n.LangId == language.Id)?.Title, Level: i + 1))
                .Where(x => x.Name is not null)
                .GroupBy(x => x.Name!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in repeated)
                foreach (var (_, level) in group.Skip(1))
                    problems.Add(new { code = "repeated", field = "level", level, langId = language.Id });
        }

        return clean;
    }

    /// <summary>Two declared curricula with one name, in any language, would be indistinguishable on the board.</summary>
    private async Task<IEnumerable<object>> NameClashesAsync(List<NodeTitle> titles, Guid? self, CancellationToken cancellationToken)
    {
        if (titles.Count == 0) return [];

        var others = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.KindKey == NodeKinds.CurriculumRoot && n.CurriculumVersion!.CurriculumId != self)
            .SelectMany(n => n.Translations.Select(t => new { t.LangId, t.Title }))
            .ToListAsync(cancellationToken);

        return titles
            .Where(t => others.Any(o => o.LangId == t.LangId && string.Equals(o.Title, t.Title, StringComparison.OrdinalIgnoreCase)))
            .Select(t => (object)new { code = "taken", field = "title", langId = t.LangId });
    }

    private static string NameIn(List<NodeTitle> names, IReadOnlyList<ContentLanguage> languages, string code) =>
        names.FirstOrDefault(n => languages.FirstOrDefault(l => l.Id == n.LangId)?.Code == code)?.Title ?? names.First().Title;

    private static ServiceResult<StudioCurriculumDto> Missing() =>
        ServiceResult<StudioCurriculumDto>.Failure(WorkspaceErrors.CurriculumNotFound, ServiceErrorKind.NotFound, "Curriculum not found.");

    private static ServiceResult<StudioCurriculumDto> Invalid(List<object> problems) =>
        ServiceResult<StudioCurriculumDto>.Failure(WorkspaceErrors.CurriculumInvalid, ServiceErrorKind.Validation,
            "The curriculum's name or levels cannot be saved as they are.",
            new Dictionary<string, object?> { ["problems"] = problems });
}
