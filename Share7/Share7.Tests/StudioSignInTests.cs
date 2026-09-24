using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Auth.Models;
using Share7.Application.Staff;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Staff;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The member's side of Studio sign-in: 2-step with its replay and recovery rules, and the
/// rotating refresh session with its theft detection.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class StudioSignInTests : StaffTestBase
{
    public StudioSignInTests(SqlServerFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------------ 2-step

    [Fact]
    public async Task Two_step_is_proved_before_it_is_on_and_then_asked_at_every_sign_in()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);

        string key;
        string confirmCode;
        StudioTwoStepEnabledDto enabled;

        await using (var scope = AsMember(member.UserId))
        {
            var account = scope.Get<IStudioAccountService>();

            var begun = await account.BeginTwoStepAsync(member.UserId);
            Assert.True(begun.Succeeded);
            Assert.StartsWith("otpauth://totp/Share7%20Studio%3A", begun.Value!.AuthenticatorUri);
            Assert.Contains("issuer=Share7%20Studio", begun.Value.AuthenticatorUri);
            key = begun.Value.SharedKey;

            // Starting setup changes nothing yet, and does not sign this page out.
            Assert.NotNull(await CheckTokenAsync(session.AccessToken));

            var wrong = await account.ConfirmTwoStepAsync(member.UserId, session.SessionId, "000000");
            Assert.Equal(StudioErrors.TwoStepInvalid, wrong.Error);

            confirmCode = StaffTestHost.Totp(key);
            var confirmed = await account.ConfirmTwoStepAsync(member.UserId, session.SessionId, confirmCode);
            Assert.True(confirmed.Succeeded, confirmed.Error?.Code);
            enabled = confirmed.Value!;
        }

        Assert.Equal(10, enabled.RecoveryCodes.Codes.Count);

        // This device carries on with its re-keyed session; its old token does not.
        Assert.Null(await CheckTokenAsync(session.AccessToken));
        Assert.NotNull(await CheckTokenAsync(enabled.Session.AccessToken));

        var password = await SignInAsync(member.Username, Password);
        Assert.Null(password.Value!.Session);
        var challenge = password.Value.TwoStepChallenge!;

        await using (var scope = Anonymous())
        {
            var auth = scope.Get<IStudioAuthService>();

            // The code that switched 2-step on cannot be replayed to sign in.
            var replay = await auth.CompleteTwoStepAsync(new StudioTwoStepRequest(challenge, confirmCode, null), Browser);
            Assert.Equal(StudioErrors.TwoStepInvalid, replay.Error);

            var next = await auth.CompleteTwoStepAsync(new StudioTwoStepRequest(challenge, StaffTestHost.Totp(key, stepOffset: 1), null), Browser);
            Assert.True(next.Succeeded, next.Error?.Code);
            Assert.NotNull(next.Value!.Session);

            var forged = await auth.CompleteTwoStepAsync(new StudioTwoStepRequest("not-a-challenge", StaffTestHost.Totp(key, 2), null), Browser);
            Assert.Equal(StudioErrors.ChallengeExpired, forged.Error);
        }

        await using var check = Check();
        Assert.True(await check.StaffSessions.Where(s => s.UserId == member.UserId && s.RevokedAtUtc == null).AllAsync(s => s.TwoStepVerified));
        Assert.True(await check.AuditEvents.AnyAsync(e => e.Action == AuditActions.StudioTwoStepEnabled && e.ActorUserId == member.UserId));
    }

    [Fact]
    public async Task A_recovery_code_works_once_however_it_is_typed()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);
        IReadOnlyList<string> codes;

        await using (var scope = AsMember(member.UserId))
        {
            var account = scope.Get<IStudioAccountService>();
            var key = (await account.BeginTwoStepAsync(member.UserId)).Value!.SharedKey;
            codes = (await account.ConfirmTwoStepAsync(member.UserId, session.SessionId, StaffTestHost.Totp(key))).Value!.RecoveryCodes.Codes;
        }

        // "ABCDE-FGHJK" typed as "abcde fghjk".
        var typed = codes[0].Replace("-", " ").ToLowerInvariant();

        var first = await SignInAsync(member.Username, Password);
        await using (var scope = Anonymous())
        {
            var used = await scope.Get<IStudioAuthService>().CompleteTwoStepAsync(new StudioTwoStepRequest(first.Value!.TwoStepChallenge!, null, typed), Browser);
            Assert.True(used.Succeeded, used.Error?.Code);
        }

        var second = await SignInAsync(member.Username, Password);
        await using (var scope = Anonymous())
        {
            var again = await scope.Get<IStudioAuthService>().CompleteTwoStepAsync(new StudioTwoStepRequest(second.Value!.TwoStepChallenge!, null, typed), Browser);
            Assert.Equal(StudioErrors.TwoStepInvalid, again.Error);
        }

        await using var check = Check();
        var audit = await check.AuditEvents.SingleAsync(e => e.Action == AuditActions.StudioRecoveryCodeUsed && e.ActorUserId == member.UserId);
        Assert.Contains("\"recoveryCodesLeft\":9", audit.DataJson);
    }

    [Fact]
    public async Task Two_step_cannot_be_turned_off_while_super_admins_require_it()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);

        StudioSessionTokens current;
        await using (var scope = AsMember(member.UserId))
        {
            var account = scope.Get<IStudioAccountService>();
            var key = (await account.BeginTwoStepAsync(member.UserId)).Value!.SharedKey;
            current = (await account.ConfirmTwoStepAsync(member.UserId, session.SessionId, StaffTestHost.Totp(key))).Value!.Session;
        }

        StaffSecuritySettingsDto original;
        await using (var scope = AsSuperAdmin())
        {
            var team = scope.Get<ITeamAdminService>();
            original = await team.GetSecurityAsync();
            await team.UpdateSecurityAsync(new UpdateStaffSecuritySettingsRequest(
                true, original.SessionLifetimeHours, original.IdleTimeoutHours, original.MinimumPasswordLength, original.SetupLinkLifetimeHours));
        }

        try
        {
            await using var scope = AsMember(member.UserId);
            var account = scope.Get<IStudioAccountService>();

            Assert.Equal(StudioErrors.TwoStepCannotDisable, (await account.DisableTwoStepAsync(member.UserId, current.SessionId, Password)).Error);
        }
        finally
        {
            await using var scope = AsSuperAdmin();
            await scope.Get<ITeamAdminService>().UpdateSecurityAsync(new UpdateStaffSecuritySettingsRequest(
                original.RequireTwoStep, original.SessionLifetimeHours, original.IdleTimeoutHours, original.MinimumPasswordLength, original.SetupLinkLifetimeHours));
        }

        await using (var scope = AsMember(member.UserId))
        {
            var account = scope.Get<IStudioAccountService>();
            Assert.Equal(StudioErrors.WrongPassword, (await account.DisableTwoStepAsync(member.UserId, current.SessionId, OtherPassword)).Error);

            var off = await account.DisableTwoStepAsync(member.UserId, current.SessionId, Password);
            Assert.True(off.Succeeded, off.Error?.Code);
        }

        var plain = await SignInAsync(member.Username, Password);
        Assert.NotNull(plain.Value!.Session);
    }

    // ------------------------------------------------------------------ sessions

    [Fact]
    public async Task Refreshing_rotates_the_token_and_an_old_one_turning_up_later_ends_the_session()
    {
        var member = await NewMemberAsync();
        var first = await ActivateAsync(member);

        StudioSessionTokens second;
        await using (var scope = Anonymous())
        {
            var auth = scope.Get<IStudioAuthService>();

            var rotated = await auth.RefreshAsync(first.RefreshToken, Browser);
            Assert.True(rotated.Succeeded, rotated.Error?.Code);
            second = rotated.Value!;
            Assert.NotEqual(first.RefreshToken, second.RefreshToken);
            Assert.Equal(first.SessionId, second.SessionId);

            // A second tab that raced the first, moments later: told to retry, nothing ends.
            Assert.Equal(StudioErrors.SessionSuperseded, (await auth.RefreshAsync(first.RefreshToken, Browser)).Error);
        }

        // The same old token, well after the rotation: it was copied. The session ends for everyone.
        await using (var setup = Check())
        {
            await setup.StaffSessions.Where(s => s.Id == first.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.RotatedAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        }

        await using (var scope = Anonymous())
        {
            var auth = scope.Get<IStudioAuthService>();
            Assert.Equal(StudioErrors.SessionInvalid, (await auth.RefreshAsync(first.RefreshToken, Browser)).Error);
            Assert.Equal(StudioErrors.SessionInvalid, (await auth.RefreshAsync(second.RefreshToken, Browser)).Error);
        }

        await using var check = Check();
        Assert.Equal(StaffSessionEndReasons.RefreshTokenReused, (await check.StaffSessions.SingleAsync(s => s.Id == first.SessionId)).RevokedReason);
        Assert.True(await check.AuditEvents.AnyAsync(e => e.Action == AuditActions.StudioRefreshTokenReused && e.TargetId == first.SessionId.ToString()));
    }

    [Fact]
    public async Task A_session_left_idle_too_long_ends()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);

        await using (var setup = Check())
        {
            await setup.StaffSessions.Where(s => s.Id == session.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSeenAtUtc, DateTime.UtcNow.AddHours(-13)));
        }

        await using var scope = Anonymous();
        Assert.Equal(StudioErrors.SessionInvalid, (await scope.Get<IStudioAuthService>().RefreshAsync(session.RefreshToken, Browser)).Error);
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_device_and_signs_out_every_other()
    {
        var member = await NewMemberAsync();
        var here = await ActivateAsync(member);
        var elsewhere = (await SignInAsync(member.Username, Password)).Value!.Session!;

        StudioSessionTokens renewed;
        await using (var scope = AsMember(member.UserId))
        {
            var account = scope.Get<IStudioAccountService>();

            var same = await account.ChangePasswordAsync(member.UserId, here.SessionId, new StudioChangePasswordRequest(Password, Password), Browser);
            Assert.Contains("sameAsCurrent", (IEnumerable<string>)same.Details!["problems"]!);

            var changed = await account.ChangePasswordAsync(member.UserId, here.SessionId, new StudioChangePasswordRequest(Password, OtherPassword), Browser);
            Assert.True(changed.Succeeded, changed.Error?.Code);
            renewed = changed.Value!;
        }

        Assert.NotNull(await CheckTokenAsync(renewed.AccessToken));
        Assert.Null(await CheckTokenAsync(elsewhere.AccessToken));

        await using (var scope = Anonymous())
        {
            var auth = scope.Get<IStudioAuthService>();
            Assert.True((await auth.RefreshAsync(renewed.RefreshToken, Browser)).Succeeded);
            Assert.Equal(StudioErrors.SessionInvalid, (await auth.RefreshAsync(elsewhere.RefreshToken, Browser)).Error);
        }

        Assert.Equal(StudioErrors.SignInFailed, (await SignInAsync(member.Username, Password)).Error);
        Assert.True((await SignInAsync(member.Username, OtherPassword)).Succeeded);
    }

    [Fact]
    public async Task Signing_out_ends_this_session_even_with_an_expired_access_token()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);

        await using (var scope = Anonymous())
            await scope.Get<IStudioAuthService>().SignOutAsync(session.RefreshToken);

        Assert.Null(await CheckTokenAsync(session.AccessToken));

        await using (var scope = Anonymous())
            Assert.Equal(StudioErrors.SessionInvalid, (await scope.Get<IStudioAuthService>().RefreshAsync(session.RefreshToken, Browser)).Error);
    }

    [Fact]
    public async Task A_studio_token_is_the_only_token_a_member_can_hold()
    {
        var member = await NewMemberAsync();
        var session = await ActivateAsync(member);

        var studio = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        Assert.Equal(["Share7.Studio"], studio.Audiences);

        // No role claims at all: the Studio role is looked up per request, never carried.
        Assert.DoesNotContain(studio.Claims, c => c.Type.Contains("role", StringComparison.OrdinalIgnoreCase));

        // Cutover (plan P6) closed the other door. Until then this member could sign in on the game
        // side with the same password and be handed a Share7.Client token, and the two were proved
        // separate by comparing them; now there is nothing to compare, which is the stronger claim.
        // Approved default #2 said a staff account is staff-only, and the sign-in finally says so.
        await using var scope = Anonymous();
        var game = await scope.Get<IAuthService>().LoginAsync(
            new LoginRequest { Username = member.Username, Password = Password }, null);

        Assert.False(game.Succeeded);
        Assert.Null(game.AccessToken);
        Assert.Contains("Content Studio", string.Join(" ", game.Errors));
    }
}

