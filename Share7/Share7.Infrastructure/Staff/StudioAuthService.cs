using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Staff;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Staff;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// Studio sign-in. Only an <see cref="StaffStatus.Active"/> account with a Studio profile and the
/// ContentTeam role gets in; everything else fails the same way, so the form never confirms which
/// usernames exist. See ContentStudioPhase1.md §3.
/// </summary>
public class StudioAuthService : IStudioAuthService
{
    private static readonly IReadOnlyList<string> MemberRoles = [Roles.ContentTeam];

    /// <summary>How long an accepted 2-step code stays unusable, comfortably past its own validity window.</summary>
    private static readonly TimeSpan CodeReplayWindow = TimeSpan.FromMinutes(3);

    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly StudioSessions _sessions;
    private readonly StudioTokenIssuer _tokens;
    private readonly StaffScopeReader _scopes;
    private readonly IStudioSessionValidator _validator;
    private readonly IAuditLog _audit;

    public StudioAuthService(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        StudioSessions sessions,
        StudioTokenIssuer tokens,
        StaffScopeReader scopes,
        IStudioSessionValidator validator,
        IAuditLog audit)
    {
        _users = users;
        _db = db;
        _sessions = sessions;
        _tokens = tokens;
        _scopes = scopes;
        _validator = validator;
        _audit = audit;
    }

    // ------------------------------------------------------------------ password

