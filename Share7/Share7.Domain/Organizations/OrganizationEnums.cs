namespace Share7.Domain.Organizations;

/// <summary>
/// What an organization is. One recursive entity covers district → school → department because the
/// shapes differ only in what sits above and below them — a separate table per level would make
/// "every cohort under this district" a different query at every depth
/// (<c>Docs/EducationalArchitecture.md</c> §9.1).
/// </summary>
public enum OrganizationKind
{
    /// <summary>A group of schools under one administration.</summary>
    District = 0,

    /// <summary>The common case, and the one every report is designed around.</summary>
    School = 1,

    /// <summary>A faculty or subject department inside a school.</summary>
    Department = 2,

    /// <summary>A "senter" — private tuition, usually several unconnected cohorts per teacher.</summary>
    TutoringCentre = 3,

    /// <summary>Content-side rather than learner-side: owns a bank, never a roster.</summary>
    Publisher = 4
}

/// <summary>
/// Whether an organization may still act. <b>Never deleted</b> — a closed school's cohorts are the
/// context every historical observation was collected in, and a report about last term has to be
/// able to name it.
/// </summary>
public enum OrganizationStatus
{
    Active = 0,

    /// <summary>Reads still resolve; writes and new enrolments are refused. Usually non-payment.</summary>
    Suspended = 1,

    /// <summary>Gone as an operating entity. History stays readable to platform admins only.</summary>
    Closed = 2
}

/// <summary>
/// What a person may do inside one organization.
/// <para>
/// <b>This replaces the flat global role for everything org-scoped.</b>
/// <c>Share7.Domain.Constants.Roles</c> keeps its four values for platform administration only: a
/// global "Teacher" claim says nothing about <i>whose</i> children a person may look at, and that
/// is the only question that matters here (§9.1).
/// </para>
/// </summary>
public enum OrgRole
{
    /// <summary>Runs the organization: membership, cohorts, configuration, aggregate reporting.</summary>
    OrgAdmin = 0,

    /// <summary>Teaches. Sees learners through cohort membership and through nothing else.</summary>
    Teacher = 1,

    /// <summary>Administrative staff. Rosters and logistics, no learner evidence.</summary>
    Staff = 2,

    /// <summary>A learner the organization has enrolled.</summary>
    Learner = 3
}

/// <summary>Whether a membership is currently in force.</summary>
public enum MembershipStatus
{
    /// <summary>Granted and effective.</summary>
    Active = 0,

    /// <summary>Offered and not yet accepted — the B2B2C case, where the learner activates.</summary>
    Invited = 1,

    /// <summary>Ended. Kept, because a report about last term must still name who taught it.</summary>
    Revoked = 2
}

/// <summary>What a person is to a cohort.</summary>
public enum CohortRole
{
    Learner = 0,
    Teacher = 1,

    /// <summary>
    /// A second teacher, a cover teacher, a TA. Deliberately the same mechanism as
    /// <see cref="Teacher"/> rather than a new one — "teacher covering another teacher's class" is
    /// a second membership row, not a new authorization concept (§9.3).
    /// </summary>
    Assistant = 2
}

/// <summary>Whether a cohort is still running.</summary>
public enum CohortStatus
{
    Active = 0,

    /// <summary>The year ended. Rosters and reports stay readable; nothing new is collected.</summary>
    Archived = 1
}

/// <summary>Who a guardian is to a learner. Recorded because consent is a legal relationship.</summary>
public enum GuardianRelationship
{
    Parent = 0,
    Guardian = 1,
    Relative = 2,

    /// <summary>A tutor or staff member acting in loco parentis. Narrower consent in practice.</summary>
    Carer = 3
}

/// <summary>
/// What a guardian link permits, as flags rather than a boolean.
/// <para>
/// <b>A boolean set at signup was the thing this exists to avoid.</b> "The parent consented" is not
/// a fact that survives a new feature: consent to see a mastery report is not consent to let the
/// child's examination result be used for calibration, and conflating them is how platforms end up
/// having consented to things nobody agreed to (§18.3).
/// </para>
/// </summary>
[Flags]
public enum GuardianConsentScope
{
    None = 0,

    /// <summary>See mastery verdicts, coverage and next steps. What a parent portal reads.</summary>
    ViewProgress = 1 << 0,

    /// <summary>See sittings and their results.</summary>
    ViewAssessments = 1 << 1,

    /// <summary>
    /// Allow the learner's reported examination outcome to join the calibration sample. The one
    /// scope a minor cannot grant for themselves — <c>ExamOutcomeService.SelfConsentAge</c>.
    /// </summary>
    CalibrationUse = 1 << 2,

    /// <summary>Act on the learner's behalf: enrolments, curriculum choice, org invitations.</summary>
    ManageEnrollment = 1 << 3
}

/// <summary>
/// One edit an overlay makes to a curriculum it does not own.
/// <para>
/// A closed set on purpose. An overlay is a diff resolved at read time, and every kind here can be
/// applied by walking the official tree once — which is what keeps "a teacher customised the
/// national curriculum" from meaning "there are now two national curricula" (§7.4, §17.2).
/// </para>
/// </summary>
public enum OverlayEditKind
{
    /// <summary>Remove a node from what this cohort sees. The official version is untouched.</summary>
    Hide = 0,

    /// <summary>Change a node's position among its siblings.</summary>
    Reorder = 1,

    /// <summary>Add a node the official curriculum does not have, owned by the overlay.</summary>
    Insert = 2,

    /// <summary>Show a different node in an official one's place — usually the org's own material.</summary>
    Substitute = 3,

    /// <summary>Leave the tree alone and move when it is taught. Pacing, not content.</summary>
    Repace = 4
}

/// <summary>Publication state of an overlay. The same two-state lifecycle as everything versioned here.</summary>
public enum OverlayStatus
{
    Draft = 0,
    Published = 1,
    Withdrawn = 2
}
