using Share7.Application.Organizations.Models;
using Share7.Domain.Organizations;

namespace Share7.Application.Organizations.Interfaces;

/// <summary>
/// Cohorts, their rosters and their assignments.
///
/// <para><b>Adding a learner to a cohort is the act that creates visibility.</b> It provisions an
/// enrolment owned by the organization, and org-side reads filter on that ownership — so a school
/// sees the work done under its own enrolment and acquires nothing retroactively from the learner's
/// private one (§9.2). There is no separate "share my data" step, and no way to grant an org
/// visibility without an enrolment existing to hang it on.</para>
/// </summary>
public interface ICohortService
{
    Task<IReadOnlyList<CohortDto>> ListAsync(
        Guid orgId, Guid langId, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<CohortDto?> GetAsync(Guid cohortId, Guid langId, CancellationToken cancellationToken = default);

    Task<CohortDto> CreateAsync(
        CreateCohortRequest request, Guid langId, Guid actingUserId,
        CancellationToken cancellationToken = default);

    Task<CohortDto> SetStatusAsync(
        Guid cohortId, CohortStatus status, Guid langId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CohortMemberDto>> ListMembersAsync(
        Guid cohortId, bool includeLeft = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds somebody to the roster.
    ///
    /// <para>For a learner this also creates an <c>Enrollment</c> with
    /// <c>OwnerOrgId</c> set to the cohort's organization, placed at the cohort's curriculum
    /// version and node. <b>It is never marked primary</b>: a learner who already follows a
    /// curriculum of their own keeps that answer to "what are you studying", and a school adding
    /// them to a class does not overwrite it.</para>
    /// </summary>
    Task<CohortMemberDto> AddMemberAsync(
        Guid cohortId, AddCohortMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes somebody off the roster by dating their membership, and ends the enrolment it
    /// provisioned. <b>Neither row is deleted</b>: the evidence collected while they were in the
    /// cohort is real and stays attached to the enrolment that collected it.
    /// </summary>
    Task RemoveMemberAsync(Guid cohortMembershipId, CancellationToken cancellationToken = default);

    /// <summary>Every cohort this user is in, in either direction — learner or teacher.</summary>
    Task<IReadOnlyList<CohortDto>> ListForUserAsync(
        Guid userId, Guid langId, CancellationToken cancellationToken = default);

    // ── assignments ──────────────────────────────────────────────────────────

    Task<IReadOnlyList<AssignmentDto>> ListAssignmentsAsync(
        Guid cohortId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets work for a cohort. Exactly one of node or form, because "do this lesson" and "sit this
    /// paper" are different instructions and an assignment carrying both would leave the client to
    /// choose.
    /// </summary>
    Task<AssignmentDto> CreateAssignmentAsync(
        CreateAssignmentRequest request, Guid langId, Guid actingUserId,
        CancellationToken cancellationToken = default);

    Task WithdrawAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The open assignments waiting for one learner, across every cohort they are in. What a client
    /// asks for when it wants to say "your teacher set you this".
    /// </summary>
    Task<IReadOnlyList<AssignmentDto>> ListForLearnerAsync(
        Guid learnerId, Guid langId, CancellationToken cancellationToken = default);
}
