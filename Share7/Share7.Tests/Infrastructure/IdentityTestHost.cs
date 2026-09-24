using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Share7.Domain.Constants;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// A real <see cref="UserManager{TUser}"/> over the test database.
/// <para>
/// Deliberately the genuine article rather than a mock: deletion goes through
/// <c>UserManager.DeleteAsync</c>, which is what triggers the Identity-side cascades
/// (roles, claims, logins, tokens). A mock would report success without ever exercising them.
/// </para>
/// </summary>
public static class IdentityTestHost
{
    public static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context) =>
        BuildProvider(context).GetRequiredService<UserManager<ApplicationUser>>();

    /// <summary>
    /// Creates whichever of <see cref="Roles.All"/> the test database lacks — what
    /// <c>Program.cs</c> does on every startup. The fixture builds the schema from migrations
    /// alone, so without this <c>AddToRoleAsync</c> throws on a role that does not exist.
    /// </summary>
    public static async Task EnsureRolesAsync(ApplicationDbContext context)
    {
        var roleManager = BuildProvider(context).GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new ApplicationRole(role));
        }
    }

    private static ServiceProvider BuildProvider(ApplicationDbContext context)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(context);
        services.AddScoped<DbContext>(_ => context);

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        return services.BuildServiceProvider();
    }
}
