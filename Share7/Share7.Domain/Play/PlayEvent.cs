using Share7.Domain.Commerce;
using Share7.Domain.Games;
using Share7.Domain.Leaderboards;

namespace Share7.Domain.Play;

/// <summary>
/// An authored competition: one game, one mode, optionally one world, its own ladder, its own rules
/// and its own prizes.
/// <para>
/// <b>An event has no lifecycle of its own.</b> It is a binding onto a leaderboard cycle, and the
/// cycle owns the window and the state machine — scheduled, open, closed, settled. Two sources of
/// truth for "is this running" is how an event ends up open on the ladder and finished in the app,
/// so there are deliberately no start and end columns on this row.
/// </para>
/// <para>
/// <b>The board is the event's own.</b> Created with the event, in the same transaction, bound back
/// by <see cref="BoardId"/>: a prize table pays against final ranks, and a ladder that also carried
/// ordinary play would pay for gameplay the entrants never entered.
/// </para>
/// </summary>
public class PlayEvent
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable machine key, e.g. <c>runner.classic.desert.2026w37</c>. Unique and immutable: awards,
    /// analytics and support tickets all name it long after the event is over.
    /// </summary>
    public string EventKey { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>
    /// The rule-set entrants compete under. One mode per event, because a ladder that mixed two of
    /// them would rank whichever was easier rather than whoever played best.
    /// </summary>
    public Guid ModeId { get; set; }
    public GameMode? Mode { get; set; }

    /// <summary>
    /// The world every entry runs in, or null to leave the mode's own world selection alone. A key
    /// rather than a foreign key by choice — see <c>GameWorld.WorldKey</c>.
    /// </summary>
    public string? WorldKey { get; set; }

    /// <summary>
    /// Whether entrants may play <see cref="WorldKey"/> without owning it, for as long as the event
    /// runs. <b>An event must never be unplayable because a child has not bought its world</b>, so
    /// this is true by default and false is the deliberate choice of an operator running a
    /// competition inside content everybody already has.
    /// </summary>
    public bool GrantsWorldForDuration { get; set; } = true;

    /// <summary>The ladder this event's results project into. Created with the event.</summary>
    public Guid BoardId { get; set; }
    public LeaderboardBoard? Board { get; set; }

    /// <summary>
    /// The single cycle of that board — and the only place this event's window lives. Read through
    /// for start, end and state.
    /// </summary>
    public Guid CycleId { get; set; }
    public LeaderboardCycle? Cycle { get; set; }

    /// <summary>
    /// Which slice of players the prize table ranks within: everyone, or one grade against itself.
    /// <para>
    /// Grade cohorts are how a competition stays fair across a K-12 span without splitting into a
    /// dozen events: one ladder, one prize table, and a six-year-old is never ranked against a
    /// twelve-year-old for the same prize.
    /// </para>
    /// </summary>
    public LeaderboardCohort PrizeCohort { get; set; } = LeaderboardCohort.All;

    // ---- entry rules ---------------------------------------------------------------------

    /// <summary>
    /// How many entries one account may settle per UTC day, or null for no limit. The bound that
    /// keeps a week-long ladder from being won by whoever could play the most times in one evening.
    /// </summary>
    public int? MaxEntriesPerDay { get; set; }

    /// <summary>Total entries per account for the whole event, or null for no limit.</summary>
    public int? MaxEntriesTotal { get; set; }

    /// <summary>Lowest <c>Grade.Order</c> that may enter; 0 is ungated.</summary>
    public int MinGradeOrder { get; set; }

    /// <summary>Highest <c>Grade.Order</c> that may enter; 0 is ungated.</summary>
    public int MaxGradeOrder { get; set; }

    /// <summary>Player level required to enter; 0 is ungated.</summary>
    public int MinLevel { get; set; }

    /// <summary>
    /// A product an entrant must own to take part, or null for an open event.
    /// <para>
    /// <b>Refused outright when the prize table carries a real-world prize.</b> An entry fee plus a
    /// prize of value is a paid competition, which is a different legal object in most of the world
    /// and not one this platform offers to children. Enforced at authoring, not at entry.
    /// </para>
    /// </summary>
    public Guid? EntryProductId { get; set; }
    public Product? EntryProduct { get; set; }

    /// <summary>
    /// How long a winner has to be dealt with before a real-world prize claim lapses, in days. Only
    /// meaningful for an event whose prize table has a real-world tier.
    /// </summary>
    public int ClaimWindowDays { get; set; } = 30;

    /// <summary>How the entries settle. Null uses the platform default profile.</summary>
    public Guid? EconomyProfileId { get; set; }
    public EconomyProfile? EconomyProfile { get; set; }

    // ---- presentation --------------------------------------------------------------------

    /// <summary>Addressables address of the event's banner art, resolved by the client. Optional.</summary>
    public string? BannerAddress { get; set; }

    /// <summary>Accent colour as <c>#RRGGBB</c>, so an operator can theme a card without a release.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Events sort by this first, so a headline event can be pinned above a routine one.</summary>
    public int SortOrder { get; set; }

    // ---- lifecycle -----------------------------------------------------------------------

    /// <summary>The operator kill switch. An inactive event is invisible to clients and refuses entries.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When an operator called the event off, if they did. Distinct from <see cref="IsActive"/>: a
    /// cancelled event stays visible as cancelled so an entrant is told what happened to the week
    /// they played, and its prizes are never awarded.
    /// </summary>
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>Operator-facing note for why. Never shown to a child.</summary>
    public string? CancelReason { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<PlayEventTranslation> Translations { get; set; } = new List<PlayEventTranslation>();

    public ICollection<EventPrizeTier> PrizeTiers { get; set; } = new List<EventPrizeTier>();

    /// <summary>Whether entries are accepted right now. The cycle decides; this row only adds the kill switch.</summary>
    public bool AcceptsEntries(LeaderboardCycleState cycleState) =>
        IsActive && CancelledAtUtc is null && cycleState == LeaderboardCycleState.Open;
}

/// <summary>
/// An event's name, blurb and rules text in one language.
/// <para>
/// The rules are translated content rather than a client string table entry: an operator writes the
/// rules of the competition they are running, and a shipped build cannot have a key for it.
/// </para>
/// </summary>
public class PlayEventTranslation
{
    public Guid EventId { get; set; }
    public PlayEvent? Event { get; set; }

    public Guid LangId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// How to win, in the entrant's language. Shown in full before the first entry, because a
    /// competition whose rules a child cannot read is not a competition they agreed to.
    /// </summary>
    public string Rules { get; set; } = string.Empty;
}
