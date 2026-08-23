using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Turns recorded results into ranked entries.
/// <para>
/// Everything here is built around one property: **replaying a result must change nothing.** A
/// shared host recycles workers mid-batch, a job table delivers at-least-once, and an operator
/// will eventually rebuild a cycle by hand. If any of those could double-count, every number on
/// every board would be quietly untrustworthy, and nobody would find out until a child was paid
/// for a rank they did not earn.
/// </para>
/// </summary>
public class LeaderboardProjector : ILeaderboardProjector
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDisplayNameService _displayNames;
    private readonly ILogger<LeaderboardProjector> _logger;

    public LeaderboardProjector(
        ApplicationDbContext dbContext,
        IDisplayNameService displayNames,
        ILogger<LeaderboardProjector> logger)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
        _logger = logger;
    }

    public async Task<int> ProjectPendingAsync(
        int batchSize, CancellationToken cancellationToken = default)
    {
        // Oldest first, so a cycle boundary is crossed in the order the results actually happened
        // rather than the order the database felt like returning them.
        var pending = await _dbContext.GameResults
            .Where(r => r.ProjectedAtUtc == null && !r.IsFlagged)
            .OrderBy(r => r.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return 0;

        var boards = await ActiveBoardsAsync(cancellationToken);

        if (boards.Count == 0)
        {
            // No board wants these yet. Stamp them anyway — they stay in GameResults forever, so a
            // board authored next month is backfilled by a rebuild rather than by leaving an
            // ever-growing pending queue for the projector to re-read on every pass.
            await StampAsync(pending, cancellationToken);
            return pending.Count;
        }

        var handles = await _displayNames.EnsureHandlesAsync(
            pending.Select(r => r.UserId).Distinct().ToList(), cancellationToken);

        var hidden = await HiddenUsersAsync(handles.Keys, cancellationToken);

        var touchedCycles = new HashSet<Guid>();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var result in pending)
        {
            foreach (var board in boards)
            {
                if (!Selects(board, result))
                    continue;

                var cycle = await CycleForAsync(board, result.OccurredAtUtc, cancellationToken);

                if (cycle is null)
                    continue;

                foreach (var (cohort, cohortKey) in CohortsFor(board, result))
                {
                    await ApplyAsync(
                        cycle, cohort, cohortKey, result, board.Aggregation,
                        handles[result.UserId], hidden.Contains(result.UserId), cancellationToken);
                }

                touchedCycles.Add(cycle.Id);
            }

            // Claimed in the same transaction as the entries it moved. A crash before the commit
            // leaves it unclaimed and the next pass redoes exactly the work that was lost.
            result.ProjectedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Ranks are recomputed by a separate job rather than inline: one pass over a cycle costs
        // the same whether it follows one result or five hundred, so doing it per result would
        // multiply the expensive half of the work by the cheap half's frequency.
        foreach (var cycleId in touchedCycles)
            await EnqueueReindexAsync(cycleId, cancellationToken);

        _logger.LogInformation(
            "Projected {Count} results across {Cycles} cycles.", pending.Count, touchedCycles.Count);

        return pending.Count;
    }

    /// <summary>
    /// Folds one result into one player's row, honouring the board's aggregation.
    /// </summary>
    private async Task ApplyAsync(
        LeaderboardCycle cycle,
        LeaderboardCohort cohort,
        Guid cohortKey,
        GameResult result,
        LeaderboardAggregation aggregation,
        string handle,
        bool isHidden,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.LeaderboardEntries.FirstOrDefaultAsync(
            e => e.CycleId == cycle.Id
                 && e.Cohort == cohort
                 && e.CohortKey == cohortKey
                 && e.UserId == result.UserId,
            cancellationToken);

        if (entry is null)
        {
            _dbContext.LeaderboardEntries.Add(new LeaderboardEntry
            {
                Id = Guid.NewGuid(),
                CycleId = cycle.Id,
                Cohort = cohort,
                CohortKey = cohortKey,
                UserId = result.UserId,
                Value = result.Value,
                AchievedAtUtc = result.OccurredAtUtc,
                DisplayName = handle,
                IsHidden = isHidden,
                LastResultId = result.Id,
                UpdatedAtUtc = DateTime.UtcNow
            });

            cycle.TotalRanked += 1;
            return;
        }

        switch (aggregation)
        {
            case LeaderboardAggregation.Best:
                // A worse result is a no-op. Demoting on it would make "stop playing once you are
                // ahead" the winning strategy, which is the opposite of the point.
                if (!Beats(result.Value, entry.Value, cycle.Board?.SortDirection ?? LeaderboardSortDirection.Desc))
                    return;

                entry.Value = result.Value;
                entry.AchievedAtUtc = result.OccurredAtUtc;
                break;

            case LeaderboardAggregation.Sum:
                entry.Value += result.Value;

                // The tie-break stays at the *first* contribution: a player who reached 500 points
                // on Monday should outrank one who reached 500 on Friday, and moving the timestamp
                // on every addition would reverse that.
                break;

            case LeaderboardAggregation.Last:
                entry.Value = result.Value;
                entry.AchievedAtUtc = result.OccurredAtUtc;
                break;
        }

        entry.DisplayName = handle;
        entry.IsHidden = isHidden;
        entry.LastResultId = result.Id;
        entry.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static bool Beats(long candidate, long current, LeaderboardSortDirection direction) =>
        direction == LeaderboardSortDirection.Desc ? candidate > current : candidate < current;

    public async Task ReindexCycleAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        var cycle = await _dbContext.LeaderboardCycles
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);

        if (cycle?.Board is null)
            return;

        var descending = cycle.Board.SortDirection == LeaderboardSortDirection.Desc;

        // Done in SQL rather than by loading rows: a popular cycle is hundreds of thousands of
        // entries, and pulling them into memory to number them would be the single largest
        // allocation in the process.
        //
        // ROW_NUMBER over (value, then earliest achiever) is the whole ranking rule, and the
        // ordering matches IX_LeaderboardEntry_Ordering so the window function reads an index
        // rather than sorting.
        var valueOrder = descending ? "DESC" : "ASC";

        var sql = $"""
            WITH ranked AS (
                SELECT
                    [Id],
                    ROW_NUMBER() OVER (
                        PARTITION BY [CycleId], [Cohort], [CohortKey]
                        ORDER BY [Value] {valueOrder}, [AchievedAtUtc] ASC, [Id] ASC
                    ) AS [NewRank]
                FROM [LeaderboardEntries]
                WHERE [CycleId] = @cycleId AND [IsFlagged] = 0
            )
            UPDATE e
            SET e.[Rank] = r.[NewRank]
            FROM [LeaderboardEntries] e
            INNER JOIN ranked r ON r.[Id] = e.[Id]
            WHERE e.[Rank] <> r.[NewRank];
            """;

        await _dbContext.Database.ExecuteSqlRawAsync(
            sql,
            [new Microsoft.Data.SqlClient.SqlParameter("@cycleId", cycleId)],
            cancellationToken);

        // Hidden players keep their rank — they are excluded from listings, not from the ladder.
        // Flagged ones are excluded from ranking entirely and are left at 0 by the filter above.
        cycle.TotalRanked = await _dbContext.LeaderboardEntries
            .CountAsync(e => e.CycleId == cycleId && !e.IsFlagged, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RebuildCycleAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        var cycle = await _dbContext.LeaderboardCycles
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);

        if (cycle?.Board is null)
            return;

        _logger.LogWarning("Rebuilding leaderboard cycle {CycleId} from GameResults.", cycleId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await _dbContext.LeaderboardEntries
            .Where(e => e.CycleId == cycleId)
            .ExecuteDeleteAsync(cancellationToken);

        // Unclaim every result this cycle covers so the ordinary projection path replays them.
        // Rebuild deliberately reuses that path rather than having a second implementation — two
        // code paths that must agree on ranking is one more than can be kept in agreement.
        var grace = TimeSpan.FromSeconds(cycle.Board.GraceSeconds);

        await _dbContext.GameResults
            .Where(r => r.OccurredAtUtc >= cycle.StartsAtUtc
                        && r.OccurredAtUtc < cycle.EndsAtUtc.Add(grace)
                        && (cycle.Board.GameId == null || r.GameId == cycle.Board.GameId)
                        && r.Metric == cycle.Board.Metric)
            .ExecuteUpdateAsync(
                update => update.SetProperty(r => r.ProjectedAtUtc, (DateTime?)null),
                cancellationToken);

        cycle.TotalRanked = 0;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ------------------------------------------------------------- selection

    private Task<List<LeaderboardBoard>> ActiveBoardsAsync(CancellationToken cancellationToken) =>
        _dbContext.LeaderboardBoards
            .Where(b => b.IsActive)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Whether a board wants this result. Kept deliberately narrow — game, metric, grade and
    /// language — because every condition here runs per result per board.
    /// </summary>
    private static bool Selects(LeaderboardBoard board, GameResult result)
    {
        if (!string.Equals(board.Metric, result.Metric, StringComparison.Ordinal))
            return false;

        if (board.GameId is { } gameId && gameId != result.GameId)
            return false;

        if (board.GradeId is { } gradeId && gradeId != result.GradeId)
            return false;

        if (board.LangId is { } langId && langId != result.LangId)
            return false;

        return true;
    }

    /// <summary>
    /// Which cohorts this result lands in.
    /// <para>
    /// **Only the two the schema can answer.** <c>All</c> always, and <c>Grade</c> when the result
    /// carried a grade snapshot. School, class, friends and country are declared on the enum and
    /// refused at board authoring, because there is no enrolment relation, no social graph and no
    /// country on the profile to resolve them from.
    /// </para>
    /// </summary>
    private static IEnumerable<(LeaderboardCohort Cohort, Guid Key)> CohortsFor(
        LeaderboardBoard board, GameResult result)
    {
        var supported = board.SupportedCohorts;

        if (supported.Contains(nameof(LeaderboardCohort.All), StringComparison.OrdinalIgnoreCase))
            yield return (LeaderboardCohort.All, Guid.Empty);

        if (result.GradeId is { } gradeId
            && supported.Contains(nameof(LeaderboardCohort.Grade), StringComparison.OrdinalIgnoreCase))
        {
            yield return (LeaderboardCohort.Grade, gradeId);
        }
    }

    /// <summary>
    /// The open cycle a result belongs in, or null when none does.
    /// <para>
    /// A result that arrives after its cycle closed is still accepted inside the board's grace
    /// window. A device on a poor connection is not cheating, and dropping a child's run because
    /// their bus went through a tunnel is not a defensible anti-cheat posture.
    /// </para>
    /// </summary>
    private async Task<LeaderboardCycle?> CycleForAsync(
        LeaderboardBoard board, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var grace = TimeSpan.FromSeconds(board.GraceSeconds);

        return await _dbContext.LeaderboardCycles
            .Include(c => c.Board)
            .FirstOrDefaultAsync(
                c => c.BoardId == board.Id
                     && c.StartsAtUtc <= occurredAtUtc
                     && occurredAtUtc < c.EndsAtUtc
                     && (c.State == LeaderboardCycleState.Open
                         || (c.State == LeaderboardCycleState.Closed
                             && c.ClosedAtUtc != null
                             && occurredAtUtc >= c.ClosedAtUtc.Value - grace)),
                cancellationToken);
    }

    private async Task<HashSet<Guid>> HiddenUsersAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.ToList();

        var listed = await _dbContext.PlayerDisplayNames
            .AsNoTracking()
            .Where(n => ids.Contains(n.UserId) && (n.IsHidden || n.IsHiddenByGuardian))
            .Select(n => n.UserId)
            .ToListAsync(cancellationToken);

        return listed.ToHashSet();
    }

    private async Task StampAsync(List<GameResult> results, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        foreach (var result in results)
            result.ProjectedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnqueueReindexAsync(Guid cycleId, CancellationToken cancellationToken)
    {
        var outstanding = await _dbContext.LeaderboardJobs.AnyAsync(
            j => j.Kind == LeaderboardJobKind.Reindex
                 && j.CycleId == cycleId
                 && (j.State == LeaderboardJobState.Pending || j.State == LeaderboardJobState.Running),
            cancellationToken);

        if (outstanding)
            return;

        _dbContext.LeaderboardJobs.Add(new LeaderboardJob
        {
            Id = Guid.NewGuid(),
            Kind = LeaderboardJobKind.Reindex,
            CycleId = cycleId,
            State = LeaderboardJobState.Pending,
            RunAfterUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another worker enqueued the same reindex between the check and the insert. The
            // unique filtered index caught it, which is exactly what it is for.
            _dbContext.ChangeTracker.Clear();
        }
    }
}
