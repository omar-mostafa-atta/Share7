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
using static Share7.Infrastructure.Staff.StudioAuthService;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// A member's own Studio account: what they see about themselves, their password, their 2-step
/// sign-in and their signed-in devices. Everything here acts on the caller only — the user id comes
/// from the validated token, never from the request body.
/// </summary>
public class StudioAccountService : IStudioAccountService
{
    /// <summary>
    /// Identity's own names for where it keeps the authenticator key and recovery codes
    /// (<c>UserStoreBase</c>). Written through the public token API so that starting a 2-step setup
    /// does not move the security stamp — which would sign the member out of the page they are on.
    /// </summary>
    private const string IdentityStoreProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyName = "AuthenticatorKey";
    private const string RecoveryCodesName = "RecoveryCodes";

    private const string AuthenticatorIssuer = "Share7 Studio";
    private const int RecoveryCodeCount = 10;

    private static readonly IReadOnlyList<string> MemberRoles = [Roles.ContentTeam];

    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly StudioSessions _sessions;
    private readonly StaffScopeReader _scopes;
    private readonly IAuditLog _audit;

    public StudioAccountService(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        StudioSessions sessions,
        StaffScopeReader scopes,
        IAuditLog audit)
    {
        _users = users;
        _db = db;
        _sessions = sessions;
        _scopes = scopes;
        _audit = audit;
    }

