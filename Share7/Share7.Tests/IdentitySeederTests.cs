using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The seed admin can no longer be created with the built-in default password in Production.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SeedAdminTests
{
    private readonly SqlServerFixture _fixture;

    public SeedAdminTests(SqlServerFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(null)]
    [InlineData(IdentitySeeder.DefaultAdminPassword)]
    public async Task Production_never_creates_the_admin_with_the_default_password(string? configured)
    {
        await using var context = _fixture.CreateContext();
        var logs = new ListLogger<IdentitySeeder>();
        var username = $"admin_{Guid.NewGuid():N}"[..20];

        var seeded = await (await SeederAsync(context, logs)).SeedAdminAsync(
            new SeedAccountOptions { Username = username, Password = configured }, isProduction: true);

        Assert.False(seeded);
        Assert.False(await context.Users.AnyAsync(u => u.UserName == username));
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task Production_creates_the_admin_with_a_configured_password()
    {
        await using var context = _fixture.CreateContext();
        var username = $"admin_{Guid.NewGuid():N}"[..20];
        var seeder = await SeederAsync(context);

        Assert.True(await seeder.SeedAdminAsync(
            new SeedAccountOptions { Username = username, Password = "Long-Unique#Pass-2026" }, isProduction: true));

        var admin = await context.Users.SingleAsync(u => u.UserName == username);
        Assert.True(await IdentityTestHost.CreateUserManager(context).IsInRoleAsync(admin, Roles.Admin));
    }

    [Fact]
    public async Task An_existing_admin_still_on_the_default_password_is_reported_every_start()
    {
        await using var context = _fixture.CreateContext();
        var username = $"admin_{Guid.NewGuid():N}"[..20];

        // An account created before the guard existed. The test user manager enforces the stock
        // Identity rules, which the old default does not meet, so the hash is set directly.
        var users = IdentityTestHost.CreateUserManager(context);
        var legacy = new ApplicationUser { UserName = username };
        legacy.PasswordHash = users.PasswordHasher.HashPassword(legacy, IdentitySeeder.DefaultAdminPassword);
        await users.CreateAsync(legacy);

        var logs = new ListLogger<IdentitySeeder>();
        var seeded = await (await SeederAsync(context, logs)).SeedAdminAsync(
            new SeedAccountOptions { Username = username, Password = "Long-Unique#Pass-2026" }, isProduction: true);

        // Kept working — locking the only admin out of a live platform is its own outage — and said
        // loudly.
        Assert.True(seeded);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("default password"));
    }

    private static async Task<IdentitySeeder> SeederAsync(ApplicationDbContext context, ListLogger<IdentitySeeder>? logs = null)
    {
        await IdentityTestHost.EnsureRolesAsync(context);
        return new IdentitySeeder(
            IdentityTestHost.CreateUserManager(context), context, TestAudit.For(context),
            logs ?? new ListLogger<IdentitySeeder>());
    }
}

/// <summary>
/// The first SuperAdmin comes from configuration, once. Its own database, because "no SuperAdmin
/// exists yet" is the precondition, and the shared test database has SuperAdmins other tests made.
/// </summary>
[CollectionDefinition(Name)]
public class BootstrapCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "superadmin-bootstrap";
}

[Collection(BootstrapCollection.Name)]
public class SuperAdminBootstrapTests
{
    private const string StrongPassword = "Bootstrap#Only-2026";

    private readonly SqlServerFixture _fixture;

    public SuperAdminBootstrapTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>
    /// One scenario rather than several facts: every step depends on whether a SuperAdmin exists,
    /// which is database-wide state, and splitting it would make the outcome depend on test order.
    /// </summary>
    [Fact]
    public async Task The_first_SuperAdmin_is_created_once_and_never_again()
    {
        await using var context = _fixture.CreateContext();
        await IdentityTestHost.EnsureRolesAsync(context);
        var seeder = Seeder(context);

        // Nothing configured: nothing happens.
        Assert.Equal(SuperAdminBootstrapOutcome.NotConfigured, await seeder.BootstrapSuperAdminAsync(null));

        // Too weak for the role that can mint every other privileged account.
        Assert.Equal(SuperAdminBootstrapOutcome.Refused, await seeder.BootstrapSuperAdminAsync(
            new SeedAccountOptions { Username = "root", Password = "Short#1a" }));

        // A taken username is refused rather than promoted — elevating an existing account by
        // editing a config file would be the least auditable promotion there is.
        var users = IdentityTestHost.CreateUserManager(context);
        await users.CreateAsync(new ApplicationUser { UserName = "existing_user" }, StrongPassword);
        Assert.Equal(SuperAdminBootstrapOutcome.Refused, await seeder.BootstrapSuperAdminAsync(
            new SeedAccountOptions { Username = "existing_user", Password = StrongPassword }));
        Assert.False(await users.IsInRoleAsync((await users.FindByNameAsync("existing_user"))!, Roles.SuperAdmin));

        Assert.Empty(await users.GetUsersInRoleAsync(Roles.SuperAdmin));

        // A valid section creates exactly one, and says so in the audit trail.
        Assert.Equal(SuperAdminBootstrapOutcome.Created, await seeder.BootstrapSuperAdminAsync(
            new SeedAccountOptions { Username = "platform_owner", Password = StrongPassword }));

        var superAdmins = await users.GetUsersInRoleAsync(Roles.SuperAdmin);
        var owner = Assert.Single(superAdmins);
        Assert.Equal("platform_owner", owner.UserName);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.AuditEvents.AnyAsync(e =>
            e.Action == AuditActions.SuperAdminBootstrapped && e.TargetId == owner.Id.ToString()));

        // Leaving the section in configuration cannot mint a second one on the next start.
        Assert.Equal(SuperAdminBootstrapOutcome.AlreadyExists, await Seeder(context).BootstrapSuperAdminAsync(
            new SeedAccountOptions { Username = "second_owner", Password = StrongPassword }));
        Assert.Single(await users.GetUsersInRoleAsync(Roles.SuperAdmin));
        Assert.Null(await users.FindByNameAsync("second_owner"));
    }

    private static IdentitySeeder Seeder(ApplicationDbContext context) =>
        new(IdentityTestHost.CreateUserManager(context), context, TestAudit.For(context), new ListLogger<IdentitySeeder>());
}

/// <summary>Captures log entries so a test can assert that something was said, and how loudly.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
