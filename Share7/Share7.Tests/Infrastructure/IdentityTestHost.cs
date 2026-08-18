using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(context);
        services.AddScoped<DbContext>(_ => context);

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        return services.BuildServiceProvider().GetRequiredService<UserManager<ApplicationUser>>();
    }
}
