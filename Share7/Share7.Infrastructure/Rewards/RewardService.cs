using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Economy;
using Share7.Domain.Progress;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Rewards;

/// <summary>
/// Turns a validated attempt into currency. Three invariants hold throughout:
/// <list type="bullet">
/// <item>the amount comes from <see cref="RewardRule"/>, never from anything the client sent;</item>
/// <item>one rule pays a user once per idempotency key, enforced by a unique index rather than by
/// the read that precedes it;</item>
/// <item>a rule pays everything it grants or nothing at all, and failing to pay never costs the
/// student their progress.</item>
/// </list>
/// </summary>
public class RewardService : IRewardService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWalletService _wallet;

    public RewardService(ApplicationDbContext dbContext, IWalletService wallet)
    {
        _dbContext = dbContext;
        _wallet = wallet;
    }

    public async Task<IReadOnlyList<RewardDto>> EvaluateProgressAttemptAsync(
        ProgressRewardContext context,
        CancellationToken cancellationToken = default)
    {
        // Not a defensive nicety: without the caller's transaction, a reward could commit while
        // the progress that earned it rolled back.
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Rewards must be evaluated inside an open transaction so that progress and payout commit together.");

        var events = FiredEvents(context.CompletionState);
        var reference = context.LessonId.ToString();

        // AsNoTracking keeps configuration out of the change tracker, so the savepoint rollback
        // below only ever has reward and ledger writes to clean up.
        var rules = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .Where(r => r.Enabled
                        && events.Contains(r.EventType)
                        && (r.ReferenceKey == null || r.ReferenceKey == reference))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var submissionKey = SubmissionKeyFor(context);
        var paid = new List<RewardDto>();

        for (var i = 0; i < rules.Count; i++)
        {
            var reward = await TryPayAsync(rules[i], context, submissionKey, transaction, i, cancellationToken);

            if (reward is not null)
                paid.Add(reward);
        }

        return paid;
    }

    private async Task<RewardDto?> TryPayAsync(
        RewardRule rule,
        ProgressRewardContext context,
        string submissionKey,
        IDbContextTransaction transaction,
        int index,
        CancellationToken cancellationToken)
    {
        if (rule.Grants.Count == 0)
            return null;

        // One retired currency disables the whole rule. Paying the remaining lines would record a
        // partial reward as a completed one, and the idempotency key would then stop it ever being
        // completed properly.
        if (rule.Grants.Any(g => g.Currency is null || !g.Currency.Enabled))
            return null;

        var idempotencyKey = IdempotencyKeyFor(rule, context, submissionKey);

        var existing = await _dbContext.RewardTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
            .ThenInclude(l => l.Currency)
            .FirstOrDefaultAsync(
                t => t.UserId == context.UserId
                     && t.RewardRuleId == rule.Id
                     && t.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
            // Replay only when this very submission is what paid. A `Once` rule matched by a later
            // attempt has already been paid and must report nothing, or the client shows a reward
            // animation for currency it is not receiving.
            return existing.SubmissionKey == submissionKey ? Replay(rule, existing) : null;

        if (!await PassesRepeatConstraintsAsync(rule, context.UserId, cancellationToken))
            return null;

        return await PayAsync(rule, context, submissionKey, idempotencyKey, transaction, index, cancellationToken);
    }

    /// <summary>
    /// Cooldown and daily limit, both meaningful only for <see cref="RewardRepeatPolicy.EveryTime"/>.
    /// <c>Once</c> needs neither — its idempotency key already makes a second payment impossible.
    /// </summary>
    private async Task<bool> PassesRepeatConstraintsAsync(
        RewardRule rule,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (rule.RepeatPolicy != RewardRepeatPolicy.EveryTime)
            return true;

        if (rule.CooldownSeconds is null && rule.DailyLimit is null)
            return true;

        var now = DateTime.UtcNow;

        if (rule.CooldownSeconds is { } cooldownSeconds)
        {
            var lastPaidAt = await _dbContext.RewardTransactions
                .Where(t => t.UserId == userId && t.RewardRuleId == rule.Id)
                .MaxAsync(t => (DateTime?)t.CreatedAtUtc, cancellationToken);

            if (lastPaidAt is { } lastAt && lastAt.AddSeconds(cooldownSeconds) > now)
                return false;
        }

        if (rule.DailyLimit is { } dailyLimit)
        {
            var startOfDayUtc = now.Date;

            var paidToday = await _dbContext.RewardTransactions
                .CountAsync(
                    t => t.UserId == userId && t.RewardRuleId == rule.Id && t.CreatedAtUtc >= startOfDayUtc,
                    cancellationToken);

            if (paidToday >= dailyLimit)
                return false;
        }

        return true;
    }

    private async Task<RewardDto?> PayAsync(
        RewardRule rule,
        ProgressRewardContext context,
        string submissionKey,
        string idempotencyKey,
        IDbContextTransaction transaction,
        int index,
        CancellationToken cancellationToken)
    {
        // Scoped to this rule, so a rule that cannot be paid is abandoned on its own without
        // taking the attempt's progress writes — or the other rules' payouts — down with it.
        var savepoint = $"reward_{index}";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);

        var rewardTransaction = new RewardTransaction
        {
            Id = Guid.NewGuid(),
            UserId = context.UserId,
            RewardRuleId = rule.Id,
            EventType = rule.EventType,
            SourceType = LedgerSourceType.ProgressAttempt,
            SourceId = context.LessonId.ToString(),
            IdempotencyKey = idempotencyKey,
            SubmissionKey = submissionKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.RewardTransactions.Add(rewardTransaction);

        try
        {
            // Claim the key before moving any money. The unique index decides the winner when two
            // submissions race here; the SELECT above only spares the common case from reaching it.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await AbandonAsync(transaction, savepoint, cancellationToken);
            return null;
        }

        var metadata = JsonSerializer.Serialize(new
        {
            ruleId = rule.Id,
            gameId = context.GameId,
            lessonId = context.LessonId,
            percent = context.Percent,
            attempt = context.AttemptNumber
        });

        var grants = new List<RewardGrantDto>();

        // Ordered by currency key so the ledger, the lines and the response all agree, and two
        // runs of the same reward produce byte-identical output.
        foreach (var grant in rule.Grants.OrderBy(g => g.Currency!.Key, StringComparer.Ordinal))
        {
            var applied = await _wallet.ApplyAsync(
                new WalletMutation
                {
                    UserId = context.UserId,
                    CurrencyId = grant.CurrencyId,
                    Delta = grant.Amount,
                    TransactionType = rule.TransactionType,
                    SourceType = LedgerSourceType.ProgressAttempt,
                    SourceId = context.LessonId.ToString(),
                    IdempotencyKey = idempotencyKey,
                    Metadata = metadata
                },
                cancellationToken);

            if (!applied.Succeeded)
            {
                // Everything this rule has done so far goes, including the claimed key — so a
                // configuration fixed later can pay it properly instead of finding it marked done.
                await AbandonAsync(transaction, savepoint, cancellationToken);
                return null;
            }

            _dbContext.RewardTransactionLines.Add(new RewardTransactionLine
            {
                RewardTransactionId = rewardTransaction.Id,
                CurrencyId = grant.CurrencyId,
                Amount = grant.Amount,
                BalanceAfter = applied.Value!.Amount,
                LedgerEntryId = applied.Value.LedgerEntryId
            });

            grants.Add(new RewardGrantDto
            {
                Currency = applied.Value.Currency,
                Amount = grant.Amount
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RewardDto
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            EventType = WireEnum.ToWire(rule.EventType),
            TransactionId = rewardTransaction.Id,
            Grants = grants
        };
    }

    /// <summary>
    /// Undoes this rule's writes and clears them from the change tracker.
    /// <para>
    /// The detach is not optional. Rows saved before the rollback are still tracked as
    /// <c>Unchanged</c> although they no longer exist, and a row whose save threw is still
    /// <c>Added</c> — the caller's next <c>SaveChangesAsync</c> would retry it and hit the same
    /// constraint, this time outside any savepoint.
    /// </para>
    /// </summary>
    private async Task AbandonAsync(
        IDbContextTransaction transaction,
        string savepoint,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);

        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is RewardTransaction or RewardTransactionLine or CurrencyLedgerEntry)
                entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Which events a finished attempt raises. An aced lesson raises all three, so passing and
    /// acing are separate rules that both pay rather than one rule with a branch inside it.
    /// </summary>
    private static IReadOnlyList<RewardEventType> FiredEvents(CompletionState state) => state switch
    {
        CompletionState.Aced =>
        [
            RewardEventType.LessonAttempted,
            RewardEventType.LessonCompleted,
            RewardEventType.LessonAced
        ],
        CompletionState.Completed =>
        [
            RewardEventType.LessonAttempted,
            RewardEventType.LessonCompleted
        ],
        _ => [RewardEventType.LessonAttempted]
    };

    /// <summary>
    /// Identifies one submission. Prefers the client's request id, which survives a retry, and
    /// falls back to the attempt ordinal for clients that do not send one — where a retry is
    /// indistinguishable from a genuine replay and will be paid again by an
    /// <see cref="RewardRepeatPolicy.EveryTime"/> rule.
    /// </summary>
    private static string SubmissionKeyFor(ProgressRewardContext context) =>
        string.IsNullOrWhiteSpace(context.RequestId)
            ? $"{context.GameId}:{context.LessonId}:n{context.AttemptNumber}"
            : $"{context.GameId}:{context.LessonId}:req:{context.RequestId.Trim()}";

    /// <summary>
    /// What "already paid" means for this rule. <c>Once</c> keys on the event and its lesson, so
    /// every later attempt collides with the first and is refused. <c>EveryTime</c> keys on the
    /// submission, so distinct attempts each pay while a retry of one does not.
    /// <para>
    /// The lesson is scoped by game, matching how progress itself is tracked: the same lesson in a
    /// different game is a different ladder, and starting it should not find its rewards already
    /// spent.
    /// </para>
    /// </summary>
    private static string IdempotencyKeyFor(RewardRule rule, ProgressRewardContext context, string submissionKey) =>
        rule.RepeatPolicy == RewardRepeatPolicy.Once
            ? $"{WireEnum.ToWire(rule.EventType)}:{context.GameId}:{context.LessonId}"
            : $"{WireEnum.ToWire(rule.EventType)}:{submissionKey}";

    private static RewardDto Replay(RewardRule rule, RewardTransaction existing) => new()
    {
        RuleId = rule.Id,
        RuleName = rule.Name,
        EventType = WireEnum.ToWire(existing.EventType),
        TransactionId = existing.Id,
        Grants = existing.Lines
            .OrderBy(l => l.Currency!.Key, StringComparer.Ordinal)
            .Select(l => new RewardGrantDto { Currency = l.Currency!.Key, Amount = l.Amount })
            .ToList()
    };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
