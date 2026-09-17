using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The operator surface. Read-mostly, unscoped, and behind the admin role at the route.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static MultiplayerAdminService Admin(ApplicationDbContext context) =>
        new(context, MultiplayerTest.Sessions(context));

    [Fact]
    public async Task Listing_is_not_scoped_to_any_caller()
    {
        var first = await MultiplayerTest.OpenAsync(_fixture);
        var second = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var result = await Admin(context).ListAsync(new MultiplayerAdminQuery());

        Assert.True(result.Succeeded);

        // Every other read in this domain answers only for the caller's own sessions. This one is
        // the exception, which is why it lives behind the admin role on its own controller.
        Assert.Contains(result.Value!.Sessions, s => s.Id == first.SessionId);
        Assert.Contains(result.Value.Sessions, s => s.Id == second.SessionId);
    }

    [Fact]
    public async Task Listing_filters_by_game_and_state()
    {
        var running = await MultiplayerTest.OpenAsync(_fixture);
        var other = await MultiplayerTest.OpenAsync(_fixture);

        await using var starting = _fixture.CreateContext();
        await MultiplayerTest.Sessions(starting)
            .StartAsync(running.HostId, running.SessionId, new StartMultiplayerSessionRequest());

        await using var context = _fixture.CreateContext();

        var byState = await Admin(context).ListAsync(new MultiplayerAdminQuery
        {
            State = MultiplayerSessionState.Running
        });

        Assert.Contains(byState.Value!.Sessions, s => s.Id == running.SessionId);
        Assert.DoesNotContain(byState.Value.Sessions, s => s.Id == other.SessionId);

        var byGame = await Admin(context).ListAsync(new MultiplayerAdminQuery { GameId = other.GameId });

        Assert.All(byGame.Value!.Sessions, s => Assert.Equal(other.GameId, s.GameId));
    }

    [Fact]
    public async Task Listing_reports_the_true_total_even_when_the_answer_is_truncated()
    {
        for (var i = 0; i < 3; i++)
            await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var result = await Admin(context).ListAsync(new MultiplayerAdminQuery { Limit = 1 });

        Assert.Single(result.Value!.Sessions);

        // **A truncated answer has to look truncated.** An operator who cannot tell "these are all
        // of them" from "these are the first one" will read the second as the first.
        Assert.True(result.Value.TotalMatching >= 3);
    }

    [Fact]
    public async Task Listing_carries_the_two_fields_the_player_facing_shape_omits()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var closing = _fixture.CreateContext();
        await MultiplayerTest.Sessions(closing)
            .CloseAsync(session.HostId, session.SessionId, new CloseMultiplayerSessionRequest());

        await using var context = _fixture.CreateContext();
        var result = await Admin(context).ListAsync(new MultiplayerAdminQuery());

        var row = Assert.Single(result.Value!.Sessions, s => s.Id == session.SessionId);

        // "Why did this match vanish" and "is it genuinely live" — the two questions the operator
        // surface exists to answer without a database session.
        Assert.Equal(SessionClosedReason.HostClosed, row.ClosedReason);
        Assert.NotEqual(default, row.LastHeartbeatAtUtc);
        Assert.NotEqual(default, result.Value.ServerTimeUtc);
    }

    [Fact]
    public async Task An_over_large_limit_is_clamped_rather_than_refused()
    {
        await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var result = await Admin(context).ListAsync(new MultiplayerAdminQuery { Limit = 100_000 });

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Sessions.Count <= 500);
    }

    [Fact]
    public async Task The_admin_roster_includes_members_who_have_left()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var leaving = _fixture.CreateContext();
        await MultiplayerTest.Sessions(leaving)
            .LeaveAsync(joinerId, session.SessionId, new LeaveMultiplayerSessionRequest());

        await using var context = _fixture.CreateContext();

        // The player-facing roster shows seats, so it drops departures. The operator one shows
        // history, because who left and when is the substance of most support questions.
        var playerFacing = await MultiplayerTest.Sessions(context)
            .GetPlayersAsync(session.HostId, session.SessionId);

        Assert.DoesNotContain(playerFacing.Value!, p => p.UserId == joinerId);

        var operatorView = await Admin(context).GetPlayersAsync(session.SessionId);

        var departed = Assert.Single(operatorView.Value!, p => p.UserId == joinerId);
        Assert.Equal(SessionPlayerStatus.Left, departed.Status);
    }

    [Fact]
    public async Task The_admin_roster_of_an_unknown_session_is_not_found()
    {
        await using var context = _fixture.CreateContext();
        var result = await Admin(context).GetPlayersAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
        Assert.Equal("SESSION_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task A_forced_close_ends_the_session_and_releases_every_seat()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var closed = await Admin(context).CloseAsync(session.SessionId);

        Assert.True(closed.Succeeded);
        Assert.Equal(MultiplayerSessionState.Closed, closed.Value!.State);
        Assert.Equal(SessionClosedReason.AdminClosed, closed.Value.ClosedReason);
        Assert.Equal(0, closed.Value.CurrentPlayerCount);

        await using var check = _fixture.CreateContext();
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);

        // **The seats have to go with it.** An admin close that forgot this would leave both accounts
        // unable to join anything ever again, because the one-session-at-a-time rule reads
        // membership rather than session state. Routing through the shared close is what guarantees
        // it — which this proves by starting something new.
        Assert.All(players, p => Assert.Equal(SessionPlayerStatus.Left, p.Status));

        await using var next = _fixture.CreateContext();
        var fresh = await TestData.CreateCurriculumPathAsync(next);
        var restart = await MultiplayerTest.Sessions(next)
            .CreateAsync(joinerId, MultiplayerTest.CreateRequest(fresh.GameId));

        Assert.True(restart.Succeeded);
    }

    [Fact]
    public async Task A_forced_close_does_not_rewrite_a_session_that_already_ended()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var closing = _fixture.CreateContext();
        await MultiplayerTest.Sessions(closing)
            .CloseAsync(session.HostId, session.SessionId, new CloseMultiplayerSessionRequest());

        await using var before = _fixture.CreateContext();
        var original = await MultiplayerTest.ReadSessionAsync(before, session.SessionId);

        await using var context = _fixture.CreateContext();
        var forced = await Admin(context).CloseAsync(session.SessionId);

        Assert.True(forced.Succeeded);

        // An operator can end a match. They cannot make a match that ended normally look as though
        // they had ended it — the audit trail says what happened, not what happened last.
        Assert.Equal(SessionClosedReason.HostClosed, forced.Value!.ClosedReason);
        Assert.Equal(original.EndedAtUtc, forced.Value.EndedAtUtc);
    }

    [Fact]
    public async Task Closing_an_unknown_session_is_not_found()
    {
        await using var context = _fixture.CreateContext();
        var result = await Admin(context).CloseAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Older_than_selects_what_is_still_hanging_around()
    {
        var old = await MultiplayerTest.OpenAsync(_fixture);
        var recent = await MultiplayerTest.OpenAsync(_fixture);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgeSessionAsync(aging, old.SessionId, seconds: 7200);

        await using var context = _fixture.CreateContext();
        var result = await Admin(context).ListAsync(new MultiplayerAdminQuery
        {
            OlderThanUtc = DateTime.UtcNow.AddHours(-1)
        });

        Assert.Contains(result.Value!.Sessions, s => s.Id == old.SessionId);
        Assert.DoesNotContain(result.Value.Sessions, s => s.Id == recent.SessionId);
    }
}
