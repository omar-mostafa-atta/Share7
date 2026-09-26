using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Staff;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace Share7.Tests;

/// <summary>
/// The admin sets a content-team member's username and password; there is no setup link and no
/// activation (decided 2026-09-26). The member signs in at the Studio's one address straight away.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AdminSetPasswordTests : StaffTestBase
{
    public AdminSetPasswordTests(SqlServerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_member_created_with_a_password_signs_in_straight_away_and_gets_no_link()
    {
        var username = NewUsername();
        var created = await CreateAsync(username, Password);

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));
        Assert.Null(created.Value!.SetupLink);
        Assert.Equal(StaffStatus.Active, created.Value.Member.Status);
        Assert.NotNull(created.Value.Member.ActivatedAtUtc);
        Assert.True(created.Value.Member.Security.HasPassword);

        var signedIn = await SignInAsync(username, Password);
        Assert.True(signedIn.Succeeded, signedIn.Error?.Code);
        Assert.NotNull(signedIn.Value!.Session);

        await using var check = Check();
        Assert.False(await check.StaffSetupTokens.AnyAsync(t => t.UserId == created.Value.Member.UserId));
    }

    [Theory]
    [InlineData("Short1a")]              // under the staff minimum
    [InlineData("alllowercase123456")]   // no upper-case letter
    [InlineData("Password2026!")]        // a well-known one dressed up
    public async Task A_weak_password_is_refused_and_nothing_is_created(string password)
    {
        var username = NewUsername();
        var created = await CreateAsync(username, password);

        Assert.False(created.Succeeded);
        Assert.Contains(created.Errors, e => e.StartsWith("The password", StringComparison.Ordinal) || e.StartsWith("That password", StringComparison.Ordinal));

        await using var check = Check();
        Assert.False(await check.Users.AnyAsync(u => u.UserName == username));
    }

    [Fact]
    public async Task Setting_a_password_activates_a_member_who_never_had_one_and_withdraws_their_link()
    {
        // A member made the old way, with a link they never used.
        var member = await NewMemberAsync();

        await using (var scope = AsSuperAdmin())
        {
            var set = await scope.Get<ITeamAdminService>().SetPasswordAsync(member.UserId, new SetTeamMemberPasswordRequest(Password, ClearTwoStep: false));
            Assert.True(set.Succeeded, string.Join("; ", set.Errors));
            Assert.Equal(StaffStatus.Active, set.Value!.Status);
            Assert.Null(set.Value.SetupLink);
        }

        Assert.True((await SignInAsync(member.Username, Password)).Succeeded);

        await using var check = Check();
        Assert.True(await check.AuditEvents.AnyAsync(a => a.Action == AuditActions.TeamMemberPasswordSet && a.TargetId == member.UserId.ToString()));
    }

    [Fact]
    public async Task A_new_password_signs_the_member_out_everywhere_and_the_old_one_stops_working()
    {
        var username = NewUsername();
        var created = await CreateAsync(username, Password);
        var session = (await SignInAsync(username, Password)).Value!.Session!;

        await using (var scope = AsSuperAdmin())
        {
            var set = await scope.Get<ITeamAdminService>().SetPasswordAsync(created.Value!.Member.UserId, new SetTeamMemberPasswordRequest(OtherPassword, ClearTwoStep: false));
            Assert.True(set.Succeeded, string.Join("; ", set.Errors));
        }

        Assert.Null(await CheckTokenAsync(session.AccessToken));
        Assert.False((await SignInAsync(username, Password)).Succeeded);
        Assert.True((await SignInAsync(username, OtherPassword)).Succeeded);
    }

    private async Task<Share7.Application.Common.Models.ServiceResult<CreatedTeamMemberDto>> CreateAsync(string username, string password)
    {
        // An Admin, not a SuperAdmin: adding a member is the one thing about the team an Admin does.
        await using var scope = Services.Request(Guid.NewGuid(), Roles.Admin);
        await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

        return await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
            "Ahmed Samir", username, null, "Technical lead", StudioRole.Lead,
            AllNodes: true, NodeIds: null, AllLanguages: true, LanguageIds: null, InterfaceLanguage: "en",
            Password: password));
    }
}
