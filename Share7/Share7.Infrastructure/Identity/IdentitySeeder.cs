using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Audit.Interfaces;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Identity;

/// <summary>An account described in configuration — the <c>SeedAdmin</c> and <c>SeedSuperAdmin</c> sections.</summary>
public sealed class SeedAccountOptions
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}

/// <summary>What the SuperAdmin bootstrap did on this startup.</summary>
public enum SuperAdminBootstrapOutcome
{
    /// <summary>No <c>SeedSuperAdmin</c> section, or it is empty. Nothing to do.</summary>
    NotConfigured,

    /// <summary>A SuperAdmin already exists, so the section was ignored. It can be removed.</summary>
    AlreadyExists,

    /// <summary>The account was created and given the SuperAdmin role.</summary>
    Created,

    /// <summary>The configuration was unsafe or collided with an existing account. Nothing was created.</summary>
    Refused
}

/// <summary>
/// The accounts the platform creates for itself on startup: the long-standing seed admin, and the
/// first SuperAdmin.
/// <para>
/// <b>Why this left Program.cs.</b> The seed admin used to fall back to <c>admin</c> /
/// <c>Admin123</c> whenever configuration was missing, on every environment, and nothing could test
/// that — the logic lived in the host's startup block. It is here so the two safety rules below are
/// enforced by code with tests rather than by remembering to set a config section.
/// </para>
/// </summary>
public class IdentitySeeder
{
    /// <summary>The password the seed admin historically fell back to. Refused in Production.</summary>
    public const string DefaultAdminPassword = "Admin123";

    /// <summary>
    /// The shortest SuperAdmin password the bootstrap accepts. The role that can create and remove
    /// every other privileged account gets a stronger floor than the platform-wide eight.
    /// </summary>
    public const int MinimumSuperAdminPasswordLength = 12;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLog _audit;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IAuditLog audit,
        ILogger<IdentitySeeder> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Makes sure the seed admin exists and holds the Admin role — unchanged behaviour, with one
    /// guard: <b>in Production it never creates an account with the default password.</b> A missing
    /// <c>SeedAdmin:Password</c> used to mint <c>admin</c> / <c>Admin123</c> on a public host.
    /// </summary>
    /// <returns>True when the admin exists after the call.</returns>
    public async Task<bool> SeedAdminAsync(
        SeedAccountOptions options, bool isProduction, CancellationToken cancellationToken = default)
    {
        var username = string.IsNullOrWhiteSpace(options.Username) ? "admin" : options.Username.Trim();
        var email = string.IsNullOrWhiteSpace(options.Email) ? "admin@admin.com" : options.Email.Trim();
        var passwordIsDefault = string.IsNullOrEmpty(options.Password) || options.Password == DefaultAdminPassword;
        var password = string.IsNullOrEmpty(options.Password) ? DefaultAdminPassword : options.Password;

        var admin = await _userManager.FindByNameAsync(username);

        if (admin is null)
        {
            // An install from before usernames, where the admin signed in by email.
            var legacy = await _userManager.FindByNameAsync(email);
            if (legacy is not null)
            {
                await _userManager.SetUserNameAsync(legacy, username);
                admin = legacy;
            }
        }

        if (admin is null)
        {
            if (isProduction && passwordIsDefault)
            {
                _logger.LogCritical(
                    "The seed admin '{Username}' was NOT created: SeedAdmin:Password is missing or is the " +
                    "built-in default, which is refused in Production. Set a strong SeedAdmin:Password.",
                    username);
                return false;
            }

            admin = new ApplicationUser
            {
                UserName = username,
                Email = email,
                EmailConfirmed = true,
                PreferredLanguageId = LanguageIds.English
            };

            var created = await _userManager.CreateAsync(admin, password);
            if (!created.Succeeded)
            {
                _logger.LogError(
                    "The seed admin '{Username}' could not be created: {Errors}",
                    username, string.Join("; ", created.Errors.Select(e => e.Description)));
                return false;
            }

            await _userManager.AddToRoleAsync(admin, Roles.Admin);
            return true;
        }

        if (admin.PreferredLanguageId is null)
        {
            admin.PreferredLanguageId = LanguageIds.English;
            await _userManager.UpdateAsync(admin);
        }

        if (!await _userManager.IsInRoleAsync(admin, Roles.Admin))
            await _userManager.AddToRoleAsync(admin, Roles.Admin);

        // An account created before this guard existed may still be using the default. Nothing is
        // changed automatically — locking the only admin out of a live platform is its own outage —
        // but it is said loudly, on every start, until somebody fixes it.
        if (isProduction && await _userManager.CheckPasswordAsync(admin, DefaultAdminPassword))
        {
            _logger.LogCritical(
                "The seed admin '{Username}' still has the built-in default password. Change it now: " +
                "anyone who has read this codebase can sign in as an administrator.",
                username);
        }

        return true;
    }

