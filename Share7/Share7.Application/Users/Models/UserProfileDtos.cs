using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Users.Models;

/// <summary>
/// A player's profile.
/// <para>
/// **Two fields are withheld when you are looking at somebody else: <see cref="PhoneNumber"/> and
/// <see cref="Email"/>.** They are contact details for a child, and every signed-in account can ask
/// for any user id — a roster hands them out by design. Reading your own profile, or reading one as
/// an admin, returns them; anything else returns null. <see cref="IsSelf"/> says which kind of
/// answer this is, so a client can tell "no phone number recorded" from "not shown to you".
/// </para>
/// </summary>
public class UserProfileDto
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>Null when the account has not completed a profile yet.</summary>
    public string? FullName { get; set; }

    public int? Age { get; set; }

    /// <summary>Withheld unless this is your own profile or you are an admin.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Withheld unless this is your own profile or you are an admin.</summary>
    public string? Email { get; set; }

    public Guid? GradeId { get; set; }

    public Guid? PreferredLanguageId { get; set; }

    /// <summary>
    /// Whether a profile row exists at all. False means the account registered but never completed
    /// the profile step, and every field below <see cref="UserName"/> is null.
    /// </summary>
    public bool IsProfileComplete { get; set; }

    /// <summary>True when this is the caller's own profile — see the note on withheld fields.</summary>
    public bool IsSelf { get; set; }

    public DateTime? CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Changes to a profile. **Partial: a null field is left alone, not cleared.**
/// <para>
/// Deliberately unlike <c>CompleteProfileRequest</c>, which requires everything because it is
/// creating the row. An edit screen that had to resend every field would wipe whatever it forgot to
/// load — and the field it is most likely to forget is the phone number, which nothing else can
/// recover.
/// </para>
/// <para>
/// The consequence is that there is no way to clear an optional field back to null through here.
/// That is the right trade for the fields this holds: accidental erasure is the worse failure.
/// </para>
/// </summary>
public class UpdateUserProfileRequest
{
    [MaxLength(200)]
    public string? FullName { get; set; }

    [Range(3, 100)]
    public int? Age { get; set; }

    [Phone]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>Must be a real grade. Validated, because a bad id would silently orphan the profile.</summary>
    public Guid? GradeId { get; set; }
}
