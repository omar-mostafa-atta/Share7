using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Interfaces;
using Share7.Application.Users.Models;
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
    /// <summary>
    /// A filtered, paged roster of accounts.
    /// </summary>
    /// <remarks>
    /// Added for the admin console. Until this existed the only user-facing admin
    /// operation was delete-by-id, which presumes you already know the id — so the
    /// console had no way to answer "who is on this platform".
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] Guid? gradeId,
        [FromQuery] string? role,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _userAdminService.ListAsync(
            new AdminUserQuery
            {
                Search = search,
                GradeId = gradeId,
                Role = role,
                Page = page,
                PageSize = pageSize
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// One account's profile, roles and activity counters.
    /// </summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userAdminService.GetDetailAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Balances and the tail of the currency ledger.
    /// </summary>
    /// <remarks>
    /// Read-only by design. There is no admin route that credits another user's wallet —
    /// <c>POST /api/currencies/grant</c> is restricted to the caller's own account on purpose,
    /// and this endpoint does not reopen that.
    /// </remarks>
    [HttpGet("{userId:guid}/wallet")]
    public async Task<IActionResult> GetWallet(
        Guid userId,
        CancellationToken cancellationToken,
        int take = 50)
    {
        var result = await _userAdminService.GetWalletAsync(userId, take, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Level, streak and objective progress — the admin-scoped twin of <c>GET /api/progression/me</c>.
    /// </summary>
    [HttpGet("{userId:guid}/progression")]
    public async Task<IActionResult> GetProgression(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userAdminService.GetProgressionAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// What the account owns, and how it came to own each item.
    /// </summary>
    [HttpGet("{userId:guid}/entitlements")]
    public async Task<IActionResult> GetEntitlements(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _userAdminService.GetEntitlementsAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(new { entitlements = result.Value }) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Recent runs, newest first.
    /// </summary>
    [HttpGet("{userId:guid}/runs")]
    public async Task<IActionResult> GetRuns(
        Guid userId,
        CancellationToken cancellationToken,
        int take = 50)
    {
        var result = await _userAdminService.GetRunsAsync(userId, take, cancellationToken);
        return result.Succeeded ? Ok(new { runs = result.Value }) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The roles the caller may give a new account: <c>Student</c>, <c>ContentTeam</c> and
    /// <c>Admin</c>, plus <c>SuperAdmin</c> when the caller is a SuperAdmin. A content-team account is
    /// created through <c>POST /api/admin/team</c>, which gives it a Studio profile and a setup link.
    /// </summary>
    [HttpGet("assignable-roles")]
    public IActionResult GetAssignableRoles() =>
        Ok(new { roles = _userAdminService.GetAssignableRoles(User.IsInRole(Roles.SuperAdmin)) });

    /// <summary>
    /// Creates an account with a username, a password and one role. This is how staff accounts —
    /// the content team in particular — come to exist, since registration only grants Student.
    /// </summary>
    /// <remarks>
    /// 409 when the username is taken; 403 when a non-SuperAdmin asks for SuperAdmin, or anyone asks
    /// for ContentTeam here; 400 for an unknown or unassignable role, or a password Identity rejects.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(CreateAdminUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _userAdminService.CreateUserAsync(
            request,
            User.IsInRole(Roles.SuperAdmin),
            cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { userId = result.Value!.UserId }, result.Value)
            : result.ToErrorResult();
    }

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
