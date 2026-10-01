using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Social;
using Share7.Infrastructure.Feed;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The player feed: written with the change it reports, read in order by long-poll, woken at once
/// on this instance, and honest about gaps.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PlayerFeedTests
{
    private const string Type = "test.thing.happened";

    private readonly SqlServerFixture _fixture;

    public PlayerFeedTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_event_is_read_once_its_change_commits_and_the_cursor_moves_past_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        SocialTest.Publisher(context).Stage(userId, Type, new { n = 1 });
        SocialTest.Publisher(context).Stage(userId, Type, new { n = 2 });
        await context.SaveChangesAsync();

        var feed = SocialTest.Feed(context);
        var first = (await feed.ReadAsync(userId, 0, 0)).Value!;

        Assert.Equal(2, first.Events.Count);
        Assert.True(first.Events[0].Sequence < first.Events[1].Sequence);
        Assert.Equal(2, first.Events[1].Payload.GetProperty("n").GetInt32());
        Assert.Equal(first.Events[1].Sequence, first.NextAfter);

        var next = (await feed.ReadAsync(userId, first.NextAfter, 0)).Value!;

        Assert.Empty(next.Events);
        Assert.Equal(first.NextAfter, next.NextAfter);
    }

    [Fact]
    public async Task An_event_whose_change_rolled_back_never_existed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            SocialTest.Publisher(context).Stage(userId, Type, new { });
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var check = _fixture.CreateContext();
        Assert.Empty(await SocialTest.EventsAsync(check, userId));
    }

    [Fact]
    public async Task A_waiting_read_wakes_the_moment_an_event_commits()
    {
        await using var setup = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(setup);

        var signal = new PlayerEventSignal();

        // A fallback poll far longer than the test: only the commit's wake-up can answer in time.
        var slowPoll = MultiplayerTest.Options(o => o.EventFallbackPollSeconds = 60);

        await using var reading = _fixture.CreateContext();
        var clock = Stopwatch.StartNew();
        var waiting = SocialTest.Feed(reading, signal, slowPoll).ReadAsync(userId, 0, waitSeconds: 20);

        await Task.Delay(300);

        await using (var writing = _fixture.CreateSignallingContext(signal))
        await using (var transaction = await writing.Database.BeginTransactionAsync())
        {
            SocialTest.Publisher(writing).Stage(userId, Type, new { });
            await writing.SaveChangesAsync();

            // Inside a transaction the wake-up waits for the commit, so the reader is never woken into
            // rows it cannot see yet.
            await transaction.CommitAsync();
        }

        var page = (await waiting).Value!;

        Assert.Single(page.Events);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"Took {clock.Elapsed}.");
    }

    [Fact]
    public async Task An_event_past_its_expiry_is_not_delivered()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        SocialTest.Publisher(context).Stage(userId, Type, new { }, DateTime.UtcNow.AddSeconds(-1));
        await context.SaveChangesAsync();

        Assert.Empty(await SocialTest.EventsAsync(context, userId));
    }

    [Fact]
    public async Task A_cursor_older_than_retention_is_refused_with_where_to_resume()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        SocialTest.Publisher(context).Stage(userId, Type, new { });
        SocialTest.Publisher(context).Stage(userId, Type, new { });
        await context.SaveChangesAsync();

        var events = await SocialTest.EventsAsync(context, userId);

        // Retention takes the older one — the client's cursor.
        await context.PlayerEvents.Where(e => e.Sequence == events[0].Sequence).ExecuteDeleteAsync();

        var read = await SocialTest.Feed(context).ReadAsync(userId, events[0].Sequence, 0);

        Assert.Equal("EVENTS_CURSOR_EXPIRED", read.Error?.Code);
        Assert.Equal(events[1].Sequence, read.Details!["latest"]);
    }

    [Fact]
    public async Task A_player_never_reads_another_players_events()
    {
        await using var context = _fixture.CreateContext();
        var me = await TestData.CreateUserAsync(context);
        var other = await TestData.CreateUserAsync(context);

        SocialTest.Publisher(context).Stage(other, Type, new { secret = "theirs" });
        await context.SaveChangesAsync();

        Assert.Empty(await SocialTest.EventsAsync(context, me));

        // Nor use one of theirs as a cursor to learn anything.
        var theirs = (await SocialTest.EventsAsync(context, other))[0].Sequence;
        Assert.Equal("EVENTS_CURSOR_EXPIRED", (await SocialTest.Feed(context).ReadAsync(me, theirs, 0)).Error?.Code);
    }

    [Fact]
    public async Task Reading_the_feed_is_what_shows_a_player_online()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var presence = SocialTest.Presence(context);

        Assert.Equal(PlayerPresenceState.Offline, (await presence.GetAsync([userId]))[userId]);

        await SocialTest.Feed(context).ReadAsync(userId, 0, 0);

        Assert.Equal(PlayerPresenceState.Online, (await presence.GetAsync([userId]))[userId]);
    }

    [Fact]
    public async Task Every_player_of_a_decided_match_hears_the_result_is_in()
    {
        var open = await MultiplayerTest.OpenAsync(_fixture);
        var guestId = await MultiplayerTest.JoinAsync(_fixture, open.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        await sessions.StartAsync(open.HostId, open.SessionId, new Application.Multiplayer.Models.StartMultiplayerSessionRequest());
        await sessions.CloseAsync(open.HostId, open.SessionId, new Application.Multiplayer.Models.CloseMultiplayerSessionRequest());

        // Nobody reported, the grace period is over: the match is decided (unranked, everyone forfeits).
        await context.MultiplayerSessions
            .Where(s => s.Id == open.SessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.EndedAtUtc, DateTime.UtcNow.AddMinutes(-10)));

        var result = await MultiplayerTest.Results(context).GetAsync(open.HostId, open.SessionId);
        Assert.NotEqual(Application.Multiplayer.Models.MatchResultStatus.Pending, result.Value!.State);

        foreach (var player in new[] { open.HostId, guestId })
        {
            var heard = Assert.Single(await SocialTest.EventsAsync(context, player, "multiplayer.match.result_ready"));
            Assert.Equal(open.SessionId.ToString(), heard.Text("sessionId"));
        }
    }
}
