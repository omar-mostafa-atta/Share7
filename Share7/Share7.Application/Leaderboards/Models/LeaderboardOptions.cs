namespace Share7.Application.Leaderboards.Models;

/// <summary>
/// Operational knobs for ranking, bound from the <c>Leaderboards</c> configuration section.
/// <para>
/// Configuration rather than constants because none of these is a design decision: the right
/// batch size depends on what the shared host tolerates, and the right listing default is a
/// product and legal ruling that must be changeable without a migration or a deploy.
/// </para>
/// </summary>
public class LeaderboardOptions
{
    public const string SectionName = "Leaderboards";

    /// <summary>
    /// Master switch. Off by default — unlike a rate limit, an unfinished leaderboard protects
    /// nothing by being on, and boards that are half-populated in production are worse than boards
    /// that are absent. Turn it on when the first board is authored.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// **Whether a child is listed on public boards unless they opt out.**
    /// <para>
    /// The safe default is <c>true</c> *only* because the name on the row is a generated handle
    /// that discloses nothing — no real name, no email, no grade, no account id. If display names
    /// ever become player-chosen, this must be revisited: an unlisted-by-default posture is the
    /// defensible one the moment a row can carry something a child typed about themselves.
    /// </para>
    /// <para>
    /// Set to <c>false</c> for an opt-in posture. Existing rows are not rewritten by the change —
    /// it governs accounts issued a handle after it takes effect.
    /// </para>
    /// </summary>
    public bool ListedByDefault { get; set; } = true;

    /// <summary>How many pending results one projection pass folds in. Sized for a shared host.</summary>
    public int ProjectionBatchSize { get; set; } = 500;

    /// <summary>
    /// How long a worker's claim on a job survives without being renewed. A worker killed by an
    /// app-pool recycle cannot release its claim, so the lease has to expire on its own or the job
    /// is stuck forever.
    /// </summary>
    public int JobClaimSeconds { get; set; } = 300;

    /// <summary>How many times a failing job is retried before it is left for an operator.</summary>
    public int JobMaxAttempts { get; set; } = 5;

    /// <summary>
    /// How long an idempotency log row is kept. Far longer than any client retry budget, short
    /// enough that the table does not grow without bound.
    /// </summary>
    public int RequestLogRetentionHours { get; set; } = 720;

    /// <summary>
    /// How many days of <c>GameResults</c> to keep. **Zero switches retention off**, which is a
    /// supported configuration — a deployment small enough not to need it should not be quietly
    /// deleting its own history.
    /// <para>
    /// Ninety days is comfortably longer than any cycle this platform runs, so nothing is ever
    /// deleted while it could still change a live rank. What it does cost is reach: a rebuild from
    /// source can only go back this far. That is the deliberate trade — entries are durable, cycles
    /// settle long before the window closes, and the alternative is keeping every row a five-year-old
    /// platform ever wrote for a rebuild nobody will run.
    /// </para>
    /// </summary>
    public int ResultRetentionDays { get; set; } = 90;

    /// <summary>
    /// Rows deleted per pass. Bounded because an unbounded delete on a table of this size takes lock
    /// escalation, and a production incident, with it.
    /// </summary>
    public int RetentionBatchSize { get; set; } = 5_000;

    /// <summary>How often the retention sweeper wakes. Floored at five minutes.</summary>
    public int RetentionIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Most batches one tick will run. Lets a backlog drain over a single tick rather than a batch an
    /// hour, while stopping a first run against years of history from holding the table all night.
    /// </summary>
    public int MaxRetentionPassesPerTick { get; set; } = 20;

    /// <summary>Default page size for a board read, and the cap on what a caller may ask for.</summary>
    public int DefaultPageSize { get; set; } = 25;

    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Shared secret an external pinger presents to drive deferred work, since the caller is a
    /// machine with no account.
    /// <para>
    /// **Null closes the endpoint rather than opening it.** An unset secret must never degrade
    /// into an unauthenticated write surface — that failure mode is how a maintenance hook becomes
    /// a denial-of-service lever.
    /// </para>
    /// </summary>
    public string? MaintenanceKey { get; set; }
}
