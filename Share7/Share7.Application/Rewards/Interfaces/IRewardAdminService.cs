using Share7.Application.Common.Models;
using Share7.Application.Rewards.Models;

namespace Share7.Application.Rewards.Interfaces;

/// <summary>
/// Authoring for the reward rule table. Rules are configuration, so this is Admin-only — a player
/// able to reach it could set their own payout, which is the exact thing the server-authoritative
/// model exists to prevent.
/// <para>
/// There is no delete. A rule that has paid somebody has to stay resolvable for its reward
/// transactions to remain explicable, so retiring via <c>enabled: false</c> is the supported way
/// to take one out of circulation.
/// </para>
/// </summary>
public interface IRewardAdminService
{
    Task<IReadOnlyList<RewardRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<RewardRuleDto>> CreateRuleAsync(
        CreateRewardRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RewardRuleDto>> UpdateRuleAsync(
        Guid ruleId,
        UpdateRewardRuleRequest request,
        CancellationToken cancellationToken = default);
}
