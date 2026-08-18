using Share7.Application.Rewards.Models;

namespace Share7.Application.Rewards.Interfaces;

/// <summary>
/// Decides what a validated gameplay outcome is worth and pays it.
/// <para>
/// The authority boundary lives here: the client reports an event, this decides the amount. There
/// is no overload taking a payout from the caller, and there is no endpoint that reaches this
/// without going through server-side regrading first.
/// </para>
/// </summary>
public interface IRewardService
{
    /// <summary>
    /// Evaluates every enabled rule matching a finished attempt and credits what they pay.
    /// <para>
    /// **Must be called inside an open transaction.** It composes with the caller's unit of work
    /// so that progress and the rewards earned for it commit together — <c>WalletService</c> joins
    /// the same transaction rather than opening its own.
    /// </para>
    /// <para>
    /// Never throws for an unpayable rule. A rule referencing a retired currency, or one whose
    /// cooldown has not elapsed, is skipped and omitted from the result; a rule that fails partway
    /// is rolled back to a savepoint so it pays nothing rather than paying half. Losing a reward
    /// must never cost the student their progress.
    /// </para>
    /// </summary>
    /// <returns>One entry per rule that actually paid, empty when none did.</returns>
    Task<IReadOnlyList<RewardDto>> EvaluateProgressAttemptAsync(
        ProgressRewardContext context,
        CancellationToken cancellationToken = default);
}
