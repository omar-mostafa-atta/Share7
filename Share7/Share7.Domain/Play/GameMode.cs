using Share7.Domain.Commerce;
using Share7.Domain.Games;

namespace Share7.Domain.Play;

/// <summary>
/// One rule-set of one mini-game — Classic, Sudden Death, Time Attack — as the platform offers it.
/// Pairs with Unity's <c>GameModeDefinition</c>, joined by <see cref="ModeKey"/>.
/// <para>
/// <b>Two catalogues, and this one owns policy.</b> The client asset carries the rules a match is
/// actually played by, which this server cannot see and deliberately does not model: a phase graph
/// is content. What lives here is everything a shipped build must not be the authority on —
/// whether the mode is offered at all, when, to whom, and what a session of it is worth. That split
/// is the whole reason the row exists: withdrawing a broken mode has to be an UPDATE, not a store
/// release, and a client three months old must stop offering it the moment it is switched off.
/// </para>
/// <para>
/// <b>Accounting here is a ceiling, not a grant.</b> <see cref="CountsTowardMastery"/> and
/// <see cref="SettlesEconomy"/> say what this mode may ever do; the context of the session says what
/// this particular session may do; and the server pays the intersection. Neither half can override
/// the other, and the client's copy of either is a display hint.
/// </para>
/// </summary>
public class GameMode
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>
    /// Stable machine key, e.g. <c>runner.mode.classic</c>. Unique, and equal to the Unity
    /// definition's <c>modeId</c> — that equality is the entire join between the two catalogues.
    /// <para>
    /// <b>Immutable once published.</b> Runs, results, boards and analytics all key on it, so a
    /// rename is a new mode with no history rather than the same mode under another name.
    /// </para>
    /// </summary>
    public string ModeKey { get; set; } = string.Empty;

    /// <summary>
    /// Which shapes of session this mode is offered in, as a <see cref="PlayTopologies"/> bitfield.
    /// Enforced by matchmaking, so the client's copy loses to this one.
    /// </summary>
    public PlayTopologies Topologies { get; set; } = PlayTopologies.Solo;

    public int MinPlayers { get; set; } = 1;

    public int MaxPlayers { get; set; } = 1;

    /// <summary>
    /// The operator kill switch. An inactive mode is omitted from the client read entirely rather
    /// than returned with a flag — the same way a retired game is — and refuses new sessions.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The mode a session gets when it names none. Exactly one per game, enforced by a filtered
    /// unique index.
    /// <para>
    /// This is what makes the whole domain additive against clients that predate it: a build that
    /// has never heard of modes sends no <c>modeKey</c>, and its runs are priced as this mode rather
    /// than refused. Which is also why it must stay unconditionally available — a default behind an
    /// entitlement or a window would leave those builds unable to play at all.
    /// </para>
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>Null means "already open". The window a seasonal mode appears in.</summary>
    public DateTime? AvailableFromUtc { get; set; }

    /// <summary>Null means "never closes".</summary>
    public DateTime? AvailableToUtc { get; set; }

    /// <summary>
    /// Whether playing it requires owning <see cref="EntitlementProduct"/>. Checked at run start —
    /// the picker's copy of this is a hint, and a hint is not a gate.
    /// </summary>
    public bool RequiresEntitlement { get; set; }

    /// <summary>The product the entitlement is sold or granted under. Required when <see cref="RequiresEntitlement"/>.</summary>
    public Guid? EntitlementProductId { get; set; }
    public Product? EntitlementProduct { get; set; }

    /// <summary>
    /// The <c>Grade.Order</c> this mode opens at; 0 is ungated. Compared against the player's grade
    /// rather than their level, because a mode gated by difficulty of content belongs to the grade.
    /// </summary>
    public int MinGradeOrder { get; set; }

    /// <summary>
    /// Whether a session of this mode can ever move mastery. False for anything that re-asks
    /// questions the player has already answered — otherwise replaying is indistinguishable from learning.
    /// </summary>
    public bool CountsTowardMastery { get; set; } = true;

    /// <summary>Whether a session of this mode can ever pay. False makes it worth nothing, whatever the context.</summary>
    public bool SettlesEconomy { get; set; } = true;

    /// <summary>
    /// Whether results from this mode may rank at all. A practice-shaped mode sets it false and
    /// posts to no board, which is cheaper and more honest than authoring a board nobody should be on.
    /// </summary>
    public bool CountsTowardRanking { get; set; } = true;

    /// <summary>
    /// How much of what a session earns is actually paid. Null uses the platform default profile —
    /// the ordinary case, and the reason a new mode needs no economy authoring to be playable.
    /// </summary>
    public Guid? EconomyProfileId { get; set; }
    public EconomyProfile? EconomyProfile { get; set; }

    /// <summary>Position in the client's picker. Lower first.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<GameModeTranslation> Translations { get; set; } = new List<GameModeTranslation>();

    /// <summary>Whether the mode is inside its authored window at <paramref name="atUtc"/>.</summary>
    public bool IsWithinWindow(DateTime atUtc) =>
        (AvailableFromUtc is not { } from || from <= atUtc)
        && (AvailableToUtc is not { } to || atUtc < to);

    /// <summary>Whether a client should be offered it right now: switched on and inside its window.</summary>
    public bool IsOffered(DateTime atUtc) => IsActive && IsWithinWindow(atUtc);
}

/// <summary>
/// A mode's name and description in one language. Composite key (ModeId, LangId), exactly like
/// <c>GameTranslation</c>: one mode keeps one id in every language, so its leaderboard is not split
/// in half by the language it was played in.
/// </summary>
public class GameModeTranslation
{
    public Guid ModeId { get; set; }
    public GameMode? Mode { get; set; }

    public Guid LangId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