/// <summary>
/// Admin and SuperAdmin tokens on the main API are re-checked against the account (H2): a deleted,
/// demoted or re-stamped administrator's token stops working within the minute. Student tokens are
/// untouched — the game contract depends on it.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PrivilegedTokenTests : StaffTestBase
{
    public PrivilegedTokenTests(SqlServerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_admin_token_carries_a_stamp_fingerprint_and_a_student_token_does_not()
    {
        var (adminName, _) = await NewAccountAsync(Roles.Admin);
        var (studentName, _) = await NewAccountAsync(Roles.Student);

        await using var scope = Anonymous();
        var auth = scope.Get<IAuthService>();

        var admin = new JwtSecurityTokenHandler().ReadJwtToken((await auth.LoginAsync(new LoginRequest { Username = adminName, Password = Password }, null)).AccessToken);
        var student = new JwtSecurityTokenHandler().ReadJwtToken((await auth.LoginAsync(new LoginRequest { Username = studentName, Password = Password }, null)).AccessToken);

        Assert.Contains(admin.Claims, c => c.Type == StaffSecrets.StampClaim);
        Assert.DoesNotContain(student.Claims, c => c.Type == StaffSecrets.StampClaim);
    }

    [Fact]
    public async Task An_admin_token_stops_working_when_the_stamp_moves_or_the_role_goes()
    {
        var (_, adminId) = await NewAccountAsync(Roles.Admin);

        string stamp;
        await using (var check = Check())
            stamp = StaffSecrets.StampHash((await check.Users.SingleAsync(u => u.Id == adminId)).SecurityStamp);

        await using (var scope = Anonymous())
        {
            var validator = scope.Get<IPrivilegedTokenValidator>();
            Assert.True(await validator.IsStillValidAsync(adminId, stamp, [Roles.Admin]));
            Assert.False(await validator.IsStillValidAsync(adminId, null, [Roles.Admin]));
            Assert.False(await validator.IsStillValidAsync(adminId, stamp, [Roles.SuperAdmin]));
        }

        await using (var scope = Anonymous())
        {
            var users = scope.Get<UserManager<ApplicationUser>>();
            await users.RemoveFromRoleAsync((await users.FindByIdAsync(adminId.ToString()))!, Roles.Admin);

            var validator = scope.Get<IPrivilegedTokenValidator>();
            validator.Forget(adminId);
            Assert.False(await validator.IsStillValidAsync(adminId, stamp, [Roles.Admin]));
        }

        await using (var scope = Anonymous())
        {
            var validator = scope.Get<IPrivilegedTokenValidator>();
            Assert.False(await validator.IsStillValidAsync(Guid.NewGuid(), stamp, [Roles.Admin]));
        }
    }

    private async Task<(string Username, Guid Id)> NewAccountAsync(string role)
    {
        await using var scope = Services.Request();
        await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

        var users = scope.Get<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = NewUsername() };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        await users.AddToRoleAsync(user, role);

        return (user.UserName!, user.Id);
    }
}

