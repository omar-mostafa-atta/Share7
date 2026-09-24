using Share7.Domain.Staff;

namespace Share7.Application.Staff.Models;

// ============================================================================================
// Shared between Team & Access (the SuperAdmin side) and the Studio (the member's own side).
// ============================================================================================

/// <summary>A node title in both interface languages. <see cref="Ar"/> is null when there is no Arabic title.</summary>
public sealed record LocalizedTitleDto(string En, string? Ar);

/// <summary>
/// One branch of a member's scope. <see cref="Trail"/> runs from the grade down to the node itself,
/// so "Primary 4 › First term › Science" can be drawn in either language.
/// <see cref="Exists"/> is false for a node that has since been removed from the curriculum.
/// </summary>
public sealed record ScopeNodeDto(Guid Id, string Kind, IReadOnlyList<LocalizedTitleDto> Trail, bool Exists);

public sealed record ScopeLanguageDto(Guid Id, string Code, string Name);

/// <summary>What a member may change: parts of the curriculum (or all) × languages (or all).</summary>
public sealed record StaffScopeDto(
    bool AllNodes,
    IReadOnlyList<ScopeNodeDto> Nodes,
    bool AllLanguages,
    IReadOnlyList<ScopeLanguageDto> Languages);

/// <summary>A person named on a record — resolved at read time, never stored on the record itself.</summary>
public sealed record PersonRefDto(Guid UserId, string Name);

/// <summary>The rules a new staff password must meet, so the form can show them before the server refuses.</summary>
public sealed record StaffPasswordRulesDto(int MinimumLength, bool RequireUppercase, bool RequireLowercase, bool RequireDigit);

// ============================================================================================
// Team & Access — /api/admin/team (SuperAdmin only)
// ============================================================================================

public sealed record TeamOverviewDto(
    IReadOnlyList<TeamMemberListItemDto> Members,
    IReadOnlyList<LegacyContentAccountDto> LegacyAccounts,
    TeamCountsDto Counts,
    bool RequireTwoStep,

    /// <summary>False when <c>Studio:PublicUrl</c> is not configured: setup links are then relative paths.</summary>
    bool StudioAddressConfigured);

public sealed record TeamCountsDto(int Active, int Invited, int Suspended, int Deactivated);

public sealed record TeamMemberListItemDto(
    Guid UserId,
    string Username,
    string FullName,
    string? JobTitle,
    StudioRole StudioRole,
    StaffScopeDto Scope,
    StaffStatus Status,
    bool TwoStepEnabled,
    DateTime? LastActiveAtUtc,
    DateTime CreatedAtUtc,
    DateTime? ActivatedAtUtc,

    /// <summary>When the member's unused setup link expires. Null when none is pending.</summary>
    DateTime? SetupLinkExpiresAtUtc);

/// <summary>
/// An account holding the ContentTeam role with no Studio profile — created by an admin from the
/// Users page before Team &amp; Access existed. A SuperAdmin gives it a profile ("set up in the Studio").
/// </summary>
public sealed record LegacyContentAccountDto(Guid UserId, string Username, DateTime CreatedAtUtc);

public sealed record TeamMemberDetailDto(
    Guid UserId,
    string Username,
    string FullName,
    string? JobTitle,
    string? WorkEmail,
    string InterfaceLanguage,
    StudioRole StudioRole,
    StaffScopeDto Scope,
    StaffStatus Status,
    string? StatusReason,
    DateTime? StatusChangedAtUtc,
    PersonRefDto? StatusChangedBy,
    DateTime CreatedAtUtc,
    PersonRefDto? CreatedBy,
    DateTime? ActivatedAtUtc,
    DateTime? LastActiveAtUtc,
    string? Notes,
    TeamMemberSecurityDto Security,
    PendingSetupLinkDto? SetupLink,
    IReadOnlyList<StaffSessionDto> Sessions,
    IReadOnlyList<StaffSignInDto> RecentSignIns,

    /// <summary>Send back with an edit; a stale value means somebody else changed the member first.</summary>
    string RowVersion);

