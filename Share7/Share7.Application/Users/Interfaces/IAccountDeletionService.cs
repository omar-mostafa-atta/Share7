using Share7.Application.Common.Models;

namespace Share7.Application.Users.Interfaces;

public interface IAccountDeletionService
{
    /// <summary>
    /// Permanently deletes the caller's own account and everything it owns.
    /// <para>
    /// Idempotent: succeeds when the account is already gone, so a client retrying after a
    /// dropped response does not get an error for work that already completed.
    /// </para>
    /// </summary>
    Task<ServiceResult> DeleteOwnAccountAsync(Guid userId, CancellationToken cancellationToken = default);
}
