using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Users.Interfaces;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Users;

public class AccountDeletionService : IAccountDeletionService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public AccountDeletionService(UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<ServiceResult> DeleteOwnAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        // Already gone. The caller is holding a still-valid access token for an account that no
        // longer exists, which is exactly what a retry after a dropped response looks like —
        // report success rather than a 404 for work that already completed.
        if (user is null)
            return ServiceResult.Success();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await UserOwnedData.PurgeAsync(_dbContext, userId, cancellationToken);

        var deleted = await _userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            return ServiceResult.Failure(
                ApiErrors.AccountDeletionRefused,
                ServiceErrorKind.Conflict,
                string.Join(" ", deleted.Errors.Select(e => e.Description)));
        }

        await transaction.CommitAsync(cancellationToken);
        return ServiceResult.Success();
    }
}
