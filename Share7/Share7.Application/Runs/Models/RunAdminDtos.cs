using Share7.Application.Runs.Models;
using System.ComponentModel.DataAnnotations;
using Share7.Domain.Runs;

namespace Share7.Application.Runs.Models.Admin;

// ---- valuations ------------------------------------------------------------------------------

/// <summary>
/// Authors what a pickup is worth. **This is the whole economy-tuning surface** — a weekend
/// double-value event, a nerf after launch, a richer coin in a harder mini-game are all edits here,
/// with no client release and no deploy.
/// </summary>
public class CreatePickupValuationRequest
{
    /// <summary>
    /// Which game this price applies to, or **null for the platform default** every unconfigured
    /// mini-game resolves through. Exactly one default may exist per (kind, currency).
    /// </summary>
    public Guid? GameId { get; set; }

    /// <summary>A lowercase token: <c>coin</c>, <c>chest_small</c>, <c>mg147_starfish</c>.</summary>
    [Required]
    [MaxLength(PickupKinds.MaxLength)]
    public string PickupKind { get; set; } = string.Empty;

    [Required]
    public Guid CurrencyId { get; set; }

    /// <summary>Whole units one pickup pays. Zero is legal — "collectible, but not currency".</summary>
    [Range(0, long.MaxValue)]
    public long UnitValue { get; set; }

    /// <summary>
    /// Most of this kind a single run can be paid for. **Required and positive** — an unbounded row
    /// is an unbounded payout, and this is the first bound a forged claim meets.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxPerRun { get; set; }

    /// <summary>
    /// Most one account can be paid for in a UTC day, across every run. Optional for a soft currency;
    /// **mandatory for a hard one**, and refused at creation without it, because a missing bound
    /// discovered later is currency already in circulation.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? MaxPerDay { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Retunes an existing price. <c>GameId</c>, <c>PickupKind</c> and <c>CurrencyId</c> are deliberately
/// absent: changing what a row *prices* would strand the <c>RunPayout</c> rows recorded against it,
/// which claim to explain a payout for something the row no longer describes. Retire it and author a
/// replacement.
/// </summary>
public class UpdatePickupValuationRequest
{
    [Range(0, long.MaxValue)]
    public long UnitValue { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxPerRun { get; set; }

    [Range(1, int.MaxValue)]
    public int? MaxPerDay { get; set; }

    public bool Enabled { get; set; } = true;
}

public class PickupValuationDto
{
    public Guid Id { get; init; }
    public Guid? GameId { get; init; }

    /// <summary>Null for the platform default, so a listing reads without joining to the catalog.</summary>
    public string? GameKey { get; init; }

    public string PickupKind { get; init; } = string.Empty;
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
    public bool Enabled { get; init; }

    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

// ---- flagged-run review ----------------------------------------------------------------------

/// <summary>
/// One run as an admin sees it: what was claimed, what was paid, and why the two differ.
/// <para>
/// The point of the review queue is that **an implausible run is capped and paid, never discarded**.
/// Something has to look at the flags afterwards, or "flagged for review" is just a column.
/// </para>
/// </summary>
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
