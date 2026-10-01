using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Constants;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Play;
using Share7.Infrastructure.Progression;
using Share7.Tests.Infrastructure;
using Xunit;
using MSOptions = Microsoft.Extensions.Options.Options;

namespace Share7.Tests;

/// <summary>
/// Queued matchmaking: ranked players formed into rated rooms by skill, parties placed together, and
/// every race the worker can lose leaving nothing behind.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MatchmakingTicketTests
{
    private readonly SqlServerFixture _fixture;

    public MatchmakingTicketTests(SqlServerFixture fixture) => _fixture = fixture;

    private static MatchmakingTicketService Tickets(ApplicationDbContext context, MultiplayerOptions? options = null)
    {
        var resolved = options ?? MultiplayerTest.Options();
        var wrapped = MSOptions.Create(resolved);

        return new MatchmakingTicketService(
            context,
            MultiplayerTest.Sessions(context, resolved),
            new SessionLessonMatcher(context, EngineTest.Unlocks(context), EngineTest.Reads(context)),
            new PlaySelectionResolver(context, new LevelService(context)),
            new StubLanguageService(LanguageIds.English),
            new RatingService(context, SocialTest.Publisher(context, resolved), wrapped, NullLogger<RatingService>.Instance),
            SocialTest.Publisher(context, resolved),
            wrapped,
            NullLogger<MatchmakingTicketService>.Instance);
    }

    private sealed record Arena(Guid GameId, GameMode Mode);

    /// <summary>A multiplayer game with a default versus mode (ranked unless told otherwise), seating <paramref name="seats"/>.</summary>
    private async Task<Arena> ArenaAsync(int seats = 2, bool ranked = true)
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.SetSeatsAsync(context, path.GameId, 1, 4);

        var mode = await context.AddModeAsync(path.GameId, isDefault: true, topologies: PlayTopologies.Solo | PlayTopologies.Versus);
        var rule = MatchWinRule.Build([("outcome", "higher")]).Rule!.ToJson();

        await context.GameModes.Where(m => m.Id == mode.Id).ExecuteUpdateAsync(set => set
            .SetProperty(m => m.Ranked, ranked)
            .SetProperty(m => m.WinRuleJson, rule)
            .SetProperty(m => m.MaxPlayers, seats));

        return new Arena(path.GameId, mode);
    }

    private static EnqueueTicketRequest Queue(Arena arena, bool ranked = true, Guid? partyId = null) => new()
    {
        GameId = arena.GameId,
        ModeKey = arena.Mode.ModeKey,
        Ranked = ranked,
        ProtocolVersion = 1,
        PartyId = partyId
    };

    private static async Task BackdateAsync(ApplicationDbContext context, Guid ticketId, int seconds) =>
        await context.MatchmakingTickets.Where(t => t.Id == ticketId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.EnqueuedAtUtc, t => t.EnqueuedAtUtc.AddSeconds(-seconds)));

    [Fact]
    public async Task Two_close_ranked_players_are_formed_into_one_rated_room_already_seated()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var first = await TestData.CreateUserAsync(context);
        var second = await TestData.CreateUserAsync(context);
        var tickets = Tickets(context);

        var a = (await tickets.EnqueueAsync(first, Queue(arena))).Value!;
        await BackdateAsync(context, a.Id, 3);
        var b = (await tickets.EnqueueAsync(second, Queue(arena))).Value!;

        Assert.Equal(1, await tickets.FormMatchesAsync());

        var matched = (await tickets.CurrentAsync(second)).Value!;
        Assert.Equal(TicketState.Matched, matched.State);
        Assert.Equal(first, matched.HostUserId);

        var session = await context.MultiplayerSessions.AsNoTracking().SingleAsync(s => s.Id == matched.SessionId);
        Assert.True(session.IsRated);
        Assert.Equal(SessionVisibility.Private, session.Visibility);
        Assert.Equal(MultiplayerSessionState.Creating, session.State);
        Assert.Equal(2, session.CurrentPlayerCount);
        Assert.Equal(2, await context.MultiplayerSessionPlayers.CountAsync(p => p.SessionId == session.Id));

        var toHost = Assert.Single(await SocialTest.EventsAsync(context, first, PlayerEventTypes.MatchFound));
        Assert.True(toHost.Payload.GetProperty("youHost").GetBoolean());
        Assert.Equal(session.TransportSessionName, toHost.Text("transportSessionName"));
        Assert.False(Assert.Single(await SocialTest.EventsAsync(context, second, PlayerEventTypes.MatchFound))
            .Payload.GetProperty("youHost").GetBoolean());
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Far_apart_ratings_wait_until_the_band_has_widened()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var strong = await TestData.CreateUserAsync(context);
        var weak = await TestData.CreateUserAsync(context);

        context.PlayerRatings.AddRange(
            new PlayerRating { UserId = strong, ModeId = arena.Mode.Id, Mu = 40, Sigma = 2, SeasonKey = RankedSeasons.KeyFor(DateTime.UtcNow), UpdatedAtUtc = DateTime.UtcNow },
            new PlayerRating { UserId = weak, ModeId = arena.Mode.Id, Mu = 12, Sigma = 2, SeasonKey = RankedSeasons.KeyFor(DateTime.UtcNow), UpdatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var tickets = Tickets(context);
        var a = (await tickets.EnqueueAsync(strong, Queue(arena))).Value!;
        var b = (await tickets.EnqueueAsync(weak, Queue(arena))).Value!;

        Assert.Equal(0, await tickets.FormMatchesAsync());

        // A minute of waiting: the band (5 + 0.5/s) now spans the gap of 28.
        await BackdateAsync(context, a.Id, 60);
        await BackdateAsync(context, b.Id, 60);

        Assert.Equal(1, await tickets.FormMatchesAsync());
    }

    [Fact]
    public async Task Ranked_is_queued_alone_and_only_in_a_ranked_mode()
    {
        var ranked = await ArenaAsync();
        var casual = await ArenaAsync(ranked: false);

        await using var context = _fixture.CreateContext();
        var (leader, friend) = await SocialTest.ClassmatesAsync(context);
        var parties = SocialTest.Parties(context);
        var party = (await parties.CreateAsync(leader)).Value!;
        var invite = (await parties.InviteAsync(leader, party.Id, new InvitePlayerRequest { UserId = friend })).Value!;
        await parties.AcceptInviteAsync(friend, invite.Id);

        var tickets = Tickets(context);

        Assert.Equal("RANKED_SOLO_ONLY", (await tickets.EnqueueAsync(leader, Queue(ranked, partyId: party.Id))).Error?.Code);
        Assert.Equal("MODE_NOT_RANKED", (await tickets.EnqueueAsync(leader, Queue(casual))).Error?.Code);
        Assert.Equal("NOT_PARTY_LEADER", (await tickets.EnqueueAsync(friend, Queue(casual, ranked: false, partyId: party.Id))).Error?.Code);
    }

    [Fact]
    public async Task Searching_again_is_the_same_ticket_and_a_cancelled_one_is_never_matched()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var me = await TestData.CreateUserAsync(context);
        var other = await TestData.CreateUserAsync(context);
        var tickets = Tickets(context);

        var first = (await tickets.EnqueueAsync(me, Queue(arena))).Value!;
        var again = (await tickets.EnqueueAsync(me, Queue(arena))).Value!;
        Assert.Equal(first.Id, again.Id);

        var cancelled = (await tickets.CancelAsync(me, first.Id)).Value!;
        Assert.Equal(TicketState.Cancelled, cancelled.State);

        await tickets.EnqueueAsync(other, Queue(arena));
        Assert.Equal(0, await tickets.FormMatchesAsync());
    }

    [Fact]
    public async Task A_player_already_in_a_room_cannot_queue()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var me = await TestData.CreateUserAsync(context);
        await MultiplayerTest.Sessions(context).CreateAsync(me, MultiplayerTest.CreateRequest(arena.GameId));

        Assert.Equal("ALREADY_IN_SESSION", (await Tickets(context).EnqueueAsync(me, Queue(arena))).Error?.Code);
    }

    [Fact]
    public async Task When_the_host_never_brings_the_room_up_the_others_go_back_in_line()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var host = await TestData.CreateUserAsync(context);
        var guest = await TestData.CreateUserAsync(context);
        var tickets = Tickets(context);

        var hostTicket = (await tickets.EnqueueAsync(host, Queue(arena))).Value!;
        await BackdateAsync(context, hostTicket.Id, 3);
        var guestTicket = (await tickets.EnqueueAsync(guest, Queue(arena))).Value!;
        await tickets.FormMatchesAsync();

        var sessionId = (await tickets.CurrentAsync(guest)).Value!.SessionId!.Value;

        // What the sweeper does to a room nobody confirmed: failed, every seat released.
        await context.MultiplayerSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.State, MultiplayerSessionState.Failed).SetProperty(s => s.EndedAtUtc, DateTime.UtcNow));
        await context.MultiplayerSessionPlayers.Where(p => p.SessionId == sessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, SessionPlayerStatus.Left));

        await tickets.FormMatchesAsync();

        var back = await context.MatchmakingTickets.AsNoTracking().SingleAsync(t => t.Id == guestTicket.Id);
        Assert.Equal(TicketState.Searching, back.State);
        Assert.Null(back.SessionId);
        Assert.Equal("host_no_show", back.EndReason);

        var noShow = await context.MatchmakingTickets.AsNoTracking().SingleAsync(t => t.Id == hostTicket.Id);
        Assert.Equal(TicketState.Cancelled, noShow.State);

        Assert.Single(await SocialTest.EventsAsync(context, guest, PlayerEventTypes.MatchmakingRequeued));
    }

    [Fact]
    public async Task A_ticket_that_searched_too_long_expires_and_says_so()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var me = await TestData.CreateUserAsync(context);
        var tickets = Tickets(context);

        var ticket = (await tickets.EnqueueAsync(me, Queue(arena))).Value!;
        await context.MatchmakingTickets.Where(t => t.Id == ticket.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));

        await tickets.FormMatchesAsync();

        Assert.Equal(TicketState.Expired, (await context.MatchmakingTickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id)).State);
        Assert.Single(await SocialTest.EventsAsync(context, me, PlayerEventTypes.MatchmakingExpired));

        // Free to search again at once.
        Assert.NotEqual(ticket.Id, (await tickets.EnqueueAsync(me, Queue(arena))).Value!.Id);
    }

    [Fact]
    public async Task Players_who_blocked_each_other_are_never_matched()
    {
        var arena = await ArenaAsync();

        await using var context = _fixture.CreateContext();
        var a = await TestData.CreateUserAsync(context);
        var b = await TestData.CreateUserAsync(context);
        await SocialTest.Social(context).BlockAsync(a, b);

        var tickets = Tickets(context);
        var first = (await tickets.EnqueueAsync(a, Queue(arena))).Value!;
        var second = (await tickets.EnqueueAsync(b, Queue(arena))).Value!;
        await BackdateAsync(context, first.Id, 120);
        await BackdateAsync(context, second.Id, 120);

        Assert.Equal(0, await tickets.FormMatchesAsync());
    }

    [Fact]
    public async Task A_party_is_placed_together_in_an_open_lobby_with_room_for_all_of_it()
    {
        var arena = await ArenaAsync(seats: 4, ranked: false);

        await using var context = _fixture.CreateContext();
        var lobbyHost = await TestData.CreateUserAsync(context);
        var sessions = MultiplayerTest.Sessions(context);
        var lobby = (await sessions.CreateAsync(lobbyHost, new CreateMultiplayerSessionRequest
        {
            GameId = arena.GameId,
            ModeKey = arena.Mode.ModeKey,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            MaxPlayers = 4,
            ProtocolVersion = 1
        })).Value!;
        await sessions.StartAsync(lobbyHost, lobby.Id, new StartMultiplayerSessionRequest());

        var (leader, friend) = await SocialTest.ClassmatesAsync(context);
        var parties = SocialTest.Parties(context);
        var party = (await parties.CreateAsync(leader)).Value!;
        await parties.AcceptInviteAsync(friend, (await parties.InviteAsync(leader, party.Id, new InvitePlayerRequest { UserId = friend })).Value!.Id);

        var tickets = Tickets(context);
        var ticket = await tickets.EnqueueAsync(leader, Queue(arena, ranked: false, partyId: party.Id));
        Assert.True(ticket.Succeeded, ticket.Error?.Code);

        Assert.Equal(1, await tickets.FormMatchesAsync());

        var seated = await context.MultiplayerSessionPlayers.AsNoTracking()
            .Where(p => p.SessionId == lobby.Id && p.Status != SessionPlayerStatus.Left)
            .Select(p => p.UserId)
            .ToListAsync();

        Assert.Equal(new[] { lobbyHost, leader, friend }.OrderBy(x => x), seated.OrderBy(x => x));
        Assert.Equal(lobby.Id, (await tickets.CurrentAsync(friend)).Value!.SessionId);
    }

    [Fact]
    public async Task A_party_with_no_lobby_to_join_opens_one_that_strangers_can_fill()
    {
        var arena = await ArenaAsync(seats: 4, ranked: false);

        await using var context = _fixture.CreateContext();
        var (leader, friend) = await SocialTest.ClassmatesAsync(context);
        var parties = SocialTest.Parties(context);
        var party = (await parties.CreateAsync(leader)).Value!;
        await parties.AcceptInviteAsync(friend, (await parties.InviteAsync(leader, party.Id, new InvitePlayerRequest { UserId = friend })).Value!.Id);

        var tickets = Tickets(context);
        var ticket = (await tickets.EnqueueAsync(leader, Queue(arena, ranked: false, partyId: party.Id))).Value!;

        Assert.Equal(0, await tickets.FormMatchesAsync());

        await BackdateAsync(context, ticket.Id, 30);
        Assert.Equal(1, await tickets.FormMatchesAsync());

        var room = (await tickets.CurrentAsync(leader)).Value!;
        var session = await context.MultiplayerSessions.AsNoTracking().SingleAsync(s => s.Id == room.SessionId);
        Assert.Equal(SessionVisibility.Public, session.Visibility);
        Assert.False(session.IsRated);

        // The leader brings the room up; a stranger matchmaking the usual way lands in it.
        await MultiplayerTest.Sessions(context).StartAsync(leader, session.Id, new StartMultiplayerSessionRequest());

        var stranger = await TestData.CreateUserAsync(context);
        var joined = await MultiplayerTest.Matchmaking(context).MatchmakeAsync(stranger, new MatchmakeRequest
        {
            GameId = arena.GameId,
            ModeKey = arena.Mode.ModeKey,
            ProtocolVersion = 1,
            CreateIfNoneFound = false,
            TransportSessionName = MultiplayerTest.NewTransportName()
        });

        Assert.Equal(MatchOutcome.Joined, joined.Value!.Outcome);
        Assert.Equal(session.Id, joined.Value.Session!.Id);
    }

    [Fact]
    public async Task Two_workers_running_at_once_form_one_match()
    {
        var arena = await ArenaAsync();

        await using (var setup = _fixture.CreateContext())
        {
            var tickets = Tickets(setup);
            var a = (await tickets.EnqueueAsync(await TestData.CreateUserAsync(setup), Queue(arena))).Value!;
            await BackdateAsync(setup, a.Id, 3);
            await tickets.EnqueueAsync(await TestData.CreateUserAsync(setup), Queue(arena));
        }

        await using var one = _fixture.CreateContext();
        await using var two = _fixture.CreateContext();

        var passes = await Task.WhenAll(Tickets(one).FormMatchesAsync(), Tickets(two).FormMatchesAsync());

        Assert.Equal(1, passes.Sum());

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.GameId == arena.GameId));
    }
}
