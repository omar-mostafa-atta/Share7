namespace Share7.Domain.Economy;

/// <summary>
/// One immutable record of a balance change. **Append-only** — entries are never updated or
/// deleted; a mistake is corrected by writing a compensating entry
/// (<see cref="CurrencyTransactionType.Correction"/> or
/// <see cref="CurrencyTransactionType.Reversal"/>) so the history stays truthful.
/// <para>
/// The balance table can be rebuilt from this by summing <see cref="Amount"/> per
/// (user, currency). That is the point: if the two ever disagree, the ledger is right.
/// </para>
/// </summary>
public class CurrencyLedgerEntry
{
    /// <summary>
    /// Sequential rather than a Guid. The ledger is read in order — "what happened to this
    /// account, in sequence" — and a random key makes that ordering arbitrary.
    /// </summary>
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>
    /// Signed delta: positive credits, negative debits. Storing the movement rather than an
    /// absolute lets the balance be re-derived by summing.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// The balance immediately after this entry, captured under the same row lock that applied
    /// the change. Makes a disagreement between ledger and balance table pinpointable to a single
    /// entry instead of requiring a full replay.
    /// </summary>
    public long BalanceAfter { get; set; }

    public CurrencyTransactionType TransactionType { get; set; }

    public LedgerSourceType SourceType { get; set; }

    /// <summary>
    /// Free-form reference into the source domain — a lesson id, an offer id, an admin's user id.
    /// Deliberately not a foreign key: the ledger has to outlive the things it references, and an
    /// FK would either block their deletion or cascade away the audit trail.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// The idempotency key of the operation that produced this entry, when it had one. Not itself
    /// the uniqueness guarantee — that lives on the owning table (reward or purchase) — but it is
    /// what lets an auditor trace an entry back to the request that caused it.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Optional JSON for dimensions this schema has no column for yet. Keeps a future module from
    /// needing a migration to record context alongside its entries.
    /// </summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
