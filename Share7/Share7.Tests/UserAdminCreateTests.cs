using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Users.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Progression;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Admin-created accounts. Content-team accounts are no longer made here — a SuperAdmin creates
/// them in Team &amp; Access (<c>TeamAccessTests</c>).
/// <para>
/// **The privilege line carries most of the weight here.** Creating an account is handing someone
/// a key; an Admin who could mint another Admin would make the SuperAdmin-only delete rule
/// meaningless, since the account they could not remove is one they could simply multiply.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class UserAdminCreateTests
{
    // Valid under the app's rules (8+, upper, lower, digit) and under the test host's Identity
    // defaults, which additionally want a symbol.
    private const string GoodPassword = "Content#2026";

    private readonly SqlServerFixture _fixture;

    public UserAdminCreateTests(SqlServerFixture fixture) => _fixture = fixture;

    private static async Task<UserAdminService> ServiceAsync(ApplicationDbContext context)
    {
        await IdentityTestHost.EnsureRolesAsync(context);

        return new UserAdminService(
            IdentityTestHost.CreateUserManager(context),
            context,
            new WalletService(context),
            new LevelService(context),
            ObjectiveTestExtensions.CreateObjectiveService(context),
            TestAudit.For(context));
    }

    private static string NewUsername() => $"staff_{Guid.NewGuid():N}"[..20];

    private static CreateAdminUserRequest Request(string username, string role, string password = GoodPassword) =>
        new() { Username = username, Password = password, Role = role };

    private static Task<List<string>> RolesOfAsync(ApplicationDbContext context, Guid userId) =>
        (from userRole in context.UserRoles
         join role in context.Roles on userRole.RoleId equals role.Id
         where userRole.UserId == userId
         select role.Name!).ToListAsync();

    // -----------------------------------------------------------------------------------------
    // creating
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_admin_creates_a_student_account_that_can_sign_in()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);
        var username = NewUsername();

        var result = await service.CreateUserAsync(Request(username, Roles.Student), actorIsSuperAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal(username, result.Value!.UserName);
        Assert.Equal([Roles.Student], result.Value.Roles);

        await using var check = _fixture.CreateContext();
        var user = await check.Users.SingleAsync(u => u.Id == result.Value.UserId);

        Assert.Equal([Roles.Student], await RolesOfAsync(check, user.Id));

        // The password is the one the admin typed, which is the whole point: they hand it over.
        Assert.True(await IdentityTestHost.CreateUserManager(check).CheckPasswordAsync(user, GoodPassword));

        // So the first token carries a language claim and the console's picker agrees with the tree.
        Assert.Equal(LanguageIds.English, user.PreferredLanguageId);
    }

    [Fact]
    public async Task The_username_is_stored_trimmed_so_the_sign_in_form_can_match_it()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);
        var username = NewUsername();

        var result = await service.CreateUserAsync(Request($"  {username} ", Roles.Student), actorIsSuperAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal(username, result.Value!.UserName);
    }

    [Fact]
    public async Task A_taken_username_is_a_conflict()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);
        var username = NewUsername();
        await TestData.CreateUserAsync(context, username);

        var result = await service.CreateUserAsync(Request(username, Roles.Student), actorIsSuperAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
    }

    [Fact]
    public async Task A_weak_password_is_refused_with_identitys_reasons_and_leaves_nothing_behind()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);
        var username = NewUsername();

        var result = await service.CreateUserAsync(Request(username, Roles.Student, password: "short"), actorIsSuperAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
        Assert.NotEmpty(result.Errors);

        await using var check = _fixture.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.UserName == username));
    }

    // -----------------------------------------------------------------------------------------
    // who may give which role
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_admin_creates_an_admin_but_never_a_super_admin()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);

        // Every role but SuperAdmin is an Admin's to give (decided 2026-09-26).
        var admin = await service.CreateUserAsync(Request(NewUsername(), Roles.Admin), actorIsSuperAdmin: false);
        Assert.True(admin.Succeeded);

        // A SuperAdmin they could mint is a SuperAdmin they could sign in as.
        var username = NewUsername();
        var superAdmin = await service.CreateUserAsync(Request(username, Roles.SuperAdmin), actorIsSuperAdmin: false);
        Assert.Equal(ServiceErrorKind.Forbidden, superAdmin.ErrorKind);

        await using var check = _fixture.CreateContext();
        Assert.Equal([Roles.Admin], await RolesOfAsync(check, admin.Value!.UserId));
        Assert.False(await check.Users.AnyAsync(u => u.UserName == username));
    }

    [Fact]
    public async Task A_super_admin_can_create_an_admin()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);

        var result = await service.CreateUserAsync(Request(NewUsername(), Roles.Admin), actorIsSuperAdmin: true);

        Assert.True(result.Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.Equal([Roles.Admin], await RolesOfAsync(check, result.Value!.UserId));
    }

    [Theory]
    [InlineData(Roles.Teacher)]   // exists, but grants nothing — Roles.md §4
    [InlineData("Janitor")]       // not a role at all
    [InlineData("contentteam")]   // role names are exact; a near-miss is not quietly corrected
    public async Task A_role_that_cannot_be_given_is_refused_even_for_a_super_admin(string role)
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);

        var result = await service.CreateUserAsync(Request(NewUsername(), role), actorIsSuperAdmin: true);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
    }

    [Fact]
    public async Task The_assignable_roles_follow_the_same_line_as_creation()
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);

        Assert.Equal([Roles.Student, Roles.ContentTeam, Roles.Admin], service.GetAssignableRoles(actorIsSuperAdmin: false));
        Assert.Equal(
            [Roles.Student, Roles.ContentTeam, Roles.Admin, Roles.SuperAdmin],
            service.GetAssignableRoles(actorIsSuperAdmin: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Content_team_accounts_are_made_in_team_and_access_not_here(bool actorIsSuperAdmin)
    {
        await using var context = _fixture.CreateContext();
        var service = await ServiceAsync(context);
        var username = NewUsername();

        var result = await service.CreateUserAsync(Request(username, Roles.ContentTeam), actorIsSuperAdmin);

        Assert.Equal(ServiceErrorKind.Forbidden, result.ErrorKind);
        Assert.Contains("Team & Access", result.Errors.Single());

        await using var check = _fixture.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.UserName == username));
    }
}
