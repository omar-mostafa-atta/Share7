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

    /// <summary>
    /// Whether this currency may be named as a **price**. False makes it earn-only: it accumulates
    /// and is never debited by a purchase.
    /// <para>
    /// This exists for <c>xp</c>, and it is what makes the player level cheap to compute. A
    /// spendable balance falls, so a level derived from it would fall too — preventing that needs
    /// either a second lifetime-earned counter or a <c>SUM()</c> over a forever-growing ledger on a
    /// hot read path. Making the currency non-spendable collapses *lifetime earned* and *current
    /// balance* into the same number, so the level is a pure function of one stored value.
    /// </para>
    /// <para>
    /// Enforced where prices are authored, not here. A debit is still structurally possible — an
    /// admin correction must stay able to undo a mistaken grant — but nothing a player does can
    /// cause one.
    /// </para>
    /// </summary>
    public bool IsSpendable { get; set; } = true;

    /// <summary>
    /// Whether this is a **hard** currency: one people pay real money for.
    /// <para>
    /// Declared now, before <c>gems</c> exists, because the rule it carries is much easier to hold
    /// from the start than to retrofit — *a hard currency must never be earnable from an uncapped
    /// gameplay source*. A gem that spawns on a procedural track is an unbounded, unauditable path
    /// to something with a real price attached: a fraud and refund surface, and the end of the IAP
    /// price anchor.
    /// </para>
    /// <para>
    /// See <c>CURRENCY_BACKEND_IMPLEMENTATION_GUIDE.md</c> §7 for the constraints that attach to
    /// this once a gameplay earning path exists. Today nothing sets it true.
    /// </para>
    /// </summary>
    public bool IsHard { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserCurrencyBalance> Balances { get; set; } = new List<UserCurrencyBalance>();
}
