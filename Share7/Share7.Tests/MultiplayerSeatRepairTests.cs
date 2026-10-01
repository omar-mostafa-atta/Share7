using Microsoft.EntityFrameworkCore;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence.Migrations;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The data repair that has to succeed before <c>UQ_SessionPlayer_OneLiveSeat</c> can be created on a
/// production database that already holds what the index forbids.
/// <para>
/// **A repair that has only ever run against an empty table has not been tested.** Every fixture
/// migrates from scratch, so the migration itself only proves the SQL parses. This drops the index,
/// writes exactly the rows the race and a crash could have left behind, runs the repair, and recreates
/// the index — which is the step that would take the API down at startup if the repair missed a row.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerSeatRepairTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerSeatRepairTests(SqlServerFixture fixture) => _fixture = fixture;

    private const string DropIndexSql =
        "DROP INDEX [UQ_SessionPlayer_OneLiveSeat] ON [MultiplayerSessionPlayers];";

    private const string CreateIndexSql =
        """
        CREATE UNIQUE INDEX [UQ_SessionPlayer_OneLiveSeat] ON [MultiplayerSessionPlayers] ([UserId])
        WHERE [Status] <> 'LEFT' AND [Status] <> 'REMOVED';
        """;

    [Fact]
    public async Task The_repair_leaves_nothing_the_one_live_seat_index_would_refuse()
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var twiceSeated = await TestData.CreateUserAsync(context);
        var stranded = await TestData.CreateUserAsync(context);
        var hostA = await TestData.CreateUserAsync(context);
        var hostB = await TestData.CreateUserAsync(context);
        var hostC = await TestData.CreateUserAsync(context);

        var now = DateTime.UtcNow;

        var older = Session(curriculum.GameId, hostA, MultiplayerSessionState.Created, count: 2);
        var newer = Session(curriculum.GameId, hostB, MultiplayerSessionState.Created, count: 2);
        var ended = Session(curriculum.GameId, hostC, MultiplayerSessionState.Closed, count: 0);
        ended.EndedAtUtc = now.AddMinutes(-5);
        ended.ClosedReason = SessionClosedReason.HostClosed;

        await context.Database.ExecuteSqlRawAsync(DropIndexSql);

        try
        {
            context.MultiplayerSessions.AddRange(older, newer, ended);
            context.MultiplayerSessionPlayers.AddRange(
                Seat(older.Id, hostA, 0, now.AddMinutes(-10), isHost: true),
                Seat(older.Id, twiceSeated, 1, now.AddMinutes(-9)),
                Seat(newer.Id, hostB, 0, now.AddMinutes(-3), isHost: true),
                Seat(newer.Id, twiceSeated, 1, now.AddMinutes(-2)),

                // A crash between the close and the seat release left these two in an ended match.
                Seat(ended.Id, hostC, 0, now.AddMinutes(-20), isHost: true),
                Seat(ended.Id, stranded, 1, now.AddMinutes(-19)));

            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync(MultiplayerOneLiveSeatPerAccount.ReleaseSeatsInEndedSessionsSql);
            await context.Database.ExecuteSqlRawAsync(MultiplayerOneLiveSeatPerAccount.KeepOnlyNewestLiveSeatSql);
            await context.Database.ExecuteSqlRawAsync(MultiplayerOneLiveSeatPerAccount.RecountLiveSessionsSql);
        }
        finally
        {
            // Recreated whatever happened above: every other test in the collection shares this
            // database and relies on the index. If this throws, the repair missed a row — which is
            // exactly the failure it exists to prevent.
            await context.Database.ExecuteSqlRawAsync(CreateIndexSql);
        }

        await using var check = _fixture.CreateContext();

        // The account seated twice keeps the seat in the match it joined most recently.
        var seats = await check.MultiplayerSessionPlayers.AsNoTracking()
            .Where(p => p.UserId == twiceSeated && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed)
            .ToListAsync();

        Assert.Equal(newer.Id, Assert.Single(seats).SessionId);

        // The count of the session that lost the ghost is recomputed from what is actually seated.
        Assert.Equal(1, (await MultiplayerTest.ReadSessionAsync(check, older.Id)).CurrentPlayerCount);
        Assert.Equal(2, (await MultiplayerTest.ReadSessionAsync(check, newer.Id)).CurrentPlayerCount);

        // Nobody is left holding a seat in a match that ended.
        var endedSeats = await MultiplayerTest.ReadPlayersAsync(check, ended.Id);
        Assert.All(endedSeats, p => Assert.Equal(SessionPlayerStatus.Left, p.Status));
    }

    private static MultiplayerSession Session(Guid gameId, Guid hostId, MultiplayerSessionState state, int count) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        HostUserId = hostId,
        TransportSessionName = $"repair_{Guid.NewGuid():N}"[..24],
        State = state,
        Visibility = SessionVisibility.Public,
        MaxPlayers = 4,
        MinPlayers = 1,
        CurrentPlayerCount = count,
        ProtocolVersion = 1,
        CreatedAtUtc = DateTime.UtcNow,
        LastHeartbeatAtUtc = DateTime.UtcNow
    };

    private static MultiplayerSessionPlayer Seat(Guid sessionId, Guid userId, int slot, DateTime joinedAtUtc, bool isHost = false) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        UserId = userId,
        Slot = slot,
        IsHost = isHost,
        Status = SessionPlayerStatus.Connected,
        JoinedAtUtc = joinedAtUtc,
        LastSeenAtUtc = joinedAtUtc
    };
}
