using Share7.Domain.Economy;

namespace Share7.Domain.Rewards;

/// <summary>
/// One currency actually paid by a <see cref="RewardTransaction"/>. A multi-currency reward has
/// one line per currency, all written inside the transaction that moved the balances.
/// <para>
/// Not redundant with the ledger. The ledger answers "what happened to this balance"; these lines
/// answer "what did this reward pay", which is what a **retry** has to replay back to the client
/// verbatim. Reconstructing that by matching ledger rows on an idempotency key string would work,
/// but it would put a text index on the hot path of every attempt submission.
/// </para>
/// </summary>
public class RewardTransactionLine
{
    /// <summary>Sequential, like the ledger — these are read in the order they were written.</summary>
    public long Id { get; set; }

    public Guid RewardTransactionId { get; set; }
    public RewardTransaction? RewardTransaction { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Whole units credited. Always positive — a rule never debits.</summary>
    public long Amount { get; set; }

    /// <summary>The user's balance in this currency immediately after the credit landed.</summary>
    public long BalanceAfter { get; set; }

    /// <summary>
    /// The <see cref="CurrencyLedgerEntry"/> this line produced, so a reward can be traced to its
    /// exact effect on the balance.
    /// <para>
    /// A bare number rather than a foreign key. Both this table and the ledger cascade from the
    /// user, and a second relation between them would give SQL Server two cascade paths to the
    /// same table, which it refuses outright. Keeping it a plain reference also stops the Rewards
    /// domain taking a structural dependency on Economy's tables.
    /// </para>
    /// </summary>
    public long LedgerEntryId { get; set; }
}
