namespace Share7.Domain.Multiplayer;

/// <summary>
/// One completed multiplayer operation, keyed by the client's idempotency key so a retry after a
/// lost response replays the original answer instead of acting twice.
/// <para>
/// **Only successes are recorded, and this is the whole design.** Commerce learned it the expensive
/// way: its idempotency index originally covered every state, so a *refused* purchase permanently
/// burned its <c>requestId</c> and a student who topped up and retried got their own stale "no"
/// replayed forever (see the <c>PurchaseIdempotencyOnCompletedOnly</c> migration).
/// </para>
/// <para>
/// The same trap is sharper here. A join refused with <c>SESSION_FULL</c> is *expected* to succeed
/// thirty seconds later when somebody leaves. If refusals were logged, that child would be locked
/// out of the session for as long as they kept retrying with the same key — which is exactly what a
/// well-behaved client does.
/// </para>
/// </summary>
public class MultiplayerRequestLog
{
    /// <summary>
    /// Part of the composite key with <see cref="RequestId"/>. **Scoped per user deliberately**: one
    /// child's key must not be able to replay — or block — another child's operation.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>Client-minted, reused across retries of the same operation.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Which operation the key was spent on: <c>create</c>, <c>join</c>, <c>leave</c>,
    /// <c>start</c>, <c>close</c>, <c>matchmake</c>, <c>host-transfer</c>.
    /// <para>
    /// Recorded so a key reused across two *different* operations is visible rather than silently
    /// replaying the wrong body. It is not part of the key — one key, one operation.
    /// </para>
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    public Guid? SessionId { get; set; }

    /// <summary>
    /// The exact response body first returned, replayed verbatim.
    /// <para>
    /// Commerce replays by re-reading the transaction and re-rendering it, which is cheaper. That
    /// does not work here: <c>MatchmakeResponse.outcome</c> distinguishes <c>Joined</c> from
    /// <c>Created</c>, and once the session exists nothing in the schema still remembers which of
    /// the two happened. Storing the body is what makes that answer stable.
    /// </para>
    /// </summary>
    public string ResponseJson { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    /// <summary>Swept after <c>RequestLogRetentionHours</c> — far longer than any client retry budget.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
