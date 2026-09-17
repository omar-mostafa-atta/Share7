namespace Share7.Domain.Telemetry;

/// <summary>
/// One client play session, folded from its events.
/// <para>
/// **Session length is the only honest measure of play time the platform has.** The server sees
/// requests, not attention: a token lives for an hour whether the child played for it or put the
/// tablet down, and inferring a session from request gaps would count a slow lesson as an absence.
/// So the client declares its own boundaries and this table records what it declared.
/// </para>
/// </summary>
public class TelemetrySession
{
    /// <summary>The client's session id, carried on every event in the session.</summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Earliest <c>OccurredAtUtc</c> seen. Moves backwards only, never forwards.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>Latest <c>OccurredAtUtc</c> seen. Moves forwards only.</summary>
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>
    /// Set when a <c>session_end</c> event arrives. Null means the session was never closed — a
    /// crash, a kill, or a battery. **That population is a metric, not a defect to be tidied
    /// away:** a rise in unclosed sessions is a stability signal.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// Length in whole seconds, as the client reported it at <c>session_end</c>, clamped to the
    /// span between <see cref="StartedAtUtc"/> and <see cref="LastSeenAtUtc"/> when it was not.
    /// </summary>
    public int DurationSeconds { get; set; }

    public int EventCount { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    /// <summary>UTC day of <see cref="StartedAtUtc"/>. A session that crosses midnight belongs to the day it began.</summary>
    public DateTime DayUtc { get; set; }

    /// <summary>
    /// Highest event <c>Sequence</c> folded into this row.
    /// <para>
    /// The replay guard. The projector is at-least-once, so a crash between fold and commit
    /// re-reads a batch; without this, every counter above would be added twice. Same device as
    /// <c>UserObjectiveProgress.LastSequence</c>.
    /// </para>
    /// </summary>
    public long LastSequence { get; set; }
}

/// <summary>
/// One row per (user, UTC day) they were active. **The retention substrate**, and the reason D30
/// is a group-by rather than a self-join over the raw stream.
/// </summary>
public class TelemetryUserDay
{
    public Guid UserId { get; set; }

    public DateTime DayUtc { get; set; }

    /// <summary>
    /// The user's install cohort — the first day they were ever seen. Denormalised from
    /// <see cref="TelemetryUserLifecycle"/>.
    /// <para>
    /// **Immutable once written.** If it could move, every historical cohort the user belongs to
    /// would silently change size, and last month's retention number would stop matching last
    /// month's report. The projector refuses to move it backwards and flags the event instead.
    /// </para>
    /// </summary>
    public DateTime FirstSeenDayUtc { get; set; }

    /// <summary>
    /// <c>DayUtc - FirstSeenDayUtc</c> in whole days. Zero on the install day, 1 on D1, 30 on D30.
    /// <para>
    /// **This column is the entire performance argument for the retention feature.** Derived it
    /// would make every cell of the triangle a join between the largest table in the system and
    /// itself; stored, the nightly pass is one grouped scan of the days that changed and the
    /// dashboard reads a table with tens of thousands of rows. See Rule 4.
    /// </para>
    /// </summary>
    public int DayIndex { get; set; }

    public int SessionCount { get; set; }

    public int EventCount { get; set; }

    /// <summary>Summed session length for the day. Zero is legitimate — a launch with no closed session.</summary>
    public int PlaySeconds { get; set; }

    /// <summary>Mini-game runs started. Correlates to <c>Runs</c> without duplicating it.</summary>
    public int RunCount { get; set; }

