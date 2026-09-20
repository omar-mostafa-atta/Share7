using Share7.Domain.Structure;

namespace Share7.Domain.Organizations;

/// <summary>
/// A class, a group, a tutoring batch — the unit a teacher actually teaches.
///
/// <para><b>It is the missing relation that made assignments impossible.</b>
/// <c>PlayContextKind.Assignment</c> has existed since the play-context work and was refused at the
/// boundary in <c>PlaySelectionResolver</c> with, in the code's own words, "there is no class
/// relation to resolve one against". This is that relation (§17.4).</para>
///
/// <para><b>Archived, never deleted.</b> Every observation collected under this cohort's enrolments
/// was collected in its context, and a report about last term has to name it.</para>
/// </summary>
public class Cohort
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }
    public Organization? Org { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The school year or term this runs in, as the organization writes it — "2026/2027 Term 1".
    /// A string rather than a date range because academic periods are named institutionally and
    /// two customers rarely agree on where one ends.
    /// </summary>
    public string AcademicPeriod { get; set; } = string.Empty;

    /// <summary>
    /// What this cohort studies. Null for a cohort that is a group of people rather than a course —
    /// a tutoring batch that covers several curricula, say.
    /// </summary>
    public Guid? CurriculumVersionId { get; set; }
    public CurriculumVersion? CurriculumVersion { get; set; }

    /// <summary>
    /// Where in that curriculum this cohort sits — the grade node. What makes
    /// "the cohort's blueprint" resolvable without asking each learner.
    /// </summary>
    public Guid? PlacementNodeId { get; set; }

    /// <summary>The overlay this cohort reads the curriculum through, when the org has authored one.</summary>
    public Guid? OverlayId { get; set; }

    public CohortStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<CohortMembership> Memberships { get; set; } = new List<CohortMembership>();
}

/// <summary>
/// One person's place in one cohort, with the dates it applied.
///
/// <para><b><c>LeftAtUtc</c>, not deletion.</b> A report about last term must still name who was in
/// it — including the child who moved schools in November, whose evidence from September is real
/// and belongs to the cohort that collected it (§9.1).</para>
/// </summary>
public class CohortMembership
{
    public Guid Id { get; set; }

    public Guid CohortId { get; set; }
    public Cohort? Cohort { get; set; }

    public Guid UserId { get; set; }

    public CohortRole Role { get; set; }

    public DateTime JoinedAtUtc { get; set; }

    public DateTime? LeftAtUtc { get; set; }

    /// <summary>
    /// The enrolment this membership provisioned, when the org created one. <b>The link that makes
    /// §9.2 work</b>: an org sees evidence through enrolments it owns, and this is where the
    /// ownership was established.
    /// </summary>
    public Guid? EnrollmentId { get; set; }

    public bool IsActive => LeftAtUtc is null;
}

/// <summary>
/// Work a teacher set a cohort, with a deadline and a declared condition set.
///
/// <para><b>An assignment is not a new kind of evidence.</b> It is a cohort, a piece of content and
/// a set of conditions — and its evidence strength follows those conditions like everything else. A
/// supervised in-class assignment is <c>Assessment</c>-class; homework is not, <b>because you
/// cannot know who did it</b>. That rule is not configurable here: <see cref="IsSupervised"/> is
/// what a teacher asserts, and the evidence contract decides what it is worth (§17.4).</para>
///
/// <para>Either a node or a form, never both: "do this lesson" and "sit this paper" are different
/// instructions, and an assignment that carried both would leave the client choosing.</para>
/// </summary>
public class Assignment
{
    public Guid Id { get; set; }

    public Guid CohortId { get; set; }
    public Cohort? Cohort { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>A lesson to work through. Null when this assignment is a form.</summary>
    public Guid? NodeId { get; set; }

    /// <summary>A sealed assessment form to sit. Null when this assignment is a lesson.</summary>
    public Guid? AssessmentFormId { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    public DateTime? DueAtUtc { get; set; }

    /// <summary>
    /// Whether the teacher will be watching. The single input that decides whether this work can
    /// support an exam-grade claim; unsupervised work is honest, useful practice and nothing more.
    /// </summary>
    public bool IsSupervised { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime? WithdrawnAtUtc { get; set; }

    public bool IsOpen => WithdrawnAtUtc is null;
}
