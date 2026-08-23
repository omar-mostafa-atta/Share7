using Share7.Domain.Leaderboards;

namespace Share7.Domain.Objectives;

/// <summary>
/// One thing a player can be asked to do. **Always data, never code** — a quest is an INSERT, with
/// no migration, no deploy and no client release, exactly as a leaderboard board is.
/// <para>
/// This single table is every daily quest, every weekly quest and every achievement the platform
/// will ever have. They differ by <see cref="Kind"/>, which is a statement about the cycle and
/// nothing else. What an objective *pays* is deliberately absent: that lives in a
/// <c>RewardRule</c> keyed on <c>OBJECTIVE_COMPLETED</c> with this objective's
/// <see cref="Key"/> as its reference, so there is one payout path for the whole platform rather
/// than a second one that can create currency.
/// </para>
/// </summary>
public class Objective
{
    public Guid Id { get; set; }

    /// <summary>
    /// The stable public token, lowercase and dot-separated — e.g.
    /// <c>daily.lessons.complete.3</c>.
    /// <para>
    /// **Stable forever once published.** The reward rule that pays for it keys on this, the client
    /// maps its art from it, and analytics group by it — so a rename is a new objective with no
    /// history rather than the same objective under another name.
    /// </para>
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public ObjectiveKind Kind { get; set; }

    /// <summary>
    /// What is being counted. A value from <c>LeaderboardMetrics</c>, validated at authoring time
    /// for the same reason a board's is: an objective on a metric nothing raises is dead
    /// configuration that an operator creates, sees no error from, and waits forever on.
    /// </summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>
    /// Narrows to one sub-dimension of the metric — a pickup kind, a currency key — matched against
    /// <see cref="GameResult.Scope"/>. Null counts every scope.
    /// <para>
    /// This is what makes "collect 200 coins" and "collect 20 gems" two rows over one metric.
    /// </para>
    /// <para>
    /// **Not a curriculum filter.** "Complete three lessons in Mathematics" would need the projector
    /// to resolve a lesson id up the chapter/subject tree on the hot path, which is a different and
    /// much more expensive feature; it is deliberately not half-built here.
    /// </para>
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>The value the counter must reach. Always positive.</summary>
    public long Target { get; set; }

    /// <summary>
    /// How repeated results combine into the counter. Reuses the leaderboard vocabulary verbatim
    /// rather than declaring a parallel one: <c>Sum</c> for "collect 200",
    /// <c>Best</c> for "survive 5 minutes in a single run", <c>Last</c> for a latest-value metric.
    /// </summary>
    public LeaderboardAggregation Aggregation { get; set; } = LeaderboardAggregation.Sum;

    /// <summary>Restricts counting to one game. Null counts every game.</summary>
    public Guid? GameId { get; set; }

    /// <summary>
    /// Offers this objective only to one grade. Null offers it to everyone.
    /// <para>
    /// A filter on who *sees* it, not on what counts — a child's results count toward whatever
    /// objectives they hold, and their grade is snapshotted onto each result anyway.
    /// </para>
    /// </summary>
    public Guid? GradeId { get; set; }

    /// <summary>
    /// Language tree this objective belongs to. Content ids are language-scoped, so anything that
    /// selects content has to be too; null offers it in every language.
    /// </summary>
    public Guid? LangId { get; set; }

    /// <summary>Event window. Null on both means "whenever it is active".</summary>
    public DateTime? AvailableFromUtc { get; set; }

    public DateTime? AvailableToUtc { get; set; }

    /// <summary>
    /// A token the client maps to its own art — <c>quest_lessons</c>, <c>badge_first_ace</c>.
    /// **Never a URL and never display text**, following the precedent <c>Offer.BadgeKey</c> set.
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>Display order within its kind. Presentation only.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Retires an objective without deleting it. Progress rows, completions and the reward
    /// transactions that paid for them all stay resolvable — a completed objective is somebody's
    /// record, and the ledger has to stay explicable.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ObjectiveTranslation> Translations { get; set; } =
        new List<ObjectiveTranslation>();
}

/// <summary>
/// An objective's name and description in one language.
/// <para>
/// Separate rows per language, exactly as <c>GameTranslation</c> and
/// <c>LeaderboardBoardTranslation</c> do. The objective keeps one id across every language so its
/// history is not split in half, and the client is handed a single already-localised <c>name</c>
/// with no <c>nameEn</c>/<c>nameAr</c> to choose between — the split
/// <c>Docs/Localization.md</c> exists to stop anyone merging.
/// </para>
/// </summary>
public class ObjectiveTranslation
{
    public Guid Id { get; set; }

    public Guid ObjectiveId { get; set; }
    public Objective? Objective { get; set; }

    public Guid LangId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
