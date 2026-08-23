using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Economy;
using Share7.Domain.Constants;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Rewards;

/// <summary>
/// Authoring for reward rules.
/// <para>
/// Validation here is deliberately strict and refuses rather than ignores. A rule that is accepted
/// but can never fire — an event type that does not exist, a cooldown on a policy that has no use
/// for one — looks identical to a working rule in every screen, and the only symptom is students
/// quietly not being paid.
/// </para>
/// </summary>
public class RewardAdminService : IRewardAdminService
{
    private readonly ApplicationDbContext _dbContext;

    public RewardAdminService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RewardRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .OrderBy(r => r.EventType)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return rules.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<RewardRuleDto>> CreateRuleAsync(
        CreateRewardRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WireEnum.TryFromWire<RewardEventType>(request.EventType, out var eventType)
            || eventType == RewardEventType.Unknown)
            return Invalid(
                $"'{request.EventType}' is not a reward event type. Valid values: {ValidNames<RewardEventType>()}.");

        if (ValidateReferenceKey(eventType, request.ReferenceKey) is { } referenceFailure)
            return referenceFailure;

        var policy = await ParsePolicyAsync(
            eventType,
            request.RepeatPolicy,
            request.TransactionType,
            request.CooldownSeconds,
            request.DailyLimit,
            request.Grants,
            cancellationToken);

        if (!policy.Succeeded)
            return Rewrap(policy);

        var now = DateTime.UtcNow;

