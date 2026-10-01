using Share7.Application.Multiplayer.Models;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>Asking a classmate into your room, and everything that can happen to the invite.</summary>
[Collection(SqlServerCollection.Name)]
public class SessionInvitationTests
{
    private readonly SqlServerFixture _fixture;

    public SessionInvitationTests(SqlServerFixture fixture) => _fixture = fixture;

    private sealed record Room(Guid HostId, Guid ClassmateId, Guid SessionId);

    /// <summary>A private room, confirmed, whose host has a classmate to invite.</summary>
    private async Task<Room> RoomAsync()
    {
        await using var context = _fixture.CreateContext();
        var (host, classmate) = await SocialTest.ClassmatesAsync(context);
        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.SetSeatsAsync(context, curriculum.GameId, 1, 4);

        var sessions = MultiplayerTest.Sessions(context);
        var created = await sessions.CreateAsync(
            host, MultiplayerTest.CreateRequest(curriculum.GameId, visibility: SessionVisibility.Private));
        await sessions.StartAsync(host, created.Value!.Id, new StartMultiplayerSessionRequest());

        return new Room(host, classmate, created.Value.Id);
    }

    private static InvitePlayerRequest Invite(Guid userId) => new() { UserId = userId };
    private static AcceptInvitationRequest Accept() => new() { ProtocolVersion = 1 };

    [Fact]
    public async Task A_classmate_is_invited_and_hears_about_it_by_handle()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invited = await SocialTest.Invitations(context).InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId));

        Assert.True(invited.Succeeded, invited.Error?.Code);
        Assert.Equal(InvitationState.Pending, invited.Value!.State);

        var heard = Assert.Single(await SocialTest.EventsAsync(context, room.ClassmateId, PlayerEventTypes.InviteReceived));
        Assert.Equal(invited.Value.Id.ToString(), heard.Text("invitationId"));
        Assert.Equal(invited.Value.FromDisplayName, heard.Text("fromDisplayName"));
        Assert.False(string.IsNullOrEmpty(heard.Text("fromDisplayName")));
    }

    [Fact]
    public async Task A_stranger_cannot_be_invited()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var stranger = await TestData.CreateUserAsync(context);

        var refused = await SocialTest.Invitations(context).InviteAsync(room.HostId, room.SessionId, Invite(stranger));

        Assert.Equal("SOCIAL_NOT_ALLOWED", refused.Error?.Code);
        Assert.Empty(await SocialTest.EventsAsync(context, stranger));
    }

    [Fact]
    public async Task Pressing_invite_again_sends_the_same_one_invite()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context);

        var first = await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId));
        var again = await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId));

        Assert.Equal(first.Value!.Id, again.Value!.Id);
        Assert.Single(await SocialTest.EventsAsync(context, room.ClassmateId, PlayerEventTypes.InviteReceived));
    }

    [Fact]
    public async Task Accepting_seats_the_player_tells_the_sender_and_survives_a_retry()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context);
        var invite = (await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId))).Value!;

        var seated = await invitations.AcceptAsync(room.ClassmateId, invite.Id, Accept());

        Assert.True(seated.Succeeded, seated.Error?.Code);
        Assert.Contains(seated.Value!.Players, p => p.UserId == room.ClassmateId);
        Assert.Single(await SocialTest.EventsAsync(context, room.HostId, PlayerEventTypes.InviteAccepted));

        var retry = await invitations.AcceptAsync(room.ClassmateId, invite.Id, Accept());

        Assert.True(retry.Succeeded, retry.Error?.Code);
        Assert.Equal(room.SessionId, retry.Value!.Id);
    }

    [Fact]
    public async Task An_invite_opens_a_private_room_even_once_codes_are_required()
    {
        var room = await RoomAsync();
        var strict = MultiplayerTest.Options(o => o.RequireJoinCodeForPrivateSessions = true);

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context, strict);
        var invite = (await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId))).Value!;

        var seated = await invitations.AcceptAsync(room.ClassmateId, invite.Id, Accept());

        Assert.True(seated.Succeeded, seated.Error?.Code);
    }

    [Fact]
    public async Task A_decline_is_not_announced_to_the_sender()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context);
        var invite = (await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId))).Value!;

        var declined = await invitations.DeclineAsync(room.ClassmateId, invite.Id);

        Assert.Equal(InvitationState.Declined, declined.Value!.State);
        Assert.Empty(await SocialTest.EventsAsync(context, room.HostId));
    }

    [Fact]
    public async Task A_withdrawn_invite_disappears_for_the_recipient_and_cannot_be_accepted()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context);
        var invite = (await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId))).Value!;

        await invitations.CancelAsync(room.HostId, invite.Id);

        Assert.Single(await SocialTest.EventsAsync(context, room.ClassmateId, PlayerEventTypes.InviteCancelled));
        Assert.Empty((await invitations.PendingAsync(room.ClassmateId)).Value!);

        var late = await invitations.AcceptAsync(room.ClassmateId, invite.Id, Accept());
        Assert.Equal("INVITE_NOT_PENDING", late.Error?.Code);
    }

    [Fact]
    public async Task An_invite_to_a_room_that_has_started_has_expired()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context);
        var invite = (await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId))).Value!;

        await MultiplayerTest.Sessions(context).StartAsync(room.HostId, room.SessionId, new StartMultiplayerSessionRequest());
        await MultiplayerTest.Sessions(context).CloseAsync(room.HostId, room.SessionId, new CloseMultiplayerSessionRequest());

        var late = await invitations.AcceptAsync(room.ClassmateId, invite.Id, Accept());

        Assert.Equal("INVITE_NOT_PENDING", late.Error?.Code);
        Assert.Equal("Expired", late.Details!["state"]);
    }

    [Fact]
    public async Task Blocking_withdraws_pending_invites_and_stops_new_ones()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var invitations = SocialTest.Invitations(context);
        await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId));

        await SocialTest.Social(context).BlockAsync(room.ClassmateId, room.HostId);

        Assert.Empty((await invitations.PendingAsync(room.ClassmateId)).Value!);
        Assert.Equal("SOCIAL_NOT_ALLOWED",
            (await invitations.InviteAsync(room.HostId, room.SessionId, Invite(room.ClassmateId))).Error?.Code);
    }

    [Fact]
    public async Task Only_a_player_in_the_room_can_invite_into_it()
    {
        var room = await RoomAsync();

        await using var context = _fixture.CreateContext();
        var outsider = await TestData.CreateUserAsync(context);
        await SocialTest.ClassAsync(context, [outsider, room.ClassmateId]);

        var refused = await SocialTest.Invitations(context).InviteAsync(outsider, room.SessionId, Invite(room.ClassmateId));

        Assert.Equal("SESSION_NOT_FOUND", refused.Error?.Code);
    }
}
