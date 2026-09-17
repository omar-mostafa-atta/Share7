using Share7.Application.Common.Models;
using Share7.Application.Progression.Models;

namespace Share7.Application.Progression.Interfaces;

/// <summary>
/// The level curve, and the only thing that reads it.
/// <para>
/// **Deliberately pure — it computes and never pays.** Level-up rewards are paid by
/// <c>IRewardService</c> like every other reward, through the same rules, ledger and idempotency
/// index. Letting this service grant would be the second payout path the economy is arranged to
/// prevent, and it would make <c>RewardService</c> and this call each other.
/// </para>
/// </summary>
public interface ILevelService
{
    /// <summary>
    /// The currency the level is derived from. Exposed so callers can find an XP grant among a
    /// mixed payout without hard-coding an id of their own.
    /// </summary>
    Guid XpCurrencyId { get; }

    /// <summary>The stable wire key of that currency, for callers that only see keys.</summary>
    string XpCurrencyKey { get; }

    /// <summary>
    /// The level a given lifetime XP total sits at, plus the band around it.
    /// <para>
    /// Returns level 1 with an empty band when no curve is authored, rather than throwing. A
    /// missing curve is a configuration gap; it must not take down a lesson result screen.
    /// </para>
    /// </summary>
    Task<PlayerLevelDto> DescribeAsync(long xp, CancellationToken cancellationToken = default);

    /// <summary>Reads the player's XP balance and describes where it puts them.</summary>
    Task<PlayerLevelDto> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every level crossed moving from <paramref name="xpBefore"/> to <paramref name="xpAfter"/>,
    /// ascending. Empty when the move crossed nothing, and empty when XP went **down** — a
    /// correction that removes XP does not un-level anybody.
    /// <para>
    /// A list rather than a single level because one generous grant can cross several, and each is
    /// a separate reward event. Collapsing them to "you reached level 7" would silently drop
    /// whatever levels 5 and 6 were configured to pay.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<int>> LevelsCrossedAsync(
        long xpBefore,
        long xpAfter,
        CancellationToken cancellationToken = default);

    /// <summary>The authored curve, ascending. Admin read.</summary>
    Task<IReadOnlyList<LevelThresholdDto>> GetCurveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the whole curve. Refuses a set that is not contiguous from level 1, does not start
    /// at <c>0</c> XP, or does not strictly increase — see <see cref="ReplaceLevelCurveRequest"/>.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<LevelThresholdDto>>> ReplaceCurveAsync(
        ReplaceLevelCurveRequest request,
        CancellationToken cancellationToken = default);
}