public sealed record TeamMemberSecurityDto(
    bool TwoStepEnabled,
    int RecoveryCodesLeft,
    bool HasPassword,
    DateTime? LockedOutUntilUtc,
    int FailedAttempts);

public sealed record PendingSetupLinkDto(StaffSetupPurpose Purpose, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

public sealed record StaffSessionDto(
    Guid Id,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    DateTime ExpiresAtUtc,
    string? IpAddress,
    string? Device,
    bool TwoStepVerified);

public sealed record StaffSignInDto(DateTime OccurredAtUtc, StaffSignInOutcome Outcome, string? IpAddress, string? Device);

/// <summary>
/// A setup link, shown to the SuperAdmin exactly once. <see cref="IsAbsolute"/> is false when the
/// Studio's public address is not configured — the link is then a path to append to it.
/// </summary>
public sealed record SetupLinkDto(string Url, bool IsAbsolute, DateTime ExpiresAtUtc, StaffSetupPurpose Purpose);

public sealed record CreatedTeamMemberDto(TeamMemberDetailDto Member, SetupLinkDto SetupLink);

public sealed record CreateTeamMemberRequest(
    string FullName,
    string Username,
    string? WorkEmail,
    string? JobTitle,
    StudioRole StudioRole,
    bool AllNodes,
    IReadOnlyList<Guid>? NodeIds,
    bool AllLanguages,
    IReadOnlyList<Guid>? LanguageIds,
    string? InterfaceLanguage);

public sealed record AdoptLegacyAccountRequest(
    string FullName,
    string? WorkEmail,
    string? JobTitle,
    StudioRole StudioRole,
    bool AllNodes,
    IReadOnlyList<Guid>? NodeIds,
    bool AllLanguages,
    IReadOnlyList<Guid>? LanguageIds,
    string? InterfaceLanguage);

public sealed record UpdateTeamMemberProfileRequest(
    string FullName,
    string? JobTitle,
    string? WorkEmail,
    string InterfaceLanguage,
    string RowVersion);

public sealed record UpdateTeamMemberAccessRequest(
    StudioRole StudioRole,
    bool AllNodes,
    IReadOnlyList<Guid>? NodeIds,
    bool AllLanguages,
    IReadOnlyList<Guid>? LanguageIds,
    string RowVersion);

public sealed record UpdateTeamMemberNotesRequest(string? Notes);

public sealed record SuspendTeamMemberRequest(string? Reason);

/// <summary>Deactivation is final, so the SuperAdmin retypes the username to confirm.</summary>
public sealed record DeactivateTeamMemberRequest(string Reason, string ConfirmUsername);

public sealed record ResetTeamMemberAccessRequest(bool ClearTwoStep);

/// <summary>The curriculum and languages a scope can be built from.</summary>
public sealed record TeamScopeOptionsDto(IReadOnlyList<ScopeTreeNodeDto> Nodes, IReadOnlyList<ScopeLanguageDto> Languages);

public sealed record ScopeTreeNodeDto(Guid Id, Guid? ParentId, string Kind, LocalizedTitleDto Title, int Depth, int Order);

public sealed record StaffSecuritySettingsDto(
    bool RequireTwoStep,
    int SessionLifetimeHours,
    int IdleTimeoutHours,
    int MinimumPasswordLength,
    int SetupLinkLifetimeHours,
    DateTime UpdatedAtUtc,
    PersonRefDto? UpdatedBy,

    /// <summary>Active members who have not turned 2-step on — who "require" would send to set it up.</summary>
    int ActiveMembersWithoutTwoStep);

public sealed record UpdateStaffSecuritySettingsRequest(
    bool RequireTwoStep,
    int SessionLifetimeHours,
    int IdleTimeoutHours,
    int MinimumPasswordLength,
    int SetupLinkLifetimeHours);

// ============================================================================================
// Audit log — /api/admin/audit (SuperAdmin only)
// ============================================================================================

public sealed record AuditQuery(
    Guid? ActorUserId = null,
    string? Area = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditEventDto(
    long Sequence,
    DateTime OccurredAtUtc,
    PersonRefDto? Actor,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string Area,
    string? TargetType,
    string? TargetId,
    string Summary,
    string? DataJson,
    string? IpAddress,
    string? Device,
    string? CorrelationId);

public sealed record AuditPageDto(IReadOnlyList<AuditEventDto> Items, int Page, int PageSize, int Total);

public sealed record AuditFacetsDto(
    IReadOnlyList<string> Areas,
    IReadOnlyList<string> Actions,
    IReadOnlyList<PersonRefDto> Actors);

// ============================================================================================
// The Studio — /api/studio (the member's own account)
// ============================================================================================

public sealed record StudioClientInfo(string? IpAddress, string? UserAgent);

public sealed record StudioSignInRequest(string Username, string Password);

public sealed record StudioTwoStepRequest(string Challenge, string? Code, string? RecoveryCode);

public sealed record StudioSetupTokenRequest(string Token);

public sealed record StudioSetupCompleteRequest(string Token, string Password, string? InterfaceLanguage);

/// <summary>What the setup page shows before the member chooses a password.</summary>
public sealed record StudioSetupLinkInfoDto(
    string FullName,
    string Username,
    StaffSetupPurpose Purpose,
    DateTime ExpiresAtUtc,
    StudioRole StudioRole,
    StaffScopeDto Scope,
    string InterfaceLanguage,
    StaffPasswordRulesDto PasswordRules,
    bool TwoStepRequired,
    bool TwoStepEnabled);

/// <summary>A signed-in Studio session. The refresh token never goes in a response body — it is a cookie.</summary>
public sealed record StudioSessionTokens(
    Guid SessionId,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime SessionExpiresAtUtc);

/// <summary>Either a session, or a 2-step challenge to answer first.</summary>
public sealed record StudioSignInOutcome(
    StudioSessionTokens? Session,
    string? TwoStepChallenge,
    DateTime? TwoStepChallengeExpiresAtUtc);

public sealed record StudioMeDto(
    Guid UserId,
    string Username,
    string FullName,
    string? JobTitle,
    StudioRole StudioRole,
    StaffScopeDto Scope,
    string InterfaceLanguage,
    StudioTwoStepStatusDto TwoStep,
    StaffPasswordRulesDto PasswordRules,
    DateTime? ActivatedAtUtc,
    StudioCurrentSessionDto Session);

public sealed record StudioTwoStepStatusDto(
    bool Enabled,
    bool Required,
    int RecoveryCodesLeft,

    /// <summary>True when 2-step is required and not yet on: the Studio shows only the setup screen.</summary>
    bool SetupRequired);

public sealed record StudioCurrentSessionDto(Guid Id, DateTime ExpiresAtUtc, bool TwoStepVerified);

public sealed record StudioSessionListItemDto(
    Guid Id,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    DateTime ExpiresAtUtc,
    string? IpAddress,
    string? Device,
    bool IsCurrent);

public sealed record StudioInterfaceLanguageRequest(string Language);

public sealed record StudioChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record StudioPasswordConfirmationRequest(string Password);

public sealed record StudioTwoStepCodeRequest(string Code);

/// <summary>The key to type into an authenticator app, and the <c>otpauth://</c> address a QR code encodes.</summary>
public sealed record StudioTwoStepSetupDto(string SharedKey, string AuthenticatorUri);

/// <summary>Shown once. Each code signs in once, in place of the authenticator app.</summary>
public sealed record StudioRecoveryCodesDto(IReadOnlyList<string> Codes);

/// <summary>
/// 2-step is on. Turning it on moves the account's security stamp, which ends every other device;
/// <see cref="Session"/> is this device, re-keyed so it carries on.
/// </summary>
public sealed record StudioTwoStepEnabledDto(StudioRecoveryCodesDto RecoveryCodes, StudioSessionTokens Session);

/// <summary>
/// What the per-request check knows about a valid Studio session — read from the database (and
/// cached for at most a minute), never from the token.
/// </summary>
public sealed record StudioSessionState(
    Guid UserId,
    Guid SessionId,
    StudioRole StudioRole,

    /// <summary>False while 2-step is required and not yet set up: only the account endpoints are open.</summary>
    bool FullAccess);
