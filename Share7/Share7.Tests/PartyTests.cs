using Share7.Application.Multiplayer.Models;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>Parties: classmates who stay together, and a leader who opens rooms for all of them at once.</summary>
[Collection(SqlServerCollection.Name)]
public class PartyTests
{
    private readonly SqlServerFixture _fixture;

    public PartyTests(SqlServerFixture fixture) => _fixture = fixture;

    private static InvitePlayerRequest Invite(Guid userId) => new() { UserId = userId };

    /// <summary>A class of <paramref name="size"/> learners, the first leading a party the second has joined.</summary>
    private async Task<(Guid[] Class, Guid PartyId)> PartyOfTwoAsync(int size = 3, MultiplayerOptions? options = null)
    {
        await using var context = _fixture.CreateContext();
        var learners = new Guid[size];
        for (var i = 0; i < size; i++) learners[i] = await TestData.CreateUserAsync(context);
        await SocialTest.ClassAsync(context, learners);

        var parties = SocialTest.Parties(context, options);
        var party = (await parties.CreateAsync(learners[0])).Value!;
        var invite = (await parties.InviteAsync(learners[0], party.Id, Invite(learners[1]))).Value!;
        var joined = await parties.AcceptInviteAsync(learners[1], invite.Id);
        Assert.True(joined.Succeeded, joined.Error?.Code);

        return (learners, party.Id);
    }

    [Fact]
    public async Task A_classmate_joins_and_the_party_hears_about_it()
    {
        var (learners, partyId) = await PartyOfTwoAsync();

        await using var context = _fixture.CreateContext();
        var party = (await SocialTest.Parties(context).CurrentAsync(learners[1])).Value!;

        Assert.Equal(partyId, party.Id);
        Assert.Equal(2, party.Members.Count);
        Assert.True(party.Members.Single(m => m.UserId == learners[0]).IsLeader);
        Assert.All(party.Members, m => Assert.False(string.IsNullOrEmpty(m.DisplayName)));
        Assert.Single(await SocialTest.EventsAsync(context, learners[0], PlayerEventTypes.PartyUpdated));
        Assert.Single(await SocialTest.EventsAsync(context, learners[1], PlayerEventTypes.PartyInviteReceived));
    }

    [Fact]
    public async Task A_party_holds_no_more_than_its_size()
    {
        var small = MultiplayerTest.Options(o => o.PartyMaxSize = 2);
        var (learners, partyId) = await PartyOfTwoAsync(options: small);

        await using var context = _fixture.CreateContext();
        var parties = SocialTest.Parties(context, small);

        // Invited before it filled, accepted after: the count, not the invite, decides.
        await parties.LeaveAsync(learners[1], partyId);
        var late = (await parties.InviteAsync(learners[0], partyId, Invite(learners[2]))).Value!;
        var back = (await parties.InviteAsync(learners[0], partyId, Invite(learners[1]))).Value!;
        await parties.AcceptInviteAsync(learners[1], back.Id);

        Assert.Equal("PARTY_FULL", (await parties.AcceptInviteAsync(learners[2], late.Id)).Error?.Code);
    }

    [Fact]
    public async Task Joining_another_party_moves_the_player_out_of_the_old_one()
    {
        var (learners, firstParty) = await PartyOfTwoAsync();

        await using var context = _fixture.CreateContext();
        var parties = SocialTest.Parties(context);

        var second = (await parties.CreateAsync(learners[2])).Value!;
        var invite = (await parties.InviteAsync(learners[2], second.Id, Invite(learners[1]))).Value!;
        var moved = await parties.AcceptInviteAsync(learners[1], invite.Id);

        Assert.Equal(second.Id, moved.Value!.Id);

        var old = (await parties.CurrentAsync(learners[0])).Value!;
        Assert.Equal(firstParty, old.Id);
        Assert.Equal(learners[0], Assert.Single(old.Members).UserId);
    }

    [Fact]
    public async Task A_leader_leaving_hands_over_and_the_last_one_out_ends_it()
    {
        var (learners, partyId) = await PartyOfTwoAsync();

        await using var context = _fixture.CreateContext();
        var parties = SocialTest.Parties(context);

        await parties.LeaveAsync(learners[0], partyId);

        var handed = (await parties.CurrentAsync(learners[1])).Value!;
        Assert.Equal(learners[1], handed.LeaderUserId);

        await parties.LeaveAsync(learners[1], partyId);

        Assert.Equal("PARTY_NOT_FOUND", (await parties.CurrentAsync(learners[1])).Error?.Code);
    }

    [Fact]
    public async Task Only_the_leader_invites_removes_or_starts_play_and_never_a_stranger()
    {
        var (learners, partyId) = await PartyOfTwoAsync();

        await using var context = _fixture.CreateContext();
        var parties = SocialTest.Parties(context);
        var stranger = await TestData.CreateUserAsync(context);

        Assert.Equal("NOT_PARTY_LEADER", (await parties.InviteAsync(learners[1], partyId, Invite(learners[2]))).Error?.Code);
        Assert.Equal("NOT_PARTY_LEADER", (await parties.RemoveAsync(learners[1], partyId, learners[0])).Error?.Code);
        Assert.Equal("SOCIAL_NOT_ALLOWED", (await parties.InviteAsync(learners[0], partyId, Invite(stranger))).Error?.Code);

        var removed = await parties.RemoveAsync(learners[0], partyId, learners[1]);
        Assert.Single(removed.Value!.Members);
    }

    [Fact]
    public async Task Play_opens_a_room_kept_for_the_party_and_calls_everyone_in_once()
    {
        var (learners, partyId) = await PartyOfTwoAsync();

        await using var context = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var stranger = await TestData.CreateUserAsync(context);
        var parties = SocialTest.Parties(context);

        var play = new PartyPlayRequest
        {
            GameId = curriculum.GameId,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            ProtocolVersion = 1,
            RequestId = "party-play-once"
        };

        var room = await parties.PlayAsync(learners[0], partyId, play);
        var retried = await parties.PlayAsync(learners[0], partyId, play);

        Assert.True(room.Succeeded, room.Error?.Code);
        Assert.Equal(room.Value!.Id, retried.Value!.Id);
        Assert.Single(await SocialTest.EventsAsync(context, learners[1], PlayerEventTypes.PartyPlayStarted));
        Assert.Equal(room.Value.Id, (await parties.CurrentAsync(learners[1])).Value!.CurrentSessionId);

        var sessions = MultiplayerTest.Sessions(context);
        await sessions.StartAsync(learners[0], room.Value.Id, new StartMultiplayerSessionRequest());

        Assert.Equal("SESSION_RESERVED",
            (await sessions.JoinAsync(stranger, room.Value.Id, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 })).Error?.Code);
        Assert.True((await sessions.JoinAsync(learners[1], room.Value.Id, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 })).Succeeded);
    }

    [Fact]
    public async Task Play_waits_for_a_member_still_in_a_room_and_says_who()
    {
        var (learners, partyId) = await PartyOfTwoAsync();

        await using var context = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.Sessions(context).CreateAsync(learners[1], MultiplayerTest.CreateRequest(curriculum.GameId));

        var refused = await SocialTest.Parties(context).PlayAsync(learners[0], partyId, new PartyPlayRequest
        {
            GameId = curriculum.GameId,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            ProtocolVersion = 1
        });

        Assert.Equal("PARTY_MEMBER_BUSY", refused.Error?.Code);
        Assert.Equal([learners[1]], (IEnumerable<Guid>)refused.Details!["userIds"]!);
    }
}
