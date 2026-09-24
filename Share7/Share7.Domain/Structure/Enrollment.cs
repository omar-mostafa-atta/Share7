namespace Share7.Domain.Structure;

/// <summary>How a learner came to be enrolled, which decides how far the placement is trusted.</summary>
public enum EnrollmentSource
{
    /// <summary>The learner picked it themselves at sign-up. What every existing account is.</summary>
    SelfDeclared = 0,

    /// <summary>A verified guardian set it.</summary>
    Guardian = 1,

    /// <summary>A school or tutoring centre set it. The only source a school report may rely on.</summary>
    Organization = 2,

    /// <summary>Created by the backfill from <c>StudentProfile.GradeId</c>.</summary>
    Migrated = 3,

    /// <summary>Derived from a diagnostic administration rather than declared.</summary>
    Placement = 4
}

/// <summary>
/// learner × curriculum version × period. **The replacement for <c>StudentProfile.GradeId</c>.**
/// <para>
/// The single-grade column could not express any of: a learner who advances (and whose history
/// should stay attached to the year it happened in), a learner studying two curricula at once, a
/// learner who switches from the national curriculum to IGCSE mid-year, or a learner whose school
/// places them differently from what they told the app. All four are ordinary, and all four were
/// unrepresentable — §8.3.
/// </para>
/// <para>
/// <c>StudentProfile.GradeId</c> stays readable and writable throughout the overlap. Nothing is
/// forced to migrate on a schedule; the column is deprecated by having a better answer available,
/// not by being removed.
/// </para>
/// </summary>
public class Enrollment
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid CurriculumVersionId { get; set; }
    public CurriculumVersion? CurriculumVersion { get; set; }

    /// <summary>
    /// Where in the tree this enrollment places the learner — a grade node today. Null for a
    /// curriculum a learner is following without a fixed position in it.
    /// </summary>
    public Guid? PlacementNodeId { get; set; }
    public CurriculumNode? PlacementNode { get; set; }

    public EnrollmentSource Source { get; set; }

    /// <summary>
    /// The organization that owns this enrolment, when one does. Null for every enrolment a learner
    /// made for themselves.
    ///
    /// <para><b>This one column is the whole of multi-tenancy here</b> (Docs/EducationalArchitecture.md
    /// §9.2, §9.4). Row-level scoping through the enrolment indirection, on one database — not
    /// database-per-tenant, not schema-per-tenant. What is irreversible is having the column and the
    /// indirection; physical isolation stays available later for the few customers who contract for
    /// it, and is never needed by the many who will not.</para>
    ///
    /// <para><b>It is also what makes the B2B2C case correct rather than merely handled.</b> A
    /// learner who used Share7 privately before their school adopted it keeps that enrolment with a
    /// null owner, and the school's enrolment is a second row. Because visibility is scoped by
    /// enrolment rather than by learner, the school gets the work done under its own enrolment and
    /// acquires nothing retroactively. There is no code that implements that rule; it falls out of
    /// filtering on this column.</para>
    /// </summary>
    public Guid? OwnerOrgId { get; set; }

    /// <summary>
    /// The one enrollment that answers "what is this learner studying" when something has to pick.
    /// Exactly one active primary per learner, enforced by a filtered unique index.
    /// </summary>
    public bool IsPrimary { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// **Ended, never deleted.** A finished year is a fact about the learner's history, and the
    /// evidence collected under it stays attached to the placement it was collected in.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
