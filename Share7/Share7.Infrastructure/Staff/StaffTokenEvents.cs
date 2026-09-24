using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Share7.Application.Staff.Interfaces;
using Share7.Domain.Constants;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// The per-request checks, hooked into token validation so no controller can forget them.
/// </summary>
public static class StaffTokenEvents
{
    /// <summary>The Studio's own authentication scheme. Only <c>/api/studio</c> names it.</summary>
    public const string StudioScheme = "Studio";

    private static readonly string[] PrivilegedRoles = [Roles.Admin, Roles.SuperAdmin];

    /// <summary>
    /// Main-API tokens: an Admin or SuperAdmin token must still describe a live account that holds
    /// that role and whose security stamp has not moved. Every other token — students, and the
    /// content team on the old sign-in — passes through exactly as before.
    /// </summary>
    public static async Task ValidatePrivilegedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
            return;

        var claimed = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Where(PrivilegedRoles.Contains).Distinct().ToList();
        if (claimed.Count == 0)
            return;

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail("The token names no account.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IPrivilegedTokenValidator>();

        if (!await validator.IsStillValidAsync(userId, principal.FindFirstValue(StaffSecrets.StampClaim), claimed, context.HttpContext.RequestAborted))
            context.Fail("This administrator sign-in is no longer valid. Sign in again.");
    }

    /// <summary>
    /// Studio tokens: the session must be live and the account active, and the member's Studio
    /// role and 2-step standing are attached here — from the database, never from the token.
    /// </summary>
    public static async Task ValidateStudioAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;

        if (principal?.Identity is not ClaimsIdentity identity ||
            !Guid.TryParse(principal.FindFirstValue("sub"), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue(StaffSecrets.SessionClaim), out var sessionId) ||
            principal.FindFirstValue(StaffSecrets.StampClaim) is not { } stampHash)
        {
            context.Fail("Not a Studio token.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IStudioSessionValidator>();
        var state = await validator.ValidateAsync(userId, sessionId, stampHash, context.HttpContext.RequestAborted);

        if (state is null)
        {
            context.Fail("This Studio session has ended.");
            return;
        }

        identity.AddClaim(new Claim(StaffSecrets.StudioRoleClaim, state.StudioRole.ToString()));
        identity.AddClaim(new Claim(StaffSecrets.StudioAccessClaim, state.FullAccess ? StaffSecrets.FullAccess : StaffSecrets.SetupOnlyAccess));
    }
}
