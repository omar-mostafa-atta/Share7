using Microsoft.EntityFrameworkCore;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Proves the filtered unique indexes exist and actually refuse the rows they are meant to refuse.
/// <para>
/// **This is not a duplicate of the service tests, and it is the most important file in the pair.**
/// The index predicates are hand-written SQL naming the *stored* enum form (<c>'CLOSED'</c>, not
/// <c>'Closed'</c>). Get one wrong and the index silently constrains nothing — the service tests all
/// still pass, because the service's own checks catch the sequential cases, and only genuine
/// concurrency reveals that the last line of defence was never there. So these tests bypass the
/// service entirely and write straight to the database.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerIndexTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerIndexTests(SqlServerFixture fixture) => _fixture = fixture;

    private static MultiplayerSession Session(Guid gameId, Guid hostId, string transportName) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        HostUserId = hostId,
        TransportSessionName = transportName,
        State = MultiplayerSessionState.Created,
        Visibility = SessionVisibility.Public,
        MaxPlayers = 2,
        MinPlayers = 1,
        CurrentPlayerCount = 1,
        ProtocolVersion = 1,
        CreatedAtUtc = DateTime.UtcNow,
        LastHeartbeatAtUtc = DateTime.UtcNow
    };

    private static MultiplayerSessionPlayer Player(Guid sessionId, Guid userId, int slot) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        UserId = userId,
        Slot = slot,
        Status = SessionPlayerStatus.Joined,
        JoinedAtUtc = DateTime.UtcNow,
        LastSeenAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Two_live_sessions_cannot_share_a_transport_name()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostA = await TestData.CreateUserAsync(context);
        var hostB = await TestData.CreateUserAsync(context);

        const string name = "idx_transport_clash";

        context.MultiplayerSessions.Add(Session(fixture.GameId, hostA, name));
        await context.SaveChangesAsync();

        context.MultiplayerSessions.Add(Session(fixture.GameId, hostB, name));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_terminal_session_releases_its_transport_name()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostA = await TestData.CreateUserAsync(context);
        var hostB = await TestData.CreateUserAsync(context);

        const string name = "idx_transport_reuse";

        var first = Session(fixture.GameId, hostA, name);
        first.State = MultiplayerSessionState.Closed;
        first.ClosedReason = SessionClosedReason.HostClosed;
        first.EndedAtUtc = DateTime.UtcNow;

        context.MultiplayerSessions.Add(first);
        await context.SaveChangesAsync();

        // The filter is what makes this legal. A room name is a scarce, reusable resource — if a
        // finished match kept its name forever, a busy game would run out.
        context.MultiplayerSessions.Add(Session(fixture.GameId, hostB, name));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.MultiplayerSessions.CountAsync(s => s.TransportSessionName == name));
    }

    [Fact]
    public async Task One_user_cannot_hold_two_seats_in_the_same_session()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var session = Session(fixture.GameId, hostId, $"idx_double_{Guid.NewGuid():N}"[..24]);
        context.MultiplayerSessions.Add(session);
        context.MultiplayerSessionPlayers.Add(Player(session.Id, hostId, 0));
        await context.SaveChangesAsync();

        // Different slot, same user — the shape a perfectly-timed double join would produce.
        context.MultiplayerSessionPlayers.Add(Player(session.Id, hostId, 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_players_cannot_hold_the_same_slot()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);
        var otherId = await TestData.CreateUserAsync(context);

        var session = Session(fixture.GameId, hostId, $"idx_slot_{Guid.NewGuid():N}"[..24]);
        context.MultiplayerSessions.Add(session);
        context.MultiplayerSessionPlayers.Add(Player(session.Id, hostId, 0));
        await context.SaveChangesAsync();

        context.MultiplayerSessionPlayers.Add(Player(session.Id, otherId, 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_departed_member_releases_their_seat_for_reuse()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);
        var rejoinerId = await TestData.CreateUserAsync(context);

        var session = Session(fixture.GameId, hostId, $"idx_rejoin_{Guid.NewGuid():N}"[..24]);
        context.MultiplayerSessions.Add(session);
        context.MultiplayerSessionPlayers.Add(Player(session.Id, hostId, 0));

        var departed = Player(session.Id, rejoinerId, 1);
        departed.Status = SessionPlayerStatus.Left;
        departed.LeftAtUtc = DateTime.UtcNow;
        context.MultiplayerSessionPlayers.Add(departed);

        await context.SaveChangesAsync();

        // Same user, same slot, back again. Without the filter on the two unique indexes this would
        // be refused and nobody could ever rejoin a session they had left.
        context.MultiplayerSessionPlayers.Add(Player(session.Id, rejoinerId, 1));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.MultiplayerSessionPlayers
            .CountAsync(p => p.SessionId == session.Id && p.UserId == rejoinerId));
    }
}
