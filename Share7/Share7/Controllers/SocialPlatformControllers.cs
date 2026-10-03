using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Social;
using Share7.Domain.Constants;
using Share7.Domain.Social;

namespace Share7.API.Controllers;

[ApiController, Authorize, Route("api/social")]
public sealed class SocialProfilesController(IGamingProfileService profiles, ISocialSafetyService safety, ICurrentUserService current) : ControllerBase
{
    [HttpGet("profiles/{user:guid}")]
    public Task<IActionResult> Profile(Guid user, CancellationToken token) => Run(id => profiles.ReadAsync(id, user, token));
    [HttpGet("directory")]
    public Task<IActionResult> Directory(long after = 0, CancellationToken token = default) => Run(id => profiles.DirectoryAsync(id, after, token));
    [HttpGet("showcase/content")]
    public Task<IActionResult> Content(CancellationToken token) => Run(id => profiles.ContentAsync(id, token));
    [HttpPut("showcase"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Select(ShowcaseSelectionRequest request, CancellationToken token) => Run(id => profiles.SelectAsync(id, request, token));
    [HttpPut("following/{user:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Follow(Guid user, SwitchRequest request, CancellationToken token) => Run(id => profiles.FollowAsync(id, user, request.Enabled, token));
    [HttpGet("activity")]
    public Task<IActionResult> Activity(long before = 0, CancellationToken token = default) => Run(id => profiles.ActivityAsync(id, before, token));
    [HttpGet("privacy")]
    public async Task<IActionResult> Privacy(CancellationToken token) => current.UserId is { } id ? Ok(await safety.PrivacyAsync(id, token)) : Unauthorized();
    [HttpPut("privacy"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Privacy(PrivacyDto request, CancellationToken token) => Run(id => safety.SetPrivacyAsync(id, request, token));
    [HttpPut("mutes/{user:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Mute(Guid user, SwitchRequest request, CancellationToken token) => Run(id => safety.MuteAsync(id, user, request.Enabled, token));
    [HttpPost("reports"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Report(ReportRequest request, CancellationToken token) => Run(id => safety.ReportAsync(id, request, token));
    [HttpGet("restrictions")]
    public async Task<IActionResult> Restrictions(CancellationToken token) => current.UserId is { } id ? Ok(await safety.RestrictionsAsync(id, token)) : Unauthorized();
    [HttpPost("restrictions/{restriction:guid}/appeal"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Appeal(Guid restriction, ReasonRequest request, CancellationToken token) => Run(id => safety.AppealAsync(id, restriction, request.ReasonCode, token));
    private async Task<IActionResult> Run<T>(Func<Guid, Task<ServiceResult<T>>> call)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await call(id); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
    private async Task<IActionResult> Run(Func<Guid, Task<ServiceResult>> call)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await call(id); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}

public sealed record SwitchRequest(bool Enabled);
public sealed record ReasonRequest(string ReasonCode);

[ApiController, Authorize, Route("api/inbox")]
public sealed class InboxController(IInboxService inbox, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(long before = 0, CancellationToken token = default)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await inbox.ReadAsync(id, before, token); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
    [HttpPut("{eventId:guid}/read"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> MarkRead(Guid eventId, CancellationToken token)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await inbox.MarkReadAsync(id, eventId, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
    [HttpGet("preferences")]
    public async Task<IActionResult> Preferences(CancellationToken token) => current.UserId is { } id ? Ok(await inbox.PreferencesAsync(id, token)) : Unauthorized();
    [HttpPut("preferences/{category}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Preference(string category, SwitchRequest request, CancellationToken token)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await inbox.PreferenceAsync(id, category, request.Enabled, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}

[ApiController, Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin), Route("api/admin/social")]
public sealed class AdminSocialController(ISocialProfileAdminService profiles, ISocialSafetyService safety) : ControllerBase
{
    [HttpPut("identities/{user:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Identity(Guid user, OfficialProfileInput request, CancellationToken token) => Answer(await profiles.SetIdentityAsync(user, request, token));
    [HttpGet("showcase/content")]
    public async Task<IActionResult> Content(CancellationToken token) => Ok(await profiles.ContentAsync(token));
    [HttpPut("showcase/content"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Content(ShowcaseContentDto request, CancellationToken token) => Answer(await profiles.SetContentAsync(request, token));
    [HttpPost("identities/{user:guid}/activity"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Activity(Guid user, OfficialActivityInput request, CancellationToken token) => Answer(await profiles.PublishActivityAsync(user, request, token));
    [HttpGet("reports")]
    public async Task<IActionResult> Reports(long after = 0, ModerationState? state = null, CancellationToken token = default) => Ok(await safety.CasesAsync(after, state, token));
    [HttpGet("appeals")]
    public async Task<IActionResult> Appeals(CancellationToken token) => Ok(await safety.AppealsAsync(token));
    [HttpPost("reports/{report:guid}/decision"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Decide(Guid report, ModerationDecisionRequest request, CancellationToken token)
    {
        if (request.PermanentRestriction && !User.IsInRole(Roles.SuperAdmin)) return Forbid();
        return Answer(await safety.DecideAsync(report, request, token));
    }
    [HttpPost("restrictions/{restriction:guid}/appeal-review"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> ReviewAppeal(Guid restriction, AppealReviewRequest request, CancellationToken token) => Answer(await safety.ReviewAppealAsync(restriction, request.Revoke, request.ReasonCode, token));
    [HttpPost("restrictions/{restriction:guid}/revoke"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Revoke(Guid restriction, ReasonRequest request, CancellationToken token) => Answer(await safety.RevokeAsync(restriction, request.ReasonCode, token));
    private IActionResult Answer(ServiceResult result) => result.Succeeded ? NoContent() : result.ToApiErrorResult();
}
public sealed record AppealReviewRequest(bool Revoke, string ReasonCode);

[ApiController, Authorize, Route("api/orgs/guardian/social-consent")]
public sealed class GuardianSocialConsentController(IGuardianSocialConsentService consent, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => current.UserId is { } id ? Ok(await consent.ListAsync(id, token)) : Unauthorized();
    [HttpPut("{link:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Set(Guid link, SwitchRequest request, CancellationToken token)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await consent.SetAsync(id, link, request.Enabled, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
