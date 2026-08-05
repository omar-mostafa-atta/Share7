using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Interfaces;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserAdminService _userAdminService;
    private readonly ICurrentUserService _currentUserService;

    public AdminUsersController(IUserAdminService userAdminService, ICurrentUserService currentUserService)
    {
        _userAdminService = userAdminService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Permanently deletes a user, together with their refresh tokens and student profile.
    /// <para>
    /// This is a hard delete and cannot be undone. Refused if you target your own account, or
    /// if the target is an Admin/Super Admin and you are not a Super Admin.
    /// </para>
    /// </summary>
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userAdminService.DeleteUserAsync(
            userId,
            _currentUserService.UserId,
            User.IsInRole(Roles.SuperAdmin),
            cancellationToken);

        return result.Succeeded ? NoContent() : result.ToErrorResult();
    }
}
