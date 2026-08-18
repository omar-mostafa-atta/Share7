using Share7.Domain.Economy;

namespace Share7.Domain.Rewards;

/// <summary>
/// Configuration answering "what is this gameplay outcome worth?". **The client never sees a rule
/// and never supplies an amount** — it reports an event, the server decides the payout.
/// <para>
/// One rule is one policy over one event. What it actually pays lives in
/// <see cref="Grants"/>, one row per currency, so a single rule can hand out coins *and* gems as
/// one atomic act with one cooldown and one audit record. Putting the currency on the rule itself
/// would force two rules for that, with two counters that can drift apart and a failure mode where
/// half the reward lands.
/// </para>
/// <para>
/// Rules compose rather than override. A global rule and a lesson-specific rule for the same event
/// both fire, and the student is paid both — that is how "10 coins for any lesson, 50 bonus for
/// the final one" is expressed. There is deliberately no most-specific-wins precedence to reason
/// about.
/// </para>
/// </summary>
public class RewardRule
{
    public Guid Id { get; set; }

    /// <summary>Human-readable label for admin tooling. Never rendered to a player.</summary>
    public string Name { get; set; } = string.Empty;

    public RewardEventType EventType { get; set; }

    /// <summary>
    /// Narrows the rule to one thing — for lesson events, a lesson id as text.
    /// <para>
    /// **Null means "every occurrence of this event"**, which is the normal case. Stored as a
    /// string rather than a Guid so a later event type can scope by something that is not a
    /// lesson (a game key, a chapter, a season id) without another column.
    /// </para>
    /// </summary>
    public string? ReferenceKey { get; set; }

    public RewardRepeatPolicy RepeatPolicy { get; set; }

    /// <summary>
    /// Minimum seconds between two payouts of this rule to one user. Ignored unless
    /// <see cref="RepeatPolicy"/> is <see cref="RewardRepeatPolicy.EveryTime"/>.
    /// </summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>
    /// Most payouts of this rule to one user per UTC day. Ignored unless <see cref="RepeatPolicy"/>
    /// is <see cref="RewardRepeatPolicy.EveryTime"/>.
    /// <para>
    /// Counted, not reserved: two attempts racing at the boundary can both pass the check and take
    /// the total one over. Tolerated on purpose — the alternative is a per-(user, rule, day)
    /// counter row to lock, which is a lot of machinery to stop a student earning one extra coin.
    /// </para>
    /// </summary>
    public int? DailyLimit { get; set; }

    /// <summary>
    /// What the resulting ledger entries are stamped with. Held on the rule rather than derived
    /// from the event so a new kind of reward is a row, not a migration and a switch statement.
    /// </summary>
    public CurrencyTransactionType TransactionType { get; set; } = CurrencyTransactionType.LessonReward;

    /// <summary>
    /// Switches the rule off without deleting it. Retiring rather than deleting keeps historical
    /// <see cref="RewardTransaction"/> rows resolvable — the ledger has to stay explicable.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<RewardRuleGrant> Grants { get; set; } = new List<RewardRuleGrant>();
}
