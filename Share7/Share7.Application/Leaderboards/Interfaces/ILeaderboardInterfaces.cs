using Share7.Application.Leaderboards.Models;

namespace Share7.Application.Leaderboards.Interfaces;

/// <summary>
/// Writes authoritative gameplay results down. **There is no client-facing counterpart to this
/// interface and there must never be one.**
/// <para>
/// A route that accepted a client-stated score would make the client a participant in ranking,
/// the way accepting a client-stated price would have made it a participant in pricing — which is
/// the failure <c>OfferId</c> was designed to prevent. Ranking is projected from results the
/// server itself graded.
/// </para>
/// </summary>
public interface IGameResultRecorder
{
    /// <summary>
    /// Records one piece of gameplay and queues it for projection.
    /// <para>
    /// **Call inside the caller's own transaction.** The result is the source of truth for every
    /// board, so it commits or rolls back with the gameplay it describes — an attempt that
    /// committed without its result would be a rank silently lost, and the alternative ordering
    /// (result without attempt) would be a rank silently invented.
    /// </para>
    /// <para>
    /// Projection is *not* done here. This writes rows and enqueues a job; the ranking work
    /// happens later, off the request, so a leaderboard can never add latency to gameplay.
    /// </para>
    /// </summary>
    Task RecordAsync(GameResultContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Issues and reads the only name a public board may display.
/// <para>
/// Every other name this schema holds is unsafe to show: <c>StudentProfile.FullName</c> is a
/// child's real name, and Identity's <c>UserName</c> is unmoderated free text that the external
/// login path sets to the user's email address.
/// </para>
/// </summary>
public interface IDisplayNameService
{
    /// <summary>
    /// The player's handle, generating one on first use. Safe to call concurrently — a collision
    /// on the unique index is retried rather than surfaced.
    /// </summary>
    Task<string> EnsureHandleAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Handles for many players at once, generating any that are missing.</summary>
    Task<IReadOnlyDictionary<Guid, string>> EnsureHandlesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the player is listed publicly, and who decided. A guardian's decision outranks the
    /// player's own.
    /// </summary>
    Task<bool> IsHiddenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The player's own listing preference. Refused when a guardian has forced the account
    /// unlisted — a child must not be able to undo that by toggling their own setting.
    /// </summary>
    Task<bool> SetHiddenAsync(Guid userId, bool isHidden, CancellationToken cancellationToken = default);
}

/// <summary>
/// Folds recorded results into ranked entries. Runs off the request, from the job table.
/// </summary>
public interface ILeaderboardProjector
{
    /// <summary>
    /// Projects a batch of pending results and returns how many it handled.
    /// <para>
    /// **Idempotent.** Each result is claimed by stamping <c>ProjectedAtUtc</c> in the same
    /// transaction as the entry it updated, so a crash mid-batch redoes exactly the work that was
    /// lost and a replay of an already-counted row changes nothing. That property is the whole
    /// reason a rebuild is safe to run against live data.
    /// </para>
    /// </summary>
    Task<int> ProjectPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes materialised ranks for one cycle, in every cohort it holds.
    /// <para>
    /// Ranks are stored rather than computed on read because this deployment has no Redis, and
    /// ordering a live board on every page request is a scan. This is the job that pays that cost
    /// once instead.
    /// </para>
    /// </summary>
    Task ReindexCycleAsync(Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears a cycle's entries and replays every result that belongs to it from
    /// <c>GameResults</c>, then reindexes.
    /// <para>
    /// The disaster-recovery path, and the proof that entries are genuinely derived: if this
    /// produced different ranks, the projector would be wrong.
    /// </para>
    /// </summary>
    Task RebuildCycleAsync(Guid cycleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides whether a recorded result is believable enough to rank.
/// <para>
/// **Its only output is a reason to flag, never a decision to reject.** Every rule it applies also
/// fires on honest players — a wrong device clock, a dropped connection, a whole classroom
/// finishing at once — so a suspicious result is kept, left out of the ranking, and shown to a
/// person. A leaderboard that silently deletes a child's genuine run has done more harm than the
/// cheat it was guarding against.
/// </para>
/// </summary>
public interface IPlausibilityGuard
{
    /// <summary>Why this result should be flagged, or null when nothing about it looks wrong.</summary>
    Task<string?> ReasonToFlagAsync(
        Guid userId,
        Guid gameId,
        string metric,
        long value,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);
}
