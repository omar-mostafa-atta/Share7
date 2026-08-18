namespace Share7.Domain.Economy;

/// <summary>
/// A spendable in-game currency, e.g. coins. **Virtual only** — no row here represents real
/// money, and amounts are whole units, never fractional.
/// </summary>
public class Currency
{
    public Guid Id { get; set; }

    /// <summary>
    /// The stable identifier the client speaks: <c>"coins"</c>. This — not <see cref="Id"/> — is
    /// what appears on the wire, because a balance the client caches has to survive the row being
    /// re-seeded in a fresh environment.
    /// <para>
    /// Unique, lowercase, and **must not change between deployments**. Renaming one orphans every
    /// cached client balance; add a new currency instead.
    /// </para>
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable name for admin tooling. Not what the client displays.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Retires a currency without deleting it. Existing balances and ledger history stay
    /// resolvable; new credits and debits are refused.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserCurrencyBalance> Balances { get; set; } = new List<UserCurrencyBalance>();
}
