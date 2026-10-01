using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Social;
using Share7.Domain.Organizations;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Who may reach whom: classmates yes, strangers never, a block in either direction overrides, and
/// nobody can tell a block from "not connected".
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SocialPolicyTests
{
    private readonly SqlServerFixture _fixture;

    public SocialPolicyTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Classmates_may_reach_each_other_and_a_stranger_may_not()
    {
        await using var context = _fixture.CreateContext();
        var (a, b) = await SocialTest.ClassmatesAsync(context);
        var stranger = await TestData.CreateUserAsync(context);
        var policy = SocialTest.Policy(context);

        Assert.True((await policy.CanInteractAsync(a, b, SocialAction.Invite)).Allowed);
        Assert.True((await policy.CanInteractAsync(b, a, SocialAction.Challenge)).Allowed);
        Assert.False((await policy.CanInteractAsync(a, stranger, SocialAction.Invite)).Allowed);
        Assert.False((await policy.CanInteractAsync(stranger, a, SocialAction.SeePresence)).Allowed);
    }

    [Fact]
    public async Task A_block_either_way_reads_exactly_like_not_being_connected()
    {
        await using var context = _fixture.CreateContext();
        var (a, b) = await SocialTest.ClassmatesAsync(context);
        var stranger = await TestData.CreateUserAsync(context);

        await SocialTest.Social(context).BlockAsync(b, a);
        var policy = SocialTest.Policy(context);

        var blockedWay = await policy.CanInteractAsync(b, a, SocialAction.Invite);
        var otherWay = await policy.CanInteractAsync(a, b, SocialAction.Invite);
        var unconnected = await policy.CanInteractAsync(a, stranger, SocialAction.Invite);

        Assert.False(blockedWay.Allowed);
        Assert.False(otherWay.Allowed);

        // The blocked player cannot learn they were blocked by comparing refusals.
        Assert.Equal(unconnected.Reason, otherWay.Reason);
    }

    [Fact]
    public async Task A_teacher_is_not_a_playmate_and_a_class_that_ended_connects_nobody()
    {
        await using var context = _fixture.CreateContext();
        var learner = await TestData.CreateUserAsync(context);
        var classmate = await TestData.CreateUserAsync(context);
        var teacher = await TestData.CreateUserAsync(context);
        var cohortId = await SocialTest.ClassAsync(context, [learner, classmate], teacher);
        var policy = SocialTest.Policy(context);

        Assert.False((await policy.CanInteractAsync(teacher, learner, SocialAction.Invite)).Allowed);
        Assert.True((await policy.CanInteractAsync(learner, classmate, SocialAction.Invite)).Allowed);

        await context.Cohorts.Where(c => c.Id == cohortId)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CohortStatus.Archived));

        Assert.False((await policy.CanInteractAsync(learner, classmate, SocialAction.Invite)).Allowed);
    }

    [Fact]
    public async Task Real_names_are_never_allowed()
    {
        await using var context = _fixture.CreateContext();
        var (a, b) = await SocialTest.ClassmatesAsync(context);

        Assert.False((await SocialTest.Policy(context).CanInteractAsync(a, b, SocialAction.SeeRealName)).Allowed);
    }

    [Fact]
    public async Task The_play_with_list_is_classmates_by_handle_with_presence_and_never_the_blocked()
    {
        await using var context = _fixture.CreateContext();
        var me = await TestData.CreateUserAsync(context);
        var online = await TestData.CreateUserAsync(context);
        var inLobby = await TestData.CreateUserAsync(context);
        var blocked = await TestData.CreateUserAsync(context);
        await SocialTest.ClassAsync(context, [me, online, inLobby, blocked]);

        await SocialTest.Presence(context).TouchAsync(online);

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.Sessions(context).CreateAsync(inLobby, MultiplayerTest.CreateRequest(curriculum.GameId));

        await SocialTest.Social(context).BlockAsync(me, blocked);

        var list = (await SocialTest.Social(context).ConnectionsAsync(me)).Value!;

        Assert.Equal(new[] { inLobby, online }.OrderBy(x => x), list.Select(c => c.UserId).OrderBy(x => x));
        Assert.Equal(PlayerPresenceState.Online, list.Single(c => c.UserId == online).Presence);
        Assert.Equal(PlayerPresenceState.InLobby, list.Single(c => c.UserId == inLobby).Presence);
        Assert.All(list, c => Assert.Equal(SocialRelation.Classmate, c.Relation));
        Assert.All(list, c => Assert.False(string.IsNullOrEmpty(c.DisplayName)));
    }

    [Fact]
    public async Task Blocking_any_id_succeeds_and_reveals_nothing_about_it()
    {
        await using var context = _fixture.CreateContext();
        var me = await TestData.CreateUserAsync(context);

        var result = await SocialTest.Social(context).BlockAsync(me, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Empty((await SocialTest.Social(context).BlocksAsync(me)).Value!);
    }

    [Fact]
    public async Task Matchmaking_never_seats_a_blocked_pair_together()
    {
        var open = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var seeker = await TestData.CreateUserAsync(context);
        await SocialTest.Social(context).BlockAsync(open.HostId, seeker);

        var result = await MultiplayerTest.Matchmaking(context).MatchmakeAsync(seeker, new MatchmakeRequest
        {
            GameId = open.GameId,
            ProtocolVersion = 1,
            CreateIfNoneFound = false,
            TransportSessionName = MultiplayerTest.NewTransportName()
        });

        // The only open lobby is hosted by someone who blocked the seeker: no match, and no reason given.
        Assert.True(result.Succeeded, result.Error?.Code);
        Assert.Equal(MatchOutcome.NoMatch, result.Value!.Outcome);
    }
}
