using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

/// <summary>A host with a session already confirmed up to <c>Created</c>, so it accepts joins.</summary>
public record OpenSession(Guid HostId, Guid SessionId, Guid GameId);

/// <summary>
/// Builders for the multiplayer services and the fixtures they need.
/// <para>
/// The services are constructed by hand rather than resolved from a container: these tests are
/// about session behaviour, and a service provider would only add a way for the test's wiring to
/// differ from production's without either one failing.
/// </para>
/// </summary>
public static class MultiplayerTest
{
    public static MultiplayerOptions Options(Action<MultiplayerOptions>? configure = null)
    {
        var options = new MultiplayerOptions { AcceptedProtocolVersions = [1] };
        configure?.Invoke(options);
        return options;
    }

    public static MultiplayerSessionService Sessions(ApplicationDbContext context, MultiplayerOptions? options = null) =>
        new(context, new MultiplayerRequestLogStore(context), MSOptions.Create(options ?? Options()));

    public static MatchmakingService Matchmaking(ApplicationDbContext context, MultiplayerOptions? options = null)
    {
        var resolved = options ?? Options();

        return new MatchmakingService(
            context,
            Sessions(context, resolved),
            new MultiplayerRequestLogStore(context),
            MSOptions.Create(resolved));
    }

    public static MultiplayerSweepService Sweeper(ApplicationDbContext context, MultiplayerOptions? options = null) =>
        new(context, MSOptions.Create(options ?? Options()), NullLogger<MultiplayerSweepService>.Instance);

    public static CreateMultiplayerSessionRequest CreateRequest(
        Guid gameId,
        string? transportName = null,
        int protocolVersion = 1,
        string? requestId = null,
        SessionVisibility? visibility = null,
        CurriculumPathDto? path = null,
        bool isRanked = false) =>
        new()
        {
            GameId = gameId,
            TransportSessionName = transportName ?? NewTransportName(),
            ProtocolVersion = protocolVersion,
            RequestId = requestId,
            Visibility = visibility,
            CurriculumPath = path,
            IsRanked = isRanked
        };

    public static string NewTransportName() => $"room_{Guid.NewGuid():N}"[..24];

    /// <summary>Widens a game's seat count — the default catalog row only fits two.</summary>
    public static async Task SetSeatsAsync(ApplicationDbContext context, Guid gameId, int minPlayers, int maxPlayers)
    {
        await context.Games
            .Where(g => g.Id == gameId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(g => g.MinPlayers, minPlayers)
                .SetProperty(g => g.MaxPlayers, maxPlayers));
    }

    /// <summary>
    /// Creates a session and confirms it up to <c>Created</c>. Pass <paramref name="maxPlayers"/> to
    /// widen the game first.
    /// </summary>
    public static async Task<OpenSession> OpenAsync(
        SqlServerFixture fixture,
        string? transportName = null,
        int? maxPlayers = null,
        CurriculumPathDto? path = null,
        bool isRanked = false)
    {
        await using var context = fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);

        if (maxPlayers is { } seats)
            await SetSeatsAsync(context, curriculum.GameId, 1, seats);

        var hostId = await TestData.CreateUserAsync(context);

        var created = await Sessions(context).CreateAsync(
            hostId, CreateRequest(curriculum.GameId, transportName, path: path, isRanked: isRanked));

        var confirmed = await Sessions(context).StartAsync(
            hostId, created.Value!.Id, new StartMultiplayerSessionRequest());

        if (!confirmed.Succeeded)
            throw new InvalidOperationException("Test fixture could not confirm the session up to Created.");

        return new OpenSession(hostId, created.Value.Id, curriculum.GameId);
    }

    /// <summary>Seats an extra player and hands back their id.</summary>
    public static async Task<Guid> JoinAsync(SqlServerFixture fixture, Guid sessionId, int protocolVersion = 1)
    {
        await using var context = fixture.CreateContext();

        var userId = await TestData.CreateUserAsync(context);

        var joined = await Sessions(context).JoinAsync(
            userId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = protocolVersion });

        if (!joined.Succeeded)
            throw new InvalidOperationException(
                $"Test fixture could not seat a player: {joined.Error?.Code}.");

        return userId;
    }

    /// <summary>
    /// Backdates a session's clocks. **Tests drive time by editing rows rather than by waiting** —
    /// a sweep test that slept for its own timeout would take a minute and still be flaky.
    /// </summary>
    public static Task AgeSessionAsync(ApplicationDbContext context, Guid sessionId, int seconds)
    {
        var when = DateTime.UtcNow.AddSeconds(-seconds);

        return context.MultiplayerSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.LastHeartbeatAtUtc, when)
                .SetProperty(s => s.CreatedAtUtc, when));
    }

    /// <summary>Backdates one member's last-seen time, and optionally forces their status.</summary>
    public static Task AgePlayerAsync(
        ApplicationDbContext context,
        Guid sessionId,
        Guid userId,
        int seconds,
        SessionPlayerStatus? status = null)
    {
        var when = DateTime.UtcNow.AddSeconds(-seconds);

        return context.MultiplayerSessionPlayers
            .Where(p => p.SessionId == sessionId && p.UserId == userId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.LastSeenAtUtc, when)
                .SetProperty(p => p.Status, p => status ?? p.Status));
    }

    public static Task<MultiplayerSession> ReadSessionAsync(ApplicationDbContext context, Guid sessionId) =>
        context.MultiplayerSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);

    public static Task<List<MultiplayerSessionPlayer>> ReadPlayersAsync(
        ApplicationDbContext context,
        Guid sessionId) =>
        context.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.Slot)
            .ToListAsync();
}

/// <summary>
/// Alias so <c>Options</c> above can be a method name without colliding with
/// <c>Microsoft.Extensions.Options.Options</c>.
/// </summary>
internal static class MSOptions
{
    public static IOptions<T> Create<T>(T value) where T : class =>
        Microsoft.Extensions.Options.Options.Create(value);
}
