namespace Share7.API.RateLimiting;

/// <summary>
/// Names of the endpoint policies, so an <c>[EnableRateLimiting]</c> attribute and the
/// registration cannot drift apart over a typo — a misspelled policy name throws at request time,
/// on the one route the limit was supposed to protect.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Anonymous credential routes, partitioned by address.
    /// See <see cref="RateLimitOptions.AuthPermitsPerMinute"/>.
    /// </summary>
    public const string Auth = "auth";

    /// <summary>
    /// Authenticated state-changing routes, partitioned by user.
    /// See <see cref="RateLimitOptions.WritePermitsPerMinute"/>.
    /// </summary>
    public const string Writes = "writes";

    /// <summary>
    /// Telemetry ingest, partitioned by user.
    /// <para>
    /// **Its own policy because it is neither a read nor an ordinary write.** A client that has
    /// been offline for a week drains a backlog in a burst of batches, which is correct behaviour
    /// and would trip the write limit; but each batch carries up to a hundred events, so the same
    /// number of requests moves two orders of magnitude more rows. See
    /// <see cref="RateLimitOptions.TelemetryPermitsPerMinute"/>.
    /// </para>
    /// </summary>
    public const string Telemetry = "telemetry";
}
