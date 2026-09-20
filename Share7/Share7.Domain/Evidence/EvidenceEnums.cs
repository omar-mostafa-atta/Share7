namespace Share7.Domain.Evidence;

/// <summary>
/// How strongly an observation may be relied upon, declared by the contract that produced it
/// rather than computed from the outcome.
/// <para>
/// **This is an ordinal, and the order is load-bearing.** Proficiency filters select a floor;
/// exam-facing reporting takes <see cref="Assessment"/> only. See
/// <c>Docs/EducationalArchitecture.md</c> §3.3.
/// </para>
/// </summary>
public enum EvidenceStrength
{
    /// <summary>
    /// Not evidence. The default for anything no contract names, and the value a contract carries
    /// while it is being trialled. Recorded, never counted.
    /// </summary>
    None = 0,

    /// <summary>
    /// A designed gameplay interaction that correlates with a target but is not an item response.
    /// Enters the model only at the contract's declared weight, which **starts at zero** and is
    /// raised only once paired <see cref="Assessment"/> evidence shows it predicts.
    /// </summary>
    Indicative = 1,

    /// <summary>
    /// A genuine item response collected under conditions that inflate performance — retries
    /// allowed, hints available, unbounded time, or unverifiable (offline) conditions. Counts
    /// toward the learner model and recency; **never toward an exam projection.**
    /// </summary>
    Practice = 2,

    /// <summary>
    /// An item administration under controlled conditions: first encounter, unhinted, unaided, no
    /// retry within the administration. The only class an exam projection may read.
    /// </summary>
    Assessment = 3
}

/// <summary>
/// How an interaction reached the learner, which decides whether its conditions can be trusted and
/// whether the response is individually attributable.
/// </summary>
public enum EvidenceDeliveryMode
{
    /// <summary>One learner, one device, server-timed session.</summary>
    Solo = 0,

    /// <summary>
    /// Several learners answering together. **Not individually attributable** — an observation
    /// carries a group id and is discounted or excluded rather than credited to one child.
    /// </summary>
    CoOp = 1,

    /// <summary>Administered with an invigilator present, which is what makes a claim exam-grade.</summary>
    Supervised = 2,

    /// <summary>
    /// Collected while the device was offline and synced later. **Capped at
    /// <see cref="EvidenceStrength.Practice"/>**: the client clock is unverifiable, so the
    /// conditions cannot support an exam-grade claim.
    /// </summary>
    Offline = 3
}

/// <summary>
/// What kind of thing a contract turns into evidence. Phase 0 recognises item responses only;
/// gameplay interaction kinds arrive with their own contracts and are never assumed.
/// </summary>
public static class InteractionKinds
{
    /// <summary>A learner answered an assessment item and the server graded it.</summary>
    public const string ItemResponse = "item_response";

    /// <summary>
    /// An item served inside a formal sitting — an <c>AssessmentAdministration</c>, whose
    /// conditions the server set before the first question was handed out.
    /// <para>
    /// **A different kind of interaction from a lesson attempt, and therefore a different
    /// contract.** In a lesson the conditions are whatever the game happened to allow; in a sitting
    /// they are declared up front by whoever opened it, enforced server-side for its whole
    /// duration, and unchangeable once a learner has started. That difference is the entire basis
    /// on which a sitting's evidence may be exam-grade and a lesson's usually is not, so it belongs
    /// in a contract a human published rather than in a branch somebody wrote.
    /// </para>
    /// </summary>
    public const string AssessmentResponse = "assessment_response";
}
