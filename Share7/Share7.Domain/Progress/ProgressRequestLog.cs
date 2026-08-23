namespace Share7.Domain.Progress;

/// <summary>
/// One completed attempt submission, keyed by the client's idempotency key so a retry after a lost
/// response replays the original answer instead of recording a second attempt.
/// <para>
/// **Only successes are recorded**, exactly as <see cref="Multiplayer.MultiplayerRequestLog"/> does,
/// and for the same reason commerce learned the expensive way: an idempotency index covering
/// refusals permanently burns the key, so a student who fixes whatever was refused and retries gets
/// their own stale "no" replayed forever. A refused attempt — a locked lesson, a disabled game — is
/// expected to succeed later.
/// </para>
/// <para>
/// Before this existed, <c>requestId</c> reached only the reward engine: a retry was deduplicated
/// for payment but still incremented <c>Attempts</c> and overwrote the lesson row. That is
/// tolerable while an attempt is private, and not tolerable once it projects onto a public
/// leaderboard, where every retry becomes an extra scoring event.
/// </para>
/// </summary>
public class ProgressRequestLog
{
    /// <summary>
    /// Part of the composite key with <see cref="RequestId"/>. **Scoped per user deliberately**: one
    /// child's key must not be able to replay — or block — another child's submission.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>Client-minted, reused across every retry of the same run.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Which operation the key was spent on: <c>attempt</c> today, <c>gameresult</c> when the
    /// non-curriculum result route lands.
    /// <para>
    /// Recorded so a key reused across two *different* operations is visible rather than silently
    /// replaying the wrong body. It is not part of the key — one key, one operation.
    /// </para>
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Diagnostic pointer to what was submitted. Deliberately not a foreign key.</summary>
    public Guid? LessonId { get; set; }

    /// <summary>
    /// The exact response body first returned, replayed verbatim.
    /// <para>
    /// Stored rather than re-derived because the response is not reconstructible after the fact:
    /// <c>unlocked[]</c> reports what *this* submission opened, and a replay that recomputed it
    /// would return an empty list for a run that really did unlock the next lesson.
    /// </para>
    /// </summary>
    public string ResponseJson { get; set; } = string.Empty;

    /// <summary>Swept after the retention window — far longer than any client retry budget.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
