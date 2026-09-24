using Share7.Application.Common.Models;

namespace Share7.Application.Staff;

/// <summary>
/// The machine codes the Studio endpoints return, in the <c>{ code, messageKey, details }</c>
/// envelope. The Studio is bilingual, so the server never sends it prose: it looks
/// <see cref="ApiErrorCode.MessageKey"/> up in its English and Arabic message files.
/// <para>
/// Kept apart from <see cref="ApiErrors"/>, which is the Unity client's contract. Add; do not rename.
/// </para>
/// </summary>
public static class StudioErrors
{
    /// <summary>Wrong username or password, or not a Studio account — deliberately indistinguishable.</summary>
    public static readonly ApiErrorCode SignInFailed = new("STUDIO_SIGN_IN_FAILED", "errors.signIn.failed");

    /// <summary>Too many wrong attempts. <c>details.retryAfterUtc</c> says when to try again.</summary>
    public static readonly ApiErrorCode LockedOut = new("STUDIO_LOCKED_OUT", "errors.signIn.lockedOut");

    /// <summary>Correct password, but a SuperAdmin has suspended the account.</summary>
    public static readonly ApiErrorCode Suspended = new("STUDIO_ACCOUNT_SUSPENDED", "errors.signIn.suspended");

    /// <summary>Correct password, but the account has been closed for good.</summary>
    public static readonly ApiErrorCode Deactivated = new("STUDIO_ACCOUNT_DEACTIVATED", "errors.signIn.deactivated");

    public static readonly ApiErrorCode TwoStepInvalid = new("STUDIO_TWO_STEP_INVALID", "errors.twoStep.invalid");

    /// <summary>The sign-in waited too long between password and code. Start again.</summary>
    public static readonly ApiErrorCode ChallengeExpired = new("STUDIO_CHALLENGE_EXPIRED", "errors.twoStep.challengeExpired");

    /// <summary>
    /// The setup link cannot be used. <c>details.reason</c>: <c>expired</c>, <c>used</c>,
    /// <c>replaced</c> or <c>unknown</c>.
    /// </summary>
    public static readonly ApiErrorCode SetupLinkInvalid = new("STUDIO_SETUP_LINK_INVALID", "errors.setup.linkInvalid");

    /// <summary>
    /// The new password breaks a rule. <c>details.problems</c> lists them: <c>tooShort</c>,
    /// <c>needsUppercase</c>, <c>needsLowercase</c>, <c>needsDigit</c>, <c>common</c>,
    /// <c>containsUsername</c>, <c>sameAsCurrent</c>.
    /// </summary>
    public static readonly ApiErrorCode PasswordRejected = new("STUDIO_PASSWORD_REJECTED", "errors.password.rejected");

    public static readonly ApiErrorCode WrongPassword = new("STUDIO_WRONG_PASSWORD", "errors.password.wrong");

    /// <summary>2-step is required for staff and this member has not set it up yet.</summary>
    public static readonly ApiErrorCode TwoStepRequired = new("STUDIO_TWO_STEP_REQUIRED", "errors.twoStep.required");

    public static readonly ApiErrorCode TwoStepCannotDisable = new("STUDIO_TWO_STEP_CANNOT_DISABLE", "errors.twoStep.cannotDisable");

    public static readonly ApiErrorCode TwoStepNotStarted = new("STUDIO_TWO_STEP_NOT_STARTED", "errors.twoStep.notStarted");

    public static readonly ApiErrorCode TwoStepAlreadyOn = new("STUDIO_TWO_STEP_ALREADY_ON", "errors.twoStep.alreadyOn");

    /// <summary>The session is over — signed out elsewhere, expired, or revoked. Sign in again.</summary>
    public static readonly ApiErrorCode SessionInvalid = new("STUDIO_SESSION_INVALID", "errors.session.invalid");

    /// <summary>Another tab refreshed the session at the same moment. Retry once; the cookie is already current.</summary>
    public static readonly ApiErrorCode SessionSuperseded = new("STUDIO_SESSION_SUPERSEDED", "errors.session.superseded");

    public static readonly ApiErrorCode NotFound = new("STUDIO_NOT_FOUND", "errors.notFound");

    public static readonly ApiErrorCode Invalid = new("STUDIO_INVALID_REQUEST", "errors.invalidRequest");
}
