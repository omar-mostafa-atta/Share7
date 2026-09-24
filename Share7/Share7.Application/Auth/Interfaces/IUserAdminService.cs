using Share7.Application.Common.Models;
using Share7.Application.Users.Models;

namespace Share7.Application.Auth.Interfaces;

public interface IUserAdminService
{
    /// <summary>
    /// A filtered, paged roster of accounts.
    /// </summary>
    /// <remarks>
    /// Added for the admin console, which previously had no way to see who exists — the
    /// only user-facing admin operation was delete-by-id, which assumes you already know
    /// the id. Paged rather than complete: this table grows with the audience, and an
    /// endpoint that returns all of it is one that stops working exactly when the platform
    /// starts succeeding.
    /// </remarks>
    Task<AdminUserPageDto> ListAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One account's profile, roles and activity counters.
    /// </summary>
    Task<ServiceResult<AdminUserDetailDto>> GetDetailAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Balances plus the tail of the currency ledger.
    /// </summary>
    /// <remarks>
    /// Read-only. Crediting another user's wallet is deliberately not exposed — see the note on
    /// <c>CurrenciesController.Grant</c>, which keeps that route scoped to the caller.
    /// </remarks>
    Task<ServiceResult<AdminUserWalletDto>> GetWalletAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Level, streak and objective progress — the admin-scoped twin of
    /// <c>GET /api/progression/me</c>.
    /// </summary>
    Task<ServiceResult<AdminUserProgressionDto>> GetProgressionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the account owns, and how it came to own each one.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<AdminUserEntitlementDto>>> GetEntitlementsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent runs, newest first.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<AdminUserRunDto>>> GetRunsAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The roles the caller may give a new account, in the order the console should offer them.
    /// <para>
    /// The console reads this instead of keeping its own list, so the rule — including
    /// "only a Super Admin can create a privileged account" — lives in one place, and a role added
    /// here appears in the console without a client change.
    /// </para>
    /// </summary>
    IReadOnlyList<string> GetAssignableRoles(bool actorIsSuperAdmin);

    /// <summary>
    /// Creates a sign-in-ready account with a username, a password and exactly one role.
    /// <para>
    /// Refused when the username is taken (409), the role is unknown or cannot be assigned at all
    /// (400), the role is privileged and the caller is not a SuperAdmin (403), or the password
    /// fails Identity's rules (400, with Identity's own sentences).
    /// </para>
    /// </summary>
    Task<ServiceResult<AdminUserListItemDto>> CreateUserAsync(
        CreateAdminUserRequest request,
        bool actorIsSuperAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a user along with their refresh tokens and student profile.
    /// <para>
    /// Refused when <paramref name="userId"/> is the caller themselves, or when the target
    /// holds Admin/SuperAdmin and the caller is not a SuperAdmin.
    /// </para>
    /// </summary>
    Task<ServiceResult> DeleteUserAsync(
        Guid userId,
        Guid? actingUserId,
        bool actorIsSuperAdmin,
        CancellationToken cancellationToken = default);
}
