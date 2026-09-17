using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.RateLimiting;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Auth.Models;
using Share7.Application.Common.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(IAuthService authService, ICurrentUserService currentUserService)
    {
        _authService = authService;
        _currentUserService = currentUserService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(new { errors = result.Errors });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : Unauthorized(new { errors = result.Errors });
    }

    [HttpPost("external-login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ExternalLogin(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ExternalLoginAsync(request, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(new { errors = result.Errors });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : Unauthorized(new { errors = result.Errors });
    }

    [HttpPost("revoke")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var revoked = await _authService.RevokeTokenAsync(request.RefreshToken, GetIpAddress(), cancellationToken);
        return revoked ? NoContent() : NotFound();
    }

    /// <summary>
    /// Fills in the student's profile and content language.
    /// <para>
    /// Returns a fresh token pair on success. The language chosen here lives in an
    /// access-token claim, so the caller's existing token would keep serving the old
    /// language until it expired — the client must replace its stored tokens with these,
    /// exactly as it does for POST /api/users/me/preferred-language.
    /// </para>
    /// </summary>
    [HttpPost("complete-profile")]
    [Authorize]
    public async Task<IActionResult> CompleteProfile(CompleteProfileRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _authService.CompleteProfileAsync(userId, request, cancellationToken);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        var tokens = await _authService.ReissueTokensAsync(userId, GetIpAddress(), cancellationToken);
        return tokens.Succeeded
            ? Ok(tokens)
            : BadRequest(new { errors = tokens.Errors });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value });
        return Ok(claims);
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
