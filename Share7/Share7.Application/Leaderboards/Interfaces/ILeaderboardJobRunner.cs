namespace Share7.Application.Leaderboards.Interfaces;

/// <summary>
/// Drains the leaderboard job table.
/// <para>
/// **This exists because the deployment cannot be trusted to keep a process alive.** Shared IIS
/// recycles app pools and shuts down idle workers, so a background loop is not a schedule — it is
/// a hope. Making the work claimable rows means it survives a recycle and can be driven from three
/// directions at once: the in-process timer when there happens to be one, an external ping, and a
/// read that notices something is overdue.
/// </para>
/// <para>
/// All three call this same method, and it is safe to call from all three simultaneously: work is
/// claimed under a lease, so two workers cannot run the same job.
/// </para>
/// </summary>
public interface ILeaderboardJobRunner
{
    /// <summary>
    /// Claims and runs up to <paramref name="maxJobs"/> due jobs, returning how many completed.
    /// <para>
    /// Never throws for a job's own failure: a job that dies is recorded, retried, and eventually
    /// left for an operator, because one poisoned cycle must not stop every other board from
    /// updating.
    /// </para>
    /// </summary>
    Task<int> RunDueAsync(int maxJobs, CancellationToken cancellationToken = default);
}

/// <summary>
/// Moves cycles through their states and keeps a live window in existence.
/// <para>
/// Separate from the projector because the two fail differently and should not take each other
/// down: a board whose ranking is stuck should still roll over into next week, and a rollover that
/// cannot create a window should not stop every other board's results from being counted.
/// </para>
/// </summary>
public interface ILeaderboardRolloverService
{
    /// <summary>
    /// Opens scheduled cycles that are due, closes open cycles that have ended, and creates the
    /// window covering now for any board missing one. Returns how many rows it changed.
    /// <para>
    /// Safe to run concurrently: the unique window index is what arbitrates, not the caller.
    /// </para>
    /// </summary>
    Task<int> RolloverAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Freezes a closed cycle's final ranks and pays them.
/// <para>
/// **Everything here has to survive being run twice.** The job table delivers at-least-once and a
/// shared host can kill a worker between the grant and the record of it, so the settlement row's
/// unique index claims each placing and the paid flag flips inside the same transaction as the
/// payout.
/// </para>
/// </summary>
public interface ILeaderboardSettlementService
{
    /// <summary>
    /// Settles one closed cycle. Idempotent: a cycle already settled succeeds without doing
    /// anything, because a retry finding the work done is the system behaving correctly.
    /// </summary>
    Task<Common.Models.ServiceResult> SettleAsync(
        Guid cycleId, CancellationToken cancellationToken = default);
}
