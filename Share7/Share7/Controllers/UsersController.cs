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
using Share7.Application.Users.Models;
using Share7.Domain.Constants;

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
    private readonly IUserProfileService _userProfileService;

    public UsersController(
        ILanguageService languageService,
        IAuthService authService,
        ICurrentUserService currentUserService,
        IAccountDeletionService accountDeletionService,
        IEquipmentService equipmentService,
        IUserProfileService userProfileService)
    {
        _languageService = languageService;
        _authService = authService;
        _currentUserService = currentUserService;
        _accountDeletionService = accountDeletionService;
        _equipmentService = equipmentService;
        _userProfileService = userProfileService;
    }

    /// <summary>
    /// Whether the caller may see and edit other people's profiles. Read from the token's roles
    /// rather than passed in, so no request can claim it.
    /// </summary>
    private bool IsAdmin() => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin);

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
    /// A player's profile. Omit <paramref name="userId"/> for your own.
    /// <code>
    /// GET /api/users/profile              ← yourself, contact details included
    /// GET /api/users/profile?userId=…     ← someone else, contact details withheld
    /// </code>
    /// <para>
    /// **`phoneNumber` and `email` come back null for anyone but yourself**, unless you are an
    /// admin. They are a child's contact details and every signed-in account can name any user id —
    /// a session roster hands them out — so a profile read has to assume the id came from a
    /// stranger. `isSelf` distinguishes "not recorded" from "not shown to you".
    /// </para>
    /// <para>
    /// `isProfileComplete: false` means the account registered but never completed the profile step;
    /// every field below `userName` is null in that case. Still `200`, not `404`.
    /// </para>
    /// </summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile([FromQuery] Guid? userId, CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } callerId)
            return Unauthorized();

        var result = await _userProfileService.GetAsync(callerId, userId, IsAdmin(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Edits a profile. Omit <paramref name="userId"/> for your own; naming someone else requires
    /// Admin.
    /// <para>
    /// **Partial — a field you omit is left alone, not cleared.** Unlike
    /// `POST /api/auth/complete-profile`, which requires everything because it creates the row. An
    /// edit screen that had to resend every field would wipe whatever it forgot to load, and the
    /// field most likely to be forgotten is the phone number, which nothing else can recover.
    /// </para>
    /// <para>
    /// Refused with `PROFILE_NOT_FOUND` when there is no profile yet — this edits, it does not
    /// create. `fullName: ""` is refused rather than treated as a clear; omit it instead.
    /// </para>
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        UpdateUserProfileRequest request,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } callerId)
            return Unauthorized();

        var result = await _userProfileService.UpdateAsync(
            callerId, userId, IsAdmin(), request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
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
    /// <para>
    /// Pass <c>?userId=</c> to read **another player's** outfit instead — that is how a client
    /// dresses the opponents in a multiplayer session, whose ids come from the session roster.
    /// Omitting it keeps the original behaviour exactly, so existing clients need no change.
    /// </para>
    /// <para>
    /// Deliberately not restricted: an outfit is what everyone in the match can already see on
    /// screen. It carries no personal data, which is why this differs from
    /// <c>GET /api/users/profile</c>, where contact details are withheld.
    /// </para>
    /// </summary>
    [HttpGet("me/equipment")]
    public async Task<IActionResult> GetEquipment(
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } callerId)
            return Unauthorized();

        return Ok(await _equipmentService.GetAsync(userId ?? callerId, cancellationToken));
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
