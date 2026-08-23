namespace Share7.Domain.Leaderboards;

/// <summary>
/// A board definition. **Always data, never code** — adding a board is an INSERT, with no
/// migration, no deploy and no client release.
/// <para>
/// The rule this enforces is that "which results count" is authored rather than compiled. A board
/// that needed a code change to exist would make every seasonal event a release, and the events
/// are the point.
/// </para>
/// </summary>
public class LeaderboardBoard
{
    public Guid Id { get; set; }

    /// <summary>
    /// The stable public name, <c>{scope}.{subject}.{metric}.{period}</c>, lowercase and
    /// dot-separated — e.g. <c>global.game.runner.score.weekly</c>.
    /// <para>
    /// **Stable forever once published.** Analytics and reward rules key on it, so a rename is a
    /// new board with no history rather than the same board under another name.
    /// </para>
    /// <para>
    /// Capped at 110 rather than 128 so <c>{BoardKey}:{rankBand}</c> still fits a reward rule's
    /// 128-character <c>ReferenceKey</c> — see the settlement path.
    /// </para>
    /// </summary>
    public string BoardKey { get; set; } = string.Empty;

    /// <summary>
    /// Which game's results feed this board, or null for a platform-wide board spanning all of
    /// them.
    /// </summary>
    public Guid? GameId { get; set; }

    /// <summary>The metric being ranked. Validated against <c>LeaderboardMetrics.Known</c>.</summary>
    public string Metric { get; set; } = string.Empty;

    public LeaderboardSortDirection SortDirection { get; set; }

    public LeaderboardAggregation Aggregation { get; set; }

    public LeaderboardPeriod Period { get; set; }

    /// <summary>
    /// Cohorts this board offers, stored as a comma-separated list of <see cref="LeaderboardCohort"/>
    /// names. Authoring refuses any cohort the schema cannot currently resolve.
    /// </summary>
    public string SupportedCohorts { get; set; } = string.Empty;

    /// <summary>
    /// Restricts the board to one grade's content, for boards that should not put a KG1 child and
    /// a Grade 6 child on the same ladder. Null ranks across all grades.
    /// </summary>
    public Guid? GradeId { get; set; }

    /// <summary>
    /// Language tree this board belongs to. Content ids are language-scoped, so a board that
    /// selects on curriculum has to be too.
    /// </summary>
    public Guid? LangId { get; set; }

    /// <summary>
    /// How deep an unentitled caller may page. Null means no limit.
    /// <para>
    /// **Entitlement affects visibility, never rank.** A paid tier may read further down the
    /// board; it must never change anyone's <c>Value</c> or <c>Rank</c>. That is both the
    /// no-pay-to-win commitment and the reason the top page is publicly cacheable at all — a rank
    /// that varied per caller could not be shared.
    /// </para>
    /// </summary>
    public int? VisibleRankLimit { get; set; }

    /// <summary>
    /// Inactive boards disappear from listings but keep every cycle, entry and settlement. A board
    /// is retired, never deleted — its history is somebody's record.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// How long after <c>EndsAtUtc</c> a late result is still accepted into the cycle, in seconds.
    /// A device on a poor connection is not cheating.
    /// </summary>
    public int GraceSeconds { get; set; } = 60;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<LeaderboardBoardTranslation> Translations { get; set; } =
        new List<LeaderboardBoardTranslation>();

    public ICollection<LeaderboardCycle> Cycles { get; set; } = new List<LeaderboardCycle>();
}

/// <summary>
/// A board's title and description in one language.
/// <para>
/// Separate rows per language, exactly like <c>GameTranslation</c>: the board keeps one id across
/// every language so its history is not split in half, and the client is handed a single
/// already-localised <c>name</c> with no <c>nameEn</c>/<c>nameAr</c> to choose between.
/// </para>
/// </summary>
public class LeaderboardBoardTranslation
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }
    public LeaderboardBoard? Board { get; set; }

    public Guid LangId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
