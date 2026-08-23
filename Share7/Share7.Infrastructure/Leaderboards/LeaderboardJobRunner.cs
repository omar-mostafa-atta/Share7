using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Claims due jobs under a lease and runs them.
/// <para>
/// The claim is a **lease, not a lock**. A worker killed by an app-pool recycle cannot release
/// anything, so a lock would strand the job forever; a lease expires on its own and the next
/// worker picks it up. That single decision is what makes deferred leaderboard work survive a
/// hosting environment that can terminate a process without warning.
/// </para>
/// </summary>
public class LeaderboardJobRunner : ILeaderboardJobRunner
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILeaderboardProjector _projector;
    private readonly ILeaderboardRolloverService _rollover;
    private readonly LeaderboardOptions _options;
    private readonly ILogger<LeaderboardJobRunner> _logger;

    /// <summary>Identifies this worker's claims. Per-instance, so a recycle looks like a new worker.</summary>
    private static readonly Guid WorkerId = Guid.NewGuid();

    public LeaderboardJobRunner(
        ApplicationDbContext dbContext,
        ILeaderboardProjector projector,
        ILeaderboardRolloverService rollover,
        IOptions<LeaderboardOptions> options,
        ILogger<LeaderboardJobRunner> logger)
    {
        _dbContext = dbContext;
        _projector = projector;
        _rollover = rollover;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunDueAsync(int maxJobs, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return 0;

        var completed = 0;

        for (var i = 0; i < maxJobs; i++)
        {
            var job = await ClaimNextAsync(cancellationToken);

            if (job is null)
                break;

            try
            {
                await ExecuteAsync(job, cancellationToken);

                job.State = LeaderboardJobState.Completed;
                job.CompletedAtUtc = DateTime.UtcNow;
                job.ClaimedBy = null;
                job.ClaimExpiresAtUtc = null;

                completed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One poisoned cycle must not stop every other board from updating, so the failure
                // is recorded against the job and the loop continues.
                job.LastError = $"{ex.GetType().Name}: {ex.Message}";
                job.ClaimedBy = null;
                job.ClaimExpiresAtUtc = null;

                job.State = job.Attempts >= _options.JobMaxAttempts
                    ? LeaderboardJobState.Failed
                    : LeaderboardJobState.Pending;

                // Linear backoff. A job failing because the database is busy should not come
                // straight back and make it busier.
                job.RunAfterUtc = DateTime.UtcNow.AddSeconds(30 * job.Attempts);

                _logger.LogError(
                    ex, "Leaderboard job {JobId} ({Kind}) failed on attempt {Attempt}.",
                    job.Id, job.Kind, job.Attempts);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return completed;
    }

    /// <summary>
    /// Takes the oldest due job, if any, atomically.
    /// <para>
    /// The claim is one <c>UPDATE … WHERE</c> rather than a read followed by a write: two workers
    /// reading the same pending row and both deciding to run it is the whole failure this guards,
    /// and no amount of application-side checking closes that window.
    /// </para>
    /// </summary>
    private async Task<LeaderboardJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseUntil = now.AddSeconds(_options.JobClaimSeconds);

        // Pending work, or Running work whose lease has expired because its worker died.
        var claimable = _dbContext.LeaderboardJobs
            .Where(j => j.RunAfterUtc <= now
                        && (j.State == LeaderboardJobState.Pending
                            || (j.State == LeaderboardJobState.Running
                                && j.ClaimExpiresAtUtc != null
                                && j.ClaimExpiresAtUtc < now)))
            .OrderBy(j => j.RunAfterUtc)
            .Take(1);

        var claimed = await claimable.ExecuteUpdateAsync(
            update => update
                .SetProperty(j => j.State, LeaderboardJobState.Running)
                .SetProperty(j => j.ClaimedBy, WorkerId)
                .SetProperty(j => j.ClaimExpiresAtUtc, leaseUntil)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1),
            cancellationToken);

        if (claimed == 0)
            return null;

        // Re-read what we just claimed. Safe because the claim is ours by worker id and lease.
        return await _dbContext.LeaderboardJobs
            .Where(j => j.ClaimedBy == WorkerId
                        && j.State == LeaderboardJobState.Running
                        && j.ClaimExpiresAtUtc == leaseUntil)
            .OrderBy(j => j.RunAfterUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ExecuteAsync(LeaderboardJob job, CancellationToken cancellationToken)
    {
        switch (job.Kind)
        {
            case LeaderboardJobKind.Project:
                await _projector.ProjectPendingAsync(_options.ProjectionBatchSize, cancellationToken);
                break;

            case LeaderboardJobKind.Reindex when job.CycleId is { } reindexCycle:
                await _projector.ReindexCycleAsync(reindexCycle, cancellationToken);
                break;

            case LeaderboardJobKind.Rollover:
                await _rollover.RolloverAsync(cancellationToken);
                break;

            case LeaderboardJobKind.Prune:
                await PruneAsync(cancellationToken);
                break;

            case LeaderboardJobKind.Settle:
                // Phase 4. Declared here so the kind is stable in the enum and in the database
                // before the handler exists — renumbering a job kind later would silently
                // re-point rows already written.
                _logger.LogWarning("Leaderboard settlement is not implemented yet.");
                break;

            default:
                _logger.LogWarning("Leaderboard job {JobId} has no runnable shape.", job.Id);
                break;
        }
    }

    /// <summary>
    /// Deletes idempotency rows past their retention window.
    /// <para>
    /// Lives here rather than in a second background service so that the one mechanism which
    /// survives an app-pool recycle owns every recurring chore.
    /// </para>
    /// </summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddHours(-_options.RequestLogRetentionHours);

        var removed = await _dbContext.ProgressRequestLogs
            .Where(l => l.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var oldJobs = await _dbContext.LeaderboardJobs
            .Where(j => j.State == LeaderboardJobState.Completed && j.CompletedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0 || oldJobs > 0)
            _logger.LogInformation("Pruned {Logs} request logs and {Jobs} completed jobs.", removed, oldJobs);
    }
}
