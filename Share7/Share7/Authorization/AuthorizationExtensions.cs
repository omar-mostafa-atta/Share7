using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Share7.Application.Staff;
using Share7.Domain.Constants;
using Share7.Domain.Staff;
using Share7.Infrastructure.Staff;

namespace Share7.API.Authorization;

/// <summary>
/// The one place that decides which roles may do each kind of work named in <see cref="Policies"/>.
/// <para>
/// The console mirrors this table in <c>Share7.Web/src/lib/access.ts</c> to decide what to show.
/// That copy is a courtesy to the user, never a gate — this one is the gate — but the two should
/// be changed together, or the console will offer a button the API refuses.
/// </para>
/// <para>
/// <b>Studio policies authenticate with the Studio scheme only.</b> A game or admin token is not
/// even read on those endpoints, and a Studio token is not read anywhere else. The Studio role and
/// 2-step standing they test are claims the per-request check adds from the database
/// (<see cref="StaffTokenEvents.ValidateStudioAsync"/>), never claims the token carried.
/// </para>
/// </summary>
public static class AuthorizationExtensions
{
    public static IServiceCollection AddShare7Authorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // The content team came off this list at cutover (plan P6). They cannot reach it in any
            // case — the old sign-in refuses them now — but a grant nothing can exercise is a hole
            // waiting for the day somebody re-opens a door for an unrelated reason.
            .AddPolicy(Policies.ContentAuthoring, policy =>
                policy.RequireRole(Roles.Admin, Roles.SuperAdmin))
            .AddPolicy(Policies.ContentCascadeDelete, policy =>
                policy.RequireRole(Roles.Admin, Roles.SuperAdmin))
            .AddPolicy(Policies.ManageStaff, policy =>
                policy.RequireRole(Roles.SuperAdmin))
            .AddPolicy(Policies.StudioSession, policy => Studio(policy)
                .RequireClaim(StaffSecrets.StudioAccessClaim, StaffSecrets.FullAccess, StaffSecrets.SetupOnlyAccess))
            .AddPolicy(Policies.StudioMember, policy => Studio(policy)
                .RequireClaim(StaffSecrets.StudioAccessClaim, StaffSecrets.FullAccess))
            .AddPolicy(Policies.StudioAuthor, policy => Studio(policy)
                .RequireClaim(StaffSecrets.StudioAccessClaim, StaffSecrets.FullAccess)
                .RequireClaim(StaffSecrets.StudioRoleClaim, nameof(StudioRole.Author), nameof(StudioRole.Reviewer), nameof(StudioRole.Lead)))
            .AddPolicy(Policies.StudioReviewer, policy => Studio(policy)
                .RequireClaim(StaffSecrets.StudioAccessClaim, StaffSecrets.FullAccess)
                .RequireClaim(StaffSecrets.StudioRoleClaim, nameof(StudioRole.Reviewer), nameof(StudioRole.Lead)))
            .AddPolicy(Policies.StudioLead, policy => Studio(policy)
                .RequireClaim(StaffSecrets.StudioAccessClaim, StaffSecrets.FullAccess)
                .RequireClaim(StaffSecrets.StudioRoleClaim, nameof(StudioRole.Lead)));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, StudioAuthorizationResultHandler>();

        return services;
    }

    private static AuthorizationPolicyBuilder Studio(AuthorizationPolicyBuilder policy) =>
        policy.AddAuthenticationSchemes(StaffTokenEvents.StudioScheme).RequireAuthenticatedUser();
}

/// <summary>
/// Gives a Studio refusal a body the Studio can explain: a member who must set up 2-step first is
/// told so (<c>STUDIO_TWO_STEP_REQUIRED</c>) instead of receiving a bare 403. Everything else is
/// handled exactly as before.
/// </summary>
public class StudioAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            context.Request.Path.StartsWithSegments("/api/studio") &&
            context.User.HasClaim(StaffSecrets.StudioAccessClaim, StaffSecrets.SetupOnlyAccess))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = StudioErrors.TwoStepRequired.Code,
                messageKey = StudioErrors.TwoStepRequired.MessageKey,
                details = new Dictionary<string, object?>()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
