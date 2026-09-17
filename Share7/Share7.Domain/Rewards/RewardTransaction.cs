namespace Share7.Domain.Rewards;

/// <summary>
/// The record that one rule has already paid one user for one event. **Immutable** — nothing
/// updates a row here; a mistake is corrected by a compensating ledger entry, the same way the
/// rest of the economy handles it.
/// <para>
/// This table *is* the idempotency guarantee. The unique index on
/// (<see cref="UserId"/>, <see cref="RewardRuleId"/>, <see cref="IdempotencyKey"/>) is what stops
/// a double credit — not the <c>SELECT</c> that precedes the insert, which two concurrent attempts
/// can both pass. The read is an optimisation; the index is the contract.
/// </para>
/// </summary>
public class RewardTransaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid RewardRuleId { get; set; }
    public RewardRule? RewardRule { get; set; }

    /// <summary>
    /// Copied from the rule at the time of payment rather than read through the relation. A rule
    /// can be retired or edited; what this transaction was paid *for* must stay legible regardless.
    /// </summary>
    public RewardEventType EventType { get; set; }

    public Economy.LedgerSourceType SourceType { get; set; }

    /// <summary>
    /// What triggered it, in the source domain's terms — a lesson id for the lesson events.
    /// Not a foreign key, for the same reason the ledger's is not: this record has to outlive the
    /// thing it points at.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// Derived server-side from the validated attempt, never supplied by the client as a reward
    /// identity. Shape depends on the rule's repeat policy: a <c>Once</c> rule keys on the event
    /// and its reference so it can only ever match itself, while an <c>EveryTime</c> rule keys on
    /// the individual attempt.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Which submission produced this payment — the attempt ordinal, or the client's request id
    /// when it sent one.
    /// <para>
    /// Distinct from <see cref="IdempotencyKey"/> and needed alongside it. A <c>Once</c> rule keys
    /// on the event, so its key is identical on the first completion and the fiftieth; without
    /// this there is no way to tell "you are retrying the submission that earned this" from "you
    /// earned this weeks ago". The first should replay the reward back to the client, the second
    /// must report nothing earned — otherwise the client celebrates coins it did not receive.
    /// </para>
    /// </summary>
    public string SubmissionKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<RewardTransactionLine> Lines { get; set; } = new List<RewardTransactionLine>();
}
