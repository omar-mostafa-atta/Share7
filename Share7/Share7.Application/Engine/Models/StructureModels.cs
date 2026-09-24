namespace Share7.Application.Engine.Models;

/// <summary>The curriculum levels the current game can play, in depth order.</summary>
public static class NodeKinds
{
    public const string Grade = "grade";
    public const string Term = "term";
    public const string Subject = "subject";
    public const string Chapter = "chapter";
    public const string Lesson = "lesson";

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
