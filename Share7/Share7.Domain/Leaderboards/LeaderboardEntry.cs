namespace Share7.Domain.Leaderboards;

/// <summary>
/// One player's standing in one cohort of one cycle. **Derived** — every column here is
/// reproducible by replaying <see cref="GameResult"/>, and a rebuild that produced a different
/// answer would be a defect in the projector.
/// <para>
/// A player appears once per cohort they belong to, so the same run lands on both the <c>All</c>
/// row and the <c>Grade</c> row. Storing one row per cohort rather than filtering on read is what
/// keeps a cohort page an index seek instead of a scan-and-rank.
/// </para>
/// </summary>
public class LeaderboardEntry
{
    public Guid Id { get; set; }

    public Guid CycleId { get; set; }
    public LeaderboardCycle? Cycle { get; set; }

    public LeaderboardCohort Cohort { get; set; }

    /// <summary>
    /// Which cohort instance this row belongs to — the grade id for a <c>Grade</c> cohort, and
    /// <see cref="Guid.Empty"/> for <c>All</c>, which has exactly one instance.
    /// <para>
    /// Part of the key rather than a lookup, so ranking a grade never has to join the profile
    /// table, and so a child changing grade cannot retroactively move results they already earned.
    /// </para>
    /// </summary>
    public Guid CohortKey { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The ranked number, aggregated per the board's rule.</summary>
    public long Value { get; set; }

    /// <summary>
    /// When the ranked value was first reached. **The tie-break: earlier wins.**
    /// <para>
    /// Ties must break on something stable and explainable or paging duplicates and skips rows
    /// across requests. Never on user id — arbitrary, and it looks rigged to the child who always
    /// loses ties.
    /// </para>
    /// </summary>
    public DateTime AchievedAtUtc { get; set; }

    /// <summary>
    /// Materialised rank within <c>(CycleId, Cohort, CohortKey)</c>, 1-based.
    /// <para>
    /// Stored rather than computed on read because the deployment has no Redis and
    /// <c>ROW_NUMBER() OVER (ORDER BY …)</c> across a live board is a scan: it turns a 40 ms page
    /// read into seconds exactly when an event makes the board interesting. Recomputed by the
    /// reindex job; <c>0</c> means "not yet ranked since the last projection".
    /// </para>
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// The safe handle shown on this row, snapshotted at projection time.
    /// <para>
    /// A snapshot rather than a join so a settled cycle stays byte-stable forever, and so no read
    /// path can accidentally reach a child's real name by following a foreign key.
    /// </para>
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Cosmetic avatar key, display only. Never an identifier.</summary>
    public string? AvatarKey { get; set; }

    /// <summary>
    /// The player opted out of public listing. Excluded from paged reads, **still ranked** — they
    /// keep their own standing on the "me" and "around me" routes. Hiding is not forfeiting.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Excluded from public ranking pending review, because a result behind it failed a
    /// plausibility bound. The row survives so the decision is reversible.
    /// </summary>
    public bool IsFlagged { get; set; }

    /// <summary>
    /// The last result folded into this row, so the projector can recognise work it has already
    /// done and stay idempotent under replay.
    /// </summary>
    public Guid? LastResultId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
