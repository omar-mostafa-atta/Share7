using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

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

        // RefreshTokens and StudentProfiles reference UserId without a foreign key, so nothing
        // cascades — they have to be removed explicitly or they outlive the user as orphans.
        // Identity's own tables (roles/claims/logins/tokens) do cascade from AspNetUsers.
        await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.StudentProfiles
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

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
