using Share7.Domain.Evidence;
using Share7.Domain.Play;

namespace Share7.Application.Evidence.Models;

/// <summary>
/// What the caller knows about one answer, before the server decides what it means.
/// <para>
/// **Everything here is a fact or a condition, never a judgement.** The recorder grades, derives the
/// attempt ordinal, resolves the contract and assigns strength; the caller supplies only what it
/// alone can observe.
/// </para>
/// </summary>
public sealed record EvidenceAnswer
{
    /// <summary>
    /// The per-language rendering the learner was shown — a <c>Question</c> id today. The recorder
    /// resolves the item and the item version from it, because those are identity facts the caller
    /// has no business asserting.
    /// </summary>
    public required Guid ItemLocalizationId { get; init; }

    /// <summary>Which choice the learner picked, or null when the item was never reached.</summary>
    public Guid? ChoiceId { get; init; }

    /// <summary>Graded by the caller against the answer key it already loaded — never by the client.</summary>
    public required bool IsCorrect { get; init; }

    /// <summary>The payload named a choice outside this item. Kept as a diagnostic fingerprint.</summary>
    public bool WasUnrecognised { get; init; }

    // Conditions the client is the only party able to observe.
    public int? ElapsedMs { get; init; }
    public int HintsUsed { get; init; }
    public int? TimeLimitMs { get; init; }

    /// <summary>
    /// Whether the learner could re-answer this item within this administration. Null means "the
    /// game did not say", and the recorder infers it from the play context.
    /// </summary>
    public bool? RetryPermitted { get; init; }

    public DateTime? OccurredAtUtc { get; init; }
}

/// <summary>The session-level facts shared by every answer in one submission.</summary>
public sealed record EvidenceRecordingContext
{
    public required Guid LearnerId { get; init; }
    public required Guid LangId { get; init; }
    public required PlayContextKind PlayContext { get; init; }

    public Guid? GameId { get; init; }
    public Guid? ModeId { get; init; }
    public Guid? EventId { get; init; }
    public Guid? NodeId { get; init; }
    public Guid? OrgId { get; init; }
    public Guid? GroupId { get; init; }

    public int ContentVersion { get; init; }
    public EvidenceDeliveryMode DeliveryMode { get; init; } = EvidenceDeliveryMode.Solo;
    public bool WasAided { get; init; }

    public string? IdempotencyKey { get; init; }
    public required DateTime ReceivedAtUtc { get; init; }

    /// <summary>Which kind of interaction these answers are. Decides which contract applies.</summary>
    public string InteractionKind { get; init; } = InteractionKinds.ItemResponse;
}

/// <summary>What was written, for callers that need to report or assert on it.</summary>
public sealed record EvidenceRecordingResult
{
    public required int RecordedCount { get; init; }
    public required Guid ContractVersionId { get; init; }

    /// <summary>
    /// Strength per answer, keyed by the localization the caller named, derived from the contract
    /// and the conditions. Returned rather than stored — see
    /// <see cref="EvidenceContractVersion.StrengthFor"/> for why.
    /// </summary>
    public required IReadOnlyDictionary<Guid, EvidenceStrength> StrengthByLocalization { get; init; }

    public static readonly EvidenceRecordingResult Nothing = new()
    {
        RecordedCount = 0,
        ContractVersionId = Guid.Empty,
        StrengthByLocalization = new Dictionary<Guid, EvidenceStrength>()
    };
}
