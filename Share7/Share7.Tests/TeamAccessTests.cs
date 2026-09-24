using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Auth.Models;
using Share7.Application.Common.Models;
using Share7.Application.Staff;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Application.Users.Interfaces;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Staff;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;
using Share7.Infrastructure.Structure;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Shared set-up for the Team &amp; Access and Studio sign-in tests: the real container, a
/// SuperAdmin to act as, and the steps every scenario starts with.
/// </summary>
public abstract class StaffTestBase : IDisposable
{
    protected const string Password = "Blue-Harbour-Lantern-7";
    protected const string OtherPassword = "Quiet-Orchard-Signal-42";

    protected static readonly StudioClientInfo Browser =
        new("198.51.100.23", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/140.0 Safari/537.36");

    protected readonly SqlServerFixture Fixture;
    protected readonly ServiceProvider Services;
    protected readonly Guid SuperAdminId = Guid.NewGuid();

    protected StaffTestBase(SqlServerFixture fixture)
    {
        Fixture = fixture;
        Services = StaffTestHost.Build(fixture);
    }

    public void Dispose()
    {
        Services.Dispose();
        GC.SuppressFinalize(this);
    }

    protected sealed record Member(Guid UserId, string Username, string SetupSecret);

    protected AsyncServiceScope AsSuperAdmin() => Services.Request(SuperAdminId, Roles.SuperAdmin);

    protected AsyncServiceScope Anonymous() => Services.Request();

    protected AsyncServiceScope AsMember(Guid userId) => Services.Request(userId, "Studio:Author");

    protected static string NewUsername() => $"m{Guid.NewGuid():N}"[..18];

    protected static string SecretOf(SetupLinkDto link) => link.Url[(link.Url.IndexOf('#') + 1)..];

    protected async Task<Member> NewMemberAsync(StudioRole role = StudioRole.Author)
    {
        await using var scope = AsSuperAdmin();
        await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

        var result = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
            "Mona Adel", NewUsername(), null, "Science specialist", role,
            AllNodes: true, NodeIds: null, AllLanguages: true, LanguageIds: null, InterfaceLanguage: "en"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return new Member(result.Value!.Member.UserId, result.Value.Member.Username, SecretOf(result.Value.SetupLink));
    }

    protected async Task<StudioSessionTokens> ActivateAsync(Member member, string password = Password)
    {
        await using var scope = Anonymous();
        var result = await scope.Get<IStudioAuthService>().CompleteSetupAsync(
            new StudioSetupCompleteRequest(member.SetupSecret, password, "ar"), Browser);

        Assert.True(result.Succeeded, result.Error?.Code);
        return result.Value!.Session!;
    }

    protected async Task<ServiceResult<StudioSignInOutcome>> SignInAsync(string username, string password)
    {
        await using var scope = Anonymous();
        return await scope.Get<IStudioAuthService>().SignInAsync(new StudioSignInRequest(username, password), Browser);
    }

    /// <summary>What the Studio scheme's per-request check says about an access token right now.</summary>
    protected async Task<StudioSessionState?> CheckTokenAsync(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        await using var scope = Anonymous();

        return await scope.Get<IStudioSessionValidator>().ValidateAsync(
            Guid.Parse(jwt.Subject),
            Guid.Parse(jwt.Claims.First(c => c.Type == StaffSecrets.SessionClaim).Value),
            jwt.Claims.First(c => c.Type == StaffSecrets.StampClaim).Value);
    }

    protected ApplicationDbContext Check() => Fixture.CreateContext();
}

/// <summary>
/// Team &amp; Access: only a SuperAdmin makes a content-team account, the account is born with no
/// password and a one-time link, and every lifecycle step shuts the right doors — in the same
/// transaction as its audit row.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TeamAccessTests : StaffTestBase
{
    public TeamAccessTests(SqlServerFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------------ creating

    [Fact]
    public async Task A_new_member_has_no_password_only_a_one_time_link_whose_secret_is_never_stored()
    {
        var member = await NewMemberAsync(StudioRole.Reviewer);

        await using var check = Check();
        var user = await check.Users.SingleAsync(u => u.Id == member.UserId);
        var profile = await check.StaffProfiles.SingleAsync(p => p.UserId == member.UserId);
        var link = await check.StaffSetupTokens.SingleAsync(t => t.UserId == member.UserId);

        Assert.Null(user.PasswordHash);
        Assert.True(user.LockoutEnabled);
        Assert.Equal(StaffStatus.Invited, profile.Status);
        Assert.Equal(StudioRole.Reviewer, profile.StudioRole);
        Assert.Equal(SuperAdminId, profile.CreatedByUserId);

        var roles = await (from ur in check.UserRoles join r in check.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Name).ToListAsync();
        Assert.Equal([Roles.ContentTeam], roles);

        // Only the hash is kept; the secret exists in the link the SuperAdmin was shown, nowhere else.
        Assert.Equal(StaffSecrets.Hash(member.SetupSecret), link.TokenHash);
        Assert.NotEqual(member.SetupSecret, link.TokenHash);
        Assert.Equal(StaffSetupPurpose.Activation, link.Purpose);
        Assert.InRange(link.ExpiresAtUtc - link.CreatedAtUtc, TimeSpan.FromHours(71.9), TimeSpan.FromHours(72.1));

        var audit = await check.AuditEvents.SingleAsync(e => e.TargetId == member.UserId.ToString() && e.Action == AuditActions.TeamMemberCreated);
        Assert.Equal(SuperAdminId, audit.ActorUserId);
        Assert.Equal(AuditAreas.Team, audit.Area);
        Assert.DoesNotContain(member.Username, audit.Summary + audit.DataJson);
        Assert.DoesNotContain("Mona", audit.Summary + audit.DataJson);
        Assert.DoesNotContain(member.SetupSecret, audit.Summary + audit.DataJson);
    }

    [Fact]
    public async Task The_setup_link_points_at_the_studio_and_keeps_its_secret_in_the_fragment()
    {
        await using var scope = AsSuperAdmin();
        await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

        var created = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
            "Karim Nabil", NewUsername(), "karim@example.test", null, StudioRole.Author, true, null, true, null, null));

        var link = created.Value!.SetupLink;
        Assert.True(link.IsAbsolute);
        Assert.StartsWith($"{StaffTestHost.StudioUrl}/activate#", link.Url);
        Assert.DoesNotContain("?", link.Url);
    }

