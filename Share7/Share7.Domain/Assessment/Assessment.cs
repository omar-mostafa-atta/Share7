using Share7.Domain.Content;
using Share7.Domain.Evidence;

namespace Share7.Domain.Assessment;

/// <summary>
/// The stable identity of a test — "the Grade 6 mid-term maths test" — across every revision of
/// what is in it. Mutable metadata, immutable id: the thing a teacher names and a report groups by.
/// </summary>
public class Assessment
{
    public Guid Id { get; set; }

    public string AssessmentKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>What it is supposed to measure. Null for an ad-hoc test nobody blueprinted.</summary>
    public Guid? BlueprintId { get; set; }
    public AssessmentBlueprint? Blueprint { get; set; }

    /// <summary>Who owns it — an organization, a teacher, or the platform when null.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>The curriculum node it belongs to, when it belongs to one. Provenance, not structure.</summary>
    public Guid? NodeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<AssessmentForm> Forms { get; set; } = new List<AssessmentForm>();
}

/// <summary>
/// One concrete realisation of an assessment: an ordered, **immutable** list of item versions.
/// <para>
/// Immutability buys exactly one thing and it is worth the whole cost: "reproduce the test this
/// learner sat in March" is an ordered list of ids, and since item versions are themselves
/// immutable the replay is exact forever. That is a hard requirement for anything school-facing and
/// becomes a legal one the first time a grade is disputed (§4.1).
/// </para>
/// </summary>
public class AssessmentForm
{
    public Guid Id { get; set; }

    public Guid AssessmentId { get; set; }
    public Assessment? Assessment { get; set; }

    /// <summary>1-based within the assessment. A second form of the same test, not a second version of this one.</summary>
    public int FormNumber { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The blueprint this form was built from, when it was generated rather than hand-assembled.
    /// Lets a report say whether the paper actually met the spec it claims to.
    /// </summary>
    public Guid? BlueprintId { get; set; }
    public AssessmentBlueprint? Blueprint { get; set; }

    public int? TimeLimitMs { get; set; }

    /// <summary>
    /// Sealed the moment the first learner sits it. **After this the item list may not change** —
    /// enforced in the service rather than the schema, because a form under construction is a
    /// legitimate state and the database cannot tell the two apart.
    /// </summary>
    public DateTime? SealedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<AssessmentFormItem> Items { get; set; } = new List<AssessmentFormItem>();
}

/// <summary>One position in a form. The ordered list that makes exact replay possible.</summary>
public class AssessmentFormItem
{
    public Guid Id { get; set; }

    public Guid FormId { get; set; }
    public AssessmentForm? Form { get; set; }

    /// <summary>1-based position in the paper.</summary>
    public int Position { get; set; }

    /// <summary>
    /// **The version, not the item.** An item rewritten after this form was sealed is a different
    /// question, and a replay that silently served the new wording would not be a replay.
    /// </summary>
    public Guid ItemVersionId { get; set; }
    public ItemVersion? ItemVersion { get; set; }

    /// <summary>What this position is worth. Usually one; a multi-part question is not.</summary>
    public decimal Points { get; set; } = 1.0m;

    /// <summary>The blueprint line this position was drawn for, when it was generated from one.</summary>
    public Guid? BlueprintLineId { get; set; }
}

/// <summary>
/// One learner, one form, one sitting, one set of conditions, one purpose.
/// <para>
/// **The purpose lives here and not on the assessment**, because the same paper is a diagnostic in
/// September and a summative in June, and the difference is a claim about the sitting rather than
/// about the questions (§4.2). The conditions live here too, and they are what decides whether the
/// responses collected under this administration can ever reach
/// <see cref="EvidenceStrength.Assessment"/>.
/// </para>
/// </summary>
public class AssessmentAdministration
{
    public Guid Id { get; set; }

    public Guid FormId { get; set; }
    public AssessmentForm? Form { get; set; }

    public Guid LearnerId { get; set; }

    public AssessmentPurpose Purpose { get; set; }
    public AdministrationState State { get; set; }

    /// <summary>The org this sitting happened under, for scoping a school's reporting. Null for B2C.</summary>
    public Guid? OrgId { get; set; }

    /// <summary>Who administered it, when a person did. An invigilator is what makes a sitting supervised.</summary>
    public Guid? AdministeredByUserId { get; set; }

    // ----------------------------------------------------------------- declared conditions

    public EvidenceDeliveryMode DeliveryMode { get; set; } = EvidenceDeliveryMode.Solo;

    /// <summary>Could the learner re-answer a question within this sitting?</summary>
    public bool RetryPermitted { get; set; }

    /// <summary>External help available. Open book, a teacher in the room, a partner who knew.</summary>
    public bool WasAided { get; set; }

    public int? TimeLimitMs { get; set; }

    /// <summary>The language the paper was served in. One administration, one language.</summary>
    public Guid LangId { get; set; }

    // ------------------------------------------------------------------------- timing

    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// When the server will stop accepting answers. **Computed server-side from the time limit**,
    /// because a client that decides its own deadline has decided its own conditions, and the
    /// conditions are the entire basis of the claim.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Points earned over points available, computed server-side at completion. Null while in
    /// progress. **This is a score on a paper, not a proficiency** — it says what happened at one
    /// sitting and makes no claim that generalizes, which is why it lives here and not in the
    /// measurement layer.
    /// </summary>
    public decimal? PointsEarned { get; set; }
    public decimal? PointsAvailable { get; set; }

    /// <summary>The idempotency key of the request that started it, so a retried start is recognisable.</summary>
    public string? IdempotencyKey { get; set; }
}
