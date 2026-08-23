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
