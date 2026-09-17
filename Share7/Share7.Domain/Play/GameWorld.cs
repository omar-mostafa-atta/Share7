using Share7.Domain.Commerce;
using Share7.Domain.Games;

namespace Share7.Domain.Play;

/// <summary>How a player comes to own a world.</summary>
public enum WorldUnlockKind
{
    /// <summary>
    /// Everybody has it, and <b>no entitlement row is ever written for it</b>. The free floor is
    /// computed from this policy on every read, so it survives an entitlement refresh returning an
    /// empty set — the hazard the cosmetics floor already paid for once.
    /// </summary>
    Free = 0,

    /// <summary>Bought through the ordinary shop, as an entitlement to <see cref="GameWorld.Product"/>.</summary>
    Purchase = 1,

    /// <summary>Opens at a player level. Computed, never stored: levelling up should not need a grant to run.</summary>
    Level = 2,

    /// <summary>Opens at a grade. Computed from the profile the same way.</summary>
    Grade = 3,

    /// <summary>
    /// Granted by something else — a reward rule, an event prize, an operator. Stored as an
    /// entitlement, and unreachable any other way: a world nobody can buy and nobody has won stays locked.
    /// </summary>
    Reward = 4
}

/// <summary>
/// One world (environment) of one mini-game, and the rule that decides who may play in it.
/// <para>
/// <b>The World axis is presentation, and the schema is built to keep it that way.</b> There is
/// nothing here that changes what a run is worth, how hard it is, or which board it reaches — a
/// world that changed difficulty would need its own leaderboard, and the five axes would collapse
/// into a matrix. What a world can do is be owned, which is why this row exists at all.
/// </para>
/// <para>
/// <b>The content itself is the client's.</b> The server never resolves art, prefabs or scenes: it
/// stores the world's key, the same opaque identifier the Unity <c>EnvironmentDefinition</c> carries,
/// and hands it back. That is the same boundary game rows keep with scenes, for the same reason —
/// this server cannot see the content catalogue, so anything it stored about the art could only ever
/// disagree with it.
/// </para>
/// </summary>
public class GameWorld
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>
    /// The client's own environment id, e.g. <c>runner.env.desert</c>. Unique per game, immutable
    /// once published: it is what a run, an event and an entitlement all name.
    /// </summary>
    public string WorldKey { get; set; } = string.Empty;

    public WorldUnlockKind UnlockKind { get; set; } = WorldUnlockKind.Free;

    /// <summary>
    /// The product this world is sold or granted under. Required for
    /// <see cref="WorldUnlockKind.Purchase"/> and <see cref="WorldUnlockKind.Reward"/>, unused otherwise.
    /// </summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Player level it opens at, for <see cref="WorldUnlockKind.Level"/>.</summary>
    public int MinLevel { get; set; }

    /// <summary><c>Grade.Order</c> it opens at, for <see cref="WorldUnlockKind.Grade"/>.</summary>
    public int MinGradeOrder { get; set; }

    /// <summary>Position in the client's world picker. Lower first.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether the world is offered at all. An inactive world is omitted from the picker and refused
    /// at run start, but never un-owned: a child who bought it keeps the entitlement.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The world a game falls back to. Exactly one per game, and it must be free — it is what every
    /// session runs in when nothing else is chosen or owned.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<GameWorldTranslation> Translations { get; set; } = new List<GameWorldTranslation>();

    /// <summary>
    /// Whether this world is owned by a player with these attributes, ignoring entitlements — the
    /// part of ownership that is computed rather than stored.
    /// <para>
    /// Returns false for the two kinds that are stored (<see cref="WorldUnlockKind.Purchase"/> and
    /// <see cref="WorldUnlockKind.Reward"/>), so a caller always ORs this with the entitlement it
    /// already holds rather than either one being the whole answer.
    /// </para>
    /// </summary>
    public bool IsUnlockedBy(int level, int gradeOrder) => UnlockKind switch
    {
        WorldUnlockKind.Free => true,
        WorldUnlockKind.Level => level >= MinLevel,
        WorldUnlockKind.Grade => gradeOrder >= MinGradeOrder,
        _ => false
    };
}

/// <summary>A world's display name and blurb in one language.</summary>
public class GameWorldTranslation
{
    public Guid WorldId { get; set; }
    public GameWorld? World { get; set; }

    public Guid LangId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// The product kind worlds are sold under, and the SKU shape they use.
/// <para>
/// <c>ProductKind.Name</c> is contract with the client — it arrives as each grant's <c>kind</c> — so
/// the token lives here rather than being typed into an admin form twice.
/// </para>
/// </summary>
public static class WorldProducts
{
    /// <summary>The <c>ProductKind.Name</c> a world product carries, normalised to SCREAMING_SNAKE on the wire.</summary>
    public const string Kind = "WORLD";

    /// <summary>
    /// The conventional product key for a world: <c>world.{worldKey}</c>, e.g.
    /// <c>world.runner.env.desert</c>. The world key already carries its game, so prefixing the game
    /// again would only produce <c>world.game.runner.runner.env.desert</c>.
    /// </summary>
    public static string SkuFor(string worldKey) =>
        $"world.{(worldKey ?? string.Empty).Trim()}".ToLowerInvariant();
}
