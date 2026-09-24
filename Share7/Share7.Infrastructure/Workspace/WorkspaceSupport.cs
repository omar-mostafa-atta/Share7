using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Engine.Models;
using Share7.Application.Staff.Models;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Workspace;

/// <summary>
/// How drafts, release entries and anchors are stored — and so what the Studio reads back: camelCase
/// JSON with enums by name, exactly as the API writes every other body. A proposal sent with numbers
/// for its enums is read just the same.
/// </summary>
internal static class WorkspaceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

    /// <summary>A proposal from the wire, as the kind's record — or null when it is not that shape.</summary>
    public static T? Parse<T>(JsonElement element) where T : class
    {
        try
        {
            return element.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <inheritdoc cref="IStudioMemberResolver"/>
public sealed class StudioMemberResolver : IStudioMemberResolver
{
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<Guid, StudioMember?> _cache = [];

    public StudioMemberResolver(ApplicationDbContext db) => _db = db;

    public async Task<StudioMember?> ResolveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(userId, out var cached))
            return cached;

        var profile = await _db.StaffProfiles.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == StaffStatus.Active)
            .Select(p => new { p.StudioRole, p.AllNodes, p.AllLanguages })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
            return _cache[userId] = null;

        // Scope is stored as node ids and checked as paths: "inside this subject" is then one prefix
        // comparison, and a scope node that was retired simply stops matching anything live.
        var paths = profile.AllNodes
            ? []
            : await (from s in _db.StaffScopeNodes.AsNoTracking()
                     join n in _db.CurriculumNodes.AsNoTracking() on s.NodeId equals n.Id
                     where s.UserId == userId
                     select n.Path).ToListAsync(cancellationToken);

        var languages = profile.AllLanguages
            ? []
            : await _db.StaffScopeLanguages.AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => s.LanguageId)
                .ToListAsync(cancellationToken);

        return _cache[userId] = new StudioMember(
            userId, profile.StudioRole, profile.AllNodes, paths, profile.AllLanguages, languages.ToHashSet());
    }
}

/// <summary>The way down to a node, as the Studio shows it above every screen.</summary>
public sealed class NodeTrails
{
    private readonly ApplicationDbContext _db;

    public NodeTrails(ApplicationDbContext db) => _db = db;

    /// <summary>Trails for many nodes at once, each from the grade down to the node itself.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<TrailStepDto>>> ForAsync(
        IEnumerable<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var wanted = nodeIds.Distinct().ToList();
        if (wanted.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<TrailStepDto>>();

        var paths = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => wanted.Contains(n.Id))
            .Select(n => new { n.Id, n.Path })
            .ToListAsync(cancellationToken);

        var chain = paths.ToDictionary(p => p.Id, p => Segments(p.Path));
        var everyId = chain.Values.SelectMany(s => s).Distinct().ToList();

        var steps = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => everyId.Contains(n.Id))
            .Select(n => new
            {
                n.Id,
                n.KindKey,
                Titles = n.Translations.Select(t => new NodeTitle(t.LangId, t.Title)).ToList()
            })
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        return chain.ToDictionary(
            c => c.Key,
            c => (IReadOnlyList<TrailStepDto>)c.Value
                .Where(steps.ContainsKey)
                .Select(id => new TrailStepDto(id, steps[id].KindKey, steps[id].Titles))
                .ToList());
    }

    public async Task<IReadOnlyList<TrailStepDto>> ForAsync(Guid nodeId, CancellationToken cancellationToken) =>
        (await ForAsync([nodeId], cancellationToken)).TryGetValue(nodeId, out var trail) ? trail : [];

    public static IReadOnlyList<Guid> Segments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
}

/// <summary>Writes notifications into members' Studio inboxes, alongside the change that caused them.</summary>
public sealed class StudioNotifier
{
    private readonly ApplicationDbContext _db;

    public StudioNotifier(ApplicationDbContext db) => _db = db;

