namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the evidence contracts the **code** depends on existing, seeded rather than
/// authored through the admin API.
/// <para>
/// <c>LearnerResponse.EvidenceContractVersionId</c> is non-nullable, so a deployment with no
/// published contract has an attempt endpoint that records no evidence at all — silently, and
/// unrecoverably, because the evidence it did not write cannot be reconstructed later. That makes
/// the platform contract a schema-level dependency rather than configuration, and it is seeded for
/// the same reason <see cref="CurrencyIds.Xp"/> is.
/// </para>
/// <para>
/// Game-specific contracts are ordinary data and belong in the admin console: they are an assertion
/// that a particular gameplay interaction means something educationally, which is a judgement a
/// human makes and a reviewer approves. Only the platform's own item-response contract is seeded.
/// </para>
/// </summary>
public static class EvidenceContractIds
{
    /// <summary>
    /// The platform item-response contract: a lesson attempt posted to
    /// <c>POST /api/progress/attempts</c> is an administration of assessment items.
    /// </summary>
    public static readonly Guid PlatformLessonAttempt =
        Guid.Parse("b1e7c4a9-2f68-4d3b-9e57-0a4c8d15f2b6");

    /// <summary>Version 1 of the above. Published at seed time; never edited.</summary>
    public static readonly Guid PlatformLessonAttemptV1 =
        Guid.Parse("c2f8d5ba-3079-4e4c-af68-1b5d9e26a3c7");

    /// <summary>
    /// The platform formal-sitting contract: an item served inside an
    /// <c>AssessmentAdministration</c>, under conditions the server fixed before the paper was
    /// handed out. Seeded for the same reason the lesson contract is — the sitting endpoints would
    /// otherwise record nothing, silently.
    /// </summary>
    public static readonly Guid PlatformAssessmentAdministration =
        Guid.Parse("d3a9e6cb-418a-4f5d-9e57-2c6e0f37b4d9");

    /// <summary>Version 1 of the above. Published at seed time; never edited.</summary>
    public static readonly Guid PlatformAssessmentAdministrationV1 =
        Guid.Parse("e4bafd1c-529b-4a6e-8f68-3d7f1a48c5ea");
}