/// <summary>The staff password rules, one broken rule at a time.</summary>
public class StaffPasswordPolicyTests
{
    [Theory]
    [InlineData("Blue-Harbour-Lantern-7", "mona.adel")]
    [InlineData("Correct Horse Battery 9", "mona.adel")]
    public void A_long_unguessable_password_passes(string password, string username) =>
        Assert.Empty(StaffPasswordPolicy.Problems(password, username, 12));

    [Theory]
    [InlineData("Short1a", "tooShort")]
    [InlineData("alllowercase123", "needsUppercase")]
    [InlineData("ALLUPPERCASE123", "needsLowercase")]
    [InlineData("NoDigitsAnywhere", "needsDigit")]
    [InlineData("Password2026!", "common")]
    [InlineData("!!Summer2026!!", "common")]
    [InlineData("Share7-Studio-2026", "common")]
    [InlineData("Aaaaaaaaaaaa1", "common")]
    [InlineData("Qwertyuiop123", "common")]
    [InlineData("Mona.Adel-Writes-1", "containsUsername")]
    public void Each_broken_rule_is_named(string password, string expected) =>
        Assert.Contains(expected, StaffPasswordPolicy.Problems(password, "mona.adel", 12));

    [Fact]
    public void A_super_admin_can_raise_the_minimum() =>
        Assert.Contains("tooShort", StaffPasswordPolicy.Problems("Blue-Harbour-Lantern-7", "someone", 24));
}
