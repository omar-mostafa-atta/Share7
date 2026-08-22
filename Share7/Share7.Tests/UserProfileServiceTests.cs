using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Users.Models;
using Share7.Domain.Entities;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Users;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Profile reads and edits.
/// <para>
/// **The visibility rule carries most of the weight here.** Any signed-in account can name any user
/// id — a multiplayer roster hands them out by design — so a profile read has to assume the id came
/// from a stranger, and a child's phone number and email must not travel with it.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class UserProfileServiceTests
{
    private readonly SqlServerFixture _fixture;

    public UserProfileServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static UserProfileService Service(ApplicationDbContext context) => new(context);

    private static async Task<Guid> CreateProfiledUserAsync(
        ApplicationDbContext context,
        string fullName = "Layla Hassan")
    {
        var userId = await TestData.CreateUserAsync(context);
        var gradeId = await context.Grades.Select(g => g.Id).FirstAsync();

        context.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = fullName,
            Age = 11,
            PhoneNumber = "+201234567890",
            Email = "layla@example.test",
            GradeId = gradeId,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }

    // -----------------------------------------------------------------------------------------
    // reading
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Omitting_the_user_id_returns_the_callers_own_profile_in_full()
    {
        await using var context = _fixture.CreateContext();
        var userId = await CreateProfiledUserAsync(context);

        var result = await Service(context).GetAsync(userId, targetUserId: null, callerIsAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.True(result.Value.IsSelf);
        Assert.True(result.Value.IsProfileComplete);
        Assert.Equal("Layla Hassan", result.Value.FullName);
        Assert.Equal("+201234567890", result.Value.PhoneNumber);
        Assert.Equal("layla@example.test", result.Value.Email);
    }

    [Fact]
    public async Task Another_players_profile_comes_back_without_their_contact_details()
    {
        await using var context = _fixture.CreateContext();

        var subjectId = await CreateProfiledUserAsync(context);
        var strangerId = await TestData.CreateUserAsync(context);

        var result = await Service(context).GetAsync(strangerId, subjectId, callerIsAdmin: false);

        Assert.True(result.Succeeded);

        // The name is what a roster needs to render an opponent, and it is fine to share. A phone
        // number and an email address are not, and every signed-in account can ask for any id.
        Assert.Equal("Layla Hassan", result.Value!.FullName);
        Assert.Equal(11, result.Value.Age);

        Assert.Null(result.Value.PhoneNumber);
        Assert.Null(result.Value.Email);

        // The flag is what lets a client tell "not recorded" from "not shown to you".
        Assert.False(result.Value.IsSelf);
    }

    [Fact]
    public async Task An_admin_sees_the_contact_details_of_anyone()
    {
        await using var context = _fixture.CreateContext();

        var subjectId = await CreateProfiledUserAsync(context);
        var adminId = await TestData.CreateUserAsync(context);

        var result = await Service(context).GetAsync(adminId, subjectId, callerIsAdmin: true);

        Assert.Equal("+201234567890", result.Value!.PhoneNumber);
        Assert.Equal("layla@example.test", result.Value.Email);

        // Still not the caller's own profile, even though everything is visible.
        Assert.False(result.Value.IsSelf);
    }

    [Fact]
    public async Task An_account_with_no_profile_reads_as_incomplete_rather_than_missing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context).GetAsync(userId, targetUserId: null, callerIsAdmin: false);

        // 200 with a flag, not a 404 — the account exists and the client needs to know to send the
        // user through the complete-profile step.
        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsProfileComplete);
        Assert.Null(result.Value.FullName);
        Assert.NotEqual(string.Empty, result.Value.UserName);
    }

    [Fact]
    public async Task An_unknown_user_id_is_not_found()
    {
        await using var context = _fixture.CreateContext();
        var callerId = await TestData.CreateUserAsync(context);

        var result = await Service(context).GetAsync(callerId, Guid.NewGuid(), callerIsAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
        Assert.Equal("PROFILE_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task Profile_timestamps_are_marked_utc()
    {
        await using var context = _fixture.CreateContext();
        var userId = await CreateProfiledUserAsync(context);

        var result = await Service(context).GetAsync(userId, null, callerIsAdmin: false);

        // Same trap as the multiplayer DTOs: datetime2 carries no timezone, so an un-stamped value
        // serialises without the Z and a naive client parse shifts it by the device's offset.
        Assert.Equal(DateTimeKind.Utc, result.Value!.CreatedAtUtc!.Value.Kind);
    }

    // -----------------------------------------------------------------------------------------
    // editing
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_omitted_field_is_left_alone_rather_than_cleared()
    {
        await using var context = _fixture.CreateContext();
        var userId = await CreateProfiledUserAsync(context);

        var result = await Service(context).UpdateAsync(
            userId, null, callerIsAdmin: false, new UpdateUserProfileRequest { FullName = "Layla H." });

        Assert.True(result.Succeeded);
        Assert.Equal("Layla H.", result.Value!.FullName);

        // **The whole reason this is a partial update.** An edit screen that resent every field
        // would wipe whatever it forgot to load, and the phone number is the field nothing else can
        // recover.
        Assert.Equal("+201234567890", result.Value.PhoneNumber);
        Assert.Equal(11, result.Value.Age);
    }

    [Fact]
    public async Task A_blank_name_is_refused_rather_than_treated_as_a_clear()
    {
        await using var context = _fixture.CreateContext();
        var userId = await CreateProfiledUserAsync(context);

        var result = await Service(context).UpdateAsync(
            userId, null, callerIsAdmin: false, new UpdateUserProfileRequest { FullName = "   " });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);

        await using var check = _fixture.CreateContext();
        var stored = await check.StudentProfiles.AsNoTracking().FirstAsync(p => p.UserId == userId);
        Assert.Equal("Layla Hassan", stored.FullName);
    }

    [Fact]
    public async Task A_grade_that_does_not_exist_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await CreateProfiledUserAsync(context);

        var result = await Service(context).UpdateAsync(
            userId, null, callerIsAdmin: false, new UpdateUserProfileRequest { GradeId = Guid.NewGuid() });

        // A bad grade id would leave the profile pointing at nothing, and every grade-scoped read
        // for that student would quietly come back empty.
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
    }

    [Fact]
    public async Task A_player_cannot_edit_somebody_elses_profile()
    {
        await using var context = _fixture.CreateContext();

        var subjectId = await CreateProfiledUserAsync(context);
        var otherId = await TestData.CreateUserAsync(context);

        var result = await Service(context).UpdateAsync(
            otherId, subjectId, callerIsAdmin: false, new UpdateUserProfileRequest { FullName = "Hacked" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Forbidden, result.ErrorKind);

        await using var check = _fixture.CreateContext();
        var stored = await check.StudentProfiles.AsNoTracking().FirstAsync(p => p.UserId == subjectId);
        Assert.Equal("Layla Hassan", stored.FullName);
    }

    [Fact]
    public async Task An_admin_may_edit_somebody_elses_profile()
    {
        await using var context = _fixture.CreateContext();

        var subjectId = await CreateProfiledUserAsync(context);
        var adminId = await TestData.CreateUserAsync(context);

        var result = await Service(context).UpdateAsync(
            adminId, subjectId, callerIsAdmin: true, new UpdateUserProfileRequest { Age = 12 });

        Assert.True(result.Succeeded);
        Assert.Equal(12, result.Value!.Age);
        Assert.False(result.Value.IsSelf);
    }

    [Fact]
    public async Task Editing_a_profile_that_was_never_completed_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Service(context).UpdateAsync(
            userId, null, callerIsAdmin: false, new UpdateUserProfileRequest { FullName = "Someone" });

        // This edits; it does not create. Letting it insert would put a half-filled row into
        // existence and isProfileComplete would start lying.
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
        Assert.Equal("PROFILE_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task An_edit_stamps_the_update_time()
    {
        await using var context = _fixture.CreateContext();
        var userId = await CreateProfiledUserAsync(context);

        var before = await Service(context).GetAsync(userId, null, callerIsAdmin: false);
        Assert.Null(before.Value!.UpdatedAtUtc);

        var after = await Service(context).UpdateAsync(
            userId, null, callerIsAdmin: false, new UpdateUserProfileRequest { Age = 12 });

        Assert.NotNull(after.Value!.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, after.Value.UpdatedAtUtc!.Value.Kind);
    }
}
