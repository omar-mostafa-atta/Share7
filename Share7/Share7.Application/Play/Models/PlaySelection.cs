using Share7.Domain.Play;

namespace Share7.Application.Play.Models;

/// <summary>
/// What the client asked to play: a mode, a reason, and an event when there is one.
/// <para>
/// Every gameplay entry point takes this — starting a run, submitting an attempt, matchmaking — so
/// the same request shape is checked by the same code in all three. A field a client could use to
/// state what its session is worth is deliberately absent, and the absence is the contract.
/// </para>
/// </summary>
public sealed record PlaySelectionRequest
{
    /// <summary>Which game. Always known by the caller; never taken from the mode.</summary>
    public required Guid GameId { get; init; }

    /// <summary>
    /// The mode's key, or null for a client that predates modes. Null resolves to the game's
    /// default; an unknown key is refused rather than defaulted, because a silent default is how
    /// every run of a mis-spelled mode ends up priced as Classic.
    /// </summary>
    public string? ModeKey { get; init; }

    /// <summary>
    /// Why it is being played. Null means <c>curriculum</c> — what every client that predates this
    /// domain is doing, which is exactly what it always did.
    /// </summary>
    public string? ContextKey { get; init; }

    /// <summary>Required when the context is an event, refused otherwise.</summary>
    public Guid? EventId { get; init; }

    /// <summary>
    /// How many players the session seats, for the topology check. 1 for a solo run; the seat count
    /// for a match. Zero means "do not check", which matchmaking uses before a roster exists.
    /// </summary>
    public int PlayerCount { get; init; } = 1;
}

/// <summary>
/// The server's answer: the resolved mode and context, what they are jointly worth, and the profile
/// the payout scales by. Nothing here came from the client.
/// </summary>
public sealed record PlaySelection
{
    /// <summary>
    /// The resolved mode, or null when the game has no modes authored at all.
    /// <para>
    /// Null is the compatibility path and is deliberately not an error: a database migrated but not
    /// yet seeded must keep playing exactly as it did before this domain existed. Everything
    /// downstream treats it as "no policy to apply".
    /// </para>
    /// </summary>
    public GameMode? Mode { get; init; }

    public Guid? ModeId => Mode?.Id;

    public required PlayContextKind Context { get; init; }

    public PlayEvent? Event { get; init; }

    public Guid? EventId => Event?.Id;

    /// <summary>What this session may do, from the mode and the context together.</summary>
    public required PlaySettlementPolicy Policy { get; init; }

    /// <summary>
    /// How much of what it earns is actually paid. Resolved from the event first, then the mode,
    /// then the platform default — never from the client.
    /// </summary>
    public EconomyProfile? Profile { get; init; }

    /// <summary>Scales a settled amount by the profile, or leaves it alone when no profile applies.</summary>
    public long Scale(long amount) => Profile?.Scale(amount) ?? amount;

    /// <summary>Whether fixed reward rules fire for this session.</summary>
    public bool PaysRuleRewards => Policy.SettlesEconomy && (Profile?.PaysRuleRewards ?? true);

    /// <summary>The world the event pins, when it pins one.</summary>
    public string? PinnedWorldKey => Event?.WorldKey;
}
