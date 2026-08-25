using Share7.Domain.Economy;

namespace Share7.Application.Economy.Models;

/// <summary>
/// A request to price counted gameplay signals. **Carries counts and never an amount** — that
/// absence is the whole authority model, and it is the same absence <c>SubmitRunResultRequest</c>
/// enforces on the wire.
/// </summary>
public sealed record SignalPricingRequest
{
    public required Guid UserId { get; init; }

    /// <summary>Which game's prices to resolve. Falls back to the platform default rows.</summary>
    public required Guid GameId { get; init; }

    /// <summary>
    /// Who is reporting. Kinds this surface does not own are dropped before pricing — see
    /// <see cref="SignalKinds.IsReportableBy"/>.
    /// </summary>
    public required SignalSurface Surface { get; init; }

    /// <summary>How many of each kind. Already aggregated; a kind appearing twice is the caller's bug.</summary>
    public required IReadOnlyDictionary<string, int> Counts { get; init; }

    public required DateTime NowUtc { get; init; }

    /// <summary>
    /// How long the session lasted, for the per-second plausibility bound. Null on a surface with no
    /// duration — an attempt is bounded by its question count, which is tighter than any rate.
    /// </summary>
    public int? DurationMs { get; init; }

    /// <summary>
    /// What the server decided the session's active modifiers are worth. **Never a client claim.**
    /// Applied to an already-capped count, so it scales a bounded number rather than an open one.
    /// </summary>
    public long Multiplier { get; init; } = 1;

    /// <summary>
    /// Skips the account's daily currency ceiling. Used by nothing today and present so the one
    /// caller that will eventually need it — an operator's corrective re-grant — does not reach for
    /// a second pricing path.
    /// </summary>
    public bool BypassEarnCeiling { get; init; }
}

/// <summary>
/// One priced line. **Gross and net are both kept, deliberately.** When a child asks why they got 20
/// after collecting 47, and when somebody is tuning the economy six months from now, "the cap ate 27"
/// has to be answerable from data rather than reconstructed from a valuation table that has changed
/// since.
/// </summary>
public sealed record SignalLine
{
    public required string Kind { get; init; }

    /// <summary>
    /// Provenance, as <c>signal:{kind}</c>. Recorded on every ledger entry and returned to the client
    /// so a results screen can tell "47 coins collected" apart from "run completed bonus".
    /// </summary>
    public required string Source { get; init; }

    public required Guid CurrencyId { get; init; }
    public required string CurrencyKey { get; init; }

    /// <summary>How many were reported, before any cap.</summary>
    public required int ReportedCount { get; init; }

    /// <summary>How many actually paid.</summary>
    public required int PaidCount { get; init; }

    public required long UnitValue { get; init; }

    /// <summary>What the uncapped claim was worth, before modifiers.</summary>
    public required long GrossAmount { get; init; }

    /// <summary>What the caps removed. Positive.</summary>
    public required long CappedAmount { get; init; }

    /// <summary>What is to be granted. This is the number that moves a balance.</summary>
    public required long NetAmount { get; init; }
}

/// <summary>
/// What pricing decided: the lines to grant, and everything that has to be said about why they are
/// not larger.
/// <para>
/// **Caps are told, never swallowed.** A results screen that shows 47 collected and then pays 20 has
/// to be able to say why; silently paying less is how a child learns the game is unfair.
/// </para>
/// </summary>
public sealed record SignalPricing
{
    public static readonly SignalPricing Empty = new()
    {
        Lines = [],
        CapMessages = [],
        Flags = []
    };

    public required IReadOnlyList<SignalLine> Lines { get; init; }

    /// <summary>
    /// Machine tokens the client localises — <c>signal_limit</c>, <c>signal_daily_limit</c>,
    /// <c>signal_rate_limit</c>, <c>daily_coin_limit</c> — most specific first.
    /// </summary>
    public required IReadOnlyList<string> CapMessages { get; init; }

    /// <summary>Review tokens for the session row. Not shown to a player.</summary>
    public required IReadOnlyList<string> Flags { get; init; }

    public bool Any => Lines.Count > 0;
}
