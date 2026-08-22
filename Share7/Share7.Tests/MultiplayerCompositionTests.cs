using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Infrastructure;
using Share7.Infrastructure.Multiplayer;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Proves the container can actually build what the controllers ask for.
/// <para>
/// **The specific hazard is the sweeper.** A <c>BackgroundService</c> is a singleton, so taking a
/// scoped dependency — a DbContext, or the sweep service itself — creates a captive dependency: it
/// resolves once at startup, holds one context for the life of the process, and degrades in ways
/// that never show up in a service-level test. That is why it takes an
/// <c>IServiceScopeFactory</c> and opens a scope per pass, and why this test resolves it with scope
/// validation switched on.
/// </para>
/// </summary>
public class MultiplayerCompositionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=.;Initial Catalog=unused;Integrated Security=SSPI;",
                ["JwtSettings:Secret"] = new string('k', 64),
                ["JwtSettings:Issuer"] = "Share7.Api",
                ["JwtSettings:Audience"] = "Share7.Client",
                ["Multiplayer:HeartbeatIntervalSeconds"] = "20",
                ["Multiplayer:AcceptedProtocolVersions:0"] = "1",
                ["Multiplayer:AcceptedProtocolVersions:1"] = "2"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        // Scope validation is the point: it turns a captive dependency from a silent runtime problem
        // into a failure right here.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void Every_multiplayer_service_resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMultiplayerSessionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMatchmakingService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMultiplayerSweepService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMultiplayerAdminService>());
    }

    [Fact]
    public void Matchmaking_shares_one_session_service_with_the_interface()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var byInterface = scope.ServiceProvider.GetRequiredService<IMultiplayerSessionService>();
        var byType = scope.ServiceProvider.GetRequiredService<MultiplayerSessionService>();

        // Registered concretely and forwarded, so matchmaking's seating path and a direct join run
        // through the same instance — and, more importantly, the same DbContext and transaction.
        Assert.Same(byType, byInterface);
    }

    [Fact]
    public void The_sweeper_is_registered_as_a_hosted_service_and_holds_nothing_scoped()
    {
        using var provider = BuildProvider();

        // Resolved from the root provider, exactly as the host does at startup. With ValidateScopes
        // on, this throws if it has taken a scoped dependency.
        var hosted = provider.GetServices<IHostedService>().ToList();

        Assert.Single(hosted, service => service is MultiplayerSessionSweeper);
    }

    [Fact]
    public void Configuration_binds_including_the_accepted_version_list()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MultiplayerOptions>>().Value;

        Assert.Equal(20, options.HeartbeatIntervalSeconds);

        // The array form is what makes a staged rollout an ops change rather than a deploy, so it
        // binding correctly is worth asserting rather than assuming.
        Assert.Equal([1, 2], options.AcceptedProtocolVersions);

        // Untouched keys keep their defaults rather than becoming zero.
        Assert.Equal(60, options.SessionTimeoutSeconds);
    }
}
