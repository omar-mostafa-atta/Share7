using Microsoft.AspNetCore.Identity;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Users;

namespace Share7.Infrastructure.Identity;

public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public UserAdminService(UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<ServiceResult> DeleteUserAsync(
        Guid userId,
        Guid? actingUserId,
        bool actorIsSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        // Deleting the account you are authenticated as leaves the caller holding tokens for
        // a user that no longer exists — almost always a mistake, never what was intended.
        if (actingUserId is not null && actingUserId == userId)
            return ServiceResult.Forbidden("You cannot delete your own account.");

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return ServiceResult.NotFound("User not found.");

        var roles = await _userManager.GetRolesAsync(user);
        var targetIsPrivileged = roles.Contains(Roles.Admin) || roles.Contains(Roles.SuperAdmin);

        if (targetIsPrivileged && !actorIsSuperAdmin)
            return ServiceResult.Forbidden("Only a Super Admin can delete an Admin or Super Admin account.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Same sweep the user's own delete runs. Kept in UserOwnedData rather than written out
        // here so the admin path and the self-service path cannot drift — one of them gaining a
        // table the other forgets is precisely the bug this guards against.
        await UserOwnedData.PurgeAsync(_dbContext, userId, cancellationToken);

        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ServiceResult.Invalid(deleteResult.Errors.Select(e => e.Description).ToArray());
        }

        await transaction.CommitAsync(cancellationToken);
        return ServiceResult.Success();
    }
}