    public async Task<ServiceResult<StudioSignInOutcome>> SignInAsync(
        StudioSignInRequest request,
        StudioClientInfo client,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
            return Fail<StudioSignInOutcome>(StudioErrors.SignInFailed);

        var user = await _users.FindByNameAsync(request.Username.Trim());
        if (user is null)
            return Fail<StudioSignInOutcome>(StudioErrors.SignInFailed);

        var profile = await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        // Not a Studio account — a student, an admin, or a content-team account made before Team &
        // Access that no SuperAdmin has set up yet. Not recorded: there is no member to attach it to.
        if (profile is null || !await _users.IsInRoleAsync(user, Roles.ContentTeam))
            return Fail<StudioSignInOutcome>(StudioErrors.SignInFailed);

        // Too many wrong attempts. Said before the password is checked, as Identity always does —
        // a locked account is not a guessing target for the next fifteen minutes either way.
        if (profile.Status == StaffStatus.Active && await _users.IsLockedOutAsync(user))
        {
            _sessions.Record(user.Id, StaffSignInOutcome.LockedOut, client);
            await _db.SaveChangesAsync(cancellationToken);
            return LockedOut<StudioSignInOutcome>(user);
        }

        var passwordOk = user.PasswordHash is not null && await _users.CheckPasswordAsync(user, request.Password);

        if (!passwordOk)
        {
            _sessions.Record(user.Id, StaffSignInOutcome.WrongPassword, client);

            if (profile.Status == StaffStatus.Active)
            {
                await _users.AccessFailedAsync(user);

                if (await _users.IsLockedOutAsync(user))
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return LockedOut<StudioSignInOutcome>(user);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            return Fail<StudioSignInOutcome>(StudioErrors.SignInFailed);
        }

        // The password was right, so saying why the door is shut tells the holder nothing they
        // could not have learned from the SuperAdmin who shut it.
        switch (profile.Status)
        {
            case StaffStatus.Suspended:
                _sessions.Record(user.Id, StaffSignInOutcome.Suspended, client);
                await _db.SaveChangesAsync(cancellationToken);
                return Fail<StudioSignInOutcome>(StudioErrors.Suspended, ServiceErrorKind.Forbidden);

            case StaffStatus.Deactivated:
                _sessions.Record(user.Id, StaffSignInOutcome.Deactivated, client);
                await _db.SaveChangesAsync(cancellationToken);
                return Fail<StudioSignInOutcome>(StudioErrors.Deactivated, ServiceErrorKind.Forbidden);

            case StaffStatus.Invited:
                _sessions.Record(user.Id, StaffSignInOutcome.NotPermitted, client);
                await _db.SaveChangesAsync(cancellationToken);
                return Fail<StudioSignInOutcome>(StudioErrors.SignInFailed);
        }

        await _users.ResetAccessFailedCountAsync(user);

        if (user.TwoFactorEnabled)
            return Challenge(user);

        _sessions.Record(user.Id, StaffSignInOutcome.Succeeded, client);
        var session = await _sessions.OpenAsync(user, profile, twoStepVerified: false, client, cancellationToken);

        return ServiceResult<StudioSignInOutcome>.Success(new StudioSignInOutcome(session, null, null));
    }

    // ------------------------------------------------------------------ 2-step

    public async Task<ServiceResult<StudioSignInOutcome>> CompleteTwoStepAsync(
        StudioTwoStepRequest request,
        StudioClientInfo client,
        CancellationToken cancellationToken = default)
    {
        if (_tokens.ReadTwoStepChallenge(request.Challenge) is not { } challenge)
            return Fail<StudioSignInOutcome>(StudioErrors.ChallengeExpired);

        var user = await _users.FindByIdAsync(challenge.UserId.ToString());
        var profile = user is null
            ? null
            : await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (user is null || profile is null || profile.Status != StaffStatus.Active)
            return Fail<StudioSignInOutcome>(StudioErrors.SignInFailed);

        // The stamp moved since the password was checked — suspended, reset, or 2-step changed in
        // the meantime. The challenge describes an account that no longer exists in that form.
        if (!StaffSecrets.SameHash(challenge.StampHash, StaffSecrets.StampHash(user.SecurityStamp)) || !user.TwoFactorEnabled)
            return Fail<StudioSignInOutcome>(StudioErrors.ChallengeExpired);

        if (await _users.IsLockedOutAsync(user))
            return LockedOut<StudioSignInOutcome>(user);

        var now = DateTime.UtcNow;
        var usedRecoveryCode = false;
        var accepted = false;
        var code = Digits(request.Code);

        if (code.Length == 6)
        {
            var codeHash = StaffSecrets.Hash($"{user.Id:N}:{code}");
            var replayed = profile.LastTwoStepCodeHash == codeHash && profile.LastTwoStepAtUtc > now - CodeReplayWindow;

            if (!replayed &&
                await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, code))
            {
                accepted = true;
                profile.LastTwoStepCodeHash = codeHash;
                profile.LastTwoStepAtUtc = now;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            accepted = (await _users.RedeemTwoFactorRecoveryCodeAsync(user, NormaliseRecoveryCode(request.RecoveryCode))).Succeeded;
            usedRecoveryCode = accepted;
        }

        if (!accepted)
        {
            _sessions.Record(user.Id, StaffSignInOutcome.TwoStepFailed, client);
            await _users.AccessFailedAsync(user);
            await _db.SaveChangesAsync(cancellationToken);

            return await _users.IsLockedOutAsync(user)
                ? LockedOut<StudioSignInOutcome>(user)
                : Fail<StudioSignInOutcome>(StudioErrors.TwoStepInvalid);
        }

        await _users.ResetAccessFailedCountAsync(user);

        if (usedRecoveryCode)
        {
            var left = await _users.CountRecoveryCodesAsync(user);
            _audit.Record(new AuditEntry(
                AuditActions.StudioRecoveryCodeUsed, AuditAreas.Studio,
                "Signed in to the Studio with a recovery code.",
                "staff", user.Id.ToString(),
                new { recoveryCodesLeft = left },
                ActingUserId: user.Id, ActingRoles: MemberRoles));
        }

        _sessions.Record(user.Id, usedRecoveryCode ? StaffSignInOutcome.SucceededWithRecoveryCode : StaffSignInOutcome.Succeeded, client);
        var session = await _sessions.OpenAsync(user, profile, twoStepVerified: true, client, cancellationToken);

        return ServiceResult<StudioSignInOutcome>.Success(new StudioSignInOutcome(session, null, null));
    }

    // ------------------------------------------------------------------ setup links

    public async Task<ServiceResult<StudioSetupLinkInfoDto>> InspectSetupLinkAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var (link, reason) = await FindLinkAsync(token, cancellationToken);
        if (link is null)
            return LinkInvalid<StudioSetupLinkInfoDto>(reason);

        var user = await _users.FindByIdAsync(link.UserId.ToString());
        var profile = await _db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == link.UserId, cancellationToken);

