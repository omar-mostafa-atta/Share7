namespace Share7.Domain.Commerce;

/// <summary>
/// How a purchase ended. Stored as text so the audit trail reads plainly in a SQL window and
/// reordering the enum cannot re-map history — the same rule the currency ledger follows.
/// </summary>
public enum TransactionState
{
    Unknown = 0,

    /// <summary>Charged and granted. The only state that consumes a purchase limit.</summary>
    Completed,

    /// <summary>
    /// The server answered and said no — too few coins, offer expired, limit reached. **Nothing was
    /// charged and nothing was granted.** The client should not retry without changing something.
    /// </summary>
    Refused
}
