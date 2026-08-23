namespace Share7.Domain.Leaderboards;

/// <summary>
/// One authoritative gameplay result. **Append-only: never updated, never deleted** except by the
/// account-deletion path.
/// <para>
/// This is the source of truth for every number on every board. A leaderboard entry is a
/// projection of these rows and nothing else, which is what makes a rebuild possible: drop every
/// entry, replay this table, and the ranks come back identical. Anything that can only be
/// reconstructed by trusting the entry table is a design error.
/// </para>
/// <para>
/// **Nothing here comes from a client claim.** Values are computed server-side — today by the
/// re-grading in <c>ProgressService.SubmitAttemptAsync</c> — so there is no field a modified build
/// can inflate.
/// </para>
/// </summary>
public class GameResult
{
    /// <summary>
    /// PK, and the projector's idempotency key. Re-running projection over the same id must leave
    /// every entry byte-identical, which is what makes backfill and index rebuild safe to retry.
    /// </summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid GameId { get; set; }

    /// <summary>
    /// What was measured, e.g. <c>LESSONS_ACED</c>. Text rather than an enum so a board can rank a
    /// metric that shipped after this row was written; validated against
    /// <c>LeaderboardMetrics.Known</c> at authoring time so nothing can rank on a metric no code
    /// raises.
    /// </summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>
    /// The measurement, in the metric's canonical unit. Integer on purpose — a float rank is a
    /// tie-breaking argument waiting to happen, so percentages are whole percent and times are
    /// milliseconds.
    /// </summary>
    public long Value { get; set; }

    /// <summary>
    /// **Server clock**, and the field that decides which cycle this belongs to. Never the
    /// device's: a child whose tablet is set to next year would otherwise land in a cycle that has
    /// not opened.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    public GameResultSource SourceType { get; set; }

    /// <summary>The lesson, session or operation this came from. Deliberately not a foreign key.</summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// The submission's idempotency key, carried through from the attempt so a duplicate emission
    /// is visible and refusable at the index rather than only at the projector.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// The player's grade **as it was when the result happened**, snapshotted rather than read
    /// through to the profile. A child who moves up a grade mid-cycle keeps the ranking they
    /// earned in the grade they earned it in; reading live would silently re-sort finished history.
    /// </summary>
    public Guid? GradeId { get; set; }

    /// <summary>
    /// Content language at the time. Board and cycle ids are language-scoped exactly as grades
    /// are, so a result has to remember which tree it was earned in.
    /// </summary>
    public Guid? LangId { get; set; }

    /// <summary>
    /// Set when the result failed a plausibility bound. **The row is still written** — flagged
    /// results are excluded from projection and queued for review, never discarded.
    /// <para>
    /// Rejecting outright is the wrong default for a K-12 product: a bad device clock or a dropped
    /// connection would destroy a child's legitimate run with no explanation and no recovery.
    /// </para>
    /// </summary>
    public bool IsFlagged { get; set; }

    /// <summary>Why it was flagged, for the review queue. Null when it was not.</summary>
    public string? FlagReason { get; set; }

    /// <summary>
    /// When the projector folded this row into its entries, or null while it is still pending.
    /// **This is what makes projection idempotent**, and it has to be a mark on the source rather
    /// than a memo on the entry: <c>Sum</c> aggregation cannot tell a replay from a genuine second
    /// result by looking at the total, so replaying an already-counted row would quietly add it
    /// twice.
    /// <para>
    /// Set in the same transaction as the entry update, so a crash mid-batch leaves the rows
    /// unclaimed and the next pass redoes exactly the work that was lost. Clearing it is how a
    /// rebuild replays a cycle from scratch.
    /// </para>
    /// </summary>
    public DateTime? ProjectedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
