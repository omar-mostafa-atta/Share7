using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Common.Models;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Freezes a closed cycle's ranks and pays what they earned.
/// <para>
/// **The settlement job is retried by design**, because the job table delivers at-least-once and a
/// shared host can kill a worker mid-payout. Everything here is therefore arranged so that running
/// it twice pays once: the settlement row's unique index claims the placing, and
/// <c>RewardIssued</c> is set in the same transaction as the grant. Paying a child twice for third
/// place is a defect nobody reports.
/// </para>
/// </summary>
public class LeaderboardSettlementService : ILeaderboardSettlementService
{
    /// <summary>
    /// How many placings are settled per transaction. Small enough that a shared host is never
    /// holding a long write transaction across a whole board, large enough that a big cycle does
    /// not take a thousand round trips.
    /// </summary>
    private const int BatchSize = 200;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILeaderboardProjector _projector;
    private readonly IRewardService _rewards;
    private readonly ILogger<LeaderboardSettlementService> _logger;

    public LeaderboardSettlementService(
        ApplicationDbContext dbContext,
        ILeaderboardProjector projector,
        IRewardService rewards,
        ILogger<LeaderboardSettlementService> logger)
    {
        _dbContext = dbContext;
        _projector = projector;
        _rewards = rewards;
        _logger = logger;
    }

    public async Task<ServiceResult> SettleAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        var cycle = await _dbContext.LeaderboardCycles
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);

        if (cycle?.Board is null)
        {
            return ServiceResult.Failure(
                ApiErrors.LeaderboardCycleNotFound, ServiceErrorKind.NotFound, "No such cycle.");
        }

        // Already done. Returning success rather than a conflict because the caller is a retrying
        // job, and a retry finding the work complete is the system behaving correctly.
        if (cycle.State == LeaderboardCycleState.Settled)
            return ServiceResult.Success();

        if (cycle.State != LeaderboardCycleState.Closed)
        {
            return ServiceResult.Failure(
                ApiErrors.LeaderboardBoardInvalid, ServiceErrorKind.Conflict,
                "A cycle has to be closed before it can be settled.");
        }

        // One last reindex. Results that arrived inside the grace window after closing are counted
        // here — a child whose bus went through a tunnel should not lose their placing — and after
        // this point the ranks never move again.
        await _projector.ProjectPendingAsync(int.MaxValue, cancellationToken);
        await _projector.ReindexCycleAsync(cycleId, cancellationToken);

        var settled = await FreezeAsync(cycle, cancellationToken);
        var paid = await PayAsync(cycle, cancellationToken);

        cycle.State = LeaderboardCycleState.Settled;
        cycle.SettledAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Settled cycle {CycleId} of {BoardKey}: {Settled} placings frozen, {Paid} paid.",
            cycleId, cycle.Board.BoardKey, settled, paid);

        return ServiceResult.Success();
    }

    /// <summary>
    /// Writes the immutable record of where everybody finished.
    /// <para>
    /// Separate from the entry table because the two answer different questions forever: an entry
    /// is a projection a rebuild may recalculate, while this is what the player was actually told.
    /// A rebuild that changed somebody's already-awarded third place would be rewriting history.
    /// </para>
    /// </summary>
    private async Task<int> FreezeAsync(LeaderboardCycle cycle, CancellationToken cancellationToken)
    {
        var written = 0;
        var lastId = Guid.Empty;

        while (true)
        {
            // Keyset paging by entry id rather than Skip/Take: the set is stable here (ranks are
            // frozen), but an offset walk over a large cycle re-scans from the start every batch.
            var batch = await _dbContext.LeaderboardEntries
                .AsNoTracking()
                .Where(e => e.CycleId == cycle.Id && !e.IsFlagged && e.Rank > 0 && e.Id.CompareTo(lastId) > 0)
                .OrderBy(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var entry in batch)
            {
                // Hidden players are settled and paid exactly like everyone else. Opting out of
                // being *listed* is not opting out of having competed, and quietly withholding a
                // child's prize because they asked not to be shown would be a nasty surprise.
                _dbContext.LeaderboardSettlements.Add(new LeaderboardSettlement
                {
                    Id = Guid.NewGuid(),
                    CycleId = cycle.Id,
                    Cohort = entry.Cohort,
                    CohortKey = entry.CohortKey,
                    UserId = entry.UserId,
                    FinalRank = entry.Rank,
                    Value = entry.Value,
                    RewardReferenceKey =
                        LeaderboardRankBands.TightestBandFor(cycle.Board!.BoardKey, entry.Rank),
                    RewardIssued = false,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                written += batch.Count;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // A previous run already froze some of this batch. The unique index is the
                // authority, so drop what did not land and carry on — re-freezing is a no-op by
                // definition, since the ranks it would write are the same ones.
                _dbContext.ChangeTracker.Clear();
            }

            lastId = batch[^1].Id;
        }

        return written;
    }

    /// <summary>
    /// Pays every frozen placing that a rule covers, one placing per transaction.
    /// <para>
    /// <c>RewardIssued</c> flips inside the same transaction as the grant. That is the entire
    /// idempotency guarantee: the job is retried, and a retry has to be able to tell paid from
    /// unpaid without trusting that it finished last time.
    /// </para>
    /// </summary>
    private async Task<int> PayAsync(LeaderboardCycle cycle, CancellationToken cancellationToken)
    {
        var paid = 0;

        while (true)
        {
            var batch = await _dbContext.LeaderboardSettlements
                .Where(s => s.CycleId == cycle.Id
                            && !s.RewardIssued
                            && s.RewardReferenceKey != null)
                .OrderBy(s => s.FinalRank)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var placing in batch)
            {
                if (await PayOneAsync(cycle, placing, cancellationToken))
                    paid++;
            }
        }

        return paid;
    }

    private async Task<bool> PayOneAsync(
        LeaderboardCycle cycle, LeaderboardSettlement placing, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var rewards = new List<RewardDto>();

            // Every band the rank falls in, so "a bigger prize for a better rank" is expressed as
            // several rules that compose rather than one rule with branching inside it.
            foreach (var reference in
                     LeaderboardRankBands.ReferenceKeysFor(cycle.Board!.BoardKey, placing.FinalRank))
            {
                rewards.AddRange(await _rewards.EvaluateSettlementAsync(
                    new SettlementRewardContext
                    {
                        UserId = placing.UserId,
                        CycleId = cycle.Id,
                        Cohort = placing.Cohort.ToString(),
                        CohortKey = placing.CohortKey,
                        ReferenceKey = reference,
                        FinalRank = placing.FinalRank,
                        Value = placing.Value
                    },
                    cancellationToken));
            }

            // Marked issued even when no rule matched. "Nobody authored a prize for rank 47" is a
            // finished placing, not an outstanding debt, and leaving it unmarked would make every
            // later run walk the whole board again looking for work that does not exist.
            placing.RewardIssued = true;
            placing.RewardIssuedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return rewards.Count > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            // One unpayable placing must not stop the rest of the board being paid. It stays
            // unissued and the next run picks it up.
            _logger.LogError(
                ex, "Could not pay rank {Rank} of cycle {CycleId}.", placing.FinalRank, cycle.Id);

            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
