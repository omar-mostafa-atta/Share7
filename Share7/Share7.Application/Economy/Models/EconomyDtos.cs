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
}

public class CurrencyDto
{
    public Guid CurrencyId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Enabled { get; init; }
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
