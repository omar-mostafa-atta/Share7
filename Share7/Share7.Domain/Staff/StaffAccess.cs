namespace Share7.Domain.Staff;

public enum StaffSetupPurpose
{
    /// <summary>First use: the member chooses their password and becomes <see cref="StaffStatus.Active"/>.</summary>
    Activation = 0,

    /// <summary>A SuperAdmin cleared the password ("reset access"); the member chooses a new one.</summary>
    Reset = 1
}

/// <summary>
/// A one-time setup link. There is no email sending, so a SuperAdmin is shown the link once and
/// hands it over; nobody ever learns another person's password.
/// <para>
/// <b>Only a SHA-256 of the secret is stored.</b> Someone who can read this table cannot use a link
/// from it. At most one link per member is live at a time: issuing a new one revokes the old.
/// </para>
/// </summary>
public class StaffSetupToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the secret in the link.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public StaffSetupPurpose Purpose { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsUsableAt(DateTime nowUtc) => UsedAtUtc is null && RevokedAtUtc is null && nowUtc < ExpiresAtUtc;
}

/// <summary>
/// One signed-in Studio device. Every Studio access token names its session (<c>sid</c>), and the
/// server checks the session on each request, so revoking this row signs that device out within
/// the validation cache's window (60 seconds; instantly on the server that revoked it).
/// <para>
/// <b>Refresh tokens rotate.</b> Each refresh replaces <see cref="RefreshTokenHash"/> and keeps the
/// previous hash. The previous token arriving again after the grace window means it was copied —
/// the session is revoked, which signs out both the thief and the owner, and the owner simply signs
/// in again. Inside the grace window it is two tabs refreshing at once, which is harmless.
/// </para>
/// </summary>
public class StaffSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the current refresh token. Never the token itself.</summary>
    public string RefreshTokenHash { get; set; } = string.Empty;

    /// <summary>The hash this session rotated away from last, for reuse detection.</summary>
    public string? PreviousRefreshTokenHash { get; set; }
    public DateTime? RotatedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>Absolute end, fixed at sign-in from the security settings. Refreshing never extends it.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Whether a 2-step code (or recovery code) was entered for this sign-in.</summary>
    public bool TwoStepVerified { get; set; }

    /// <summary>
    /// The account's security-stamp fingerprint this session is valid for. When the stamp moves — a
    /// password or 2-step change, a SuperAdmin's reset or "sign out everywhere" — every session
    /// still holding the old one is over at its next refresh, except the one that made the change,
    /// which is carried across.
    /// </summary>
    public string StampHash { get; set; } = string.Empty;

    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>A <see cref="StaffSessionEndReasons"/> value.</summary>
    public string? RevokedReason { get; set; }

    public Guid? RevokedByUserId { get; set; }
}

/// <summary>Why a session ended — stored text, shown in Team &amp; Access. Add; do not rename.</summary>
public static class StaffSessionEndReasons
{
    public const string SignedOut = "signed-out";
    public const string RevokedByAdmin = "revoked-by-superadmin";
    public const string SignedOutEverywhere = "signed-out-everywhere";
    public const string Suspended = "suspended";
    public const string Deactivated = "deactivated";
    public const string AccessReset = "access-reset";
    public const string PasswordChanged = "password-changed";
    public const string TwoStepChanged = "two-step-changed";
    public const string RefreshTokenReused = "refresh-token-reused";
    public const string IdleTimeout = "idle-timeout";

    /// <summary>The account's security stamp moved under the session (for example a password change on another device).</summary>
    public const string SecurityChanged = "security-changed";
}

public enum StaffSignInOutcome
{
    Succeeded = 0,
    WrongPassword = 1,
    LockedOut = 2,

    /// <summary>The account exists but is not a Studio account, or is Invited.</summary>
    NotPermitted = 3,

    Suspended = 4,
    Deactivated = 5,
    TwoStepFailed = 6,

    /// <summary>A recovery code was used instead of the authenticator app.</summary>
    SucceededWithRecoveryCode = 7,

    /// <summary>First sign-in through a setup link.</summary>
    Activated = 8
}

/// <summary>
/// Studio sign-in history: every attempt against a Studio account, successful or not, with where
/// it came from. Shown on the member's page in Team &amp; Access.
/// <para>
/// Kept apart from <c>AuditEvents</c> on purpose: sign-in attempts are high-volume noise next to
/// the actions the audit trail exists for, and they have a different retention. Attempts against a
/// username that does not exist are not stored — there is no account to attach them to, and
/// recording typed usernames would collect whatever people mistype, passwords included.
/// </para>
/// </summary>
public class StaffSignInEvent
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public StaffSignInOutcome Outcome { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// The SuperAdmins' switches for staff sign-in. A single row (<see cref="SingletonId"/>).
/// </summary>
public class StaffSecuritySettings
{
    public const int SingletonId = 1;

    /// <summary>The floor no setting may go below — the plan's "at least 12 characters".</summary>
    public const int MinimumPasswordLengthFloor = 12;

    public int Id { get; set; } = SingletonId;

    /// <summary>When on, a member without 2-step is sent to set it up and can do nothing else.</summary>
    public bool RequireTwoStep { get; set; }

    /// <summary>How long a Studio sign-in lasts, however active the person is.</summary>
    public int SessionLifetimeHours { get; set; } = 24 * 7;

    /// <summary>A session unused for this long ends.</summary>
    public int IdleTimeoutHours { get; set; } = 12;

    public int MinimumPasswordLength { get; set; } = MinimumPasswordLengthFloor;

    /// <summary>How long a setup link stays valid.</summary>
    public int SetupLinkLifetimeHours { get; set; } = 72;

    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
