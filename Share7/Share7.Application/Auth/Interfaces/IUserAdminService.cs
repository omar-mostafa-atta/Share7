using Share7.Application.Common.Models;

namespace Share7.Application.Auth.Interfaces;

public interface IUserAdminService
{
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
