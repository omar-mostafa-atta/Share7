using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Authorization;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Models;
using Share7.Application.Staff;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Infrastructure.Staff;

namespace Share7.API.Controllers;

/// <summary>
/// What every Studio controller shares: who is calling (from the validated Studio token), where
/// from, and the refresh cookie.
/// <para>
/// <b>The refresh token lives only in an HttpOnly cookie</b> scoped to <c>/api/studio/auth</c>;
/// script on the page can never read it, and the access token it buys lives only in memory. The
/// routes that act on the cookie also demand an <c>X-Studio-Request</c> header, which a page on
/// another origin cannot send without a CORS preflight this API never grants — so a sibling site
/// cannot ride the cookie either.
/// </para>
/// </summary>
public abstract class StudioControllerBase : ControllerBase
{
    public const string RequestHeader = "X-Studio-Request";

    protected Guid CurrentUserId => Guid.Parse(User.FindFirstValue("sub")!);
    protected Guid CurrentSessionId => Guid.Parse(User.FindFirstValue(StaffSecrets.SessionClaim)!);

    protected StudioClientInfo Client => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null);

    protected bool HasStudioHeader => Request.Headers.ContainsKey(RequestHeader);

    protected string? RefreshCookie => Request.Cookies[StaffSecrets.RefreshCookieName];

    protected void SetRefreshCookie(StudioSessionTokens tokens) =>
        Response.Cookies.Append(StaffSecrets.RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = StaffSecrets.RefreshCookiePath,
            Expires = tokens.SessionExpiresAtUtc,
            IsEssential = true
        });

    protected void ClearRefreshCookie() =>
        Response.Cookies.Delete(StaffSecrets.RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = StaffSecrets.RefreshCookiePath
        });

    /// <summary>The body that goes with a new or renewed session. The refresh token is not in it.</summary>
    protected IActionResult Session(StudioSessionTokens tokens)
    {
        SetRefreshCookie(tokens);
        return Ok(SessionBody(tokens));
    }

    protected static object SessionBody(StudioSessionTokens tokens) => new
    {
        status = "signed-in",
        accessToken = tokens.AccessToken,
        accessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
        sessionExpiresAtUtc = tokens.SessionExpiresAtUtc
    };

    protected IActionResult Outcome(StudioSignInOutcome outcome) =>
        outcome.Session is { } session
            ? Session(session)
            : Ok(new
            {
                status = "two-step-required",
                challenge = outcome.TwoStepChallenge,
                challengeExpiresAtUtc = outcome.TwoStepChallengeExpiresAtUtc
            });

    protected IActionResult MissingHeader() =>
        ServiceResult.Failure(StudioErrors.Invalid, ServiceErrorKind.Validation, "Missing X-Studio-Request header.").ToApiErrorResult();
}

/// <summary>
/// Studio sign-in: <c>/api/studio/auth</c>. See ContentStudioPhase1.md §3 for the whole flow.
/// Every response here is <c>no-store</c> — they carry tokens, setup details or both.
/// </summary>
[ApiController]
[Route("api/studio/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StudioAuthController : StudioControllerBase
{
    private readonly IStudioAuthService _auth;
    private readonly IStudioAccountService _account;

    public StudioAuthController(IStudioAuthService auth, IStudioAccountService account)
    {
        _auth = auth;
        _account = account;
    }

    /// <summary>Username and password. Answers with a session, or with a 2-step challenge to complete.</summary>
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> SignIn(StudioSignInRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.SignInAsync(request, Client, cancellationToken);
        return result.Succeeded ? Outcome(result.Value!) : result.ToApiErrorResult();
    }

    /// <summary>The six-digit code from the authenticator app — or one recovery code — for a challenge.</summary>
    [HttpPost("two-step")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> TwoStep(StudioTwoStepRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.CompleteTwoStepAsync(request, Client, cancellationToken);
        return result.Succeeded ? Outcome(result.Value!) : result.ToApiErrorResult();
    }

    /// <summary>What a setup link is for — shown before the member chooses a password. POST so the secret stays out of URLs and logs.</summary>
    [HttpPost("setup/inspect")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> InspectSetup(StudioSetupTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.InspectSetupLinkAsync(request.Token, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Chooses the password through a setup link, and signs in.</summary>
    [HttpPost("setup/complete")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> CompleteSetup(StudioSetupCompleteRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.CompleteSetupAsync(request, Client, cancellationToken);
        return result.Succeeded ? Outcome(result.Value!) : result.ToApiErrorResult();
    }

    /// <summary>A new access token from the refresh cookie, which rotates.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!HasStudioHeader)
            return MissingHeader();

        var result = await _auth.RefreshAsync(RefreshCookie ?? string.Empty, Client, cancellationToken);

        if (result.Succeeded)
            return Session(result.Value!);

        // Superseded means another tab rotated the cookie a moment ago: leave the cookie alone —
        // it is already the new one — and let the client retry.
        if (result.Error != StudioErrors.SessionSuperseded)
            ClearRefreshCookie();

        // 401 rather than 400: the Studio treats it as "sign in again", like an expired token.
        return result.Error == StudioErrors.SessionInvalid
            ? StatusCode(StatusCodes.Status401Unauthorized, new
            {
                code = StudioErrors.SessionInvalid.Code,
                messageKey = StudioErrors.SessionInvalid.MessageKey,
                details = new { }
            })
            : result.ToApiErrorResult();
    }

    /// <summary>Ends this device's session. Works even after the access token has expired.</summary>
    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        if (!HasStudioHeader)
            return MissingHeader();

        await _auth.SignOutAsync(RefreshCookie, cancellationToken);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>Who is signed in: profile, Studio role, scope, 2-step standing and this session.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.StudioSession)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await _account.GetMeAsync(CurrentUserId, CurrentSessionId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
