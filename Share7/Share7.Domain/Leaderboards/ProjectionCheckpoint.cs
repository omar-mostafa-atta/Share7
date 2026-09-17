namespace Share7.Domain.Leaderboards;

/// <summary>
/// How far one consumer has read the <see cref="GameResult"/> stream.
/// <para>
/// **A second consumer needs a second position, and <see cref="GameResult.ProjectedAtUtc"/> only
/// holds one.** That column is the leaderboard projector's mark, set in the same transaction as its
/// entry updates; overloading it with "and the objective projector has seen this too" would make
/// each consumer's progress depend on the other's, so a leaderboard rebuild would silently rewind
/// quests. A cursor per consumer keeps them independent, and generalises to whatever reads this
/// stream next.
/// </para>
/// <para>
/// The watermark is a <see cref="GameResult.Sequence"/>, not a timestamp: two results written in
/// the same millisecond cannot be separated by a clock, so a time cursor either re-reads them or
/// skips one.
/// </para>
/// </summary>
public class ProjectionCheckpoint
{
    /// <summary>
    /// Who is reading — <c>objectives</c>. The key, so a consumer cannot accidentally hold two
    /// positions.
    /// </summary>
    public string Consumer { get; set; } = string.Empty;

    /// <summary>
    /// The highest <see cref="GameResult.Sequence"/> this consumer has folded in. Everything above
    /// it is pending; zero means "has read nothing", which is what a fresh consumer wants.
    /// </summary>
    public long Watermark { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Consumer names for <see cref="ProjectionCheckpoint"/>. Constants, so a typo is a build error.</summary>
public static class ProjectionConsumers
{
    public const string Objectives = "objectives";

    /// <summary>
    /// The telemetry rollup projector. Reads <c>TelemetryEvents</c>, not <c>GameResult</c> — a
    /// second stream with its own cursor, which is exactly the generality this table was built for.
    /// </summary>
    public const string Telemetry = "telemetry";
}
