using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Share7.Application.Audit.Interfaces;
using Share7.Infrastructure;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// The real service container — <c>AddInfrastructure</c>, exactly as the API builds it — over the
/// test database. Team &amp; Access and Studio sign-in lean on Identity's token providers, the
/// memory cache and a dozen collaborators; wiring them by hand would test a container that does
/// not exist in production.
/// </summary>
public static class StaffTestHost
{
    public const string StudioUrl = "https://studio.test";

    public static ServiceProvider Build(SqlServerFixture fixture) => Build(fixture.ConnectionString);

    /// <summary>The same container over any database — the API smoke tests point it at the API process's own.</summary>
    public static ServiceProvider Build(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["JwtSettings:Secret"] = "staff-tests-signing-secret-that-is-long-enough-0123456789",
                ["JwtSettings:Issuer"] = "Share7.Api",
                ["JwtSettings:Audience"] = "Share7.Client",
                ["Studio:PublicUrl"] = StudioUrl
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();

        // What the API host adds on top of AddInfrastructure and some services here depend on.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<Share7.Application.Common.Interfaces.ICurrentUserService, AnonymousCurrentUser>();

        // Each scope is one "request"; the test says who is making it.
        services.AddScoped<RequestActor>();
        services.AddScoped<IAuditActor>(provider => provider.GetRequiredService<RequestActor>());

        services.AddInfrastructure(configuration);
        services.Replace(ServiceDescriptor.Scoped<IAuditActor>(provider => provider.GetRequiredService<RequestActor>()));

        return services.BuildServiceProvider();
    }

    /// <summary>A new request scope, acting as <paramref name="actorId"/> with <paramref name="roles"/>.</summary>
    public static AsyncServiceScope Request(this ServiceProvider services, Guid? actorId = null, params string[] roles)
    {
        var scope = services.CreateAsyncScope();
        var actor = scope.ServiceProvider.GetRequiredService<RequestActor>();
        actor.UserId = actorId;
        actor.Roles = roles;
        return scope;
    }

    public static T Get<T>(this AsyncServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// The six digits an authenticator app would show for this key — RFC 6238, SHA-1, 30-second
    /// steps, which is what Identity's authenticator provider checks. <paramref name="stepOffset"/>
    /// moves to a neighbouring window (Identity accepts two either side).
    /// </summary>
    public static string Totp(string base32Key, int stepOffset = 0)
    {
        var key = FromBase32(base32Key.Replace(" ", string.Empty).ToUpperInvariant());
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + stepOffset;

        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);

        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;

        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return output.ToArray();
    }
}

/// <summary>The acting user for one test "request".</summary>
public sealed class RequestActor : IAuditActor
{
    public Guid? UserId { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
    public string? IpAddress => "198.51.100.23";
    public string? UserAgent => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/140.0 Safari/537.36";
    public string? CorrelationId => "staff-test";
}
