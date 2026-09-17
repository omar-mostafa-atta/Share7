using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Matchmaking: one indexed read for candidates, then the same race-proof seating path a direct join
/// uses. No queue, no worker, no lock — the retry loop is the whole race defence.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MatchmakingServiceTests
{
    private readonly SqlServerFixture _fixture;

    public MatchmakingServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static MatchmakeRequest Request(
        Guid gameId,
        bool createIfNoneFound = true,
        string? transportName = null,
        Guid? lessonId = null,
        string? requestId = null,
        bool isRanked = false) =>
        new()
        {
            GameId = gameId,
            ProtocolVersion = 1,
            IsRanked = isRanked,
            CreateIfNoneFound = createIfNoneFound,
            TransportSessionName = transportName ?? MultiplayerTest.NewTransportName(),
            RequestId = requestId,
            CurriculumPath = lessonId is { } id ? new CurriculumPathDto { LessonId = id } : null
        };

    [Fact]
    public async Task Matchmaking_joins_an_open_session_rather_than_creating_one()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(session.GameId));

        Assert.True(result.Succeeded);
        Assert.Equal(MatchOutcome.Joined, result.Value!.Outcome);
        Assert.Equal(session.SessionId, result.Value.Session!.Id);
    }

    [Fact]
    public async Task Matchmaking_fills_the_fullest_joinable_session_first()
    {
        // Both sessions belong to the same game, so both are candidates.
        var emptier = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);

        await using var setup = _fixture.CreateContext();
        var hostB = await TestData.CreateUserAsync(setup);

        var fullerCreated = await MultiplayerTest.Sessions(setup)
            .CreateAsync(hostB, MultiplayerTest.CreateRequest(emptier.GameId));

        await MultiplayerTest.Sessions(setup)
            .StartAsync(hostB, fullerCreated.Value!.Id, new StartMultiplayerSessionRequest());

        var fullerId = fullerCreated.Value.Id;

        // Take it to 3 of 4 — one join away from being playable.
        await MultiplayerTest.JoinAsync(_fixture, fullerId);
        await MultiplayerTest.JoinAsync(_fixture, fullerId);

        await using var context = _fixture.CreateContext();
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(emptier.GameId));

        Assert.True(result.Succeeded);
        Assert.Equal(MatchOutcome.Joined, result.Value!.Outcome);

        // **Fullest first is the shortest wait for everybody.** Spreading players evenly across
        // half-empty rooms is how nobody's match ever starts.
        Assert.Equal(fullerId, result.Value.Session!.Id);
    }

    [Fact]
    public async Task A_session_whose_host_went_quiet_is_never_offered()
    {
        var stale = await MultiplayerTest.OpenAsync(_fixture);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgeSessionAsync(aging, stale.SessionId, seconds: 300);

        await using var context = _fixture.CreateContext();
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(stale.GameId, createIfNoneFound: false));

        // Seating somebody into a session that is about to be swept would put them in a room that is
        // already dying.
        Assert.True(result.Succeeded);
        Assert.Equal(MatchOutcome.NoMatch, result.Value!.Outcome);
        Assert.Null(result.Value.Session);
    }

    [Fact]
    public async Task A_private_session_is_never_offered()
    {
        await using var setup = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(setup);
        var hostId = await TestData.CreateUserAsync(setup);

        var created = await MultiplayerTest.Sessions(setup).CreateAsync(
            hostId,
            MultiplayerTest.CreateRequest(curriculum.GameId, visibility: SessionVisibility.Private));

        await MultiplayerTest.Sessions(setup)
            .StartAsync(hostId, created.Value!.Id, new StartMultiplayerSessionRequest());

        await using var context = _fixture.CreateContext();
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(curriculum.GameId, createIfNoneFound: false));

        Assert.Equal(MatchOutcome.NoMatch, result.Value!.Outcome);
    }

    [Fact]
    public async Task A_session_on_a_different_lesson_is_not_a_match()
    {
        await using var setup = _fixture.CreateContext();
        var otherLesson = await TestData.CreateCurriculumPathAsync(setup);

        var session = await MultiplayerTest.OpenAsync(
            _fixture, path: new CurriculumPathDto { LessonId = otherLesson.LessonId });

        await using var context = _fixture.CreateContext();
        var mine = await TestData.CreateCurriculumPathAsync(context);
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(session.GameId, createIfNoneFound: false, lessonId: mine.LessonId));

        Assert.Equal(MatchOutcome.NoMatch, result.Value!.Outcome);

        // The same search against the lesson that session actually plays does match.
        await using var matching = _fixture.CreateContext();
        var secondSeeker = await TestData.CreateUserAsync(matching);

        var hit = await MultiplayerTest.Matchmaking(matching).MatchmakeAsync(
            secondSeeker, Request(session.GameId, createIfNoneFound: false, lessonId: otherLesson.LessonId));

        Assert.Equal(MatchOutcome.Joined, hit.Value!.Outcome);
    }

    [Fact]
    public async Task A_ranked_search_does_not_match_an_unranked_session()
    {
        var casual = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(casual.GameId, createIfNoneFound: false, isRanked: true));

        Assert.Equal(MatchOutcome.NoMatch, result.Value!.Outcome);
    }

    [Fact]
    public async Task Matchmaking_creates_a_session_when_nothing_is_open()
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var seekerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(seekerId, Request(curriculum.GameId));

        Assert.True(result.Succeeded);
        Assert.Equal(MatchOutcome.Created, result.Value!.Outcome);
        Assert.Equal(seekerId, result.Value.Session!.HostUserId);

        // Created, not Created-and-open: the caller still has to bring the transport room up and
        // confirm it, exactly as a direct create would.
        Assert.Equal(MultiplayerSessionState.Creating, result.Value.Session.State);
    }

    [Fact]
    public async Task Creating_requires_a_transport_name_but_joining_does_not()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var joining = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(joining);

        var request = Request(session.GameId);
        request.TransportSessionName = null;

        // A caller that matches into an existing session never needed a room name, so this is only
        // validated once creating is actually on the table.
        var joined = await MultiplayerTest.Matchmaking(joining).MatchmakeAsync(joinerId, request);

        Assert.True(joined.Succeeded);
        Assert.Equal(MatchOutcome.Joined, joined.Value!.Outcome);

        await using var creating = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(creating);
        var seekerId = await TestData.CreateUserAsync(creating);

        var second = Request(curriculum.GameId);
        second.TransportSessionName = null;

        var refused = await MultiplayerTest.Matchmaking(creating).MatchmakeAsync(seekerId, second);

        Assert.False(refused.Succeeded);
        Assert.Equal("VALIDATION_FAILED", refused.Error!.Code);
    }

    [Fact]
    public async Task Matchmaking_refuses_a_caller_who_is_already_in_a_session()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var result = await MultiplayerTest.Matchmaking(context)
            .MatchmakeAsync(session.HostId, Request(session.GameId));

        Assert.False(result.Succeeded);
        Assert.Equal("ALREADY_IN_SESSION", result.Error!.Code);
    }

    [Fact]
    public async Task A_replayed_matchmake_reports_the_same_outcome()
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var seekerId = await TestData.CreateUserAsync(context);

        var request = Request(curriculum.GameId, requestId: "match-retry");

        var first = await MultiplayerTest.Matchmaking(context).MatchmakeAsync(seekerId, request);
        Assert.Equal(MatchOutcome.Created, first.Value!.Outcome);

        // **This is why the response body is stored rather than re-derived.** Once the session
        // exists, nothing in the schema still records whether this call joined it or made it — so a
        // retry could not otherwise be told which happened.
        await using var retry = _fixture.CreateContext();
        var second = await MultiplayerTest.Matchmaking(retry)
            .MatchmakeAsync(seekerId, Request(curriculum.GameId, requestId: "match-retry"));

        Assert.True(second.Succeeded);
        Assert.Equal(MatchOutcome.Created, second.Value!.Outcome);
        Assert.Equal(first.Value.Session!.Id, second.Value.Session!.Id);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.HostUserId == seekerId));
    }

    [Fact]
    public async Task Concurrent_matchmaking_never_oversubscribes_a_session()
    {
        await using var setup = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(setup);

        // Two seats per session, so twelve seekers must end up spread across at least six sessions.
        await MultiplayerTest.SetSeatsAsync(setup, curriculum.GameId, 1, 2);

        var seekers = new List<Guid>();
        for (var i = 0; i < 12; i++)
            seekers.Add(await TestData.CreateUserAsync(setup));

        var attempts = seekers.Select(async userId =>
        {
            await using var context = _fixture.CreateContext();
            return await MultiplayerTest.Matchmaking(context)
                .MatchmakeAsync(userId, Request(curriculum.GameId));
        });

        var results = await Task.WhenAll(attempts);

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error?.Code));

        await using var check = _fixture.CreateContext();

        var sessions = await check.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.GameId == curriculum.GameId)
            .ToListAsync();

        // **The invariant that matters.** Under contention some seekers join and some create; what
        // must never happen is a session reporting more players than it has seats.
        Assert.All(sessions, s => Assert.True(
            s.CurrentPlayerCount <= s.MaxPlayers,
            $"Session {s.Id} holds {s.CurrentPlayerCount} of {s.MaxPlayers} seats."));

        // Everybody ended up somewhere, exactly once.
        var seated = await check.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => seekers.Contains(p.UserId)
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ToListAsync();

        Assert.Equal(seekers.Count, seated.Count);
        Assert.Equal(seekers.Count, seated.Select(p => p.UserId).Distinct().Count());

        // And the denormalised counts agree with the memberships they are meant to summarise.
        foreach (var session in sessions)
        {
            var actual = seated.Count(p => p.SessionId == session.Id);
            Assert.Equal(session.CurrentPlayerCount, actual);
        }
    }
}