    public async Task<ServiceResult<StudioMeDto>> GetMeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return Fail<StudioMeDto>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        var session = await _db.StaffSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new StudioCurrentSessionDto(s.Id, s.ExpiresAtUtc, s.TwoStepVerified))
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
            return Fail<StudioMeDto>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        var settings = await _sessions.SettingsAsync(cancellationToken);
        var recoveryLeft = user.TwoFactorEnabled ? await _users.CountRecoveryCodesAsync(user) : 0;

        return ServiceResult<StudioMeDto>.Success(new StudioMeDto(
            user.Id,
            user.UserName!,
            profile.FullName,
            profile.JobTitle,
            profile.StudioRole,
            await _scopes.ReadAsync(user.Id, cancellationToken),
            profile.InterfaceLanguage,
            new StudioTwoStepStatusDto(
                user.TwoFactorEnabled,
                settings.RequireTwoStep,
                recoveryLeft,
                SetupRequired: settings.RequireTwoStep && !user.TwoFactorEnabled),
            StaffPasswordPolicy.Rules(settings.MinimumPasswordLength),
            profile.ActivatedAtUtc,
            session));
    }

    public async Task<ServiceResult> SetInterfaceLanguageAsync(Guid userId, string language, CancellationToken cancellationToken = default)
    {
        if (!StaffInterfaceLanguages.All.Contains(language))
            return Fail<bool>(StudioErrors.Invalid);

        var profile = await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
            return Fail<bool>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        profile.InterfaceLanguage = language;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------------ password

    public async Task<ServiceResult<StudioSessionTokens>> ChangePasswordAsync(
        Guid userId,
        Guid sessionId,
        StudioChangePasswordRequest request,
        StudioClientInfo client,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        if (await CheckPasswordAsync(user, request.CurrentPassword) is { } wrong)
            return Fail<StudioSessionTokens>(wrong.Error!, wrong.ErrorKind, wrong.Details);

        var settings = await _sessions.SettingsAsync(cancellationToken);
        var problems = StaffPasswordPolicy.Problems(request.NewPassword, user.UserName!, settings.MinimumPasswordLength).ToList();
        if (request.NewPassword == request.CurrentPassword)
            problems.Add("sameAsCurrent");

        if (problems.Count > 0)
            return PasswordRejected<StudioSessionTokens>(problems);

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            var changed = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!changed.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return PasswordRejected<StudioSessionTokens>(IdentityProblems(changed));
            }

            var otherDevices = await _sessions.EndAllAsync(userId, StaffSessionEndReasons.PasswordChanged, userId, cancellationToken, except: sessionId);
            await _sessions.EndLegacySignInsAsync(userId, "Password changed in the Studio", cancellationToken);

            _audit.Record(new AuditEntry(
                AuditActions.StudioPasswordChanged, AuditAreas.Studio,
                "Changed their Studio password; every other device was signed out.",
                "staff", userId.ToString(),
                new { otherDevicesSignedOut = otherDevices }));

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await CarryOverAsync(user, sessionId, cancellationToken);
    }

    // ------------------------------------------------------------------ 2-step

    public async Task<ServiceResult<StudioTwoStepSetupDto>> BeginTwoStepAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return Fail<StudioTwoStepSetupDto>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        if (user.TwoFactorEnabled)
            return Fail<StudioTwoStepSetupDto>(StudioErrors.TwoStepAlreadyOn, ServiceErrorKind.Conflict);

        // A fresh key every time setup starts, so an abandoned attempt (a screenshot of the QR code
        // left somewhere) is worthless once the member starts again.
        var key = _users.GenerateNewAuthenticatorKey();
        await _users.SetAuthenticationTokenAsync(user, IdentityStoreProvider, AuthenticatorKeyName, key);

        var label = Uri.EscapeDataString($"{AuthenticatorIssuer}:{user.UserName}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(AuthenticatorIssuer)}&digits=6&period=30";

        return ServiceResult<StudioTwoStepSetupDto>.Success(new StudioTwoStepSetupDto(Group(key), uri));
    }

    public async Task<ServiceResult<StudioTwoStepEnabledDto>> ConfirmTwoStepAsync(
        Guid userId,
        Guid sessionId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return Fail<StudioTwoStepEnabledDto>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        if (user.TwoFactorEnabled)
            return Fail<StudioTwoStepEnabledDto>(StudioErrors.TwoStepAlreadyOn, ServiceErrorKind.Conflict);

        if (string.IsNullOrEmpty(await _users.GetAuthenticatorKeyAsync(user)))
            return Fail<StudioTwoStepEnabledDto>(StudioErrors.TwoStepNotStarted, ServiceErrorKind.Conflict);

        var digits = Digits(code);
        if (digits.Length != 6 ||
            !await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, digits))
        {
            return Fail<StudioTwoStepEnabledDto>(StudioErrors.TwoStepInvalid);
        }

        IEnumerable<string> codes;

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            await _users.SetTwoFactorEnabledAsync(user, true);
            codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];

            profile.LastTwoStepCodeHash = StaffSecrets.Hash($"{user.Id:N}:{digits}");
            profile.LastTwoStepAtUtc = DateTime.UtcNow;
            profile.UpdatedAtUtc = DateTime.UtcNow;

            // Every other device signed in without a second step. Turning it on is the moment to
            // make them prove it.
            var otherDevices = await _sessions.EndAllAsync(userId, StaffSessionEndReasons.TwoStepChanged, userId, cancellationToken, except: sessionId);

            await _db.StaffSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.TwoStepVerified, true), cancellationToken);

            _audit.Record(new AuditEntry(
                AuditActions.StudioTwoStepEnabled, AuditAreas.Studio,
                "Turned on 2-step sign-in; every other device was signed out.",
                "staff", userId.ToString(),
                new { otherDevicesSignedOut = otherDevices, recoveryCodes = RecoveryCodeCount }));

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var session = await CarryOverAsync(user, sessionId, cancellationToken);
        if (!session.Succeeded)
            return Fail<StudioTwoStepEnabledDto>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        return ServiceResult<StudioTwoStepEnabledDto>.Success(
            new StudioTwoStepEnabledDto(new StudioRecoveryCodesDto(codes.ToList()), session.Value!));
    }

    public async Task<ServiceResult<StudioSessionTokens>> DisableTwoStepAsync(
        Guid userId,
        Guid sessionId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return Fail<StudioSessionTokens>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        if ((await _sessions.SettingsAsync(cancellationToken)).RequireTwoStep)
            return Fail<StudioSessionTokens>(StudioErrors.TwoStepCannotDisable, ServiceErrorKind.Forbidden);

        if (!user.TwoFactorEnabled)
            return Fail<StudioSessionTokens>(StudioErrors.TwoStepNotStarted, ServiceErrorKind.Conflict);

        if (await CheckPasswordAsync(user, password) is { } wrong)
            return Fail<StudioSessionTokens>(wrong.Error!, wrong.ErrorKind, wrong.Details);

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            await _users.SetTwoFactorEnabledAsync(user, false);
            await _users.RemoveAuthenticationTokenAsync(user, IdentityStoreProvider, AuthenticatorKeyName);
            await _users.RemoveAuthenticationTokenAsync(user, IdentityStoreProvider, RecoveryCodesName);

            await _sessions.EndAllAsync(userId, StaffSessionEndReasons.TwoStepChanged, userId, cancellationToken, except: sessionId);

            _audit.Record(new AuditEntry(
                AuditActions.StudioTwoStepDisabled, AuditAreas.Studio,
                "Turned off 2-step sign-in.",
                "staff", userId.ToString()));

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await CarryOverAsync(user, sessionId, cancellationToken);
    }

    public async Task<ServiceResult<StudioRecoveryCodesDto>> RegenerateRecoveryCodesAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return Fail<StudioRecoveryCodesDto>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);

        if (!user.TwoFactorEnabled)
            return Fail<StudioRecoveryCodesDto>(StudioErrors.TwoStepNotStarted, ServiceErrorKind.Conflict);

        if (await CheckPasswordAsync(user, password) is { } wrong)
            return Fail<StudioRecoveryCodesDto>(wrong.Error!, wrong.ErrorKind, wrong.Details);

        var codes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? []).ToList();

        _audit.Record(new AuditEntry(
            AuditActions.StudioRecoveryCodesRegenerated, AuditAreas.Studio,
            "Replaced their 2-step recovery codes; the old ones stopped working.",
            "staff", userId.ToString()));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<StudioRecoveryCodesDto>.Success(new StudioRecoveryCodesDto(codes));
    }

    // ------------------------------------------------------------------ devices

    public async Task<IReadOnlyList<StudioSessionListItemDto>> GetSessionsAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await _db.StaffSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Select(s => new StudioSessionListItemDto(
                s.Id, s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.IpAddress, DeviceNames.Describe(s.UserAgent), s.Id == currentSessionId))
            .ToList();
    }

    public async Task<ServiceResult> RevokeOwnSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!await _sessions.EndOneAsync(userId, sessionId, StaffSessionEndReasons.SignedOut, userId, cancellationToken))
            return Fail<bool>(StudioErrors.NotFound, ServiceErrorKind.NotFound);

        _audit.Record(new AuditEntry(
            AuditActions.StudioSessionRevoked, AuditAreas.Studio,
            "Signed one of their devices out of the Studio.",
            "staff-session", sessionId.ToString()));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(ApplicationUser? User, StaffProfile? Profile)> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        var profile = user is null
            ? null
            : await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == userId && p.Status == StaffStatus.Active, cancellationToken);

        return (user, profile);
    }

    /// <summary>
    /// Null when the password is right. A wrong one counts towards the lockout like any other
    /// sign-in attempt, so a session left open on a shared computer cannot be used to guess it.
    /// </summary>
    private async Task<ServiceResult?> CheckPasswordAsync(ApplicationUser user, string? password)
    {
        if (!string.IsNullOrEmpty(password) && await _users.CheckPasswordAsync(user, password))
        {
            await _users.ResetAccessFailedCountAsync(user);
            return null;
        }

        await _users.AccessFailedAsync(user);
        return Fail<bool>(StudioErrors.WrongPassword);
    }

    private async Task<ServiceResult<StudioSessionTokens>> CarryOverAsync(ApplicationUser user, Guid sessionId, CancellationToken cancellationToken)
    {
        // The stamp moved inside the transaction; read it back rather than trust the tracked copy.
        await _db.Entry(user).ReloadAsync(cancellationToken);

        return await _sessions.CarryOverAsync(user, sessionId, cancellationToken) is { } tokens
            ? ServiceResult<StudioSessionTokens>.Success(tokens)
            : Fail<StudioSessionTokens>(StudioErrors.SessionInvalid, ServiceErrorKind.NotFound);
    }

    /// <summary>"JBSWY3DPEHPK3PXP" → "jbsw y3dp ehpk 3pxp": easier to type from one screen into another.</summary>
    private static string Group(string key) =>
        string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(chunk => new string(chunk)));
}
