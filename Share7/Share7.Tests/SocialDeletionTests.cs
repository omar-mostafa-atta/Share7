using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Infrastructure.Users;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// An account on either side of every social table — blocks, friendships, requests, invites,
/// challenges, the feed — deletes cleanly, and the other player's record of them goes too.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SocialDeletionTests
{
    private readonly SqlServerFixture _fixture;

    public SocialDeletionTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Deleting_a_player_removes_them_from_every_social_record_both_ways()
    {
        await using var context = _fixture.CreateContext();

        var (leaving, staying) = await SocialTest.ClassmatesAsync(context);
        var blocker = await TestData.CreateUserAsync(context);
        await SocialTest.GuardianConsentAsync(context, leaving);
        await SocialTest.GuardianConsentAsync(context, staying);

        // Friends both ways, a block against them, an invite from them, and a feed.
        var friends = SocialTest.Friends(context);
        var request = await friends.AddByCodeAsync(staying, new() { Code = (await friends.GetCodeAsync(leaving)).Value!.Code });
        await friends.AcceptAsync(leaving, request.Value!.Id);
        await SocialTest.Social(context).BlockAsync(blocker, leaving);

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var sessions = MultiplayerTest.Sessions(context);
        var room = await sessions.CreateAsync(leaving, MultiplayerTest.CreateRequest(curriculum.GameId));
        await sessions.StartAsync(leaving, room.Value!.Id, new StartMultiplayerSessionRequest());
        await SocialTest.Invitations(context).InviteAsync(leaving, room.Value.Id, new InvitePlayerRequest { UserId = staying });

        // Its own context, as its own request would have: nothing from the setup is still tracked.
        await using var deleting = _fixture.CreateContext();
        var deleted = await new AccountDeletionService(IdentityTestHost.CreateUserManager(deleting), deleting)
            .DeleteOwnAccountAsync(leaving);

        Assert.True(deleted.Succeeded, string.Join("; ", deleted.Errors));

        await using var check = _fixture.CreateContext();
        Assert.False(await check.Friendships.AnyAsync(f => f.UserId == leaving || f.FriendUserId == leaving));
        Assert.False(await check.FriendRequests.AnyAsync(r => r.SenderUserId == leaving || r.RecipientUserId == leaving));
        Assert.False(await check.PlayerBlocks.AnyAsync(b => b.UserId == leaving || b.BlockedUserId == leaving));
        Assert.False(await check.SessionInvitations.AnyAsync(i => i.SenderUserId == leaving || i.RecipientUserId == leaving));
        Assert.False(await check.PlayerEvents.AnyAsync(e => e.RecipientUserId == leaving));
        Assert.False(await check.PlayerFriendCodes.AnyAsync(c => c.UserId == leaving));
        Assert.True(await check.Users.AnyAsync(u => u.Id == staying));
    }
}
