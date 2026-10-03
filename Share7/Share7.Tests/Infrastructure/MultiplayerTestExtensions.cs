using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Runs.Models;
using Share7.Infrastructure.Objectives;
using Share7.Domain.Multiplayer;
using Share7.Domain.Constants;
using Share7.Infrastructure.Leaderboards;
using Share7.Infrastructure.Play;
using Share7.Infrastructure.Progress;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Feed;
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

    /// <summary>
    /// The session service with its real collaborators: the lesson matcher that decides what a
    /// seated roster can play, and the play resolver that decides which mode a match is in. Stubs
    /// here would make every seating test pass under rules nothing ships.
    /// </summary>
    public static MultiplayerSessionService Sessions(
        ApplicationDbContext context, MultiplayerOptions? options = null, Guid? langId = null)
    {
        var resolved = MSOptions.Create(options ?? Options());

        return new(context,
            new MultiplayerRequestLogStore(context),
            new SessionLessonMatcher(context, EngineTest.Unlocks(context), EngineTest.Reads(context)),
            new PlaySelectionResolver(context, new LevelService(context)),
            new StubLanguageService(langId ?? LanguageIds.English),
            Names(context, resolved),
            resolved,
            NullLogger<MultiplayerSessionService>.Instance);
    }

    /// <summary>The roster namer production uses, over the leaderboards' real handle issuer.</summary>
    public static RosterNameResolver Names(ApplicationDbContext context, IOptions<MultiplayerOptions> options) =>
        new(context,
            new DisplayNameService(context, MSOptions.Create(new LeaderboardOptions())),
            options,
            NullLogger<RosterNameResolver>.Instance);

    public static MatchmakingService Matchmaking(
        ApplicationDbContext context, MultiplayerOptions? options = null, Guid? langId = null)
    {
        var resolved = options ?? Options();

        return new MatchmakingService(
            context,
            Sessions(context, resolved, langId),
            new MultiplayerRequestLogStore(context),
            new SessionLessonMatcher(context, EngineTest.Unlocks(context), EngineTest.Reads(context)),
            new PlaySelectionResolver(context, new LevelService(context)),
            new StubLanguageService(langId ?? LanguageIds.English),
            MSOptions.Create(resolved));
    }

    /// <summary>
    /// A sweeper whose process has been up for an hour — past every warm-up window, which is the
    /// state every sweep test except the warm-up ones means. Pass <paramref name="startedAtUtc"/> to
    /// test the warm-up itself.
    /// </summary>
    public static MultiplayerSweepService Sweeper(
        ApplicationDbContext context, MultiplayerOptions? options = null, DateTime? startedAtUtc = null)
    {
        var resolved = options ?? Options();
        var warmup = new SweeperWarmup(startedAtUtc ?? DateTime.UtcNow.AddHours(-1));

        return new(context,
            Results(context, resolved, warmup),
            SocialTest.Challenges(context, resolved),
            Tournaments(context, resolved),
            MSOptions.Create(resolved),
            warmup,
            NullLogger<MultiplayerSweepService>.Instance);
    }

    /// <summary>The tournament orchestrator with the same persisted collaborators as production.</summary>
    public static TournamentService Tournaments(ApplicationDbContext context, MultiplayerOptions? options = null)
    {
        var resolved = options ?? Options();
        var wrapped = MSOptions.Create(resolved);
        return new(context, Sessions(context, resolved), PlayTest.Resolver(context),
            new SessionLessonMatcher(context, EngineTest.Unlocks(context), EngineTest.Reads(context)),
            EngineTest.Unlocks(context), new StubLanguageService(LanguageIds.English), Names(context, wrapped),
            SocialTest.Publisher(context, resolved),
            new GameResultRecorder(context, new PlausibilityGuard(context), NullLogger<GameResultRecorder>.Instance),
            new ObjectiveProjector(context, NullLogger<ObjectiveProjector>.Instance), PlayTest.Awards(context),
            TestAudit.For(context), wrapped, MSOptions.Create(new RunOptions()), NullLogger<TournamentService>.Instance);
    }

    /// <summary>
    /// The match-result service with its real collaborators — the real recorder and projector, so a
    /// verdict's MATCHES_WON lands in the same transaction as the verdict, exactly as in production.
    /// </summary>
    public static MatchResultService Results(
        ApplicationDbContext context,
        MultiplayerOptions? options = null,
        SweeperWarmup? warmup = null,
        RunOptions? runOptions = null)
    {
        var resolved = MSOptions.Create(options ?? Options());

        return new MatchResultService(
            context,
            Names(context, resolved),
            new PlaySelectionResolver(context, new LevelService(context)),
            new GameResultRecorder(context, new PlausibilityGuard(context), NullLogger<GameResultRecorder>.Instance),
            new ObjectiveProjector(context, NullLogger<ObjectiveProjector>.Instance),
            new PlayerEventPublisher(context, resolved),
            new RatingService(context, new PlayerEventPublisher(context, resolved), resolved, NullLogger<RatingService>.Instance),
            warmup ?? new SweeperWarmup(DateTime.UtcNow.AddHours(-1)),
            resolved,
            MSOptions.Create(runOptions ?? new RunOptions()),
            NullLogger<MatchResultService>.Instance);
    }

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
