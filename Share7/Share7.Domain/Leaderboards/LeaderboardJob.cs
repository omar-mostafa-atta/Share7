namespace Share7.Domain.Leaderboards;

/// <summary>
/// A unit of deferred leaderboard work — projection, reindex, cycle rollover, settlement.
/// <para>
/// **This table exists because the deployment cannot be trusted to keep a process alive.** IIS
/// recycles app pools and shuts down idle workers, so a <c>BackgroundService</c> that only lives
/// in memory silently stops running and says nothing about it. Making the work a row means it
/// survives the recycle, can be claimed by whichever worker happens to be awake, can be triggered
/// externally by a ping, and can be picked up lazily on the next read that notices it is overdue.
/// </para>
/// <para>
/// The project already ships one <c>BackgroundService</c> (the multiplayer session sweeper), which
/// is exposed to exactly this risk. That is a known issue to raise, not a precedent to copy.
/// </para>
/// </summary>
public class LeaderboardJob
{
    public Guid Id { get; set; }

    public LeaderboardJobKind Kind { get; set; }

    /// <summary>The cycle this job acts on, or null for board-wide and global work.</summary>
    public Guid? CycleId { get; set; }

    public Guid? BoardId { get; set; }

    public LeaderboardJobState State { get; set; }

    /// <summary>Not eligible to run before this. Used to schedule rollover and settlement ahead of time.</summary>
    public DateTime RunAfterUtc { get; set; }

    /// <summary>
    /// Who holds the claim, and until when. A claim is a lease rather than a lock: a worker killed
    /// mid-job cannot release it, so the lease has to expire on its own or the job is stuck
    /// forever.
    /// </summary>
    public Guid? ClaimedBy { get; set; }

    public DateTime? ClaimExpiresAtUtc { get; set; }

    public int Attempts { get; set; }

    /// <summary>Last failure, kept so a job that keeps dying is diagnosable without log archaeology.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

public enum LeaderboardJobKind
{
    /// <summary>Fold pending <see cref="GameResult"/> rows into entries.</summary>
    Project = 0,

    /// <summary>Recompute materialised ranks for a cycle.</summary>
    Reindex = 1,

    /// <summary>Advance cycle states and generate the next window.</summary>
    Rollover = 2,

    /// <summary>Freeze final ranks and pay a closed cycle.</summary>
    Settle = 3,

    /// <summary>Delete expired idempotency logs.</summary>
    Prune = 4
}

public enum LeaderboardJobState
{
    Pending = 0,
    Running = 1,
    Completed = 2,

    /// <summary>Exhausted its retries. Left for an operator rather than dropped.</summary>
    Failed = 3
}