        if (user is null || profile is null || profile.Status is StaffStatus.Suspended or StaffStatus.Deactivated)
            return LinkInvalid<StudioSetupLinkInfoDto>("unknown");

        var settings = await _sessions.SettingsAsync(cancellationToken);

        return ServiceResult<StudioSetupLinkInfoDto>.Success(new StudioSetupLinkInfoDto(
            profile.FullName,
            user.UserName!,
            link.Purpose,
            link.ExpiresAtUtc,
            profile.StudioRole,
            await _scopes.ReadAsync(user.Id, cancellationToken),
            profile.InterfaceLanguage,
            StaffPasswordPolicy.Rules(settings.MinimumPasswordLength),
            settings.RequireTwoStep,
            user.TwoFactorEnabled));
    }

    public async Task<ServiceResult<StudioSignInOutcome>> CompleteSetupAsync(
        StudioSetupCompleteRequest request,
        StudioClientInfo client,
        CancellationToken cancellationToken = default)
    {
        var (link, reason) = await FindLinkAsync(request.Token, cancellationToken);
        if (link is null)
            return LinkInvalid<StudioSignInOutcome>(reason);

        var user = await _users.FindByIdAsync(link.UserId.ToString());
        var profile = await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == link.UserId, cancellationToken);

        if (user is null || profile is null || profile.Status is StaffStatus.Suspended or StaffStatus.Deactivated)
            return LinkInvalid<StudioSignInOutcome>("unknown");

        var settings = await _sessions.SettingsAsync(cancellationToken);
        var problems = StaffPasswordPolicy.Problems(request.Password, user.UserName!, settings.MinimumPasswordLength);
        if (problems.Count > 0)
            return PasswordRejected<StudioSignInOutcome>(problems);

        var now = DateTime.UtcNow;
        var firstActivation = profile.Status == StaffStatus.Invited;

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await _users.HasPasswordAsync(user))
                await _users.RemovePasswordAsync(user);

            var added = await _users.AddPasswordAsync(user, request.Password);
            if (!added.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return PasswordRejected<StudioSignInOutcome>(IdentityProblems(added));
            }

            await _users.SetLockoutEnabledAsync(user, true);
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);

            link.UsedAtUtc = now;

            if (firstActivation)
            {
                profile.Status = StaffStatus.Active;
                profile.ActivatedAtUtc = now;
                profile.StatusChangedAtUtc = now;
                profile.StatusChangedByUserId = user.Id;
            }

            if (request.InterfaceLanguage is { } language && StaffInterfaceLanguages.All.Contains(language))
                profile.InterfaceLanguage = language;

            profile.UpdatedAtUtc = now;

            _audit.Record(new AuditEntry(
                firstActivation ? AuditActions.StudioAccountActivated : AuditActions.StudioPasswordSet,
                AuditAreas.Studio,
                firstActivation
                    ? "Activated their Studio account with a setup link."
                    : "Chose a new password with a setup link after their access was reset.",
                "staff", user.Id.ToString(),
                new { setupLinkId = link.Id, purpose = link.Purpose.ToString() },
                ActingUserId: user.Id, ActingRoles: MemberRoles));

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        _validator.Forget(user.Id);

        // A reset that kept 2-step on still asks for the code: the link proves who was handed it,
        // not who holds the phone.
        if (user.TwoFactorEnabled)
            return Challenge(user);

        _sessions.Record(user.Id, StaffSignInOutcome.Activated, client);
        var session = await _sessions.OpenAsync(user, profile, twoStepVerified: false, client, cancellationToken);

        return ServiceResult<StudioSignInOutcome>.Success(new StudioSignInOutcome(session, null, null));
    }

    // ------------------------------------------------------------------ session

    public async Task<ServiceResult<StudioSessionTokens>> RefreshAsync(
        string refreshToken,
        StudioClientInfo client,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200)
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);

        var now = DateTime.UtcNow;
        var hash = StaffSecrets.Hash(refreshToken);

        var session = await _db.StaffSessions.AsNoTracking().FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, cancellationToken);

        if (session is null)
            return await RefreshWithOldTokenAsync(hash, now, cancellationToken);

        if (session.RevokedAtUtc is not null)
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);

        var settings = await _sessions.SettingsAsync(cancellationToken);

        if (now >= session.ExpiresAtUtc || now >= session.LastSeenAtUtc.AddHours(settings.IdleTimeoutHours))
        {
            await _sessions.EndOneAsync(session.UserId, session.Id, StaffSessionEndReasons.IdleTimeout, null, cancellationToken);
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);
        }

        var user = await _users.FindByIdAsync(session.UserId.ToString());
        var profile = await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == session.UserId, cancellationToken);

        // A lockout from wrong passwords does not end a working session — it guards new sign-ins,
        // and letting five bad guesses throw the real member out would be a gift to whoever guessed.
        // Suspension and deactivation end it through the status.
        if (user is null || profile is null || profile.Status != StaffStatus.Active)
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);

        var stampHash = StaffSecrets.StampHash(user.SecurityStamp);
        if (!StaffSecrets.SameHash(session.StampHash, stampHash))
        {
            await _sessions.EndOneAsync(session.UserId, session.Id, StaffSessionEndReasons.SecurityChanged, null, cancellationToken);
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);
        }

        var next = StaffSecrets.NewToken();
        var nextHash = StaffSecrets.Hash(next);

        // Conditional on the token still being current: of two refreshes racing on one token,
        // exactly one rotates it and the other is told to retry with the cookie the first set.
        var rotated = await _db.StaffSessions
            .Where(s => s.Id == session.Id && s.RefreshTokenHash == hash && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RefreshTokenHash, nextHash)
                    .SetProperty(s => s.PreviousRefreshTokenHash, hash)
                    .SetProperty(s => s.RotatedAtUtc, now)
                    .SetProperty(s => s.LastSeenAtUtc, now),
                cancellationToken);

        if (rotated == 0)
            return Fail<StudioSessionTokens>(StudioErrors.SessionSuperseded, ServiceErrorKind.Conflict);

        profile.LastActiveAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        _validator.Forget(user.Id);

        var (accessToken, accessExpires) = _tokens.AccessToken(user.Id, user.UserName!, session.Id, stampHash, now);
        return ServiceResult<StudioSessionTokens>.Success(
            new StudioSessionTokens(session.Id, accessToken, accessExpires, next, session.ExpiresAtUtc));
    }

    /// <summary>
    /// A refresh token that is no longer current. Moments after its rotation it is a second tab
    /// that lost the race — harmless, retry. Any later, it was copied: end the session, which
    /// throws out whoever holds the copy (and the owner, who signs in again).
    /// </summary>
    private async Task<ServiceResult<StudioSessionTokens>> RefreshWithOldTokenAsync(
        string hash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rotatedAway = await _db.StaffSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PreviousRefreshTokenHash == hash && s.RevokedAtUtc == null, cancellationToken);

        if (rotatedAway is null)
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);

        if (rotatedAway.RotatedAtUtc is { } rotatedAt && now - rotatedAt < StaffSecrets.RefreshReuseGrace)
            return Fail<StudioSessionTokens>(StudioErrors.SessionSuperseded, ServiceErrorKind.Conflict);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _sessions.EndOneAsync(rotatedAway.UserId, rotatedAway.Id, StaffSessionEndReasons.RefreshTokenReused, null, cancellationToken);

        _audit.Record(new AuditEntry(
            AuditActions.StudioRefreshTokenReused, AuditAreas.Security,
            "An old Studio sign-in token was presented again, so that session was ended.",
            "staff-session", rotatedAway.Id.ToString(),
            new { userId = rotatedAway.UserId },
            ActingUserId: rotatedAway.UserId, ActingRoles: MemberRoles));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid);
    }

    public async Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200)
            return;

        var hash = StaffSecrets.Hash(refreshToken);
        var session = await _db.StaffSessions.AsNoTracking()
            .Where(s => (s.RefreshTokenHash == hash || s.PreviousRefreshTokenHash == hash) && s.RevokedAtUtc == null)
            .Select(s => new { s.Id, s.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (session is not null)
            await _sessions.EndOneAsync(session.UserId, session.Id, StaffSessionEndReasons.SignedOut, session.UserId, cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    private ServiceResult<StudioSignInOutcome> Challenge(ApplicationUser user)
    {
        var (challenge, expires) = _tokens.TwoStepChallenge(user.Id, StaffSecrets.StampHash(user.SecurityStamp), DateTime.UtcNow);
        return ServiceResult<StudioSignInOutcome>.Success(new StudioSignInOutcome(null, challenge, expires));
    }

    private async Task<(StaffSetupToken? Link, string Reason)> FindLinkAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
            return (null, "unknown");

        var hash = StaffSecrets.Hash(token.Trim());
        var link = await _db.StaffSetupTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        return link switch
        {
            null => (null, "unknown"),
            { UsedAtUtc: not null } => (null, "used"),
            { RevokedAtUtc: not null } => (null, "replaced"),
            _ when DateTime.UtcNow >= link.ExpiresAtUtc => (null, "expired"),
            _ => (link, string.Empty)
        };
    }

    internal static string Digits(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// Identity issues recovery codes as <c>ABCDE-FGHJK</c> and compares them exactly. People type
    /// them in lower case, with spaces, or without the dash; all of those mean the same code.
    /// </summary>
    internal static string NormaliseRecoveryCode(string value)
    {
        var bare = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return bare.Length == 10 ? $"{bare[..5]}-{bare[5..]}" : bare;
    }

    internal static ServiceResult<T> Fail<T>(
        ApiErrorCode code,
        ServiceErrorKind kind = ServiceErrorKind.Validation,
        IReadOnlyDictionary<string, object?>? details = null) =>
        ServiceResult<T>.Failure(code, kind, code.Code, details);

    private static ServiceResult<T> LockedOut<T>(ApplicationUser user) =>
        Fail<T>(StudioErrors.LockedOut, ServiceErrorKind.Forbidden,
            new Dictionary<string, object?> { ["retryAfterUtc"] = user.LockoutEnd?.UtcDateTime });

    private static ServiceResult<T> LinkInvalid<T>(string reason) =>
        Fail<T>(StudioErrors.SetupLinkInvalid, ServiceErrorKind.NotFound,
            new Dictionary<string, object?> { ["reason"] = reason });

    internal static ServiceResult<T> PasswordRejected<T>(IReadOnlyList<string> problems) =>
        Fail<T>(StudioErrors.PasswordRejected, ServiceErrorKind.Validation,
            new Dictionary<string, object?> { ["problems"] = problems });

    /// <summary>Identity's own refusal, in the Studio's problem codes — reached only if its rules and ours ever drift.</summary>
    internal static IReadOnlyList<string> IdentityProblems(IdentityResult result) =>
        result.Errors.Select(e => e.Code switch
            {
                nameof(IdentityErrorDescriber.PasswordTooShort) => "tooShort",
                nameof(IdentityErrorDescriber.PasswordRequiresUpper) => "needsUppercase",
                nameof(IdentityErrorDescriber.PasswordRequiresLower) => "needsLowercase",
                nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "needsDigit",
                _ => "rejected"
            })
            .Distinct()
            .ToList();
}
