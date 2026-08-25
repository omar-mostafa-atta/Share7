using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Share7.Application.Common.Models;
using Share7.Application.Commerce.Interfaces;
using Share7.Domain.Commerce;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Economy;
using Share7.Domain.Progress;
using Share7.Domain.Runs;
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
    private readonly ILevelService _levels;
    private readonly IEntitlementService _entitlements;

    public RewardService(
        ApplicationDbContext dbContext,
        IWalletService wallet,
        ILevelService levels,
        IEntitlementService entitlements)
    {
        _dbContext = dbContext;
        _wallet = wallet;
        _levels = levels;
        _entitlements = entitlements;
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
            .Include(r => r.EntitlementGrants)
            .ThenInclude(g => g.Product)
            .Where(r => r.Enabled
                        && events.Contains(r.EventType)
                        && (r.ReferenceKey == null || r.ReferenceKey == reference))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var target = new PayoutTarget(
            UserId: context.UserId,
            SourceType: LedgerSourceType.ProgressAttempt,
            SourceId: context.LessonId.ToString(),
            // A `Once` rule is scoped to the lesson in this game, so every later attempt collides
            // with the first and is refused.
            OnceKey: $"{context.GameId}:{context.LessonId}",
            SubmissionKey: SubmissionKeyFor(context),
            Metadata: JsonSerializer.Serialize(new
            {
                gameId = context.GameId,
                lessonId = context.LessonId,
                percent = context.Percent,
                attempt = context.AttemptNumber
            }),
            XpBaseline: context.XpBaseline);

        return await PayWithLevelUpsAsync(rules, target, transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<RewardDto>> EvaluateObjectiveAsync(
        ObjectiveRewardContext context,
        CancellationToken cancellationToken = default)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Objective rewards must be evaluated inside an open transaction so the payout and the claim commit together.");

        // Unlike settlement, an unscoped rule *is* matched: "any completed quest is worth 5 coins"
        // is a reasonable thing for an operator to author, and a specific rule composes with it
        // exactly as the lesson rules compose.
        var rules = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .Include(r => r.EntitlementGrants)
            .ThenInclude(g => g.Product)
            .Where(r => r.Enabled
                        && r.EventType == RewardEventType.ObjectiveCompleted
                        && (r.ReferenceKey == null || r.ReferenceKey == context.ObjectiveKey))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        // The objective in its cycle *is* the payout identity: one claim, ever. Both repeat
        // policies collapse onto it, so a retried claim collides with the first rather than paying
        // a second time, while next week's cycle is a genuinely different key.
        var claim = $"{context.ObjectiveKey}:{context.CycleKey}";

        var target = new PayoutTarget(
            UserId: context.UserId,
            SourceType: LedgerSourceType.System,
            SourceId: context.ObjectiveKey,
            OnceKey: claim,
            SubmissionKey: claim,
            Metadata: JsonSerializer.Serialize(new
            {
                objective = context.ObjectiveKey,
                cycle = context.CycleKey,
                requestId = context.RequestId
            }));

        return await PayWithLevelUpsAsync(rules, target, transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<RewardDto>> EvaluateSettlementAsync(
        SettlementRewardContext context,
        CancellationToken cancellationToken = default)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Settlement rewards must be evaluated inside an open transaction so the payout and the settlement row commit together.");

        // Unlike the attempt path, an unscoped rule is *not* matched. A global "pay for any
        // LEADERBOARD_SETTLED" rule would pay every ranked child on every board the same prize,
        // which is never what an operator means — the band is the whole point of the event.
        var rules = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .Include(r => r.EntitlementGrants)
            .ThenInclude(g => g.Product)
            .Where(r => r.Enabled
                        && r.EventType == RewardEventType.LeaderboardSettled
                        && r.ReferenceKey == context.ReferenceKey)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        // One placing is one payout, whatever the rule's repeat policy says. The key is the
        // placing itself, so a retried settlement job finds the key already spent.
        var placing = $"{context.CycleId}:{context.Cohort}:{context.CohortKey}:{context.UserId}";

        var target = new PayoutTarget(
            UserId: context.UserId,
            SourceType: LedgerSourceType.System,
            SourceId: context.CycleId.ToString(),
            OnceKey: placing,
            SubmissionKey: placing,
            Metadata: JsonSerializer.Serialize(new
            {
                cycleId = context.CycleId,
                cohort = context.Cohort,
                rank = context.FinalRank,
                value = context.Value,
                band = context.ReferenceKey
            }));

        return await PayWithLevelUpsAsync(rules, target, transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<RewardDto>> EvaluateRunSettlementAsync(
        RunRewardContext context,
        CancellationToken cancellationToken = default)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Run rewards must be evaluated inside an open transaction so the payout and the settled run commit together.");

        // Runs are scoped by game rather than lesson: a run is any bounded activity that ends and
        // settles, and most of them have no lesson at all. An unscoped rule *is* matched here — unlike
        // leaderboard settlement, "pay for finishing any run" is a thing an operator genuinely means.
        var reference = context.GameId.ToString();

        var rules = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .Include(r => r.EntitlementGrants)
            .ThenInclude(g => g.Product)
            .Where(r => r.Enabled
                        && r.EventType == RewardEventType.RunSettled
                        && (r.ReferenceKey == null || r.ReferenceKey == reference))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        // The run id *is* the submission key, and needs no fallback. A run settles exactly once — a
        // replayed result returns the stored settlement without ever reaching here — so unlike an
        // attempt there is no ordinal to disambiguate and no retry that can look like a new one.
        var target = new PayoutTarget(
            UserId: context.UserId,
            SourceType: LedgerSourceType.RunSettlement,
            SourceId: context.RunId.ToString(),
            // A `Once` rule is scoped to the game, so "first ever run of this game" pays once and
            // every later run collides with it.
            OnceKey: reference,
            SubmissionKey: $"run:{context.RunId}",
            Metadata: JsonSerializer.Serialize(new
            {
                gameId = context.GameId,
                runId = context.RunId,
                durationMs = context.DurationMs,
                outcome = WireEnum.ToWire(context.Outcome)
            }),
            XpBaseline: context.XpBaseline);

        return await PayWithLevelUpsAsync(rules, target, transaction, cancellationToken);
    }

    /// <summary>
    /// Runs every matching rule against one target. Shared by both entry points so there is
    /// exactly one place that can create currency.
    /// </summary>
    private async Task<IReadOnlyList<RewardDto>> PayMatchingAsync(
        List<RewardRule> rules,
        PayoutTarget target,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken,
        int indexOffset = 0)
    {
        var paid = new List<RewardDto>();

        for (var i = 0; i < rules.Count; i++)
        {
            var reward = await TryPayAsync(
                rules[i], target, transaction, indexOffset + i, cancellationToken);

            if (reward is not null)
                paid.Add(reward);
        }

        return paid;
    }

    /// <summary>
    /// Pays the matching rules, then pays for any level the XP they granted took the player past.
    /// <para>
    /// **Level-up is a consequence of a payout, so it is resolved after one — never inside it.**
    /// Several rules can each grant XP for a single attempt (attempted, completed and aced all
    /// fire), and the level that matters is the one reached once they have all landed. Detecting
    /// per rule would announce level 6 and then level 7 for what the child experienced as one
    /// result screen.
    /// </para>
    /// <para>
    /// **This runs exactly once and cannot re-enter.** A level-up rule that granted XP would be a
    /// payout causing the event that triggers it; XP grants are stripped from these rules here, and
    /// the rewards paid below are deliberately not fed back into detection. Authoring refuses such
    /// a rule too — this is the structural half of that guarantee, the half that holds even if a
    /// rule predates the validation.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<RewardDto>> PayWithLevelUpsAsync(
        List<RewardRule> rules,
        PayoutTarget target,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var paid = await PayMatchingAsync(rules, target, transaction, cancellationToken);

        var xpGranted = paid
            .SelectMany(reward => reward.Grants)
            .Where(grant => string.Equals(grant.Currency, _levels.XpCurrencyKey, StringComparison.Ordinal))
            .Sum(grant => grant.Amount);

        // Nothing moved the balance at all: no rule paid XP, and the caller did not grant any either.
        // Skipping the read here is what keeps the common payout — coins for a finished lesson — from
        // costing an extra query for a level that cannot have changed.
        if (xpGranted <= 0 && target.XpBaseline is null) return paid;

        // Read the balance rather than tracking it through the payout: it is authoritative, it is
        // one query, and it stays correct no matter which rule paid the XP or in what order.
        var after = await _levels.GetForUserAsync(target.UserId, cancellationToken);

        // The caller's own starting point when it has one, because it granted before calling here and
        // only it knows where the player actually began. Subtraction is the fallback for a caller that
        // grants nothing — see ProgressRewardContext.XpBaseline for why it is not enough on its own.
        var before = target.XpBaseline ?? after.Xp - xpGranted;

        var crossed = await _levels.LevelsCrossedAsync(before, after.Xp, cancellationToken);

        if (crossed.Count == 0) return paid;

        var levelRules = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .Include(r => r.EntitlementGrants)
            .ThenInclude(g => g.Product)
            .Where(r => r.Enabled && r.EventType == RewardEventType.PlayerLevelUp)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        if (levelRules.Count == 0) return paid;

        // Safe to mutate: these are untracked copies, fetched here and used nowhere else. A rule
        // left with nothing to pay is skipped by TryPayAsync rather than claiming its key.
        foreach (var rule in levelRules)
        {
            rule.Grants = rule.Grants
                .Where(grant => grant.CurrencyId != _levels.XpCurrencyId)
                .ToList();
        }

        var combined = new List<RewardDto>(paid);

        // Offset so each level's savepoints stay distinct from the main pass's and from each
        // other's, rather than reusing reward_0 and moving a savepoint another frame may roll to.
        var savepointOffset = rules.Count;

        foreach (var level in crossed)
        {
            var reference = level.ToString(CultureInfo.InvariantCulture);

            var matching = levelRules
                .Where(rule => rule.ReferenceKey == null || rule.ReferenceKey == reference)
                .ToList();

            if (matching.Count == 0) continue;

            // A level is reached once, ever — so both repeat policies collapse to the same key and
            // a replayed attempt cannot pay for level 7 twice.
            var levelKey = $"level:{reference}";

            var levelTarget = new PayoutTarget(
                UserId: target.UserId,
                SourceType: LedgerSourceType.System,
                SourceId: reference,
                OnceKey: levelKey,
                SubmissionKey: levelKey,
                Metadata: JsonSerializer.Serialize(new { level, xp = after.Xp }));

            combined.AddRange(await PayMatchingAsync(
                matching, levelTarget, transaction, cancellationToken, savepointOffset));

            savepointOffset += matching.Count;
        }

        return combined;
    }

    /// <summary>
    /// Everything a payout needs that differs between an attempt and a settlement. Introduced so
    /// the two share one engine — a second payout path would mean two places that can mint
    /// currency and two answers to "why does this child have these coins".
    /// </summary>
    private sealed record PayoutTarget(
        Guid UserId,
        LedgerSourceType SourceType,
        string SourceId,
        string OnceKey,
        string SubmissionKey,
        string Metadata,
        long? XpBaseline = null);

    private async Task<RewardDto?> TryPayAsync(
        RewardRule rule,
        PayoutTarget context,
        IDbContextTransaction transaction,
        int index,
        CancellationToken cancellationToken)
    {
        // A badge rule grants no currency at all, which is the normal shape for an achievement:
        // finishing it is the reward and the badge is what says so.
        if (rule.Grants.Count == 0 && rule.EntitlementGrants.Count == 0)
            return null;

        // One retired currency disables the whole rule. Paying the remaining lines would record a
        // partial reward as a completed one, and the idempotency key would then stop it ever being
        // completed properly.
        if (rule.Grants.Any(g => g.Currency is null || !g.Currency.Enabled))
            return null;

        var idempotencyKey = IdempotencyKeyFor(rule, context);

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
            return existing.SubmissionKey == context.SubmissionKey ? Replay(rule, existing) : null;

        if (!await PassesRepeatConstraintsAsync(rule, context.UserId, cancellationToken))
            return null;

        return await PayAsync(rule, context, idempotencyKey, transaction, index, cancellationToken);
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
        PayoutTarget context,
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
            SourceType = context.SourceType,
            SourceId = context.SourceId,
            IdempotencyKey = idempotencyKey,
            SubmissionKey = context.SubmissionKey,
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
                    SourceType = context.SourceType,
                    SourceId = context.SourceId,
                    IdempotencyKey = idempotencyKey,
                    Metadata = context.Metadata
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

        var entitlements = new List<RewardEntitlementDto>();

        // Ordered for the same reason the currency grants are: two runs of one reward produce
        // byte-identical output, which is what makes a replay comparable to the original.
        foreach (var grant in rule.EntitlementGrants.OrderBy(g => g.ProductId))
        {
            var granted = await _entitlements.GrantAsync(
                context.UserId,
                grant.ProductId,
                EntitlementSource.RewardRule,
                idempotencyKey,
                cancellationToken);

            if (!granted.Succeeded)
            {
                // Same all-or-nothing rule the currency grants obey: a rule that could not hand
                // over its badge pays nothing and leaves its key unspent, so a corrected product
                // can pay it properly later rather than finding it marked done.
                await AbandonAsync(transaction, savepoint, cancellationToken);
                return null;
            }

            entitlements.Add(new RewardEntitlementDto
            {
                ProductId = grant.ProductId,
                ProductKey = grant.Product?.Key ?? string.Empty,

                // Granting is idempotent, so a re-grant is a success that changed nothing. Said
                // plainly here so a client does not celebrate a badge the child already had.
                IsNew = granted.Value?.AlreadyOwned != true
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RewardDto
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            EventType = WireEnum.ToWire(rule.EventType),
            TransactionId = rewardTransaction.Id,
            Grants = grants,
            Entitlements = entitlements
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
            if (entry.Entity is RewardTransaction or RewardTransactionLine or CurrencyLedgerEntry or Entitlement)
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
    private static string IdempotencyKeyFor(RewardRule rule, PayoutTarget context) =>
        rule.RepeatPolicy == RewardRepeatPolicy.Once
            ? $"{WireEnum.ToWire(rule.EventType)}:{context.OnceKey}"
            : $"{WireEnum.ToWire(rule.EventType)}:{context.SubmissionKey}";

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
