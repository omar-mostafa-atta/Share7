namespace Share7.Application.Engine.Models;

/// <summary>The curriculum levels the current game can play, in depth order.</summary>
public static class NodeKinds
{
    public const string Grade = "grade";
    public const string Term = "term";
    public const string Subject = "subject";
    public const string Chapter = "chapter";
    public const string Lesson = "lesson";

    /// <summary>
    /// The one node every curriculum declared in the Studio hangs from, carrying its name. The
    /// Egyptian tree has none — its grades are its top — so nothing that reads it changes.
    /// </summary>
    public const string CurriculumRoot = "curriculum";

    /// <summary>The kind a node of <paramref name="kind"/> must sit under, or null for a grade.</summary>
    public static string? ParentOf(string kind) => kind switch
    {
        Term => Grade,
        Subject => Term,
        Chapter => Subject,
        Lesson => Chapter,
        _ => null
    };

    public static int DepthOf(string kind) => kind switch
    {
        Grade => 0,
        Term => 1,
        Subject => 2,
        Chapter => 3,
        Lesson => 4,
        _ => -1
    };

    /// <summary>
    /// Kinds that can be added, renamed, moved, reordered and retired. The fourteen Egyptian grades
    /// are fixed: grade ids are stored on student profiles and gate game modes and worlds.
    /// </summary>
    public static bool IsEditable(string kind) => kind is Term or Subject or Chapter or Lesson;
}

public sealed record NodeTitle(Guid LangId, string Title);

/// <summary>Who is making a structural change, and on whose behalf.</summary>
/// <param name="ReleaseId">The Studio release applying it, when one is. Null for the old admin paths.</param>
public sealed record EngineActor(Guid? UserId, Guid? ReleaseId = null)
{
    public static readonly EngineActor Platform = new(null);
}

public sealed record CreateNodeCommand
{
    /// <summary>Supply to create the node with a known id (a Studio draft mints it up front).</summary>
    public Guid? NodeId { get; init; }

    public required Guid ParentId { get; init; }

    /// <summary>Must match the level under the parent: a term under a grade, a lesson under a chapter.</summary>
    public required string Kind { get; init; }

    public required IReadOnlyList<NodeTitle> Titles { get; init; }

    /// <summary>1-based. Null appends after the last live sibling.</summary>
    public int? Position { get; init; }

    /// <summary>
    /// What happens when <see cref="Position"/> is taken: the old admin endpoints refuse (their
    /// contract since they were written); the Studio makes room by moving later siblings down one.
    /// </summary>
    public bool ShiftSiblings { get; init; }
}

/// <summary>A node as the engine sees it, for the caller to echo back.</summary>
public sealed record NodeStateDto
{
    public required Guid Id { get; init; }
    public required string Kind { get; init; }
    public Guid? ParentId { get; init; }
    public required int Order { get; init; }
    public required int Revision { get; init; }
    public required string Path { get; init; }
    public DateTime? RetiredAtUtc { get; init; }
    public required IReadOnlyList<NodeTitle> Titles { get; init; }

    public string TitleIn(Guid langId) => Titles.FirstOrDefault(t => t.LangId == langId)?.Title ?? string.Empty;
}

/// <summary>What a structural change did, beyond the node it was aimed at.</summary>
public sealed record StructureChangeDto
{
    public required NodeStateDto Node { get; init; }

    /// <summary>Every node whose position, parent or state changed alongside the target.</summary>
    public IReadOnlyList<Guid> AlsoChanged { get; init; } = [];

    /// <summary>Whether students' unlocks are being brought in line with the new shape (see UnlockRepair).</summary>
    public bool UnlocksQueued { get; init; }
}

/// <summary>One level a curriculum declares: its key, where it sits, whether it is played, and what it is called.</summary>
public sealed record CurriculumLevel(
    Guid KindId,
    string Key,
    int Depth,
    string? ParentKey,
    bool IsPlayable,
    IReadOnlyList<NodeTitle> Names);

/// <summary>
/// A curriculum's levels, read from its version's <c>CurriculumNodeKinds</c> rather than assumed.
/// <para>
/// **Every structural rule asks this, not <see cref="NodeKinds"/>.** Egypt's five levels are data
/// too — seeded, with the same parent chain the static helpers spell out — so the served curriculum
/// answers every question exactly as before. What differs is decided by <see cref="IsServed"/>:
/// the curriculum the game serves keeps its fourteen fixed grades and its legacy compatibility
/// copy; one declared in the Studio has neither, because nothing may reach a student from it.
/// </para>
/// </summary>
public sealed record CurriculumShape
{
    public required Guid VersionId { get; init; }
    public required Guid CurriculumId { get; init; }

    /// <summary>Whether the game serves this curriculum. Today exactly one does: the Egyptian national tree.</summary>
    public required bool IsServed { get; init; }

    /// <summary>In depth order, the curriculum's own root first when it has one.</summary>
    public required IReadOnlyList<CurriculumLevel> Levels { get; init; }

    public CurriculumLevel? Level(string key) => Levels.FirstOrDefault(l => l.Key == key);

    public bool Knows(string key) => Level(key) is not null;

    /// <summary>The level a node of <paramref name="key"/> must sit under, or null at the top.</summary>
    public string? ParentOf(string key) => Level(key)?.ParentKey;

    /// <summary>The level that goes under <paramref name="key"/>, or null for the played level.</summary>
    public string? ChildOf(string key) => Levels.FirstOrDefault(l => l.ParentKey == key)?.Key;

    public bool IsPlayable(string key) => Level(key)?.IsPlayable ?? false;

    /// <summary>
    /// Whether nodes of this level can be added, renamed, moved, reordered, retired and restored.
    /// The served curriculum answers as it always has (its grades are fixed); anywhere else every
    /// declared level can, except the root that carries the curriculum's own name.
    /// </summary>
    public bool CanEdit(string key) =>
        IsServed ? NodeKinds.IsEditable(key) : Knows(key) && key != NodeKinds.CurriculumRoot;

    /// <summary>Whether a parent of this level may have its children put in a new order.</summary>
    public bool CanReorderUnder(string key) =>
        CanEdit(key) || (IsServed ? key == NodeKinds.Grade : key == NodeKinds.CurriculumRoot);
}
