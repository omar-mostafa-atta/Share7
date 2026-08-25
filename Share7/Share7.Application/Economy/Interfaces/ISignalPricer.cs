using Share7.Application.Economy.Models;

namespace Share7.Application.Economy.Interfaces;

/// <summary>
/// Turns counts of gameplay signals into amounts of currency. **The authority boundary for the
/// variable half of the economy**, and the only implementation of it.
/// <para>
/// There is no overload taking an amount and no route by which a mini-game reaches the wallet. A 3D
/// coin, a dodged obstacle and a right answer are gameplay signals; currency is what this decides
/// they were worth.
/// </para>
/// <para>
/// **Why a service rather than a method on each caller.** Two surfaces pay variable amounts — a
/// settled run and a graded attempt — and before this existed only one of them could. Copying the
/// pricing loop into the second would have produced two cap ladders, two answers to "how much can a
/// child earn in a day", and two places to fix a mispriced row. The bounds a run adds on top of this
/// (its duration, its layout, the account's runs-per-day) stay in <c>RunService</c>, because they are
/// facts about runs rather than about pricing.
/// </para>
/// <para>
/// **It prices and accrues; it does not grant.** The caller applies the returned lines through
/// <c>IWalletService</c> inside its own transaction, because only the caller knows what else has to
/// commit or roll back with them.
/// </para>
/// </summary>
public interface ISignalPricer
{
    /// <summary>
    /// Prices what was reported, applying every bound in narrowest-last order: the per-session cap,
    /// then what is left of the kind's daily allowance, then what the session's duration makes
    /// physically possible, then the modifier, then what is left of the account's daily ceiling for
    /// that currency.
    /// <para>
    /// Reads counters but writes nothing. Call <see cref="AccrueAsync"/> once the grants have
    /// actually been applied.
    /// </para>
    /// </summary>
    Task<SignalPricing> PriceAsync(
        SignalPricingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what was paid against today's counters — per signal kind, and per currency for the
    /// earning ceiling.
    /// <para>
    /// **Call inside the transaction that granted the currency, and only for lines that were actually
    /// applied.** Accruing for a grant the wallet refused would charge a child for money they never
    /// received.
    /// </para>
    /// </summary>
    Task AccrueAsync(
        Guid userId,
        IReadOnlyList<SignalLine> granted,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
