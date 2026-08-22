using Share7.Application.Common.Models;
using Share7.Application.Users.Models;

namespace Share7.Application.Users.Interfaces;

/// <summary>
/// Reading and editing player profiles.
/// <para>
/// **Both methods take the caller separately from the target**, and neither reads an identity out of
/// a request body. That separation is what lets the service decide what the caller is entitled to
/// see or change without the controller having to remember.
/// </para>
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// One profile. <paramref name="targetUserId"/> null means the caller's own.
    /// <para>
    /// Phone number and email are returned only when the target is the caller, or the caller is an
    /// admin. Any signed-in account can name any user id — a session roster hands them out — so a
    /// profile read has to assume the id came from a stranger.
    /// </para>
    /// </summary>
    Task<ServiceResult<UserProfileDto>> GetAsync(
        Guid callerId,
        Guid? targetUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies changes. <paramref name="targetUserId"/> null means the caller's own; naming somebody
    /// else requires admin.
    /// <para>
    /// Refuses on a profile that does not exist yet — creating one is
    /// <c>POST /api/auth/complete-profile</c>, which requires every field. This edits, it does not
    /// create, so a half-filled profile cannot be brought into existence through here.
    /// </para>
    /// </summary>
    Task<ServiceResult<UserProfileDto>> UpdateAsync(
        Guid callerId,
        Guid? targetUserId,
        bool callerIsAdmin,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default);
}
