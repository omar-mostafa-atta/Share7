namespace Share7.API.RateLimiting;

/// <summary>
/// Limits for the request throttles, bound from the <c>RateLimiting</c> section.
/// <para>
/// Configuration rather than constants because the right number is an operational question, not a
/// design one: it depends on how chatty the shipped client turns out to be, and finding that out
/// should not need a redeploy.
/// </para>
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Master switch. On by default — an off-by-default protection protects nothing — but present
    /// so a limit that misfires against real traffic can be dropped from configuration instead of
    /// waiting on a build.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requests a minute per caller across every endpoint, the backstop that catches whatever the
    /// specific policies below do not.
    /// <para>
    /// Sized for the client's launch burst — snapshot, balances, catalogue, equipment, offers — with
    /// a wide margin, because a global limit that trips during normal play is worse than no global
    /// limit at all. It exists to stop a script, not to shape ordinary traffic.
    /// </para>
    /// </summary>
    public int GlobalPermitsPerMinute { get; set; } = 240;

    /// <summary>
    /// Requests a minute per IP against the anonymous auth routes. This is the password-spraying
    /// budget: low on purpose, since no human logs in twenty times a minute and a credential-stuffer
    /// needs thousands of attempts to be worth running.
    /// </summary>
    public int AuthPermitsPerMinute { get; set; } = 20;

    /// <summary>
    /// Requests a minute per user against the state-changing routes — attempts, purchases, grants,
    /// session creation.
    /// <para>
    /// Generous against real play (a lesson attempt takes minutes to earn) and still tight enough
    /// that replaying a captured request is pointless, which is the actual threat: idempotency
    /// already makes the replay a no-op, and this stops it being a free denial-of-service too.
    /// </para>
    /// </summary>
    public int WritePermitsPerMinute { get; set; } = 60;

    /// <summary>
    /// Batches a minute per user against telemetry ingest.
    /// <para>
    /// Higher than the write budget on purpose. A device that has been offline drains its queue on
    /// reconnect — that is what the queue is for — and throttling it back to the write limit would
    /// mean a week's backlog takes hours to land, or expires against the backlog window first. It
    /// is still a hard ceiling: at this rate one account can offer at most a few thousand events a
    /// minute, and the batch cap is what bounds the rest.
    /// </para>
    /// </summary>
    public int TelemetryPermitsPerMinute { get; set; } = 120;

    /// <summary>
    /// Whether to read the client address from <c>X-Forwarded-For</c> instead of the socket.
    /// <para>
    /// **Off by default, and the default is the safe one in both directions.** Trusting the header
    /// when nothing strips it lets a caller forge a fresh address per request and walk straight
    /// through every IP-partitioned limit here. Not trusting it behind a proxy that rewrites the
    /// source address is the opposite failure — every anonymous caller lands in one partition and
    /// they collectively share a single login budget.
    /// </para>
    /// <para>
    /// Turn it on only when a proxy that overwrites the header sits in front of this app. IIS
    /// in-process hosting, which is what the current deployment uses, passes the real address
    /// through on the socket and needs this left alone.
    /// </para>
    /// </summary>
    public bool TrustForwardedForHeader { get; set; }
}
