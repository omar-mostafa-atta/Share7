namespace Share7.Domain.Leaderboards;

/// <summary>
/// A player's final standing in a settled cycle, and what it paid. **Immutable once written.**
/// <para>
/// Separate from <see cref="LeaderboardEntry"/> rather than a flag on it because the two answer
/// different questions forever: the entry is a projection that a rebuild is allowed to
/// recalculate, while this is the record of what the player was actually told and actually paid.
/// A rebuild that changed somebody's already-awarded third place would be rewriting history.
/// </para>
/// </summary>
public class LeaderboardSettlement
{
    public Guid Id { get; set; }

    public Guid CycleId { get; set; }
    public LeaderboardCycle? Cycle { get; set; }

    public LeaderboardCohort Cohort { get; set; }

    public Guid CohortKey { get; set; }

    public Guid UserId { get; set; }

    public int FinalRank { get; set; }

    public long Value { get; set; }

    /// <summary>
    /// The board key and rank band this was paid against, e.g.
    /// <c>global.game.runner.score.weekly:top10</c> — the same string the reward rule is authored
    /// with, recorded so a payout can be explained years later without re-deriving the band.
    /// </summary>
    public string? RewardReferenceKey { get; set; }

    /// <summary>
    /// **Set inside the same transaction as the grant.** The settlement job is retried, so this is
    /// what turns at-least-once delivery into effectively-once payment. Paying a child twice for
    /// third place is a defect nobody reports.
    /// </summary>
    public bool RewardIssued { get; set; }

    public DateTime? RewardIssuedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