    /// <summary>Lesson attempts submitted. Same relationship to <c>GameResults</c>.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Replay guard — see <see cref="TelemetrySession.LastSequence"/>.</summary>
    public long LastSequence { get; set; }
}

/// <summary>
/// One row per user, for the whole life of the account. The cohort roster and the cheap half of
/// the user-360 header.
/// <para>
/// Kept forever, unlike the raw events: a ten-year-old cohort is still answerable after its
/// events are long swept, because this row and <see cref="TelemetryUserDay"/> are what retention
/// actually reads.
/// </para>
/// </summary>
public class TelemetryUserLifecycle
{
    public Guid UserId { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    /// <summary>UTC day of <see cref="FirstSeenAtUtc"/>. The cohort key. Immutable — see <see cref="TelemetryUserDay.FirstSeenDayUtc"/>.</summary>
    public DateTime CohortDayUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>Distinct days with any activity. Not <c>LastSeen - FirstSeen</c>, which counts absence.</summary>
    public int ActiveDays { get; set; }

    public int TotalSessions { get; set; }

    public long TotalEvents { get; set; }

    public long TotalPlaySeconds { get; set; }

    /// <summary>
    /// What they first arrived on. Stamped once and never updated, so an upgrade does not rewrite
    /// which build acquired the user — which is the only question this field exists to answer.
    /// </summary>
    public string InstallAppVersion { get; set; } = string.Empty;

    public string InstallPlatform { get; set; } = string.Empty;

    /// <summary>Most recent build seen. Updated freely; this one is about the present.</summary>
    public string LastAppVersion { get; set; } = string.Empty;

    public string LastPlatform { get; set; } = string.Empty;

    /// <summary>Replay guard — see <see cref="TelemetrySession.LastSequence"/>.</summary>
    public long LastSequence { get; set; }
}

/// <summary>
/// A daily counter for one event, optionally split by one dimension.
/// <para>
/// **One generic table rather than a table per metric.** A platform that adds a table every time
/// it wants a new chart ends up with forty of them and a migration for every product question;
/// this is the same argument <c>Objectives</c> makes for modelling a daily quest and an
/// achievement as one row shape.
/// </para>
/// </summary>
public class TelemetryDailyMetric
{
    public DateTime DayUtc { get; set; }

    /// <summary>The event name this counts.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which dimension the split is on — <c>platform</c>, <c>app_version</c>, <c>game_id</c>,
    /// <c>locale</c> — or empty for the ungrouped total.
    /// <para>
    /// Empty rather than null because both halves of the key must compare by equality, and SQL
    /// Server treats NULLs as equal in a unique index but not in a join predicate. One convention,
    /// applied in both places.
    /// </para>
    /// </summary>
    public string Dimension { get; set; } = string.Empty;

    public string DimensionValue { get; set; } = string.Empty;

    /// <summary>Occurrences. Folded incrementally by the projector.</summary>
    public long Count { get; set; }

    /// <summary>
    /// Distinct users, or null until the nightly pass computes it.
    /// <para>
    /// **Null is honest, and a zero here would not be.** A distinct count cannot be folded from a
    /// stream without holding every user seen — so the projector fills <see cref="Count"/> live and
    /// leaves this for a recompute over the day. A console reading null renders "pending" instead of
    /// claiming nobody did it.
    /// </para>
    /// </summary>
    public int? UniqueUsers { get; set; }

    public DateTime? UniqueUsersComputedAtUtc { get; set; }

    /// <summary>
    /// Highest event <c>Sequence</c> folded into this counter.
    /// <para>
    /// **The one guard that matters when more than one app instance is running.** Two projectors
    /// reading the same watermark would each add the same batch to this row; the guard makes the
    /// second one a no-op instead. Per-row rather than per-consumer, because these rows are shared
    /// across every user in a day and a single cursor could not express which of them had been
    /// folded already. Same device as <c>UserObjectiveProgress.LastSequence</c>.
    /// </para>
    /// </summary>
    public long LastSequence { get; set; }
}

/// <summary>
/// One cell of the retention triangle: of everyone who installed on <see cref="CohortDayUtc"/>,
/// how many came back on day <see cref="DayIndex"/>.
/// <para>
/// Pre-aggregated so the dashboard reads tens of thousands of rows instead of billions. A year of
/// daily cohorts observed to D365 is at most 365 x 366 cells, and the nightly pass only rewrites
/// the ones that could have changed.
/// </para>
/// </summary>
public class TelemetryRetentionCohort
{
    public DateTime CohortDayUtc { get; set; }

    /// <summary>Days since install. <c>0</c> is the install day itself, and is the denominator.</summary>
    public int DayIndex { get; set; }

    /// <summary>
    /// How many users the cohort had in total. Repeated on every row of the cohort rather than
    /// looked up, so a single cell renders a percentage without a second query.
    /// </summary>
    public int CohortSize { get; set; }

    /// <summary>How many of them were active on this day index.</summary>
    public int RetainedUsers { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
