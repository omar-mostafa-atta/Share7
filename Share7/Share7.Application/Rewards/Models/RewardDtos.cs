using System.ComponentModel.DataAnnotations;
using Share7.Domain.Progress;
using Share7.Domain.Runs;

namespace Share7.Application.Rewards.Models;

// ---- evaluation input (internal) ------------------------------------------------------------

/// <summary>
/// Everything the reward engine is allowed to know about a finished attempt — all of it
/// **recomputed server-side** by <c>ProgressService</c>. There is no field here the client can
/// set that influences what a reward is worth.
/// </summary>
public class ProgressRewardContext
{
    public required Guid UserId { get; init; }
    public required Guid GameId { get; init; }
    public required Guid LessonId { get; init; }

    /// <summary>
    /// Which attempt this is, after the increment. Part of the idempotency key for rules that pay
    /// every time, so replaying attempt 3 cannot be mistaken for attempt 4.
    /// </summary>
    public required int AttemptNumber { get; init; }

    /// <summary>Server-recomputed score. Recorded as ledger metadata, not used to pick a payout.</summary>
    public required int Percent { get; init; }

    public required CompletionState CompletionState { get; init; }

    /// <summary>
    /// The client's optional idempotency key for this submission. When present it replaces the
    /// attempt ordinal in the key, which is what makes a retried submission pay once instead of
    /// twice. Absent on older clients — see <c>SubmitAttemptRequest.RequestId</c>.
    /// </summary>
    public string? RequestId { get; init; }
}

/// <summary>
/// What the settlement job knows about one player's finished placing.
/// <para>
/// Carries no amount. The rank decides which rule matches; the rule decides what it pays. A
/// context that carried a prize would put the payout back in the caller's hands, which is the one
/// thing this whole subsystem is arranged to prevent.
/// </para>
/// </summary>
public class SettlementRewardContext
{
    public required Guid UserId { get; init; }

    public required Guid CycleId { get; init; }

    /// <summary>Which cohort's ladder this placing is on, as its enum name.</summary>
    public required string Cohort { get; init; }

    public required Guid CohortKey { get; init; }

    /// <summary>
    /// The rule scope, <c>{boardKey}:{band}</c>. The band is the coarsest one the rank falls in,
    /// so a single rule can pay everyone in the top ten without ten rules.
    /// </summary>
    public required string ReferenceKey { get; init; }

    public required int FinalRank { get; init; }

    public required long Value { get; init; }
}

/// <summary>
/// What the claim path knows about one finished objective.
/// <para>
/// Carries no amount, like every other context here. The objective decides which rule matches; the
/// rule decides what it pays.
/// </para>
/// </summary>
public class ObjectiveRewardContext
{
    public required Guid UserId { get; init; }

    /// <summary>The objective's stable key. Matches the rule's <c>ReferenceKey</c>.</summary>
    public required string ObjectiveKey { get; init; }

    /// <summary>
    /// Which cycle is being claimed. Part of the idempotency key, so today's daily and yesterday's
    /// are separate payouts of the same objective rather than one that already fired.
    /// </summary>
    public required string CycleKey { get; init; }

    /// <summary>The client's optional idempotency key for this claim.</summary>
    public string? RequestId { get; init; }
}

// ---- evaluation output (client-facing) ------------------------------------------------------

/// <summary>
/// One currency credited by one reward. A **delta**, unlike <c>BalanceDto</c>, which is absolute —
/// this is "you just earned 10", not "you have 10".
/// </summary>
public class RewardGrantDto
{
    public string Currency { get; init; } = string.Empty;
    public long Amount { get; init; }
}

/// <summary>
/// One rule that fired, and everything it paid. Several can come back from a single attempt: a
/// perfect run of a lesson matches the attempted, completed and aced rules at once.
/// </summary>
/// <summary>One product handed over by a reward. A badge, usually.</summary>
public class RewardEntitlementDto
{
    public Guid ProductId { get; init; }

    /// <summary>The product's stable key, which the client maps to its own art.</summary>
    public string ProductKey { get; init; } = string.Empty;

    /// <summary>False when the player already owned it — granting is idempotent, not an error.</summary>
    public bool IsNew { get; init; }
}

public class RewardDto
{
    public Guid RuleId { get; init; }

    /// <summary>Admin-facing label. Handy in logs; Unity should not render it.</summary>
    public string RuleName { get; init; } = string.Empty;

    /// <summary>Stable machine token, e.g. <c>LESSON_COMPLETED</c>.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// The reward transaction. Stable across retries — a resubmitted attempt that matches an
    /// existing reward returns this same id rather than minting a new one.
    /// </summary>
    public Guid TransactionId { get; init; }

    public IReadOnlyList<RewardGrantDto> Grants { get; init; } = [];

    /// <summary>Products this reward handed over. Empty for the overwhelming majority of rules.</summary>
    public IReadOnlyList<RewardEntitlementDto> Entitlements { get; init; } = [];
}

