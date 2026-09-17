using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Awards an event's prize table when its ladder becomes final.
/// <para>
/// <b>Written to be run twice.</b> The settlement job is retried by design, so every placing's award
/// is claimed by a unique index before anything is paid, and the payment itself goes through the
/// reward engine's own idempotency. A child being paid twice for first place is a defect nobody
/// reports; a child being paid once, late, is one they will.
/// </para>
/// <para>
/// <b>One tier per placing — the narrowest one that covers the rank.</b> That is the opposite of how
/// the weekly boards pay, where every band a rank falls inside pays and the prizes compose. A prize
/// table written by an operator ("1st: a tablet, 2nd–3rd: 500 coins, 4th–10th: 100 coins") means
/// exactly one of its rows per winner, and paying all three to first place would be a surprise
/// nobody authored.
/// </para>
/// </summary>
public class EventPrizeAwardService : ICycleSettlementObserver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRewardService _rewards;
    private readonly ILogger<EventPrizeAwardService> _logger;

    public EventPrizeAwardService(
        ApplicationDbContext dbContext,
        IRewardService rewards,
        ILogger<EventPrizeAwardService> logger)
    {
        _dbContext = dbContext;
        _rewards = rewards;
        _logger = logger;
    }

    public async Task OnCycleSettlingAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        var playEvent = await _dbContext.PlayEvents
            .AsNoTracking()
            .Include(e => e.PrizeTiers)
            .FirstOrDefaultAsync(e => e.CycleId == cycleId, cancellationToken);

        // The ordinary case: a weekly board settling, with no event bound to it.
        if (playEvent is null) return;

        if (playEvent.CancelledAtUtc is not null || !playEvent.IsActive)
        {
            // A called-off event pays nothing, however far its ladder got. Said out loud in the log
            // because "the event ran and nobody was paid" is a support question either way.
            _logger.LogWarning(
                "Event {EventKey} settled while cancelled or inactive; no prizes were awarded.",
                playEvent.EventKey);
            return;
        }

        var tiers = playEvent.PrizeTiers
            .OrderBy(t => t.Width)
            .ThenBy(t => t.FromRank)
            .ToList();

        if (tiers.Count == 0) return;

        // Only the cohort the prize table ranks within. An event that pays per grade has its placings
        // in the Grade rows, and the All rows are a ladder nobody is being paid off.
        var placings = await _dbContext.LeaderboardSettlements
            .AsNoTracking()
            .Where(s => s.CycleId == cycleId && s.Cohort == playEvent.PrizeCohort && s.FinalRank > 0)
            .OrderBy(s => s.CohortKey)
            .ThenBy(s => s.FinalRank)
            .ToListAsync(cancellationToken);

        if (placings.Count == 0) return;

        // How many of each limited prize have already gone out, so a retry does not re-issue them and
        // a tier with three physical prizes does not promise ten.
        var issued = await _dbContext.EventAwards
            .AsNoTracking()
            .Where(a => a.EventId == playEvent.Id)
            .GroupBy(a => a.TierId)
            .Select(g => new { TierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TierId, x => x.Count, cancellationToken);

        var awarded = 0;

        foreach (var placing in placings)
        {
            var tier = tiers.FirstOrDefault(t => t.Covers(placing.FinalRank));

            if (tier is null) continue;

            if (tier.Quantity is { } quantity && issued.GetValueOrDefault(tier.Id) >= quantity)
                continue;

            if (await AwardAsync(playEvent, tier, placing, cancellationToken))
            {
                issued[tier.Id] = issued.GetValueOrDefault(tier.Id) + 1;
                awarded++;
            }
        }

        if (awarded > 0)
            _logger.LogInformation(
                "Event {EventKey} awarded {Count} prize(s) from {Placings} placing(s).",
                playEvent.EventKey, awarded, placings.Count);
    }

    /// <summary>
    /// Writes one award and pays it, in one transaction.
    /// <para>
    /// The award row is inserted and saved <b>before</b> anything is granted, so the unique index over
    /// (event, cohort, user) is what decides a race rather than a read that happened to get there
    /// first. A loser of that race rolls back having paid nothing.
    /// </para>
    /// </summary>
    private async Task<bool> AwardAsync(
        PlayEvent playEvent,
        EventPrizeTier tier,
        LeaderboardSettlement placing,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var award = new EventAward
        {
            Id = Guid.NewGuid(),
            EventId = playEvent.Id,
            TierId = tier.Id,
            UserId = placing.UserId,
            Cohort = placing.Cohort,
            CohortKey = placing.CohortKey,
            FinalRank = placing.FinalRank,
            Value = placing.Value,

            // A real-world prize is won here and delivered by a person, so it is born waiting.
            State = tier.Kind == EventPrizeKind.RealWorld
                ? EventAwardState.AwaitingClaim
                : EventAwardState.Granted,
            CreatedAtUtc = now
        };

        _dbContext.EventAwards.Add(award);

        if (tier.Kind == EventPrizeKind.RealWorld)
        {
            _dbContext.PrizeClaims.Add(new PrizeClaim
            {
                Id = Guid.NewGuid(),
                AwardId = award.Id,
                UserId = placing.UserId,
                State = PrizeClaimState.PendingReview,

                // A deadline, so an unanswered claim becomes a fact rather than an open obligation.
                ExpiresAtUtc = now.AddDays(Math.Max(1, playEvent.ClaimWindowDays)),
                CreatedAtUtc = now
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // This placing already has its award — a previous run of the retried job wrote it. Not an
            // error: it is the index doing exactly what it is there for.
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return false;
        }

        if (tier.Kind == EventPrizeKind.InGame && tier.RewardRuleId is { } ruleId)
        {
            var paid = await _rewards.EvaluateEventPrizeAsync(
                new EventPrizeRewardContext
                {
                    UserId = placing.UserId,
                    RewardRuleId = ruleId,
                    EventId = playEvent.Id,
                    TierId = tier.Id,
                    Cohort = placing.Cohort.ToString(),
                    CohortKey = placing.CohortKey,
                    FinalRank = placing.FinalRank,
                    Value = placing.Value
                },
                cancellationToken);

            var transactionId = paid.FirstOrDefault()?.TransactionId;

            if (transactionId is { } id)
            {
                var tracked = await _dbContext.EventAwards.FirstAsync(a => a.Id == award.Id, cancellationToken);
                tracked.RewardTransactionId = id;
                tracked.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
