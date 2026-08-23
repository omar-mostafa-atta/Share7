namespace Share7.Domain.Leaderboards;

/// <summary>
/// What a plausible result looks like for one game and metric. **Authored as data**, so tightening
/// a bound after a live exploit is a row edit rather than a release.
/// <para>
/// Every bound here is a *flag* threshold, never a reject threshold. An implausible result is
/// still written down, excluded from ranking, and queued for a human. Rejecting outright is the
/// wrong default for a K-12 product: the same rule that catches a modified client also catches a
/// child whose tablet clock is wrong or whose connection dropped mid-lesson, and destroying their
/// legitimate run with no explanation and no recovery is a far worse outcome than a cheat sitting
/// unranked for a day.
/// </para>
/// <para>
/// A game and metric with no row is unbounded. That is the correct default for a platform that
/// expects new mini-games: an unauthored bound must not silently flag every result the first game
/// to ship a new metric produces.
/// </para>
/// </summary>
public class LeaderboardMetricBound
{
    public Guid Id { get; set; }

    /// <summary>Null applies the bound to every game that raises this metric.</summary>
    public Guid? GameId { get; set; }

    public string Metric { get; set; } = string.Empty;

    /// <summary>
    /// Largest single value that is believable. Null for no ceiling.
    /// <para>
    /// The blunt instrument, and the one that catches the crude exploit: a lesson percentage of
    /// 4,000 or a distance longer than the level.
    /// </para>
    /// </summary>
    public long? MaxValue { get; set; }

    /// <summary>
    /// Most results of this metric one player may record per UTC day. Null for no limit.
    /// <para>
    /// Catches the exploit a per-value ceiling cannot: each result individually plausible, arriving
    /// far faster than a child could actually play.
    /// </para>
    /// </summary>
    public int? MaxResultsPerDay { get; set; }

    /// <summary>
    /// Most total value one player may accumulate in this metric per UTC day. Null for no limit.
    /// <para>
    /// The bound that matters for <c>Sum</c> boards, where the ranked number is the total and no
    /// single contribution ever looks wrong on its own.
    /// </para>
    /// </summary>
    public long? MaxValuePerDay { get; set; }

    /// <summary>
    /// Off by default is the wrong posture for a limit, but on by default is the wrong posture for
    /// an *unvalidated* limit — a mis-set bound flags every honest player on the board. Authored
    /// enabled, and switchable off from configuration when one misfires against real traffic.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}