    [Theory]
    [InlineData("1starts-with-digit")]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("emoji😀name")]
    public async Task A_badly_formed_username_is_refused(string username)
    {
        await using var scope = AsSuperAdmin();
        var result = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
            "Some Body", username, null, null, StudioRole.Author, true, null, true, null, "en"));

        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
    }

    [Fact]
    public async Task A_taken_username_is_refused_and_nothing_is_created()
    {
        var existing = await NewMemberAsync();

        await using var scope = AsSuperAdmin();
        var result = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
            "Some Body Else", existing.Username, null, null, StudioRole.Author, true, null, true, null, "en"));

        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
        Assert.Contains(result.Errors, e => e.Contains("already taken"));

        await using var check = Check();
        Assert.Equal(1, await check.StaffProfiles.CountAsync(p => p.FullName == "Some Body Else" || p.UserId == existing.UserId));
    }

    [Fact]
    public async Task A_scope_must_name_real_curriculum_and_real_languages()
    {
        await using var scope = AsSuperAdmin();
        var team = scope.Get<ITeamAdminService>();

        var empty = await team.CreateMemberAsync(new CreateTeamMemberRequest(
            "Some Body", NewUsername(), null, null, StudioRole.Author, AllNodes: false, NodeIds: [], AllLanguages: true, LanguageIds: null, "en"));
        var invented = await team.CreateMemberAsync(new CreateTeamMemberRequest(
            "Some Body", NewUsername(), null, null, StudioRole.Author, false, [Guid.NewGuid()], false, [Guid.NewGuid()], "en"));

        Assert.Equal(ServiceErrorKind.Validation, empty.ErrorKind);
        Assert.Equal(ServiceErrorKind.Validation, invented.ErrorKind);
        Assert.Equal(2, invented.Errors.Count);
    }

    [Fact]
    public async Task A_scoped_member_reads_their_scope_as_a_trail_in_both_languages()
    {
        Guid subjectId;
        await using (var setup = Check())
        {
            var path = await TestData.CreateCurriculumPathAsync(setup);
            await new CurriculumProjector(setup).SyncAsync();
            subjectId = path.SubjectId;
        }

        await using var scope = AsSuperAdmin();
        await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

        var created = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
            "Sara Fathy", NewUsername(), null, null, StudioRole.Author,
            AllNodes: false, NodeIds: [subjectId], AllLanguages: false, LanguageIds: [LanguageIds.Arabic], "ar"));

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        var scopeDto = created.Value!.Member.Scope;
        Assert.False(scopeDto.AllNodes);
        var node = Assert.Single(scopeDto.Nodes);
        Assert.Equal(subjectId, node.Id);
        Assert.Equal("subject", node.Kind);
        Assert.Equal(3, node.Trail.Count); // grade › term › subject
        Assert.True(node.Exists);
        Assert.Equal(LanguageIds.Arabic, Assert.Single(scopeDto.Languages).Id);
    }

    // ------------------------------------------------------------------ activation

    [Fact]
    public async Task The_setup_link_shows_who_it_is_for_and_activates_the_account_exactly_once()
    {
        var member = await NewMemberAsync(StudioRole.Lead);

        await using (var scope = Anonymous())
        {
            var auth = scope.Get<IStudioAuthService>();

            var info = await auth.InspectSetupLinkAsync(member.SetupSecret);
            Assert.True(info.Succeeded);
            Assert.Equal("Mona Adel", info.Value!.FullName);
            Assert.Equal(member.Username, info.Value.Username);
            Assert.Equal(StudioRole.Lead, info.Value.StudioRole);
            Assert.Equal(12, info.Value.PasswordRules.MinimumLength);

            var tooShort = await auth.CompleteSetupAsync(new StudioSetupCompleteRequest(member.SetupSecret, "Short1a", null), Browser);
            Assert.Equal(StudioErrors.PasswordRejected, tooShort.Error);
            Assert.Contains("tooShort", (IEnumerable<string>)tooShort.Details!["problems"]!);

            var common = await auth.CompleteSetupAsync(new StudioSetupCompleteRequest(member.SetupSecret, "Password2026!", null), Browser);
            Assert.Contains("common", (IEnumerable<string>)common.Details!["problems"]!);
        }

        var session = await ActivateAsync(member);
        Assert.NotNull(await CheckTokenAsync(session.AccessToken));

        await using (var check = Check())
        {
            var profile = await check.StaffProfiles.SingleAsync(p => p.UserId == member.UserId);
            Assert.Equal(StaffStatus.Active, profile.Status);
            Assert.NotNull(profile.ActivatedAtUtc);
            Assert.Equal("ar", profile.InterfaceLanguage);
            Assert.NotNull((await check.StaffSetupTokens.SingleAsync(t => t.UserId == member.UserId)).UsedAtUtc);

            var activated = await check.AuditEvents.SingleAsync(e => e.Action == AuditActions.StudioAccountActivated && e.TargetId == member.UserId.ToString());
            Assert.Equal(member.UserId, activated.ActorUserId);
            Assert.Contains(await check.StaffSignInEvents.Where(e => e.UserId == member.UserId).Select(e => e.Outcome).ToListAsync(),
                o => o == StaffSignInOutcome.Activated);
        }

        await using (var scope = Anonymous())
        {
            var again = await scope.Get<IStudioAuthService>().InspectSetupLinkAsync(member.SetupSecret);
            Assert.Equal(StudioErrors.SetupLinkInvalid, again.Error);
            Assert.Equal("used", again.Details!["reason"]);
        }
    }

    // ------------------------------------------------------------------ signing in

    [Fact]
    public async Task Only_an_active_studio_account_can_sign_in_and_every_refusal_looks_the_same()
    {
        var member = await NewMemberAsync();

        // Invited: no password yet, so the right one does not exist.
        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(member.Username, Password)).Error);

        await ActivateAsync(member);

        // A student with a real password is not a Studio account.
        string studentName;
        await using (var setup = Services.Request())
        {
            var users = setup.Get<UserManager<ApplicationUser>>();
            studentName = NewUsername();
            var student = new ApplicationUser { UserName = studentName };
            Assert.True((await users.CreateAsync(student, Password)).Succeeded);
            await users.AddToRoleAsync(student, Roles.Student);
        }

        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(studentName, Password)).Error);
        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync("nobody-" + Guid.NewGuid().ToString("N")[..8], Password)).Error);
        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(member.Username, OtherPassword)).Error);

        var signedIn = await SignInAsync(member.Username, Password);
        Assert.True(signedIn.Succeeded);
        Assert.NotNull(signedIn.Value!.Session);
        Assert.Null(signedIn.Value.TwoStepChallenge);

        await using var check = Check();
        var outcomes = await check.StaffSignInEvents.Where(e => e.UserId == member.UserId).OrderBy(e => e.Id).Select(e => e.Outcome).ToListAsync();
        Assert.Contains(StaffSignInOutcome.WrongPassword, outcomes);
        Assert.Equal(StaffSignInOutcome.Succeeded, outcomes[^1]);

        // Attempts on accounts that are not staff leave no Studio history.
        var studentId = await check.Users.Where(u => u.UserName == studentName).Select(u => u.Id).SingleAsync();
        Assert.False(await check.StaffSignInEvents.AnyAsync(e => e.UserId == studentId));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while()
    {
        var member = await NewMemberAsync();
        await ActivateAsync(member);

        for (var i = 0; i < 4; i++)
            Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(member.Username, OtherPassword)).Error);

        var fifth = await SignInAsync(member.Username, OtherPassword);
        Assert.Equal(StudioErrors.LockedOut, fifth.Error);
        Assert.NotNull(fifth.Details!["retryAfterUtc"]);

        // Even the right password waits it out.
        Assert.Equal(StudioErrors.LockedOut, (await SignInAsync(member.Username, Password)).Error);
    }

    // ------------------------------------------------------------------ lifecycle

    [Fact]
    public async Task Suspending_ends_studio_sessions_at_once_and_the_old_sign_in_was_already_shut()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);
        Assert.NotNull(await CheckTokenAsync(session.AccessToken));

        // There is no second door left to close. Until cutover (plan P6) this member could also sign
        // in to the /content pages the old way, and suspension had to reach that too; now the old
        // sign-in refuses a content-team account on its own, before anybody has suspended anything.
        await using (var legacy = Services.Request())
        {
            var old = await legacy.Get<IAuthService>().LoginAsync(new LoginRequest { Username = member.Username, Password = Password }, "203.0.113.1");
            Assert.False(old.Succeeded);
            Assert.Contains("Content Studio", string.Join(" ", old.Errors));
        }

        await using (var scope = AsSuperAdmin())
        {
            var suspended = await scope.Get<ITeamAdminService>().SuspendAsync(member.UserId, new SuspendTeamMemberRequest("Left the project"));
            Assert.True(suspended.Succeeded, string.Join("; ", suspended.Errors));
            Assert.Equal(StaffStatus.Suspended, suspended.Value!.Status);
            Assert.Equal("Left the project", suspended.Value.StatusReason);
            Assert.Empty(suspended.Value.Sessions);
        }

        // Refused on the very next request — this server dropped its cached answer.
        Assert.Null(await CheckTokenAsync(session.AccessToken));

        var studio = await SignInAsync(member.Username, Password);
        Assert.Equal(StudioErrors.Suspended, studio.Error);

        await using (var legacy = Services.Request())
        {
            // Still refused, and now for the older reason: suspension locks the Identity account
            // itself, which is checked before anything else and would hold even if a door reopened.
            var old = await legacy.Get<IAuthService>().LoginAsync(new LoginRequest { Username = member.Username, Password = Password }, "203.0.113.1");
            Assert.False(old.Succeeded);
            Assert.Contains("locked out", string.Join(" ", old.Errors));
        }

        await using (var check = Check())
        {
            Assert.False(await check.RefreshTokens.AnyAsync(t => t.UserId == member.UserId && t.RevokedAt == null));
            Assert.Equal(StaffSessionEndReasons.Suspended,
                (await check.StaffSessions.SingleAsync(s => s.UserId == member.UserId)).RevokedReason);

            var audit = await check.AuditEvents.SingleAsync(e => e.Action == AuditActions.TeamMemberSuspended && e.TargetId == member.UserId.ToString());
            Assert.Equal(SuperAdminId, audit.ActorUserId);
            Assert.Contains("\"studioSessionsEnded\":1", audit.DataJson);
        }

        await using (var scope = AsSuperAdmin())
            Assert.True((await scope.Get<ITeamAdminService>().ReactivateAsync(member.UserId)).Succeeded);

        Assert.True((await SignInAsync(member.Username, Password)).Succeeded);
    }

    [Fact]
    public async Task Deactivation_is_confirmed_by_username_and_can_never_be_undone()
    {
        var member = await NewMemberAsync();
        await ActivateAsync(member);

        await using (var scope = AsSuperAdmin())
        {
            var team = scope.Get<ITeamAdminService>();

            Assert.Equal(ServiceErrorKind.Validation,
                (await team.DeactivateAsync(member.UserId, new DeactivateTeamMemberRequest("Contract ended", "someone-else"))).ErrorKind);
            Assert.Equal(ServiceErrorKind.Validation,
                (await team.DeactivateAsync(member.UserId, new DeactivateTeamMemberRequest(" ", member.Username))).ErrorKind);

            var done = await team.DeactivateAsync(member.UserId, new DeactivateTeamMemberRequest("Contract ended", member.Username.ToUpperInvariant()));
            Assert.True(done.Succeeded, string.Join("; ", done.Errors));
            Assert.False(done.Value!.Security.HasPassword);

            Assert.Equal(ServiceErrorKind.Conflict, (await team.ReactivateAsync(member.UserId)).ErrorKind);
            Assert.Equal(ServiceErrorKind.Conflict, (await team.ResetAccessAsync(member.UserId, new ResetTeamMemberAccessRequest(false))).ErrorKind);
            Assert.Equal(ServiceErrorKind.Conflict, (await team.UpdateProfileAsync(member.UserId,
                new UpdateTeamMemberProfileRequest("New Name", null, null, "en", done.Value.RowVersion))).ErrorKind);
        }

        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(member.Username, Password)).Error);

        // The record stays; only the door is gone.
        await using var check = Check();
        Assert.True(await check.Users.AnyAsync(u => u.Id == member.UserId));
        Assert.Equal(StaffStatus.Deactivated, (await check.StaffProfiles.SingleAsync(p => p.UserId == member.UserId)).Status);
    }

    [Fact]
    public async Task Resetting_access_clears_the_password_ends_every_session_and_issues_a_new_link()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);

        SetupLinkDto link;
        await using (var scope = AsSuperAdmin())
        {
            var reset = await scope.Get<ITeamAdminService>().ResetAccessAsync(member.UserId, new ResetTeamMemberAccessRequest(ClearTwoStep: true));
            Assert.True(reset.Succeeded, string.Join("; ", reset.Errors));
            link = reset.Value!;
        }

        Assert.Equal(StaffSetupPurpose.Reset, link.Purpose);
        Assert.Null(await CheckTokenAsync(session.AccessToken));
        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(member.Username, Password)).Error);

        await using (var scope = Anonymous())
        {
            var auth = scope.Get<IStudioAuthService>();
            Assert.Equal(StaffSetupPurpose.Reset, (await auth.InspectSetupLinkAsync(SecretOf(link))).Value!.Purpose);

            var chosen = await auth.CompleteSetupAsync(new StudioSetupCompleteRequest(SecretOf(link), OtherPassword, null), Browser);
            Assert.NotNull(chosen.Value!.Session);
        }

        Assert.True((await SignInAsync(member.Username, OtherPassword)).Succeeded);

        await using var check = Check();
        Assert.True(await check.AuditEvents.AnyAsync(e => e.Action == AuditActions.StudioPasswordSet && e.ActorUserId == member.UserId));
    }

    [Fact]
    public async Task A_new_role_applies_on_the_next_request_and_a_stale_edit_is_refused()
    {
        var member = await NewMemberAsync(StudioRole.Author);
        var session = await ActivateAsync(member);
        Assert.Equal(StudioRole.Author, (await CheckTokenAsync(session.AccessToken))!.StudioRole);

        string staleVersion;
        await using (var scope = AsSuperAdmin())
        {
            var team = scope.Get<ITeamAdminService>();
            staleVersion = (await team.GetMemberAsync(member.UserId)).Value!.RowVersion;

            var changed = await team.UpdateAccessAsync(member.UserId,
                new UpdateTeamMemberAccessRequest(StudioRole.Lead, true, null, true, null, staleVersion));
            Assert.True(changed.Succeeded, string.Join("; ", changed.Errors));

            // A second SuperAdmin still holding the old version is told, not silently overwritten.
            var stale = await team.UpdateAccessAsync(member.UserId,
                new UpdateTeamMemberAccessRequest(StudioRole.Author, true, null, true, null, staleVersion));
            Assert.Equal(ServiceErrorKind.Conflict, stale.ErrorKind);
        }

        Assert.Equal(StudioRole.Lead, (await CheckTokenAsync(session.AccessToken))!.StudioRole);

        await using var check = Check();
        var audit = await check.AuditEvents.SingleAsync(e => e.Action == AuditActions.TeamMemberAccessChanged && e.TargetId == member.UserId.ToString());
        Assert.Contains("Author", audit.Summary);
        Assert.Contains("Lead", audit.Summary);
    }

    [Fact]
    public async Task Content_team_accounts_from_before_team_and_access_are_listed_and_can_be_set_up()
    {
        Guid legacyId;
        var legacyName = NewUsername();

        await using (var setup = Services.Request())
        {
            await IdentityTestHost.EnsureRolesAsync(setup.Get<ApplicationDbContext>());
            var users = setup.Get<UserManager<ApplicationUser>>();
            var legacy = new ApplicationUser { UserName = legacyName };
            Assert.True((await users.CreateAsync(legacy, Password)).Succeeded);
            await users.AddToRoleAsync(legacy, Roles.ContentTeam);
            legacyId = legacy.Id;
        }

        // Not a Studio account yet.
        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(legacyName, Password)).Error);

        await using (var scope = AsSuperAdmin())
        {
            var team = scope.Get<ITeamAdminService>();
            Assert.Contains((await team.GetOverviewAsync()).LegacyAccounts, a => a.UserId == legacyId);

            var adopted = await team.AdoptLegacyAccountAsync(legacyId, new AdoptLegacyAccountRequest(
                "Hana Samir", null, null, StudioRole.Reviewer, true, null, true, null, "en"));

            Assert.True(adopted.Succeeded, string.Join("; ", adopted.Errors));
            Assert.Equal(StaffStatus.Active, adopted.Value!.Status);

            var overview = await team.GetOverviewAsync();
            Assert.DoesNotContain(overview.LegacyAccounts, a => a.UserId == legacyId);
            Assert.Contains(overview.Members, m => m.UserId == legacyId);
        }

        // Same password as before; now it opens the Studio.
        Assert.True((await SignInAsync(legacyName, Password)).Succeeded);
    }

    [Fact]
    public async Task An_administrator_account_is_never_also_a_studio_account()
    {
        Guid adminId;
        await using (var setup = Services.Request())
        {
            await IdentityTestHost.EnsureRolesAsync(setup.Get<ApplicationDbContext>());
            var users = setup.Get<UserManager<ApplicationUser>>();
            var admin = new ApplicationUser { UserName = NewUsername() };
            Assert.True((await users.CreateAsync(admin, Password)).Succeeded);
            await users.AddToRolesAsync(admin, [Roles.Admin, Roles.ContentTeam]);
            adminId = admin.Id;
        }

        await using var scope = AsSuperAdmin();
        var adopted = await scope.Get<ITeamAdminService>().AdoptLegacyAccountAsync(adminId, new AdoptLegacyAccountRequest(
            "An Admin", null, null, StudioRole.Lead, true, null, true, null, "en"));

        Assert.Equal(ServiceErrorKind.Validation, adopted.ErrorKind);
    }

    [Fact]
    public async Task Nobody_can_delete_a_staff_account_not_an_admin_and_not_its_holder()
    {
        var member = await NewMemberAsync();
        await ActivateAsync(member);

        await using (var scope = AsSuperAdmin())
        {
            var deleted = await scope.Get<IUserAdminService>().DeleteUserAsync(member.UserId, SuperAdminId, actorIsSuperAdmin: true);
            Assert.Equal(ServiceErrorKind.Forbidden, deleted.ErrorKind);
        }

        await using (var scope = AsMember(member.UserId))
        {
            var own = await scope.Get<IAccountDeletionService>().DeleteOwnAccountAsync(member.UserId);
            Assert.False(own.Succeeded);
        }

        await using var check = Check();
        Assert.True(await check.StaffProfiles.AnyAsync(p => p.UserId == member.UserId));
    }

    // ------------------------------------------------------------------ security settings

    [Fact]
    public async Task Requiring_two_step_limits_members_without_it_to_setting_it_up()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);
        Assert.True((await CheckTokenAsync(session.AccessToken))!.FullAccess);

        StaffSecuritySettingsDto original;
        await using (var scope = AsSuperAdmin())
        {
            var team = scope.Get<ITeamAdminService>();
            original = await team.GetSecurityAsync();

            Assert.Equal(ServiceErrorKind.Validation, (await team.UpdateSecurityAsync(
                new UpdateStaffSecuritySettingsRequest(true, original.SessionLifetimeHours, original.IdleTimeoutHours, 8, original.SetupLinkLifetimeHours))).ErrorKind);

            var required = await team.UpdateSecurityAsync(new UpdateStaffSecuritySettingsRequest(
                true, original.SessionLifetimeHours, original.IdleTimeoutHours, original.MinimumPasswordLength, original.SetupLinkLifetimeHours));
            Assert.True(required.Succeeded);
            Assert.True(required.Value!.ActiveMembersWithoutTwoStep >= 1);
        }

        try
        {
            // A fresh session sees the rule immediately; an open one within the minute.
            var fresh = await SignInAsync(member.Username, Password);
            var state = await CheckTokenAsync(fresh.Value!.Session!.AccessToken);
            Assert.False(state!.FullAccess);

            await using var scope = AsMember(member.UserId);
            var me = await scope.Get<IStudioAccountService>().GetMeAsync(member.UserId, fresh.Value.Session.SessionId);
            Assert.True(me.Value!.TwoStep.SetupRequired);
        }
        finally
        {
            // The settings row is shared by every test on this database; put it back.
            await using var scope = AsSuperAdmin();
            await scope.Get<ITeamAdminService>().UpdateSecurityAsync(new UpdateStaffSecuritySettingsRequest(
                original.RequireTwoStep, original.SessionLifetimeHours, original.IdleTimeoutHours, original.MinimumPasswordLength, original.SetupLinkLifetimeHours));
        }
    }

    // ------------------------------------------------------------------ audit viewer

    [Fact]
    public async Task The_audit_viewer_filters_by_person_and_its_export_cannot_run_formulas()
    {
        var member = await NewMemberAsync();

        await using (var setup = Check())
        {
            setup.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = SuperAdminId,
                ActorRoles = Roles.SuperAdmin,
                Action = "test.formula",
                Area = "test",
                Summary = "=HYPERLINK(\"https://example.test\",\"click\")",
                TargetId = member.UserId.ToString()
            });
            await setup.SaveChangesAsync();
        }

        await using var scope = AsSuperAdmin();
        var audit = scope.Get<IAuditQueryService>();

        var mine = await audit.QueryAsync(new AuditQuery(ActorUserId: SuperAdminId));
        Assert.Equal(2, mine.Total);
        Assert.All(mine.Items, e => Assert.Equal(SuperAdminId, e.Actor!.UserId));
        Assert.Equal(PeopleDirectory.RemovedAccount, mine.Items[0].Actor!.Name);

        var created = await audit.QueryAsync(new AuditQuery(Action: AuditActions.TeamMemberCreated, TargetId: member.UserId.ToString()));
        Assert.Equal(1, created.Total);

        var csv = await audit.ExportCsvAsync(new AuditQuery(ActorUserId: SuperAdminId), 100);
        Assert.Contains("\"'=HYPERLINK(\"\"https://example.test\"\",\"\"click\"\")\"", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
    }
}
