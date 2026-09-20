using Share7.Application.Organizations.Models;
using Share7.Domain.Organizations;

namespace Share7.Application.Organizations.Interfaces;

/// <summary>
/// Organizations, their hierarchy and their membership.
///
/// <para><b>Five tables, not a system</b> (§9.1). There is no org-specific code path anywhere
/// downstream of this: an organization is configuration plus a scoping column, and everything in
/// Part 6 of the brief composes from these rows plus <c>Enrollment</c>.</para>
/// </summary>
public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationDto>> ListAsync(
        Guid? parentOrgId = null, CancellationToken cancellationToken = default);

    Task<OrganizationDto?> GetAsync(Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an organization and provisions its own item bank in the same transaction.
    ///
    /// <para><b>The bank is not optional and not deferred.</b> A school that can author content
    /// before it has a bank to author into ends up writing into the platform bank, and the trust
    /// ceiling that stops unreviewed material moving a national examination projection is a
    /// property of the bank (§10.2). Creating them together is what makes that ceiling impossible
    /// to bypass by accident.</para>
    /// </summary>
    Task<OrganizationDto> CreateAsync(
        CreateOrganizationRequest request, Guid actingUserId, CancellationToken cancellationToken = default);

    Task<OrganizationDto> SetStatusAsync(
        Guid orgId, OrganizationStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// This organization and everything beneath it. The query every org-scoped report starts from,
    /// which is why the hierarchy is one recursive table rather than a level per shape.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetSubtreeIdsAsync(
        Guid orgId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipDto>> ListMembersAsync(
        Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a role. Re-granting a revoked membership revives the existing row rather than adding
    /// a second, so "who could see this child's data, and when" stays a single readable history.
    /// </summary>
    Task<MembershipDto> GrantAsync(
        Guid orgId, GrantMembershipRequest request, Guid actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a membership. <b>Revoked, never deleted</b> — a report about last term has to name who
    /// taught it, and an access audit has to resolve against the membership that permitted it.
    /// </summary>
    Task RevokeAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>Every organization this user holds an active membership in.</summary>
    Task<IReadOnlyList<MembershipDto>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
