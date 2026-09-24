using Share7.Domain.Organizations;

namespace Share7.Application.Organizations.Interfaces;

/// <summary>
/// The one authorization rule, in one place.
///
/// <para><b>§9.3, entire:</b> a viewer may see a learner's educational data only through an
/// explicit stored relationship, and the relationship determines the scope. Every org-side,
/// guardian-side and teacher-side read in the platform goes through this method and applies the
/// scope it returns.</para>
///
/// <para><b>Deliberately not ReBAC, not a policy DSL, not an authorization service.</b> This is a
/// join and a scope filter. The architecture notes in advance the one case that looks like it needs
/// a new mechanism and does not: "a teacher covering another teacher's class" is a second
/// <c>CohortMembership</c> row. If a requirement ever genuinely cannot be expressed as a
/// relationship row, that is the signal to revisit — and not before.</para>
///
/// <para><b>It returns a scope rather than a boolean</b> because "may see" is not the question. An
/// org that owns one of a learner's three enrolments may see a third of their evidence, and a
/// caller handed <c>true</c> has no way to honour that.</para>
/// </summary>
public interface IEducationalAccessService
{
    /// <summary>
    /// What this viewer may see about this learner, and why.
    ///
    /// <para>Returns <see cref="EducationalAccessScope.Denied"/> rather than throwing: being
    /// unrelated to a child is the normal case, not an error, and a caller that has to catch an
    /// exception to render an empty state will eventually forget to.</para>
    /// </summary>
    Task<EducationalAccessScope> ResolveAsync(
        Guid viewerUserId,
        Guid learnerUserId,
        bool viewerIsPlatformAdmin = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The learners one viewer may see at all, through every relationship they hold. What a portal
    /// lists before anybody picks a child.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListVisibleLearnersAsync(
        Guid viewerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this viewer may act on an organization in the named role — the write-side check,
    /// which is a different question from seeing a child and is not derivable from it.
    /// </summary>
    Task<bool> HasOrgRoleAsync(
        Guid viewerUserId, Guid orgId, OrgRole role, CancellationToken cancellationToken = default);
}
