using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Auth.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Identity.ExternalAuth;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Signing in with Google or Facebook must not hand anybody an account they did not create.
/// <para>
/// Linking used to follow the email address alone, so anyone able to present a provider token
/// asserting an address inherited the account holding it — roles included. These pin the two
/// rules that close it, and the ordinary student paths that must keep working.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ExternalLoginLinkingTests
{
    private readonly SqlServerFixture _fixture;

    public ExternalLoginLinkingTests(SqlServerFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.ContentTeam)]
    public async Task A_staff_or_privileged_account_is_never_linked_by_email(string role)
    {
        await using var context = _fixture.CreateContext();
        var email = NewEmail();
        var staff = await CreateAccountAsync(context, email, role);

        var result = await Service(context, new FakeValidator(email, emailVerified: true)).ExternalLoginAsync(Request(), null);

        Assert.False(result.Succeeded);
        Assert.Empty(await IdentityTestHost.CreateUserManager(context).GetLoginsAsync(staff));
    }

    [Fact]
    public async Task An_address_the_provider_says_is_unverified_cannot_take_over_an_account()
    {
        await using var context = _fixture.CreateContext();
        var email = NewEmail();
        var student = await CreateAccountAsync(context, email, Roles.Student);

        var result = await Service(context, new FakeValidator(email, emailVerified: false)).ExternalLoginAsync(Request(), null);

        Assert.False(result.Succeeded);
        Assert.Empty(await IdentityTestHost.CreateUserManager(context).GetLoginsAsync(student));
    }

    [Fact]
    public async Task A_student_with_a_verified_address_is_linked_as_before()
    {
        await using var context = _fixture.CreateContext();
        var email = NewEmail();
        var student = await CreateAccountAsync(context, email, Roles.Student);

        var result = await Service(context, new FakeValidator(email, emailVerified: true)).ExternalLoginAsync(Request(), null);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(student.Id, result.UserId);
        Assert.Single(await IdentityTestHost.CreateUserManager(context).GetLoginsAsync(student));
    }

    [Fact]
    public async Task A_provider_that_does_not_report_verification_keeps_todays_behaviour()
    {
        // Facebook's case: no flag either way. Unchanged for now — see the open question in the plan.
        await using var context = _fixture.CreateContext();
        var email = NewEmail();
        var student = await CreateAccountAsync(context, email, Roles.Student);

        var result = await Service(context, new FakeValidator(email, emailVerified: null)).ExternalLoginAsync(Request(), null);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(student.Id, result.UserId);
    }

    [Fact]
    public async Task A_new_address_still_creates_a_student_account()
    {
        await using var context = _fixture.CreateContext();
        var email = NewEmail();

        var result = await Service(context, new FakeValidator(email, emailVerified: false)).ExternalLoginAsync(Request(), null);

        // Nothing to take over, so nothing to refuse: the account is new and is the caller's own.
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(Roles.Student, result.Roles);
    }

    // ------------------------------------------------------------- helpers

    private static string NewEmail() => $"link_{Guid.NewGuid():N}@example.com";

    private static ExternalLoginRequest Request() => new() { Provider = FakeValidator.Name, Token = "token" };

    private static async Task<ApplicationUser> CreateAccountAsync(ApplicationDbContext context, string email, string role)
    {
        await IdentityTestHost.EnsureRolesAsync(context);
        var users = IdentityTestHost.CreateUserManager(context);

        var user = new ApplicationUser { UserName = $"u_{Guid.NewGuid():N}"[..20], Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, "Linking#Test-2026")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static AuthService Service(ApplicationDbContext context, IExternalLoginValidator validator)
    {
        var jwt = Options.Create(new JwtSettings
        {
            Secret = new string('k', 64),
            Issuer = "Share7.Api",
            Audience = "Share7.Client",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        return new AuthService(
            IdentityTestHost.CreateUserManager(context),
            context,
            new JwtTokenGenerator(jwt),
            jwt,
            // No public address: these tests are about external linking, and the only thing the
            // Studio's address changes is the wording of a refusal none of them reaches.
            Options.Create(new StudioOptions()),
            [validator]);
    }

    /// <summary>A provider that vouches for one address, with whatever verification it is told to claim.</summary>
    private sealed class FakeValidator(string email, bool? emailVerified) : IExternalLoginValidator
    {
        public const string Name = "TestProvider";

        private readonly string _subject = Guid.NewGuid().ToString("N");

        public string Provider => Name;

        public Task<ExternalUserInfo?> ValidateAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalUserInfo?>(new ExternalUserInfo(_subject, email, "Test Person", emailVerified));
    }
}