        var rule = new RewardRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            EventType = eventType,
            ReferenceKey = string.IsNullOrWhiteSpace(request.ReferenceKey) ? null : request.ReferenceKey.Trim(),
            RepeatPolicy = policy.Value!.RepeatPolicy,
            CooldownSeconds = request.CooldownSeconds,
            DailyLimit = request.DailyLimit,
            TransactionType = policy.Value.TransactionType,
            Enabled = request.Enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var grant in request.Grants)
        {
            rule.Grants.Add(new RewardRuleGrant
            {
                Id = Guid.NewGuid(),
                RewardRuleId = rule.Id,
                CurrencyId = grant.CurrencyId,
                Amount = grant.Amount
            });
        }

        _dbContext.RewardRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<RewardRuleDto>.Success(await ReadBackAsync(rule.Id, cancellationToken));
    }

    public async Task<ServiceResult<RewardRuleDto>> UpdateRuleAsync(
        Guid ruleId,
        UpdateRewardRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.RewardRules
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

        if (rule is null)
            return ServiceResult<RewardRuleDto>.Failure(
                ApiErrors.RewardRuleNotFound,
                ServiceErrorKind.NotFound,
                $"Reward rule {ruleId} does not exist.");

        var policy = await ParsePolicyAsync(
            rule.EventType,
            request.RepeatPolicy,
            request.TransactionType,
            request.CooldownSeconds,
            request.DailyLimit,
            request.Grants,
            cancellationToken);

        if (!policy.Succeeded)
            return Rewrap(policy);

        // The grant set is replaced wholesale, so the delete has to reach the database before the
        // inserts: re-sending a currency the rule already grants would otherwise collide with the
        // unique (RewardRuleId, CurrencyId) index. The transaction keeps the rule from being left
        // with no grants if the insert then fails.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Deleted by query rather than through the navigation collection. Marking the children
        // removed *and* clearing the collection makes EF emit the delete twice — once for the
        // explicit removal and once for the orphan — and the second finds no row.
        await _dbContext.RewardRuleGrants
            .Where(g => g.RewardRuleId == rule.Id)
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete does not go through the change tracker, so any grant this context happened
        // to be tracking already now refers to a row that is gone. Left attached it would be
        // written back on the save below.
        foreach (var entry in _dbContext.ChangeTracker.Entries<RewardRuleGrant>().ToList())
        {
            if (entry.Entity.RewardRuleId == rule.Id)
                entry.State = EntityState.Detached;
        }

        foreach (var grant in request.Grants)
        {
            _dbContext.RewardRuleGrants.Add(new RewardRuleGrant
            {
                Id = Guid.NewGuid(),
                RewardRuleId = rule.Id,
                CurrencyId = grant.CurrencyId,
                Amount = grant.Amount
            });
        }

        rule.Name = request.Name.Trim();
        rule.RepeatPolicy = policy.Value!.RepeatPolicy;
        rule.CooldownSeconds = request.CooldownSeconds;
        rule.DailyLimit = request.DailyLimit;
        rule.TransactionType = policy.Value.TransactionType;
        rule.Enabled = request.Enabled;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult<RewardRuleDto>.Success(await ReadBackAsync(rule.Id, cancellationToken));
    }

    // ------------------------------------------------------------- validation

    private sealed record RulePolicy(RewardRepeatPolicy RepeatPolicy, CurrencyTransactionType TransactionType);

    private async Task<ServiceResult<RulePolicy>> ParsePolicyAsync(
        RewardEventType eventType,
        string repeatPolicyText,
        string? transactionTypeText,
        int? cooldownSeconds,
        int? dailyLimit,
        IReadOnlyList<RewardGrantRequest> grants,
        CancellationToken cancellationToken)
    {
        if (!WireEnum.TryFromWire<RewardRepeatPolicy>(repeatPolicyText, out var repeatPolicy))
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.RewardRuleInvalid,
                ServiceErrorKind.Validation,
                $"'{repeatPolicyText}' is not a repeat policy. Valid values: {ValidNames<RewardRepeatPolicy>()}.");

        if (repeatPolicy == RewardRepeatPolicy.Once && (cooldownSeconds is not null || dailyLimit is not null))
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.RewardRuleInvalid,
                ServiceErrorKind.Validation,
                "cooldownSeconds and dailyLimit apply only to EVERY_TIME rules — a ONCE rule already pays at most once.");

        var transactionType = CurrencyTransactionType.LessonReward;

        if (!string.IsNullOrWhiteSpace(transactionTypeText))
        {
            if (!WireEnum.TryFromWire<CurrencyTransactionType>(transactionTypeText, out var parsed)
                || parsed == CurrencyTransactionType.Unknown)
                return ServiceResult<RulePolicy>.Failure(
                    ApiErrors.RewardRuleInvalid,
                    ServiceErrorKind.Validation,
                    $"'{transactionTypeText}' is not a currency transaction type.");

            transactionType = parsed;
        }

        if (grants.Count == 0)
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.RewardRuleInvalid,
                ServiceErrorKind.Validation,
                "A reward rule must grant at least one currency.");

        if (grants.Any(g => g.Amount <= 0))
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.RewardRuleInvalid,
                ServiceErrorKind.Validation,
                "Every grant amount must be positive — a reward rule never deducts currency.");

        var duplicate = grants
            .GroupBy(g => g.CurrencyId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.RewardRuleInvalid,
                ServiceErrorKind.Validation,
                $"Currency {duplicate.Key} is listed more than once. Combine the amounts into a single grant.");

        // A level-up rule that granted XP would be a payout causing the event that triggers it.
        // Refused here so it cannot be authored; RewardService also strips XP from these rules at
        // evaluation time, so a rule that predates this check still cannot loop.
        if (eventType == RewardEventType.PlayerLevelUp
            && grants.Any(g => g.CurrencyId == CurrencyIds.Xp))
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.RewardRuleInvalid,
                ServiceErrorKind.Validation,
                "A PLAYER_LEVEL_UP rule cannot grant XP — the payout would cause the event that triggers it.");

        var requested = grants.Select(g => g.CurrencyId).ToList();

        var known = await _dbContext.Currencies
            .Where(c => requested.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var missing = requested.Except(known).ToList();

        if (missing.Count > 0)
            return ServiceResult<RulePolicy>.Failure(
                ApiErrors.CurrencyNotFound,
                ServiceErrorKind.NotFound,
                $"No such currency: {string.Join(", ", missing)}.");

        return ServiceResult<RulePolicy>.Success(new RulePolicy(repeatPolicy, transactionType));
    }

    /// <summary>
    /// What a reference key is allowed to look like depends on what the event scopes by. A key in
    /// the wrong shape is a typo that leaves the rule permanently unmatched — it never errors, it
    /// just never pays, which is the worst way for configuration to fail.
    /// <para>
    /// The referenced thing's *existence* is deliberately not checked: Rewards resolving ids in
    /// Curriculum would couple the two domains for a validation the admin console can do better
    /// with a picker.
    /// </para>
    /// </summary>
    private static ServiceResult<RewardRuleDto>? ValidateReferenceKey(
        RewardEventType eventType,
        string? referenceKey)
    {
        if (string.IsNullOrWhiteSpace(referenceKey)) return null;

        var key = referenceKey.Trim();

        return eventType switch
        {
            // The level reached. A null key pays on every level-up, which is how a flat
            // "20 coins per level" is expressed.
            RewardEventType.PlayerLevelUp =>
                int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level > 0
                    ? null
                    : Invalid($"referenceKey '{referenceKey}' is not a level. Use the level number, or leave it null to pay on every level-up."),

            // {boardKey}:{band} — authored by the settlement path, not a lesson id.
            RewardEventType.LeaderboardSettled =>
                key.Contains(':')
                    ? null
                    : Invalid($"referenceKey '{referenceKey}' is not a leaderboard band. Use '{{boardKey}}:{{band}}'."),

            // The lesson events, and anything added later that scopes by content.
            _ => Guid.TryParse(key, out _)
                ? null
                : Invalid($"referenceKey '{referenceKey}' is not a lesson id. Leave it null to apply the rule to every lesson.")
        };
    }

    // ------------------------------------------------------------- mapping

    private async Task<RewardRuleDto> ReadBackAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var saved = await _dbContext.RewardRules
            .AsNoTracking()
            .Include(r => r.Grants)
            .ThenInclude(g => g.Currency)
            .FirstAsync(r => r.Id == ruleId, cancellationToken);

        return ToDto(saved);
    }

    private static RewardRuleDto ToDto(RewardRule rule) => new()
    {
        RuleId = rule.Id,
        Name = rule.Name,
        EventType = WireEnum.ToWire(rule.EventType),
        ReferenceKey = rule.ReferenceKey,
        RepeatPolicy = WireEnum.ToWire(rule.RepeatPolicy),
        CooldownSeconds = rule.CooldownSeconds,
        DailyLimit = rule.DailyLimit,
        TransactionType = WireEnum.ToWire(rule.TransactionType),
        Enabled = rule.Enabled,
        CreatedAtUtc = rule.CreatedAtUtc,
        UpdatedAtUtc = rule.UpdatedAtUtc,
        Grants = rule.Grants
            .OrderBy(g => g.Currency!.Key, StringComparer.Ordinal)
            .Select(g => new RewardRuleGrantDto
            {
                CurrencyId = g.CurrencyId,
                Currency = g.Currency!.Key,
                Amount = g.Amount,
                CurrencyEnabled = g.Currency.Enabled
            })
            .ToList()
    };

    private static ServiceResult<RewardRuleDto> Invalid(string message) =>
        ServiceResult<RewardRuleDto>.Failure(ApiErrors.RewardRuleInvalid, ServiceErrorKind.Validation, message);

    /// <summary>Carries a validation failure across result types without losing the code or details.</summary>
    private static ServiceResult<RewardRuleDto> Rewrap<T>(ServiceResult<T> failure) => new()
    {
        ErrorKind = failure.ErrorKind,
        Error = failure.Error,
        Errors = failure.Errors,
        Details = failure.Details
    };

    private static string ValidNames<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetValues<TEnum>()
            .Where(value => Convert.ToInt32(value) != 0)
            .Select(WireEnum.ToWire));
}
