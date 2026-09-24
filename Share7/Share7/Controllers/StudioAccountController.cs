using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Authorization;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;

namespace Share7.API.Controllers;

/// <summary>
/// The signed-in member's own account: <c>/api/studio/account</c>. Open to a session that still
/// has to set up 2-step, because this is where they do it. Acts on the caller only.
/// </summary>
[ApiController]
[Route("api/studio/account")]
[Authorize(Policy = Policies.StudioSession)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StudioAccountController : StudioControllerBase
{
    private readonly IStudioAccountService _account;

    public StudioAccountController(IStudioAccountService account) => _account = account;

    [HttpPut("interface-language")]
    public async Task<IActionResult> SetInterfaceLanguage(StudioInterfaceLanguageRequest request, CancellationToken cancellationToken)
    {
        var result = await _account.SetInterfaceLanguageAsync(CurrentUserId, request.Language, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>Changes the password. Every other device is signed out; this one carries on with a renewed session.</summary>
    [HttpPost("password")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> ChangePassword(StudioChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _account.ChangePasswordAsync(CurrentUserId, CurrentSessionId, request, Client, cancellationToken);
        return result.Succeeded ? Session(result.Value!) : result.ToApiErrorResult();
    }

    /// <summary>Starts 2-step setup: the key and the address a QR code encodes. Nothing is switched on yet.</summary>
    [HttpPost("two-step/begin")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> BeginTwoStep(CancellationToken cancellationToken)
    {
        var result = await _account.BeginTwoStepAsync(CurrentUserId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Switches 2-step on once a code from the app checks out. Returns the recovery codes, once.</summary>
    [HttpPost("two-step/confirm")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> ConfirmTwoStep(StudioTwoStepCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _account.ConfirmTwoStepAsync(CurrentUserId, CurrentSessionId, request.Code, cancellationToken);
        if (!result.Succeeded)
            return result.ToApiErrorResult();

        SetRefreshCookie(result.Value!.Session);
        return Ok(new
        {
            recoveryCodes = result.Value.RecoveryCodes.Codes,
            session = SessionBody(result.Value.Session)
        });
    }

    [HttpPost("two-step/disable")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> DisableTwoStep(StudioPasswordConfirmationRequest request, CancellationToken cancellationToken)
    {
        var result = await _account.DisableTwoStepAsync(CurrentUserId, CurrentSessionId, request.Password, cancellationToken);
        return result.Succeeded ? Session(result.Value!) : result.ToApiErrorResult();
    }

    /// <summary>New recovery codes; the old ones stop working.</summary>
    [HttpPost("two-step/recovery-codes")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> RegenerateRecoveryCodes(StudioPasswordConfirmationRequest request, CancellationToken cancellationToken)
    {
        var result = await _account.RegenerateRecoveryCodesAsync(CurrentUserId, request.Password, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Every device this member is signed in on.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken cancellationToken) =>
        Ok(await _account.GetSessionsAsync(CurrentUserId, CurrentSessionId, cancellationToken));

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> EndSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _account.RevokeOwnSessionAsync(CurrentUserId, sessionId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
