using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Objectives.Interfaces;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Objectives;

/// <inheritdoc cref="IObjectiveProjector"/>
public class ObjectiveProjector : IObjectiveProjector
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ObjectiveProjector> _logger;

    public ObjectiveProjector(ApplicationDbContext dbContext, ILogger<ObjectiveProjector> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ProjectForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // The player's own rows only, and only those above whatever their counters have already
        // folded in. Bounded by the objectives that exist rather than by the whole stream, so this
        // stays a small indexed read on the hot path of finishing a lesson.
        var objectives = await ActiveObjectivesAsync(cancellationToken);

        if (objectives.Count == 0) return;

        var progress = await _dbContext.UserObjectiveProgress
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        var floor = FloorFor(objectives, progress);

        var results = await _dbContext.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Sequence > floor && !r.IsFlagged)
            .OrderBy(r => r.Sequence)
            .ToListAsync(cancellationToken);

        if (results.Count == 0) return;

        Fold(userId, objectives, progress, results);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProjectPendingAsync(
        int batchSize = 500, CancellationToken cancellationToken = default)
    {
        var objectives = await ActiveObjectivesAsync(cancellationToken);

        if (objectives.Count == 0) return 0;

        var checkpoint = await _dbContext.ProjectionCheckpoints
            .FirstOrDefaultAsync(c => c.Consumer == ProjectionConsumers.Objectives, cancellationToken);

        if (checkpoint is null)
        {
            checkpoint = new ProjectionCheckpoint
            {
                Consumer = ProjectionConsumers.Objectives,
                Watermark = 0,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.ProjectionCheckpoints.Add(checkpoint);
        }

        var results = await _dbContext.GameResults
            .AsNoTracking()
            .Where(r => r.Sequence > checkpoint.Watermark && !r.IsFlagged)
            .OrderBy(r => r.Sequence)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (results.Count == 0)
            return 0;

        // Grouped by player because a counter belongs to one, and because the per-row LastSequence
        // makes re-folding a result the inline pass already counted a no-op rather than a double
        // count — which is what lets these two run over the same rows without coordinating.
        foreach (var group in results.GroupBy(r => r.UserId))
        {
            var progress = await _dbContext.UserObjectiveProgress
                .Where(p => p.UserId == group.Key)
                .ToListAsync(cancellationToken);

            Fold(group.Key, objectives, progress, [.. group]);
        }

        checkpoint.Watermark = results[^1].Sequence;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return results.Count;
    }

    // ---- the fold ------------------------------------------------------------------------------

    /// <summary>
    /// Applies results to counters. **Pure bookkeeping** — it creates and updates rows and does not
    /// pay anything: a completed objective is marked <c>Completed</c>, and the payout happens when
    /// the player claims it. Keeping payment out of here is what stops the projector becoming a
    /// second place that can create currency.
    /// </summary>
    private void Fold(
        Guid userId,
        IReadOnlyList<Objective> objectives,
        List<UserObjectiveProgress> progress,
        IReadOnlyList<GameResult> results)
    {
        var now = DateTime.UtcNow;

        foreach (var objective in objectives)
        {
            foreach (var result in results)
            {
                if (!Matches(objective, result)) continue;

                var cycleKey = ObjectiveCycle.KeyFor(objective.Kind, result.OccurredAtUtc);

                var row = progress.FirstOrDefault(p =>
                    p.ObjectiveId == objective.Id && p.CycleKey == cycleKey);

                if (row is null)
                {
                    row = new UserObjectiveProgress
                    {
                        UserId = userId,
                        ObjectiveId = objective.Id,
                        CycleKey = cycleKey,
                        Value = 0,
                        State = ObjectiveState.InProgress,
                        LastSequence = 0,
                        ClaimableUntilUtc = ClaimableUntil(objective, result.OccurredAtUtc),
                        UpdatedAtUtc = now
                    };

                    progress.Add(row);
                    _dbContext.UserObjectiveProgress.Add(row);
                }

                // Already counted. The guard that lets the inline and batch passes overlap freely,
                // and the reason LastSequence lives on the row rather than only in the checkpoint:
                // a Sum counter cannot tell a replay from a genuine second result by its total.
                if (result.Sequence <= row.LastSequence) continue;

                row.LastSequence = result.Sequence;

                // A claimed or expired row is closed. Counting into it would let a finished quest
                // creep past its target and, worse, look claimable again.
                if (row.State is ObjectiveState.Claimed or ObjectiveState.Expired) continue;

                row.Value = objective.Aggregation switch
                {
                    LeaderboardAggregation.Best => Math.Max(row.Value, result.Value),
                    LeaderboardAggregation.Last => result.Value,
                    _ => row.Value + result.Value
                };

                row.UpdatedAtUtc = now;

                if (row.State == ObjectiveState.InProgress && row.Value >= objective.Target)
                {
                    row.State = ObjectiveState.Completed;
                    row.CompletedAtUtc = now;

                    _logger.LogInformation(
                        "Objective {Key} completed by {UserId} in cycle {Cycle}.",
                        objective.Key, userId, cycleKey);
                }
            }
        }
    }

    /// <summary>
    /// Whether one result counts toward one objective. Scope and game are filters; a null on either
    /// means "any", which is the common case.
    /// </summary>
    private static bool Matches(Objective objective, GameResult result)
    {
        if (!string.Equals(objective.Metric, result.Metric, StringComparison.Ordinal))
            return false;

        if (objective.GameId is { } gameId && gameId != result.GameId)
            return false;

        if (objective.Scope is { } scope
            && !string.Equals(scope, result.Scope, StringComparison.OrdinalIgnoreCase))
            return false;

        // Availability is judged on when the gameplay happened, not on when the projector ran — a
        // backfill days later must not decide an event quest was over.
        if (objective.AvailableFromUtc is { } from && result.OccurredAtUtc < from)
            return false;

        if (objective.AvailableToUtc is { } to && result.OccurredAtUtc > to)
            return false;

        return true;
    }

    /// <summary>
    /// How long a row stays claimable. The cycle's end plus a generous grace, because a completed
    /// quest must survive the cycle that produced it — see <c>UserObjectiveProgress</c>.
    /// </summary>
    private static DateTime? ClaimableUntil(Objective objective, DateTime occurredAtUtc)
    {
        var ends = ObjectiveCycle.EndsAtUtc(objective.Kind, occurredAtUtc);

        return ends?.AddDays(ClaimGraceDays);
    }

    /// <summary>
    /// Days past a cycle's end that a finished objective stays collectable. Generous on purpose:
    /// the storage is a row, and the alternative is telling a child who was away for the weekend
    /// that what they earned is gone.
    /// </summary>
    private const int ClaimGraceDays = 7;

    private Task<List<Objective>> ActiveObjectivesAsync(CancellationToken cancellationToken) =>
        _dbContext.Objectives
            .AsNoTracking()
            .Where(o => o.IsActive)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The lowest sequence worth re-reading for this player: below it, every counter has already
    /// folded everything in. Zero when they have no rows yet, which is what makes a newly authored
    /// objective backfill over their whole history.
    /// </summary>
    private static long FloorFor(
        IReadOnlyList<Objective> objectives, List<UserObjectiveProgress> progress)
    {
        if (progress.Count == 0) return 0;

        // An objective with no row at all has counted nothing, so the floor has to be zero or its
        // first cycle would start mid-stream and silently miss everything before now.
        var objectiveIds = objectives.Select(o => o.Id).ToHashSet();

        foreach (var id in objectiveIds)
        {
            if (!progress.Any(p => p.ObjectiveId == id))
                return 0;
        }

        return progress.Where(p => objectiveIds.Contains(p.ObjectiveId)).Min(p => p.LastSequence);
    }
}
