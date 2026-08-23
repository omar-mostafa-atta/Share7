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

    /// <summary>
    /// Pays a player's final leaderboard placing, through the same rules, ledger and audit trail
    /// as everything else.
    /// <para>
    /// **Deliberately not a second payout path.** A separate prize engine would mean two places
    /// that can create currency, two idempotency schemes, and two answers to "why does this child
    /// have these coins" — which is how an economy ends up with several disagreeing counters. The
    /// only thing settlement adds is a new event type and a reference key shaped
    /// <c>{boardKey}:{band}</c>.
    /// </para>
    /// <para>
    /// Like the attempt path, this must run inside an open transaction: the payout and the
    /// settlement row that records it commit together, or a retry pays a child twice.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RewardDto>> EvaluateSettlementAsync(
        SettlementRewardContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates every enabled <c>RUN_SETTLED</c> rule for a run the server itself opened and
    /// re-valued, and credits what they pay.
    /// <para>
    /// Same contract as <see cref="EvaluateProgressAttemptAsync"/> — **must be called inside an open
    /// transaction**, never throws for an unpayable rule, and rolls a half-paid rule back to a
    /// savepoint so it pays nothing rather than half. Losing a bonus must never cost the child the
    /// pickups they actually collected.
    /// </para>
    /// <para>
    /// Rules matching this event are scoped by <c>ReferenceKey</c> = the **game id**. Fixed bonuses
    /// only: the variable, per-pickup half of a run's payout comes from <c>PickupValuation</c>, which
    /// is a different mechanism for a reason — a rule grants a fixed amount and cannot express
    /// "one coin per coin collected".
    /// </para>
    /// </summary>
    /// <returns>One entry per rule that actually paid, empty when none did.</returns>
    Task<IReadOnlyList<RewardDto>> EvaluateRunSettlementAsync(
        RunRewardContext context,
        CancellationToken cancellationToken = default);
}
