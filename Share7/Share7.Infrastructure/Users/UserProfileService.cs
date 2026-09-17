using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Users.Interfaces;
using Share7.Application.Users.Models;
using Share7.Domain.Entities;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Users;

/// <summary>
/// Player profiles. See <see cref="IUserProfileService"/> for the visibility rule.
/// </summary>
public class UserProfileService : IUserProfileService
{
    private readonly ApplicationDbContext _dbContext;

    public UserProfileService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<ServiceResult<UserProfileDto>> GetAsync(
        Guid callerId,
        Guid? targetUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var userId = targetUserId ?? callerId;

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return ServiceResult<UserProfileDto>.Failure(
                ApiErrors.ProfileNotFound,
                ServiceErrorKind.NotFound,
                $"User {userId} does not exist.");

        var profile = await _dbContext.StudentProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var isSelf = userId == callerId;

        return ServiceResult<UserProfileDto>.Success(
            ToDto(user, profile, isSelf, revealContact: isSelf || callerIsAdmin));
    }

    public async Task<ServiceResult<UserProfileDto>> UpdateAsync(
        Guid callerId,
        Guid? targetUserId,
        bool callerIsAdmin,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = targetUserId ?? callerId;

        // Editing somebody else's profile is an admin action. Checked before the row is even read,
        // so a non-admin probing user ids learns nothing about which ones exist.
        if (userId != callerId && !callerIsAdmin)
            return ServiceResult<UserProfileDto>.Failure(
                ApiErrors.Forbidden,
                ServiceErrorKind.Forbidden,
                "Only an admin may edit another account's profile.");

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return ServiceResult<UserProfileDto>.Failure(
                ApiErrors.ProfileNotFound,
                ServiceErrorKind.NotFound,
                $"User {userId} does not exist.");

        var profile = await _dbContext.StudentProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        // **This edits; it does not create.** Creating a profile is complete-profile, which requires
        // every field — allowing this to insert would let a half-filled row into existence and the
        // "is the profile complete" flag would start lying.
        if (profile is null)
            return ServiceResult<UserProfileDto>.Failure(
                ApiErrors.ProfileNotFound,
                ServiceErrorKind.NotFound,
                "No profile to edit. Complete the profile first via POST /api/auth/complete-profile.");

        if (request.FullName is { } fullName)
        {
            var trimmed = fullName.Trim();

            // A partial update treats null as "leave alone", so an explicitly empty string is a
            // client bug rather than an instruction — refuse it instead of blanking the name.
            if (trimmed.Length == 0)
                return ServiceResult<UserProfileDto>.Failure(
                    ApiErrors.ValidationFailed,
                    ServiceErrorKind.Validation,
                    "fullName cannot be blank. Omit it to leave the name unchanged.");

            profile.FullName = trimmed;
        }

        if (request.Age is { } age)
            profile.Age = age;

        if (request.PhoneNumber is { } phoneNumber)
            profile.PhoneNumber = phoneNumber.Trim();

        if (request.Email is { } email)
            profile.Email = email.Trim();

        if (request.GradeId is { } gradeId)
        {
            // Validated rather than trusted: a bad id would leave the profile pointing at nothing,
            // and every grade-scoped read for that student would quietly return empty.
            if (!await _dbContext.Grades.AnyAsync(g => g.Id == gradeId, cancellationToken))
                return ServiceResult<UserProfileDto>.Failure(
                    ApiErrors.ValidationFailed,
                    ServiceErrorKind.Validation,
                    $"Grade {gradeId} does not exist.");

            profile.GradeId = gradeId;
        }

        profile.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<UserProfileDto>.Success(
            ToDto(user, profile, isSelf: userId == callerId, revealContact: true));
    }

    private static UserProfileDto ToDto(
        ApplicationUser user,
        StudentProfile? profile,
        bool isSelf,
        bool revealContact) => new()
    {
        UserId = user.Id,
        UserName = user.UserName ?? string.Empty,
        FullName = profile?.FullName,
        Age = profile?.Age,

        // The whole point of the flag: withheld reads as null, and IsSelf tells the client whether
        // null means "not recorded" or "not shown to you".
        PhoneNumber = revealContact ? profile?.PhoneNumber : null,
        Email = revealContact ? profile?.Email : null,

        GradeId = profile?.GradeId,
        PreferredLanguageId = user.PreferredLanguageId,
        IsProfileComplete = profile is not null,
        IsSelf = isSelf,

        // Re-stamped as UTC — these come back from datetime2 as Unspecified and would otherwise
        // serialise without the Z, which a naive client parse shifts by its local offset.
        CreatedAtUtc = AsUtc(profile?.CreatedAt),
        UpdatedAtUtc = AsUtc(profile?.UpdatedAt)
    };

    private static DateTime? AsUtc(DateTime? value) =>
        value is { } set
            ? (set.Kind == DateTimeKind.Utc ? set : DateTime.SpecifyKind(set, DateTimeKind.Utc))
            : null;
}
