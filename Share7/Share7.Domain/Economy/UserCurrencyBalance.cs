namespace Share7.Domain.Economy;

/// <summary>
/// How much of one currency one user holds — the fast current projection of the ledger.
/// <para>
/// **This is the authoritative balance.** The Unity wallet is a cache of it, and reconciles
/// against <c>GET /api/commerce/balances</c>.
/// </para>
/// <para>
/// One row per (user, currency), enforced by a unique index. Every mutation goes through
/// <c>WalletService</c>, which writes this row and a <see cref="CurrencyLedgerEntry"/> in the
/// same transaction — a balance that moved without a ledger row is a bug, not a shortcut.
/// </para>
/// </summary>
public class UserCurrencyBalance
{
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to <c>AspNetUsers</c>, declared in Infrastructure because Domain cannot see
    /// <c>ApplicationUser</c> without inverting the dependency direction. Cascades on delete.
    /// </summary>
    public Guid UserId { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Whole units. Never negative — debits that would overdraw are refused.</summary>
    public long Amount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
