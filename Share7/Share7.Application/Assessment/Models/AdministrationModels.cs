using Share7.Domain.Assessment;
using Share7.Domain.Evidence;

namespace Share7.Application.Assessment.Models;

/// <summary>
/// A sitting, as the client sees it. Carries the conditions it was opened under, because a client
/// that does not know whether retries are permitted cannot render the paper correctly — and
/// because those conditions are what the resulting evidence will be judged by.
/// </summary>
public sealed record AdministrationDto
{
    public required Guid AdministrationId { get; init; }
    public required Guid FormId { get; init; }
    public required string AssessmentName { get; init; }
    public required AssessmentPurpose Purpose { get; init; }
    public required AdministrationState State { get; init; }

    public required int ItemCount { get; init; }

    /// <summary>How many positions the learner has answered. Resumption reads this.</summary>
    public required int AnsweredCount { get; init; }

    public required bool RetryPermitted { get; init; }
    public required bool WasAided { get; init; }
    public required EvidenceDeliveryMode DeliveryMode { get; init; }
    public int? TimeLimitMs { get; init; }

    public required DateTime StartedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }

    /// <summary>
    /// Server-graded, and null until the sitting closes. **A score on a paper, not a proficiency**
    /// — it says what happened once and makes no claim that generalizes to anything.
    /// </summary>
    public decimal? PointsEarned { get; init; }
    public decimal? PointsAvailable { get; init; }

    /// <summary>
    /// The evidence class this sitting's answers will carry, computed from the declared conditions
    /// before a single question is served. Surfaced so an admin can see that a sitting they set up
    /// with retries on will never produce exam-grade evidence, **while there is still time to
    /// change it**, rather than discovering it in a report three months later.
    /// </summary>
    public required EvidenceStrength ExpectedStrength { get; init; }
}

/// <summary>One position on the paper, as served. The item's own content comes from the content API.</summary>
public sealed record AdministrationItemDto
{
    public required int Position { get; init; }
    public required Guid ItemVersionId { get; init; }

    /// <summary>The per-language rendering to show. Null when this language has none, which is a content fault.</summary>
    public Guid? QuestionId { get; init; }

    public string? Stem { get; init; }
    public required IReadOnlyList<AdministrationChoiceDto> Choices { get; init; }
    public required decimal Points { get; init; }

    /// <summary>Whether this position already carries an answer, for resumption.</summary>
    public required bool IsAnswered { get; init; }
}

/// <summary>
/// One option. **No correctness field**, by the same rule that already governs scoring: a client
/// that is told the answer is a client that can be read.
/// </summary>
public sealed record AdministrationChoiceDto
{
    public required Guid ChoiceId { get; init; }
    public required string Text { get; init; }
    public required int Order { get; init; }
}

/// <summary>What the client sends when a learner answers one position.</summary>
public sealed record AdministrationAnswerRequest
{
    public required int Position { get; init; }

    /// <summary>Null when the learner skipped or ran out of time. **Not the same as wrong.**</summary>
    public Guid? ChoiceId { get; init; }

    public int? ElapsedMs { get; init; }
    public int HintsUsed { get; init; }
}

/// <summary>
/// What the server says back. Correctness is returned because the learner is entitled to know, and
/// it is returned **after** the response is durably recorded, never before.
/// </summary>
public sealed record AdministrationAnswerResult
{
    public required int Position { get; init; }
    public required bool IsCorrect { get; init; }
    public required Guid? CorrectChoiceId { get; init; }
    public required bool Accepted { get; init; }

    /// <summary>Why it was not accepted, when it was not — expired, already answered, out of range.</summary>
    public string? Rejection { get; init; }

    public required int AnsweredCount { get; init; }
    public required int ItemCount { get; init; }
}

/// <summary>How a sitting is opened.</summary>
public sealed record StartAdministrationRequest
{
    /// <summary>The form to sit. Either this or <see cref="AssessmentId"/>.</summary>
    public Guid? FormId { get; init; }

    /// <summary>The assessment to sit; the service picks its newest sealed form.</summary>
    public Guid? AssessmentId { get; init; }

    public AssessmentPurpose Purpose { get; init; } = AssessmentPurpose.Formative;

    public EvidenceDeliveryMode DeliveryMode { get; init; } = EvidenceDeliveryMode.Solo;

    /// <summary>
    /// Declared by whoever sets the sitting up, never by the learner's device. A client that could
    /// assert "unaided, no retries" would be asserting its own evidence strength.
    /// </summary>
    public bool RetryPermitted { get; init; }
    public bool WasAided { get; init; }

    public Guid? LangId { get; init; }
    public string? IdempotencyKey { get; init; }
}