    public void Notify(IEnumerable<Guid> userIds, string kind, Guid? actorId, Guid? draftId = null, Guid? releaseId = null,
        Guid? assignmentId = null, Guid? nodeId = null)
    {
        var now = DateTime.UtcNow;

        // Nobody is told about what they did themselves.
        foreach (var userId in userIds.Distinct().Where(u => u != actorId))
        {
            _db.StudioNotifications.Add(new StudioNotification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = kind,
                ActorUserId = actorId,
                DraftId = draftId,
                ReleaseId = releaseId,
                AssignmentId = assignmentId,
                NodeId = nodeId,
                CreatedAtUtc = now
            });
        }
    }

    /// <summary>Active members at or above a role whose scope covers a path — who a submitted draft is for.</summary>
    public async Task<IReadOnlyList<Guid>> MembersCoveringAsync(
        string path, StudioRole atLeast, CancellationToken cancellationToken)
    {
        var members = await _db.StaffProfiles.AsNoTracking()
            .Where(p => p.Status == StaffStatus.Active && p.StudioRole >= atLeast)
            .Select(p => new { p.UserId, p.AllNodes })
            .ToListAsync(cancellationToken);

        var scoped = members.Where(m => !m.AllNodes).Select(m => m.UserId).ToList();

        var scopePaths = scoped.Count == 0
            ? []
            : await (from s in _db.StaffScopeNodes.AsNoTracking()
                     join n in _db.CurriculumNodes.AsNoTracking() on s.NodeId equals n.Id
                     where scoped.Contains(s.UserId)
                     select new { s.UserId, n.Path }).ToListAsync(cancellationToken);

        return members
            .Where(m => m.AllNodes || scopePaths.Any(s =>
                s.UserId == m.UserId && (path == s.Path || path.StartsWith(s.Path + "/", StringComparison.Ordinal))))
            .Select(m => m.UserId)
            .ToList();
    }
}

internal static class PersonRefs
{
    public static PersonRefDto Of(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        Staff.PeopleDirectory.Ref(names, id)!;
}

/// <summary>
/// Where a thing lives in the curriculum, and whether a member's scope covers all of it.
/// <para>
/// <b>Why this exists.</b> Scope limits what a person may change, and the draft machinery has
/// enforced it from the start — every draft is opened, reviewed and released against
/// <see cref="StudioMember.CoversPath"/>. The acts that happen <i>immediately</i> rather than
/// through a draft — saying what a question measures, replacing a stand-in, marking an anchor,
/// stopping answers counting, building a paper — were added later and were gated on role alone, so
/// a member scoped to one subject could reach into any other. This is the same check, for them.
/// </para>
/// <para>
/// <b>Every place, not any place.</b> A question can sit under more than one node, and changing it
/// changes it for all of them. Covering one of its homes is not permission to change the others.
/// </para>
/// </summary>
public sealed class StudioScope
{
    private readonly ApplicationDbContext _db;

    public StudioScope(ApplicationDbContext db) => _db = db;

    /// <summary>Whether the member may change a question — every node it is mapped into.</summary>
    public async Task<bool> CoversItemAsync(StudioMember member, Guid itemId, CancellationToken cancellationToken)
    {
        if (member.AllNodes) return true;

        var paths = await (from m in _db.NodeItemMappings.AsNoTracking()
                           join n in _db.CurriculumNodes.AsNoTracking() on m.NodeId equals n.Id
                           where m.ItemId == itemId
                           select n.Path).Distinct().ToListAsync(cancellationToken);

        return Covers(member, paths);
    }

    /// <summary>Whether the member may change a set of skills — every node any of them is mapped into.</summary>
    public async Task<bool> CoversTargetsAsync(
        StudioMember member, IReadOnlyList<Guid> targetIds, CancellationToken cancellationToken)
    {
        if (member.AllNodes) return true;
        if (targetIds.Count == 0) return false;

        var paths = await (from m in _db.NodeTargetMappings.AsNoTracking()
                           join n in _db.CurriculumNodes.AsNoTracking() on m.NodeId equals n.Id
                           where targetIds.Contains(m.TargetId)
                           select n.Path).Distinct().ToListAsync(cancellationToken);

        return Covers(member, paths);
    }

    /// <summary>Whether the member may change something written at one node.</summary>
    public async Task<bool> CoversNodeAsync(StudioMember member, Guid nodeId, CancellationToken cancellationToken)
    {
        if (member.AllNodes) return true;

        var path = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => n.Path)
            .FirstOrDefaultAsync(cancellationToken);

        return path is not null && member.CoversPath(path);
    }

    /// <summary>
    /// A member who is not scoped to everything covers a thing only when it has a home and they
    /// cover every one. Something mapped nowhere is inside nobody's part of the curriculum, so it
    /// stays with the people whose scope is the whole of it.
    /// </summary>
    private static bool Covers(StudioMember member, IReadOnlyList<string> paths) =>
        paths.Count > 0 && paths.All(member.CoversPath);
}
