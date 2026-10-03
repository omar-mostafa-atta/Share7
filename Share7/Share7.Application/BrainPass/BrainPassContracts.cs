using Share7.Application.Common.Models;
using Share7.Application.Rewards.Models;
using Share7.Domain.BrainPass;

namespace Share7.Application.BrainPass;

public static class BrainPassErrors
{
    public static readonly ApiErrorCode NotFound = new("BRAIN_PASS_NOT_FOUND", "brainPass.notFound");
    public static readonly ApiErrorCode Locked = new("BRAIN_PASS_TIER_LOCKED", "brainPass.tierLocked");
    public static readonly ApiErrorCode Premium = new("BRAIN_PASS_PREMIUM_REQUIRED", "brainPass.premiumRequired");
    public static readonly ApiErrorCode Closed = new("BRAIN_PASS_CLAIMS_CLOSED", "brainPass.claimsClosed");
    public static readonly ApiErrorCode Invalid = new("BRAIN_PASS_CONFIG_INVALID", "brainPass.configInvalid");
    public static readonly ApiErrorCode RewardUnavailable = new("BRAIN_PASS_REWARD_UNAVAILABLE", "brainPass.rewardUnavailable");
}
public sealed record BrainPassRuleInput(string Metric, long MinimumValue, long UnitValue, int XpPerUnit, int MaxSourceXp, int DailyCap);
public sealed record BrainPassTierInput(int Number, BrainPassTrack Track, long RequiredXp, Guid RewardRuleId);
public sealed record BrainPassSeasonInput(string Key, string NameEn, string NameAr, DateTime StartsAtUtc,
    DateTime EndsAtUtc, DateTime ClaimUntilUtc, Guid? PremiumProductId, string? ObjectiveGroupKey,
    IReadOnlyList<BrainPassRuleInput> Rules, IReadOnlyList<BrainPassTierInput> Tiers, int ExpectedVersion = 0);
public sealed record BrainPassAdminDto(Guid Id, BrainPassState State, int Version, BrainPassSeasonInput Configuration);
public sealed record BrainPassTierDto(int Number, BrainPassTrack Track, long RequiredXp, bool Unlocked, bool Claimed,
    bool CanClaim, IReadOnlyList<RewardGrantDto> Currencies, IReadOnlyList<RewardEntitlementDto> Products);
public sealed record BrainPassDto(Guid Id, string Key, string Name, BrainPassState State, DateTime StartsAtUtc,
    DateTime EndsAtUtc, DateTime ClaimUntilUtc, long Xp, bool PremiumOwned, string? ObjectiveGroupKey,
    IReadOnlyList<BrainPassTierDto> Tiers, bool CatchingUp, DateTime ServerTimeUtc);
public sealed record BrainPassClaimDto(int Tier, BrainPassTrack Track, IReadOnlyList<RewardDto> Rewards, bool Replayed);
public sealed record BrainPassRewardContext(Guid UserId, Guid SeasonId, int Tier, BrainPassTrack Track, Guid RewardRuleId);

public interface IBrainPassService
{
    Task<ServiceResult<BrainPassDto?>> CurrentAsync(Guid user, CancellationToken token = default);
    Task<ServiceResult<BrainPassDto>> ReadAsync(Guid user, Guid season, CancellationToken token = default);
    Task<ServiceResult<BrainPassClaimDto>> ClaimAsync(Guid user, Guid season, int tier, BrainPassTrack track, CancellationToken token = default);
    Task<int> ProjectPendingAsync(CancellationToken token = default);
}
public interface IBrainPassRewardService
{
    Task<IReadOnlyList<RewardDto>> EvaluateBrainPassAsync(BrainPassRewardContext context, CancellationToken token = default);
}
public interface IBrainPassAdminService
{
    Task<IReadOnlyList<BrainPassAdminDto>> ListAsync(CancellationToken token = default);
    Task<ServiceResult<BrainPassAdminDto>> SaveAsync(Guid? id, BrainPassSeasonInput request, CancellationToken token = default);
    Task<ServiceResult<BrainPassAdminDto>> PublishAsync(Guid id, int expectedVersion, CancellationToken token = default);
    Task<ServiceResult> DisableAsync(Guid id, CancellationToken token = default);
}
