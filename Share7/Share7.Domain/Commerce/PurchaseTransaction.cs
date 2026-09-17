using Share7.Domain.Economy;

namespace Share7.Domain.Commerce;

/// <summary>
/// One attempt to buy an offer: who, what, when, and how it ended.
/// <para>
/// **Refusals are recorded too**, not just successes. A student reporting "it took my coins and gave
/// me nothing" is answerable from this table, and an offer nobody can afford shows up as a run of
/// <see cref="TransactionState.Refused"/> rows rather than as silence.
/// </para>
/// <para>
/// **Append-only.** A row is written once and never edited — the state it ends in is the state it
/// keeps. Corrections are new rows plus a compensating ledger entry, exactly like the currency
/// ledger, which is what keeps the audit trail trustworthy.
/// </para>
/// </summary>
public class PurchaseTransaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OfferId { get; set; }
    public Offer? Offer { get; set; }

    public TransactionState State { get; set; }

    /// <summary>
    /// The client's idempotency key. **Unique per (user, requestId)**: a retry of the same purchase
    /// collides here and returns the original outcome rather than charging twice, which is the
    /// guarantee the commerce contract is most insistent about. That the database enforces it — not
    /// a read-then-write in the service — is what makes it hold under concurrency.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// What was actually charged, captured at purchase time rather than read back through the offer.
    /// The offer's price can change afterwards; what this account paid cannot.
    /// </summary>
    public long Price { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>
    /// Why it was refused, as a Unity localization key — <c>commerce.insufficient_balance</c>. Null
    /// on a completed purchase. The backend never stores display prose.
    /// </summary>
    public string? FailureReasonKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
