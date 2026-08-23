using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Writes results down and queues them for ranking. Nothing here ranks anything.
/// <para>
/// The split matters: recording is on the gameplay request and must stay a few indexed inserts,
/// while projection walks every open cycle and every cohort. Doing both here would put the cost of
/// a leaderboard into the latency of finishing a lesson, and the first time a popular board grew
/// large, children would feel it as the game hanging on the results screen.
/// </para>
/// </summary>
public class GameResultRecorder : IGameResultRecorder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlausibilityGuard _plausibility;
    private readonly ILogger<GameResultRecorder> _logger;

    public GameResultRecorder(
        ApplicationDbContext dbContext,
        IPlausibilityGuard plausibility,
        ILogger<GameResultRecorder> logger)
    {
        _dbContext = dbContext;
        _plausibility = plausibility;
        _logger = logger;
    }

    public async Task RecordAsync(
        GameResultContext context, CancellationToken cancellationToken = default)
    {
        if (context.Metrics.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var wrote = false;

        foreach (var draft in context.Metrics)
        {
            // A metric nothing declares is a bug in the caller, not a reason to fail a child's
            // lesson. Dropped and logged: the attempt is already graded and committed-worthy, and
            // an unranked result is recoverable by backfill while a refused attempt is not.
            if (!LeaderboardMetrics.IsKnown(draft.Metric))
            {
                _logger.LogWarning(
                    "Dropping unknown leaderboard metric {Metric} from game {GameId}.",
                    draft.Metric, context.GameId);
                continue;
            }

            // Zero carries no information for any aggregation: Sum is unchanged by it, Best cannot
            // be beaten downward, and Last would overwrite a real value with nothing.
            if (draft.Value == 0)
                continue;

            // Flag, never reject. The result is written either way — the row is evidence, and a
            // bound tight enough to catch a modified client also catches a child with a wrong
            // clock or a dropped connection. Flagged rows are excluded from projection and left
            // for a person, which is reversible; deleting a genuine run is not.
            var flagReason = await _plausibility.ReasonToFlagAsync(
                context.UserId, context.GameId, draft.Metric, draft.Value,
                context.OccurredAtUtc, cancellationToken);

            if (flagReason is not null)
            {
                _logger.LogWarning(
                    "Flagging {Metric} result for user {UserId}: {Reason}",
                    draft.Metric, context.UserId, flagReason);
            }

            _dbContext.GameResults.Add(new GameResult
            {
                Id = Guid.NewGuid(),
                UserId = context.UserId,
                GameId = context.GameId,
                Metric = draft.Metric,
                Value = draft.Value,
                OccurredAtUtc = context.OccurredAtUtc,
                SourceType = GameResultSource.Attempt,
                SourceId = context.SourceId,
                RequestId = context.RequestId,
                GradeId = context.GradeId,
                LangId = context.LangId,
                IsFlagged = flagReason is not null,
                FlagReason = flagReason,
                CreatedAtUtc = now
            });

            wrote = true;
        }

        if (!wrote)
            return;

        // The job is a wake-up call, not a work item: the projector drains everything pending when
        // it runs, so one outstanding Project job covers any number of results. Skipped when one
        // is already waiting, which is what stops a busy evening queueing a row per lesson.
        //
        // Checked rather than enforced by a unique index on purpose. A collision here would roll
        // back the results in this same transaction, and losing a child's ranked result to a
        // bookkeeping conflict is far worse than the occasional duplicate job — which costs one
        // row and one no-op drain.
        var alreadyQueued = await _dbContext.LeaderboardJobs.AnyAsync(
            j => j.Kind == LeaderboardJobKind.Project
                 && j.CycleId == null
                 && (j.State == LeaderboardJobState.Pending || j.State == LeaderboardJobState.Running),
            cancellationToken);

        if (!alreadyQueued)
        {
            _dbContext.LeaderboardJobs.Add(new LeaderboardJob
            {
                Id = Guid.NewGuid(),
                Kind = LeaderboardJobKind.Project,
                State = LeaderboardJobState.Pending,
                RunAfterUtc = now,
                CreatedAtUtc = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
