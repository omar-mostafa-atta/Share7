using System.ComponentModel.DataAnnotations;
using Share7.Domain.Economy;

namespace Share7.Application.Economy.Models;

// ---- currency definition (admin) ---------------------------------------------------------

public class CreateCurrencyRequest
{
    /// <summary>
    /// Stable client-facing identifier, e.g. "coins". Lowercase letters, digits and underscores.
    /// **Cannot be changed later** — the client caches balances against it.
    /// </summary>
    [Required]
    [MaxLength(32)]
    [RegularExpression("^[a-z][a-z0-9_]*$", ErrorMessage = "Key must be lowercase letters, digits and underscores, starting with a letter.")]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>
    /// **True for a currency people pay real money for.** Cannot be changed later, and the reason is
    /// not tidiness: flipping a soft currency hard leaves every unbounded valuation row already
    /// pointing at it in place, and flipping a hard one soft is how a currency people bought quietly
    /// becomes farmable.
    /// <para>
    /// Setting this commits to bounded gameplay sources: a <c>PickupValuation</c> for it is refused
    /// without a per-day cap, and an <c>EVERY_TIME</c> reward rule granting it is refused without a
    /// daily limit. Both at authoring time, because a missing bound found later is currency already
    /// in circulation.
    /// </para>
    /// </summary>
    public bool IsHard { get; set; }

    /// <summary>
    /// Most of this currency one account may **earn from gameplay** in a UTC day. Null means no
    /// ceiling for a soft currency; for a hard one, omitting it means **zero** — no gameplay source at
    /// all, which is the only safe default for something with a price attached.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long? DailyEarnCap { get; set; }
}

public class UpdateCurrencyRequest
{
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>Retire a currency by setting this false; balances and history survive.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Retunes the daily earning ceiling. Null lifts it for a soft currency; on a **hard** one null is
    /// read as zero rather than as "unlimited", because lifting the ceiling on something people paid
    /// for by leaving a field blank is not a decision anybody should be able to make by accident.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long? DailyEarnCap { get; set; }
}

public class CurrencyDto
{
    public Guid CurrencyId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Enabled { get; init; }

    /// <summary>Whether this is a currency people pay real money for. Immutable once created.</summary>
    public bool IsHard { get; init; }

    /// <summary>Most one account may earn from gameplay per UTC day, or null for no ceiling.</summary>
    public long? DailyEarnCap { get; init; }
}

// ---- balances ------------------------------------------------------------------------------

/// <summary>
/// One balance as the client sees it. <c>currency</c> is the stable **key**, not the row id —
/// this is the shape the Unity wallet reconciles against.
/// </summary>
public class BalanceDto
{
    public string Currency { get; init; } = string.Empty;

    /// <summary>Absolute authoritative balance, never a delta.</summary>
    public long Amount { get; init; }
}

public class BalancesResponse
{
    public IReadOnlyList<BalanceDto> Balances { get; init; } = [];
}

// ---- wallet mutation (internal) --------------------------------------------------------------

/// <summary>
/// A request to move a balance. Built by the reward, purchase and admin paths — never by the
/// client, which is why there is no model binding attribute on it.
/// </summary>
public class WalletMutation
{
    public required Guid UserId { get; init; }
    public required Guid CurrencyId { get; init; }

    /// <summary>Signed: positive credits, negative debits.</summary>
    public required long Delta { get; init; }

    public required CurrencyTransactionType TransactionType { get; init; }
    public required LedgerSourceType SourceType { get; init; }

    public string? SourceId { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? Metadata { get; init; }
}

public class WalletMutationResult
{
    public Guid CurrencyId { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>Absolute balance after the mutation.</summary>
    public long Amount { get; init; }

    public long LedgerEntryId { get; init; }
}

// ---- admin grant ---------------------------------------------------------------------------

/// <summary>
/// Credits or deducts the **caller's own** balance. There is no target user field: the account is
/// taken from the bearer token, so this can only ever move the balance of whoever is signed in.
/// </summary>
public class AdminGrantCurrencyRequest
{
    [Required]
    public Guid CurrencyId { get; set; }

    /// <summary>
    /// Signed. Positive grants, negative deducts — a deduction that would overdraw is refused
    /// rather than clamped, so a mistyped correction fails loudly.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>Free-text note stored on the ledger entry, e.g. "compensation for lost progress".</summary>
    [MaxLength(256)]
    public string? Reason { get; set; }
}
