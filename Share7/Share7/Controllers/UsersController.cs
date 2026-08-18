using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Application.Equipment.Interfaces;
using Share7.Application.Equipment.Models;
using Share7.Application.Users.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ILanguageService _languageService;
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAccountDeletionService _accountDeletionService;
    private readonly IEquipmentService _equipmentService;

    public UsersController(
        ILanguageService languageService,
        IAuthService authService,
        ICurrentUserService currentUserService,
        IAccountDeletionService accountDeletionService,
        IEquipmentService equipmentService)
    {
        _languageService = languageService;
        _authService = authService;
        _currentUserService = currentUserService;
        _accountDeletionService = accountDeletionService;
        _equipmentService = equipmentService;
    }

    /// <summary>
    /// Permanently deletes the caller's own account. **Immediate and irreversible** — there is no
    /// grace period, no pending state, and nothing to cancel.
    /// <para>
    /// Removes the account and everything it owns: profile, progress, unlocks, currency balances
    /// and ledger, and every refresh token, so no new session can be obtained. Responds `204`.
    /// </para>
    /// <para>
    /// Idempotent — calling it again with a token for the deleted account also returns `204`
    /// rather than failing, so a client retrying after a dropped response is not shown an error
    /// for work that already succeeded.
    /// </para>
    /// <para>
    /// Note the residual window: access tokens are stateless and are not consulted against the
    /// database, so one already issued keeps passing signature validation until it expires (30
    /// minutes by default). It can no longer be exchanged for a new session, and any endpoint
    /// reading account data finds nothing.
    /// </para>
    /// </summary>
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteOwnAccount(CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _accountDeletionService.DeleteOwnAccountAsync(userId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
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

    /// <summary>
    /// The caller's saved avatar outfit. **Always `200`, never `404`** — a player who has never
    /// saved gets defaults rather than an error.
    /// <para>
    /// <c>updatedAtUtc</c> is <c>null</c> exactly when nothing has ever been saved, and non-null on
    /// every stored outfit. That is the only thing distinguishing "never dressed" — where the
    /// client should upload whatever the device is wearing — from "deliberately wearing nothing",
    /// where it should undress the avatar. Both arrive as empty <c>equipped</c> and <c>colors</c>
    /// arrays, so nothing else in the body can tell them apart.
    /// </para>
    /// </summary>
    [HttpGet("me/equipment")]
    public async Task<IActionResult> GetEquipment(CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        return Ok(await _equipmentService.GetAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Replaces the caller's outfit and echoes back what was stored — same body in, same body out,
    /// with a server-stamped <c>updatedAtUtc</c>.
    /// <para>
    /// The user is taken from the token, never from the body. A save replaces the whole outfit, so
    /// an omitted or empty array means "wearing nothing", not "leave it alone".
    /// </para>
    /// <para>
    /// Answers <c>422</c> when the payload breaks a limit: more than 32 equipped entries or 256
    /// colours, a key over 64 characters or outside <c>A-Z a-z 0-9 . _ -</c>, or the same
    /// <c>slotKey</c> twice. Cosmetic keys are **not** checked against a catalogue — there isn't
    /// one, and unknown keys are stored as sent so content can ship ahead of a backend deploy.
    /// </para>
    /// </summary>
    [HttpPut("me/equipment")]
    public async Task<IActionResult> UpdateEquipment(
        UpdateEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _equipmentService.ReplaceAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
