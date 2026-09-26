using Share7.Application.Common.Models;
using Share7.Application.Staff.Models;

namespace Share7.Application.Staff.Interfaces;

/// <summary>
/// Team &amp; Access: the SuperAdmin's management of content-team accounts. The only way a Studio
/// account comes into existence. Every change is written to the audit trail in the same transaction,
/// naming the SuperAdmin through <c>IAuditActor</c>.
/// </summary>
public interface ITeamAdminService
{
    Task<TeamOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<TeamScopeOptionsDto> GetScopeOptionsAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> GetMemberAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Creates the account (no password), its profile (Invited) and a one-time setup link.</summary>
    Task<ServiceResult<CreatedTeamMemberDto>> CreateMemberAsync(CreateTeamMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gives a pre-Team &amp; Access content-team account a Studio profile. It stays Active with its password.</summary>
    Task<ServiceResult<TeamMemberDetailDto>> AdoptLegacyAccountAsync(Guid userId, AdoptLegacyAccountRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> UpdateProfileAsync(Guid userId, UpdateTeamMemberProfileRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> UpdateAccessAsync(Guid userId, UpdateTeamMemberAccessRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> UpdateNotesAsync(Guid userId, UpdateTeamMemberNotesRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> SuspendAsync(Guid userId, SuspendTeamMemberRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> ReactivateAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> DeactivateAsync(Guid userId, DeactivateTeamMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a new setup link and signs the member out everywhere. For an Active member the
    /// password is cleared (and 2-step too, if asked); for an Invited one it simply replaces the
    /// link they have not used.
    /// </summary>
    /// <summary>Sets the member's password (decided 2026-09-26: admins set passwords, no links), signing them out everywhere.</summary>
    Task<ServiceResult<TeamMemberDetailDto>> SetPasswordAsync(Guid userId, SetTeamMemberPasswordRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<SetupLinkDto>> ResetAccessAsync(Guid userId, ResetTeamMemberAccessRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult> RevokeSetupLinkAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ServiceResult<TeamMemberDetailDto>> SignOutEverywhereAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ServiceResult> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<StaffSecuritySettingsDto> GetSecurityAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<StaffSecuritySettingsDto>> UpdateSecurityAsync(UpdateStaffSecuritySettingsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The audit trail, read back for SuperAdmins.</summary>
public interface IAuditQueryService
{
    Task<AuditPageDto> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);

    Task<AuditFacetsDto> GetFacetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The query's matches as CSV, newest first, up to <paramref name="maxRows"/>. Cells that a
    /// spreadsheet would run as a formula are neutralised.
    /// </summary>
    Task<string> ExportCsvAsync(AuditQuery query, int maxRows, CancellationToken cancellationToken = default);
}

/// <summary>Studio sign-in: password, 2-step, setup links, and the rotating refresh session.</summary>
public interface IStudioAuthService
{
    Task<ServiceResult<StudioSignInOutcome>> SignInAsync(StudioSignInRequest request, StudioClientInfo client, CancellationToken cancellationToken = default);

    Task<ServiceResult<StudioSignInOutcome>> CompleteTwoStepAsync(StudioTwoStepRequest request, StudioClientInfo client, CancellationToken cancellationToken = default);

    Task<ServiceResult<StudioSetupLinkInfoDto>> InspectSetupLinkAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Sets the password from a setup link. Returns a session, or a 2-step challenge when the account already has 2-step on.</summary>
    Task<ServiceResult<StudioSignInOutcome>> CompleteSetupAsync(StudioSetupCompleteRequest request, StudioClientInfo client, CancellationToken cancellationToken = default);

    Task<ServiceResult<StudioSessionTokens>> RefreshAsync(string refreshToken, StudioClientInfo client, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session the refresh cookie belongs to. Works with an expired access token, so signing
    /// out never fails just because the page sat open too long.
    /// </summary>
    Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>The signed-in member's own account: profile, password, 2-step, devices.</summary>
public interface IStudioAccountService
{
    Task<ServiceResult<StudioMeDto>> GetMeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult> SetInterfaceLanguageAsync(Guid userId, string language, CancellationToken cancellationToken = default);

    /// <summary>Changes the password and signs every <i>other</i> device out.</summary>
    Task<ServiceResult<StudioSessionTokens>> ChangePasswordAsync(Guid userId, Guid sessionId, StudioChangePasswordRequest request, StudioClientInfo client, CancellationToken cancellationToken = default);

    Task<ServiceResult<StudioTwoStepSetupDto>> BeginTwoStepAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Turns 2-step on once the member proves the app works, and returns their recovery codes.</summary>
    Task<ServiceResult<StudioTwoStepEnabledDto>> ConfirmTwoStepAsync(Guid userId, Guid sessionId, string code, CancellationToken cancellationToken = default);

    /// <summary>Turns 2-step off (refused while SuperAdmins require it). Returns this device's re-keyed session.</summary>
    Task<ServiceResult<StudioSessionTokens>> DisableTwoStepAsync(Guid userId, Guid sessionId, string password, CancellationToken cancellationToken = default);

    Task<ServiceResult<StudioRecoveryCodesDto>> RegenerateRecoveryCodesAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioSessionListItemDto>> GetSessionsAsync(Guid userId, Guid currentSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult> RevokeOwnSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The per-request check behind every Studio token: is this session still live, is the account
/// still active, has its security stamp moved. Cached for at most a minute per session, and dropped
/// immediately on this server when something changes — so a suspension or "sign out everywhere"
/// lands within 60 seconds on every server and at once on the one that made it.
/// </summary>
public interface IStudioSessionValidator
{
    /// <summary>The session's state, or null when the token must be refused.</summary>
    Task<StudioSessionState?> ValidateAsync(Guid userId, Guid sessionId, string stampHash, CancellationToken cancellationToken = default);

    /// <summary>Drops every cached answer for this member's sessions.</summary>
    void Forget(Guid userId);
}

/// <summary>
/// The same 60-second check for Admin and SuperAdmin tokens on the main API: the account still
/// exists, is not locked, still holds the role the token claims, and its security stamp has not
/// moved. Closes the gap where a deleted or demoted admin's token kept working until it expired.
/// </summary>
public interface IPrivilegedTokenValidator
{
    Task<bool> IsStillValidAsync(Guid userId, string? stampHash, IReadOnlyCollection<string> privilegedRoles, CancellationToken cancellationToken = default);

    void Forget(Guid userId);
}
