using Share7.Application.Runs.Models;
using System.ComponentModel.DataAnnotations;
using Share7.Domain.Economy;
using Share7.Domain.Runs;

namespace Share7.Application.Runs.Models.Admin;

// ---- valuations ------------------------------------------------------------------------------

/// <summary>
/// Authors what a gameplay signal is worth. **This is the whole economy-tuning surface** — a weekend
/// double-value event, a nerf after launch, a richer coin in a harder mini-game, five XP a right
/// answer are all edits here, with no client release and no deploy.
/// <para>
/// <b>Wire compatibility.</b> <c>signalKind</c> is the field; <c>pickupKind</c> is still accepted so
/// the console shipped before the rename keeps authoring. Exactly one of them need be present.
/// </para>
/// </summary>
public class CreateSignalValuationRequest
{
    /// <summary>
    /// Which game this price applies to, or **null for the platform default** every unconfigured
    /// mini-game resolves through. Exactly one default may exist per (kind, currency).
    /// </summary>
    public Guid? GameId { get; set; }

    /// <summary>
    /// A lowercase token: <c>coin</c>, <c>near_miss</c>, <c>distance_m</c>, <c>correct_answer</c>,
    /// <c>mg147_starfish</c>.
    /// </summary>
    [MaxLength(SignalKinds.MaxLength)]
    public string SignalKind { get; set; } = string.Empty;

    /// <summary>Legacy alias for <see cref="SignalKind"/>. Accepted, never preferred.</summary>
    [MaxLength(SignalKinds.MaxLength)]
    public string? PickupKind { get; set; }

    /// <summary>Whichever of the two the caller supplied. <see cref="SignalKind"/> wins a tie.</summary>
    public string ResolvedKind =>
        !string.IsNullOrWhiteSpace(SignalKind) ? SignalKind : PickupKind ?? string.Empty;

    [Required]
    public Guid CurrencyId { get; set; }

    /// <summary>Whole units one signal pays. Zero is legal — "counted, but not currency".</summary>
    [Range(0, long.MaxValue)]
    public long UnitValue { get; set; }

    /// <summary>
    /// Most of this kind a single session can be paid for. **Required and positive** — an unbounded
    /// row is an unbounded payout, and this is the first bound a forged claim meets.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxPerRun { get; set; }

    /// <summary>
    /// Most one account can be paid for in a UTC day, across every session. Optional for a soft
    /// currency; **mandatory for a hard one**, and refused at creation without it, because a missing
    /// bound discovered later is currency already in circulation.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? MaxPerDay { get; set; }

    /// <summary>
    /// The per-second plausibility bound for this kind, or null for the platform default. Set it for
    /// anything that accrues faster than a pickup — metres of ground, points of combo — because one
    /// global rate cannot be right for both a coin and a distance.
    /// </summary>
    [Range(0.001, 100_000)]
    public double? MaxPerSecond { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Retunes an existing price. <c>GameId</c>, the kind and <c>CurrencyId</c> are deliberately absent:
/// changing what a row *prices* would strand the payout rows recorded against it, which claim to
/// explain a payout for something the row no longer describes. Retire it and author a replacement.
/// </summary>
public class UpdateSignalValuationRequest
{
    [Range(0, long.MaxValue)]
    public long UnitValue { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxPerRun { get; set; }

    [Range(1, int.MaxValue)]
    public int? MaxPerDay { get; set; }

    [Range(0.001, 100_000)]
    public double? MaxPerSecond { get; set; }

    public bool Enabled { get; set; } = true;
}

public class SignalValuationDto
{
    public Guid Id { get; init; }
    public Guid? GameId { get; init; }

    /// <summary>Null for the platform default, so a listing reads without joining to the catalog.</summary>
    public string? GameKey { get; init; }

    public string SignalKind { get; init; } = string.Empty;

    /// <summary>
    /// Legacy alias, emitted so the console shipped before the rename keeps listing kinds. Remove
    /// once nothing reads it — it is one duplicated string, not a second source of truth.
    /// </summary>
    public string PickupKind => SignalKind;

    /// <summary>
    /// Which surface may report this kind: <c>RUN</c> or <c>ATTEMPT</c>. Surfaced because a row
    /// priced for the wrong surface never pays, and the reason has to be visible rather than deduced
    /// from an empty payout.
    /// </summary>
    public string Surface { get; init; } = string.Empty;

    public Guid CurrencyId { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>Whether the currency is one people pay real money for. Drives the mandatory cap.</summary>
    public bool CurrencyIsHard { get; init; }

    /// <summary>
    /// False when the currency has been retired. The row is skipped whole at settlement while this is
    /// false — surfaced here so an admin can see *why* a price stopped paying.
    /// </summary>
    public bool CurrencyEnabled { get; init; }

    public long UnitValue { get; init; }
    public int MaxPerRun { get; init; }
    public int? MaxPerDay { get; init; }
    public double? MaxPerSecond { get; init; }
    public bool Enabled { get; init; }

    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public class RunAdminDto
{
    public Guid RunId { get; init; }
    public Guid UserId { get; init; }
    public Guid GameId { get; init; }
    public string State { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public int DurationMs { get; init; }

    public long Seed { get; init; }
    public int LayoutVersion { get; init; }
    public Guid? SessionId { get; init; }

    public bool IsFlagged { get; init; }

    /// <summary>Comma-joined machine tokens: <c>duration_clamped</c>, <c>pickup_capped</c>, …</summary>
    public string? FlagReason { get; init; }

    public bool CapReached { get; init; }
    public string? CapMessage { get; init; }

    /// <summary>What the client reported, verbatim.</summary>
    public IReadOnlyList<RunCollectedDto> Collected { get; init; } = [];

    /// <summary>What it actually paid, gross and net, one line per source.</summary>
    public IReadOnlyList<RunPayoutDto> Payouts { get; init; } = [];

    public DateTime? ReviewedAtUtc { get; init; }
    public Guid? ReviewedByUserId { get; init; }
    public string? ReviewNote { get; init; }
}

public class RunPayoutDto
{
    public string Source { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public int CollectedCount { get; init; }
    public int PaidCount { get; init; }
    public long UnitValue { get; init; }
    public long GrossAmount { get; init; }

    /// <summary>What the caps removed. This is the number that answers "why did I only get 20?".</summary>
    public long CappedAmount { get; init; }

    public long NetAmount { get; init; }
}

public class ReviewRunRequest
{
    /// <summary>What the reviewer concluded. Free text, for humans, kept on the run.</summary>
    [MaxLength(512)]
    public string? Note { get; set; }
}
