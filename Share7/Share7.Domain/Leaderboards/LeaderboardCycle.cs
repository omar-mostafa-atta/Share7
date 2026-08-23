namespace Share7.Domain.Leaderboards;

/// <summary>
/// One window of a board — this week's run of the weekly board, the single endless window of an
/// all-time board, or an authored event.
/// <para>
/// **Cycles are rows generated ahead of time, never computed from <c>now()</c> at read time.** A
/// derived window has no identity, so nothing can be settled, rewarded, cached or linked to; and
/// two servers with a millisecond of clock drift would disagree about which window a result
/// belongs in. A row makes the answer a fact rather than a calculation.
/// </para>
/// </summary>
public class LeaderboardCycle
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }
    public LeaderboardBoard? Board { get; set; }

    /// <summary>Inclusive.</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>
    /// Exclusive. An all-time cycle uses <see cref="DateTime.MaxValue"/> rather than null, so
    /// every range query is one comparison with no special case for "forever".
    /// </summary>
    public DateTime EndsAtUtc { get; set; }

    public LeaderboardCycleState State { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public DateTime? SettledAtUtc { get; set; }

    /// <summary>
    /// How many players hold a rank in this cycle across all cohorts, maintained by the projector
    /// so a page read never counts rows.
    /// </summary>
    public int TotalRanked { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<LeaderboardEntry> Entries { get; set; } = new List<LeaderboardEntry>();
}