    /// <summary>
    /// Creates the platform's first SuperAdmin from configuration, once.
    /// <para>
    /// Only a SuperAdmin can create another privileged account, so the first one has to come from
    /// outside the API. The section is honoured only while <b>no</b> SuperAdmin exists; after that it
    /// is ignored on every start, so leaving it in configuration cannot mint a second one, and
    /// removing a SuperAdmin through the API cannot be undone by a restart.
    /// </para>
    /// <para>
    /// It creates a <b>new</b> account and refuses a username that is taken: promoting an existing
    /// account by editing a config file would be the least auditable elevation possible.
    /// </para>
    /// </summary>
    public async Task<SuperAdminBootstrapOutcome> BootstrapSuperAdminAsync(
        SeedAccountOptions? options, CancellationToken cancellationToken = default)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrEmpty(options.Password))
            return SuperAdminBootstrapOutcome.NotConfigured;

        if ((await _userManager.GetUsersInRoleAsync(Roles.SuperAdmin)).Count > 0)
        {
            _logger.LogInformation(
                "SeedSuperAdmin is set but a SuperAdmin already exists, so it was ignored. It is safe to remove the section.");
            return SuperAdminBootstrapOutcome.AlreadyExists;
        }

        var username = options.Username.Trim();

        if (options.Password.Length < MinimumSuperAdminPasswordLength || options.Password == DefaultAdminPassword)
        {
            _logger.LogError(
                "SeedSuperAdmin was refused: the password must be at least {Length} characters and not a default.",
                MinimumSuperAdminPasswordLength);
            return SuperAdminBootstrapOutcome.Refused;
        }

        if (await _userManager.FindByNameAsync(username) is not null)
        {
            _logger.LogError(
                "SeedSuperAdmin was refused: the username '{Username}' is already taken. The bootstrap only " +
                "creates a new account; choose another username.",
                username);
            return SuperAdminBootstrapOutcome.Refused;
        }

        var superAdmin = new ApplicationUser
        {
            UserName = username,
            Email = string.IsNullOrWhiteSpace(options.Email) ? null : options.Email.Trim(),
            EmailConfirmed = !string.IsNullOrWhiteSpace(options.Email),
            PreferredLanguageId = LanguageIds.English
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var created = await _userManager.CreateAsync(superAdmin, options.Password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                "SeedSuperAdmin could not be created: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return SuperAdminBootstrapOutcome.Refused;
        }

        var granted = await _userManager.AddToRoleAsync(superAdmin, Roles.SuperAdmin);
        if (!granted.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                "SeedSuperAdmin could not be given the role: {Errors}",
                string.Join("; ", granted.Errors.Select(e => e.Description)));
            return SuperAdminBootstrapOutcome.Refused;
        }

        _audit.Record(new AuditEntry(
            AuditActions.SuperAdminBootstrapped,
            AuditAreas.Security,
            "Created the platform's first SuperAdmin from server configuration.",
            "account",
            superAdmin.Id.ToString()));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogWarning(
            "Created the first SuperAdmin '{Username}' from SeedSuperAdmin. Remove the password from configuration now.",
            username);

        return SuperAdminBootstrapOutcome.Created;
    }
}
