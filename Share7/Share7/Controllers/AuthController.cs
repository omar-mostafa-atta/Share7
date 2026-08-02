using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(new { errors = result.Errors });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : Unauthorized(new { errors = result.Errors });
    }

    [HttpPost("external-login")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ExternalLoginAsync(request, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(new { errors = result.Errors });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, GetIpAddress(), cancellationToken);
        return result.Succeeded ? Ok(result) : Unauthorized(new { errors = result.Errors });
    }

    [HttpPost("revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var revoked = await _authService.RevokeTokenAsync(request.RefreshToken, GetIpAddress(), cancellationToken);
        return revoked ? NoContent() : NotFound();
    }

    [HttpPost("complete-profile")]
    [Authorize]
    public async Task<IActionResult> CompleteProfile(CompleteProfileRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _authService.CompleteProfileAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(new { errors = result.Errors });
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
