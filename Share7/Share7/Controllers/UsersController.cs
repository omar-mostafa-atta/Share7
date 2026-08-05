using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ILanguageService _languageService;
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        ILanguageService languageService,
        IAuthService authService,
        ICurrentUserService currentUserService)
    {
        _languageService = languageService;
        _authService = authService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Changes the language the user's content is served in.
    /// <para>
    /// Returns a fresh token pair. The language lives in an access-token claim, so the old
    /// token would keep serving the previous language until it expired — the client must
    /// replace its stored tokens with the ones in this response.
    /// </para>
    /// </summary>
    [HttpPost("me/preferred-language")]
    public async Task<IActionResult> SetPreferredLanguage(
        SetPreferredLanguageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId is null)
            return Unauthorized();

        var updated = await _languageService.SetPreferredLanguageAsync(userId.Value, request.LanguageId, cancellationToken);
        if (!updated)
            return BadRequest(new { errors = new[] { "Unknown language id." } });

        var tokens = await _authService.ReissueTokensAsync(userId.Value, GetIpAddress(), cancellationToken);
        return tokens.Succeeded
            ? Ok(tokens)
            : BadRequest(new { errors = tokens.Errors });
    }

    /// <summary>Current content language for the caller, falling back to English.</summary>
    [HttpGet("me/preferred-language")]
    public async Task<IActionResult> GetPreferredLanguage(CancellationToken cancellationToken)
    {
        var languageId = await _languageService.ResolveCurrentAsync(cancellationToken);
        return Ok(new { languageId });
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