// ---- rule authoring (admin) -----------------------------------------------------------------

public class RewardGrantRequest
{
    [Required]
    public Guid CurrencyId { get; set; }

    /// <summary>Whole units to credit. Must be positive — a rule cannot take currency away.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "A reward grant must be a positive amount.")]
    public long Amount { get; set; }
}

public class CreateRewardRuleRequest
{
    /// <summary>
    /// Products this rule hands over — typically a badge for an achievement. Optional, and a rule
    /// granting only these with no currency is valid.
    /// </summary>
    public List<Guid> EntitlementProductIds { get; set; } = [];

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary><c>LESSON_ATTEMPTED</c>, <c>LESSON_COMPLETED</c> or <c>LESSON_ACED</c>.</summary>
    [Required]
    [MaxLength(48)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Lesson id to restrict this rule to, or null (the normal case) to apply it to every lesson.
    /// A global rule and a lesson-specific one both fire and both pay.
    /// </summary>
    [MaxLength(128)]
    public string? ReferenceKey { get; set; }

    /// <summary><c>ONCE</c> (default) or <c>EVERY_TIME</c>.</summary>
    [MaxLength(32)]
    public string RepeatPolicy { get; set; } = "ONCE";

    /// <summary>Only meaningful with <c>EVERY_TIME</c>; rejected otherwise rather than ignored.</summary>
    [Range(1, 86_400)]
    public int? CooldownSeconds { get; set; }

    /// <summary>Only meaningful with <c>EVERY_TIME</c>; rejected otherwise rather than ignored.</summary>
    [Range(1, 1_000)]
    public int? DailyLimit { get; set; }

    /// <summary>
    /// What the ledger entries are stamped with, e.g. <c>LESSON_REWARD</c>. Defaults to
    /// <c>LESSON_REWARD</c>.
    /// </summary>
    [MaxLength(48)]
    public string? TransactionType { get; set; }

    /// <summary>At least one. Each currency may appear once.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "A reward rule must grant at least one currency.")]
    public List<RewardGrantRequest> Grants { get; set; } = [];

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Replaces a rule's policy and its full grant set.
/// <para>
/// <c>EventType</c> and <c>ReferenceKey</c> are deliberately absent: changing what a rule watches
/// would strand the reward transactions already recorded against it, which claim payment for an
/// event the rule no longer represents. Retire it and author a new one.
/// </para>
/// </summary>
public class UpdateRewardRuleRequest
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(32)]
    public string RepeatPolicy { get; set; } = "ONCE";

    [Range(1, 86_400)]
    public int? CooldownSeconds { get; set; }

    [Range(1, 1_000)]
    public int? DailyLimit { get; set; }

    [MaxLength(48)]
    public string? TransactionType { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "A reward rule must grant at least one currency.")]
    public List<RewardGrantRequest> Grants { get; set; } = [];

    public bool Enabled { get; set; } = true;
}

public class RewardRuleGrantDto
{
    public Guid CurrencyId { get; init; }
    public string Currency { get; init; } = string.Empty;
    public long Amount { get; init; }

    /// <summary>
    /// False when the currency has been retired. The whole rule is skipped at evaluation time
    /// while this is false — surfaced here so the admin can see *why* a rule stopped paying.
    /// </summary>
    public bool CurrencyEnabled { get; init; }
}

public class RewardRuleDto
{
    public Guid RuleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string? ReferenceKey { get; init; }
    public string RepeatPolicy { get; init; } = string.Empty;
    public int? CooldownSeconds { get; init; }
    public int? DailyLimit { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public IReadOnlyList<RewardRuleGrantDto> Grants { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

/// <summary>
/// Everything the reward engine is allowed to know about a settled run — all of it owned by the
/// server, which opened the run and stamped its clock. There is no field here the client can set
/// that influences what a reward is worth.
/// <para>
/// **This covers the <i>fixed</i> half of a run's payout only** — "completed a run", "first run of
/// the day". What the pickups were worth is not a rule, because it varies with what was collected;
/// <c>PickupValuation</c> answers that, and both halves are granted through the same wallet inside
/// the same transaction.
/// </para>
/// </summary>
public class RunRewardContext
{
    public required Guid UserId { get; init; }
    public required Guid GameId { get; init; }

    /// <summary>
    /// Identifies the settlement, and is why a run cannot be paid twice by an <c>EVERY_TIME</c> rule:
    /// one run settles once, so its id is a submission key that no retry can duplicate.
    /// </summary>
    public required Guid RunId { get; init; }

    /// <summary>Server-clamped, not the raw client claim. Recorded as ledger metadata.</summary>
    public required int DurationMs { get; init; }

    /// <summary>How the run ended. Metadata for now; a future rule may branch on it.</summary>
    public required RunOutcome Outcome { get; init; }
}
