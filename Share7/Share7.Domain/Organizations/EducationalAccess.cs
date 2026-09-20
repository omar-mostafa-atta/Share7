namespace Share7.Domain.Organizations;

/// <summary>
/// Why a viewer may see a learner's educational data. <b>The relationship is the authorization</b>
/// — there is no permission set, no policy document and no role that grants this in the abstract
/// (§9.3).
/// </summary>
public enum AccessBasis
{
    /// <summary>Nobody may see anything. The default, and what an unrecognised viewer gets.</summary>
    None = 0,

    /// <summary>The learner, about themselves.</summary>
    Self = 1,

    /// <summary>A verified, unrevoked <see cref="GuardianLink"/> carrying the right consent scope.</summary>
    Guardian = 2,

    /// <summary>A <c>CohortMembership(role: teacher)</c> over a cohort the learner is in.</summary>
    Teacher = 3,

    /// <summary>A <c>Membership(role: org_admin)</c> over an org that owns one of the learner's enrolments.</summary>
    OrgAdmin = 4,

    /// <summary>A global platform role. <b>Audited, never routine.</b></summary>
    PlatformAdmin = 5
}

/// <summary>
/// How much of a learner's data one viewer may see, and why.
///
/// <para><b>This is the whole authorization model.</b> §9.3 is one sentence — a viewer may see a
/// learner's educational data only through an explicit stored relationship, and the relationship
/// determines the scope — and this record is that sentence's return type. Not ReBAC, not a policy
/// DSL, not a distributed authorization service: a join and a scope filter, computed in one place
/// so that there is exactly one answer to "may this person see this child".</para>
///
/// <para><b>The enrolment list is the B2B2C mechanism.</b> A learner who used Share7 privately
/// before their school adopted it has evidence the school must never retroactively acquire. The
/// rule that delivers it is <i>visibility is scoped by enrolment, not by learner</i>: a teacher's
/// scope carries the enrolment ids their cohort owns, and every evidence read filters on them. A
/// scope with <see cref="AllEnrollments"/> false and an empty list sees nothing, which is the
/// correct answer and not an error (§9.2).</para>
/// </summary>
public sealed record EducationalAccessScope
{
    /// <summary>Nobody, for no reason. Returned rather than throwing: "may not" is an answer.</summary>
    public static readonly EducationalAccessScope Denied = new()
    {
        Basis = AccessBasis.None
    };

    public AccessBasis Basis { get; init; }

    public Guid LearnerId { get; init; }

    /// <summary>
    /// True when the viewer sees the learner's whole educational history — the learner themselves,
    /// a consented guardian, or a platform admin. False for every org-side viewer, however senior.
    /// </summary>
    public bool AllEnrollments { get; init; }

    /// <summary>
    /// The enrolments this viewer may see evidence through. Empty and meaningful when
    /// <see cref="AllEnrollments"/> is false: an org that owns none of a learner's enrolments sees
    /// none of their work, including work done on the org's own premises under a personal account.
    /// </summary>
    public IReadOnlyList<Guid> EnrollmentIds { get; init; } = [];

    /// <summary>The orgs the access came through. For the audit line, not for filtering.</summary>
    public IReadOnlyList<Guid> OrgIds { get; init; } = [];

    /// <summary>
    /// Whether raw per-response detail may be read. <b>False for every viewer but the learner</b>
    /// and a platform admin: a teacher seeing a child's error patterns is pedagogy, a teacher
    /// seeing that the child answered at 11pm is surveillance, and the distinction is made here
    /// rather than left to a report to remember (§9.5, §18.2).
    /// </summary>
    public bool MayReadRawEvidence { get; init; }

    /// <summary>Whether anything at all may be read.</summary>
    public bool IsPermitted => Basis != AccessBasis.None;

    /// <summary>Whether an aggregate over this scope would be empty whatever the learner has done.</summary>
    public bool IsEmpty => !AllEnrollments && EnrollmentIds.Count == 0;
}
