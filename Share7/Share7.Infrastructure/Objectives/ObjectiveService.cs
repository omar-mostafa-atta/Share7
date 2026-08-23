using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Objectives.Models;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Objectives;

/// <inheritdoc cref="IObjectiveService"/>
public class ObjectiveService : IObjectiveService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRewardService _rewards;
    private readonly IWalletService _wallet;
    private readonly ILanguageService _languageService;

    public ObjectiveService(
        ApplicationDbContext dbContext,
        IRewardService rewards,
        IWalletService wallet,
        ILanguageService languageService)
    {
        _dbContext = dbContext;
        _rewards = rewards;
        _wallet = wallet;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<ObjectiveDto>> GetForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var gradeId = await GradeIdAsync(userId, cancellationToken);

        var offered = await _dbContext.Objectives
            .AsNoTracking()
            .Include(o => o.Translations)
            .Where(o => o.IsActive
                        && (o.AvailableFromUtc == null || o.AvailableFromUtc <= now)
                        && (o.AvailableToUtc == null || o.AvailableToUtc >= now)
                        && (o.GradeId == null || o.GradeId == gradeId)
                        && (o.LangId == null || o.LangId == langId))
            .OrderBy(o => o.Kind)
            .ThenBy(o => o.SortOrder)
            .ToListAsync(cancellationToken);

        if (offered.Count == 0) return [];

        var ids = offered.Select(o => o.Id).ToList();

        var progress = await _dbContext.UserObjectiveProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId && ids.Contains(p.ObjectiveId))
            .ToListAsync(cancellationToken);

        var dtos = new List<ObjectiveDto>(offered.Count);

        foreach (var objective in offered)
        {
            var cycleKey = ObjectiveCycle.KeyFor(objective.Kind, now);

            var row = progress.FirstOrDefault(p =>
                p.ObjectiveId == objective.Id && p.CycleKey == cycleKey);

            // A completed row from a cycle that has rolled but is still claimable outranks the empty
            // current one: the child earned it, and hiding it because midnight passed is exactly the
            // failure ClaimableUntilUtc exists to prevent.
            var claimable = progress.FirstOrDefault(p =>
                p.ObjectiveId == objective.Id
                && p.State == ObjectiveState.Completed
                && (p.ClaimableUntilUtc == null || p.ClaimableUntilUtc >= now));

            var shown = claimable ?? row;

            dtos.Add(ToDto(objective, shown, langId, now));
        }

        return dtos;
    }

    public async Task<ServiceResult<ObjectiveClaimResultDto>> ClaimAsync(
        Guid userId,
        string objectiveKey,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        var key = objectiveKey?.Trim() ?? string.Empty;

        if (key.Length == 0)
            return ServiceResult<ObjectiveClaimResultDto>.Invalid("An objective key is required.");

        var objective = await _dbContext.Objectives
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Key == key, cancellationToken);

        if (objective is null)
            return ServiceResult<ObjectiveClaimResultDto>.NotFound($"No objective '{key}'.");

        var now = DateTime.UtcNow;

        // The newest completed-and-still-claimable row, whichever cycle it belongs to. Ordering by
        // completion rather than cycle key because keys are strings and "d:2026-09-01" sorts before
        // "d:2026-10-01" only by luck of format.
        var row = await _dbContext.UserObjectiveProgress
            .Where(p => p.UserId == userId
                        && p.ObjectiveId == objective.Id
                        && p.State == ObjectiveState.Completed
                        && (p.ClaimableUntilUtc == null || p.ClaimableUntilUtc >= now))
            .OrderByDescending(p => p.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // Deliberately one refusal for both "not finished" and "already collected". They are
            // the same answer to the caller — there is nothing to collect — and distinguishing them
            // would leak whether a retry landed, which the requestId replay already handles.
            return ServiceResult<ObjectiveClaimResultDto>.Conflict(
                $"Objective '{key}' has nothing to claim.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Flipped before the payout, inside the same transaction. If the reward engine throws, the
        // claim rolls back with it and the objective is still Completed — a failed payout must
        // never be able to consume a completion.
        row.State = ObjectiveState.Claimed;
        row.ClaimedAtUtc = now;
        row.UpdatedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var rewards = await _rewards.EvaluateObjectiveAsync(
            new ObjectiveRewardContext
            {
                UserId = userId,
                ObjectiveKey = objective.Key,
                CycleKey = row.CycleKey,
                RequestId = requestId
            },
            cancellationToken);

        // Read inside the transaction: the grants above are only visible from in here, and the
        // figures returned have to be the ones the payout produced.
        var balances = await _wallet.GetBalancesAsync(userId, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult<ObjectiveClaimResultDto>.Success(new ObjectiveClaimResultDto
        {
            Key = objective.Key,
            State = WireEnum.ToWire(ObjectiveState.Claimed),
            Rewards = rewards,
            Balances = balances
        });
    }

    // ---- mapping -------------------------------------------------------------------------------

    private static ObjectiveDto ToDto(
        Objective objective, UserObjectiveProgress? row, Guid langId, DateTime now)
    {
        // The caller's language, then any translation at all. A missing translation shows the key
        // rather than an empty row — visibly wrong beats invisibly absent, and an operator can see
        // which one needs authoring.
        var translation =
            objective.Translations.FirstOrDefault(t => t.LangId == langId)
            ?? objective.Translations.FirstOrDefault();

        var state = row?.State ?? ObjectiveState.InProgress;

        return new ObjectiveDto
        {
            Key = objective.Key,
            Kind = WireEnum.ToWire(objective.Kind),
            Name = translation?.Name ?? objective.Key,
            Description = translation?.Description,
            IconKey = objective.IconKey,

            // Clamped for display: a Sum counter legitimately overshoots — the result that finished
            // it was worth more than the remainder — and "7 / 3" reads as broken to a child.
            Value = Math.Min(row?.Value ?? 0, objective.Target),
            Target = objective.Target,
            State = WireEnum.ToWire(state),
            CanClaim = state == ObjectiveState.Completed,
            CycleEndsAtUtc = ObjectiveCycle.EndsAtUtc(objective.Kind, now),
            SortOrder = objective.SortOrder
        };
    }

    private Task<Guid?> GradeIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.GradeId)
            .FirstOrDefaultAsync(cancellationToken);
}
