using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Host migration. Photon may elect a new host without asking anyone; these are the tests for the
/// backend half — arbitrating who actually holds authority, and making sure a host that comes back
/// cannot take it away again.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerHostTransferTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerHostTransferTests(SqlServerFixture fixture) => _fixture = fixture;

    private static TransferHostRequest Claim(Guid toUserId, HostTransferReason reason = HostTransferReason.HostUnreachable) =>
        new() { ToUserId = toUserId, Reason = reason };

    [Fact]
    public async Task The_current_host_may_hand_over_at_any_time()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var moved = await MultiplayerTest.Sessions(context).TransferHostAsync(
            session.HostId, session.SessionId, Claim(joinerId, HostTransferReason.Voluntary));

        Assert.True(moved.Succeeded);
        Assert.Equal(joinerId, moved.Value!.HostUserId);

        // The denormalised flag moves in the same transaction, so the roster and the session can
        // never disagree about who is in charge.
        Assert.True(Assert.Single(moved.Value.Players, p => p.UserId == joinerId).IsHost);
        Assert.False(Assert.Single(moved.Value.Players, p => p.UserId == session.HostId).IsHost);
    }

    [Fact]
    public async Task A_member_cannot_claim_a_host_that_is_still_active()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var claim = await MultiplayerTest.Sessions(context)
            .TransferHostAsync(joinerId, session.SessionId, Claim(joinerId));

        Assert.False(claim.Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, claim.ErrorKind);
        Assert.Equal("HOST_STILL_ACTIVE", claim.Error!.Code);
    }

    [Fact]
    public async Task A_member_may_claim_a_host_that_has_gone_quiet()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgePlayerAsync(aging, session.SessionId, session.HostId, seconds: 120);

        await using var context = _fixture.CreateContext();
        var claim = await MultiplayerTest.Sessions(context)
            .TransferHostAsync(joinerId, session.SessionId, Claim(joinerId));

        Assert.True(claim.Succeeded);
        Assert.Equal(joinerId, claim.Value!.HostUserId);
    }

    [Fact]
    public async Task A_member_cannot_install_a_third_party_as_host()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var claimantId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);
        var thirdId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgePlayerAsync(aging, session.SessionId, session.HostId, seconds: 120);

        await using var context = _fixture.CreateContext();

        // The grace period has elapsed, so a claim is legitimate — but only for the caller. Allowing
        // a member to name somebody else would make authority transferable by anyone at will.
        var claim = await MultiplayerTest.Sessions(context)
            .TransferHostAsync(claimantId, session.SessionId, Claim(thirdId));

        Assert.False(claim.Succeeded);
        Assert.Equal("NOT_SESSION_HOST", claim.Error!.Code);
    }

    [Fact]
    public async Task Authority_cannot_be_given_to_someone_who_is_not_a_member()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var moved = await MultiplayerTest.Sessions(context)
            .TransferHostAsync(session.HostId, session.SessionId, Claim(strangerId, HostTransferReason.Voluntary));

        Assert.False(moved.Succeeded);
        Assert.Equal("NOT_SESSION_MEMBER", moved.Error!.Code);
    }

    [Fact]
    public async Task Simultaneous_claims_produce_exactly_one_host()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 6);

        var claimants = new List<Guid>();
        for (var i = 0; i < 4; i++)
            claimants.Add(await MultiplayerTest.JoinAsync(_fixture, session.SessionId));

        await using (var aging = _fixture.CreateContext())
            await MultiplayerTest.AgePlayerAsync(aging, session.SessionId, session.HostId, seconds: 120);

        // A context per claimant — this is the OnHostMigration stampede, where every client decides
        // at once that it holds authority.
        var attempts = claimants.Select(async userId =>
        {
            await using var context = _fixture.CreateContext();
            return await MultiplayerTest.Sessions(context)
                .TransferHostAsync(userId, session.SessionId, Claim(userId));
        });

        var results = await Task.WhenAll(attempts);

        // **Exactly one.** RowVersion is what decides it; the losers are told who won rather than
        // retrying into a second migration.
        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal("HOST_STILL_ACTIVE", r.Error!.Code));

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        var winner = results.Single(r => r.Succeeded).Value!;

        Assert.Equal(winner.HostUserId, stored.HostUserId);

        // And the flag is single-valued: two rows claiming to be host is the shape of a session
        // where two clients both think they are simulating.
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);
        Assert.Single(players, p => p.IsHost);
        Assert.Equal(stored.HostUserId, Assert.Single(players, p => p.IsHost).UserId);
    }

    [Fact]
    public async Task The_old_host_is_refused_after_it_loses_authority()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var successorId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgePlayerAsync(aging, session.SessionId, session.HostId, seconds: 120);

        await using var claiming = _fixture.CreateContext();
        var claim = await MultiplayerTest.Sessions(claiming)
            .TransferHostAsync(successorId, session.SessionId, Claim(successorId));

        Assert.True(claim.Succeeded);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        // **This is the mechanism, not a side effect.** A host that dropped out and came back learns
        // it lost authority from these refusals — it can neither keep the session alive, nor restart
        // it, nor close it out from under the player now running the match.
        var beat = await sessions.HeartbeatAsync(
            session.HostId, session.SessionId, new HeartbeatRequest { ConnectedUserIds = [session.HostId] });

        Assert.Equal("NOT_SESSION_HOST", beat.Error!.Code);

        var close = await sessions.CloseAsync(
            session.HostId, session.SessionId, new CloseMultiplayerSessionRequest());

        Assert.Equal("NOT_SESSION_HOST", close.Error!.Code);

        var start = await sessions.StartAsync(
            session.HostId, session.SessionId, new StartMultiplayerSessionRequest());

        Assert.Equal("NOT_SESSION_HOST", start.Error!.Code);
    }

    [Fact]
    public async Task Transferring_to_the_current_host_is_a_no_op_rather_than_an_error()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();

        // A client retrying an unacknowledged claim asked for a state that already holds.
        var moved = await MultiplayerTest.Sessions(context).TransferHostAsync(
            session.HostId, session.SessionId, Claim(session.HostId, HostTransferReason.Voluntary));

        Assert.True(moved.Succeeded);
        Assert.Equal(session.HostId, moved.Value!.HostUserId);
    }

    [Fact]
    public async Task Authority_cannot_move_in_a_session_that_has_ended()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var closing = _fixture.CreateContext();
        await MultiplayerTest.Sessions(closing)
            .CloseAsync(session.HostId, session.SessionId, new CloseMultiplayerSessionRequest());

        await using var context = _fixture.CreateContext();
        var claim = await MultiplayerTest.Sessions(context)
            .TransferHostAsync(joinerId, session.SessionId, Claim(joinerId));

        Assert.False(claim.Succeeded);
        Assert.Equal("SESSION_CLOSED", claim.Error!.Code);
    }
}
