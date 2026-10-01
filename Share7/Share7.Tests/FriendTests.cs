using Microsoft.EntityFrameworkCore;
using Share7.Application.Social;
using Share7.Domain.Entities;
using Share7.Domain.Feed;
using Share7.Domain.Organizations;
using Share7.Domain.Social;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Friends by code: only between players allowed friends, only when both agree, and never a way for
/// a leaked code — or a block — to reach a child.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class FriendTests
{
    private readonly SqlServerFixture _fixture;

    public FriendTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>A child whose guardian has turned playing with friends on.</summary>
    private static async Task<Guid> AllowedAsync(Share7.Infrastructure.Persistence.ApplicationDbContext context)
    {
        var userId = await TestData.CreateUserAsync(context);
        await SocialTest.GuardianConsentAsync(context, userId);
        return userId;
    }

    private static async Task<Guid> AgedAsync(Share7.Infrastructure.Persistence.ApplicationDbContext context, int age)
    {
        var userId = await TestData.CreateUserAsync(context);

        context.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = "Test Learner",
            Age = age,
            GradeId = await context.Grades.Select(g => g.Id).FirstAsync(),
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }

    private static AddFriendRequest Code(string code) => new() { Code = code };

    [Fact]
    public async Task A_child_without_a_guardians_consent_has_no_friend_code_but_an_adult_does()
    {
        await using var context = _fixture.CreateContext();
        var child = await AgedAsync(context, 10);
        var unknownAge = await TestData.CreateUserAsync(context);
        var adult = await AgedAsync(context, 19);
        var friends = SocialTest.Friends(context);

        Assert.Equal("SOCIAL_CONSENT_REQUIRED", (await friends.GetCodeAsync(child)).Error?.Code);

        // Not knowing someone's age is not a reason to treat them as an adult.
        Assert.Equal("SOCIAL_CONSENT_REQUIRED", (await friends.GetCodeAsync(unknownAge)).Error?.Code);

        var code = await friends.GetCodeAsync(adult);
        Assert.True(code.Succeeded, code.Error?.Code);
        Assert.Equal(8, code.Value!.Code.Length);
        Assert.Equal(code.Value.Code, (await friends.GetCodeAsync(adult)).Value!.Code);
    }

    [Fact]
    public async Task A_friendship_needs_both_to_agree_and_then_they_can_play_together()
    {
        await using var context = _fixture.CreateContext();
        var a = await AllowedAsync(context);
        var b = await AllowedAsync(context);
        var friends = SocialTest.Friends(context);

        var code = (await friends.GetCodeAsync(a)).Value!.Code;
        var request = await friends.AddByCodeAsync(b, Code(code.ToLowerInvariant()));

        Assert.Equal(FriendRequestState.Pending, request.Value!.State);
        Assert.False((await SocialTest.Policy(context).CanInteractAsync(b, a, SocialAction.Invite)).Allowed);
        Assert.Single(await SocialTest.EventsAsync(context, a, PlayerEventTypes.FriendRequestReceived));

        var accepted = await friends.AcceptAsync(a, request.Value.Id);

        Assert.Equal(FriendRequestState.Accepted, accepted.Value!.State);
        Assert.Single(await SocialTest.EventsAsync(context, b, PlayerEventTypes.FriendRequestAccepted));

        // Not classmates — friends — and that is now enough to invite.
        Assert.True((await SocialTest.Policy(context).CanInteractAsync(b, a, SocialAction.Invite)).Allowed);
        var list = (await SocialTest.Social(context).ConnectionsAsync(b)).Value!;
        Assert.Equal(SocialRelation.Friend, Assert.Single(list).Relation);
    }

    [Fact]
    public async Task Entering_each_others_codes_makes_one_friendship()
    {
        await using var context = _fixture.CreateContext();
        var a = await AllowedAsync(context);
        var b = await AllowedAsync(context);
        var friends = SocialTest.Friends(context);

        await friends.AddByCodeAsync(a, Code((await friends.GetCodeAsync(b)).Value!.Code));
        var mutual = await friends.AddByCodeAsync(b, Code((await friends.GetCodeAsync(a)).Value!.Code));

        Assert.Equal(FriendRequestState.Accepted, mutual.Value!.State);
        Assert.Equal(2, await context.Friendships.CountAsync(f => (f.UserId == a && f.FriendUserId == b) || (f.UserId == b && f.FriendUserId == a)));
    }

    [Fact]
    public async Task Every_unusable_code_gets_the_same_answer()
    {
        await using var context = _fixture.CreateContext();
        var me = await AllowedAsync(context);
        var blocker = await AllowedAsync(context);
        var noConsent = await AgedAsync(context, 11);
        var friends = SocialTest.Friends(context);

        var blockersCode = (await friends.GetCodeAsync(blocker)).Value!.Code;
        await SocialTest.Social(context).BlockAsync(blocker, me);

        // A code minted while consent held, then consent withdrawn.
        await SocialTest.GuardianConsentAsync(context, noConsent);
        var withdrawnCode = (await friends.GetCodeAsync(noConsent)).Value!.Code;
        await context.GuardianLinks.Where(g => g.LearnerUserId == noConsent)
            .ExecuteUpdateAsync(set => set.SetProperty(g => g.RevokedAtUtc, DateTime.UtcNow));

        foreach (var code in new[] { "ZZZZZZZZ", blockersCode, withdrawnCode })
            Assert.Equal("FRIEND_CODE_NOT_FOUND", (await friends.AddByCodeAsync(me, Code(code))).Error?.Code);

        Assert.Equal("VALIDATION_FAILED",
            (await friends.AddByCodeAsync(me, Code((await friends.GetCodeAsync(me)).Value!.Code))).Error?.Code);
    }

    [Fact]
    public async Task A_rotated_code_opens_nothing()
    {
        await using var context = _fixture.CreateContext();
        var a = await AllowedAsync(context);
        var b = await AllowedAsync(context);
        var friends = SocialTest.Friends(context);

        var old = (await friends.GetCodeAsync(a)).Value!.Code;
        var fresh = (await friends.RotateCodeAsync(a)).Value!.Code;

        Assert.NotEqual(old, fresh);
        Assert.Equal("FRIEND_CODE_NOT_FOUND", (await friends.AddByCodeAsync(b, Code(old))).Error?.Code);
        Assert.True((await friends.AddByCodeAsync(b, Code(fresh))).Succeeded);
    }

    [Fact]
    public async Task Withdrawing_consent_or_blocking_ends_a_friendship()
    {
        await using var context = _fixture.CreateContext();
        var a = await AllowedAsync(context);
        var b = await AllowedAsync(context);
        var c = await AllowedAsync(context);
        var friends = SocialTest.Friends(context);

        var ab = await friends.AddByCodeAsync(b, Code((await friends.GetCodeAsync(a)).Value!.Code));
        await friends.AcceptAsync(a, ab.Value!.Id);
        var ac = await friends.AddByCodeAsync(c, Code((await friends.GetCodeAsync(a)).Value!.Code));
        await friends.AcceptAsync(a, ac.Value!.Id);

        await context.GuardianLinks.Where(g => g.LearnerUserId == b)
            .ExecuteUpdateAsync(set => set.SetProperty(g => g.RevokedAtUtc, DateTime.UtcNow));

        Assert.False((await SocialTest.Policy(context).CanInteractAsync(a, b, SocialAction.Challenge)).Allowed);

        await SocialTest.Social(context).BlockAsync(c, a);

        Assert.False(await context.Friendships.AnyAsync(f => (f.UserId == a && f.FriendUserId == c) || (f.UserId == c && f.FriendUserId == a)));
        Assert.False((await SocialTest.Policy(context).CanInteractAsync(a, c, SocialAction.Invite)).Allowed);
    }

    [Fact]
    public async Task A_declined_request_is_not_announced()
    {
        await using var context = _fixture.CreateContext();
        var a = await AllowedAsync(context);
        var b = await AllowedAsync(context);
        var friends = SocialTest.Friends(context);

        var request = await friends.AddByCodeAsync(b, Code((await friends.GetCodeAsync(a)).Value!.Code));
        var declined = await friends.DeclineAsync(a, request.Value!.Id);

        Assert.Equal(FriendRequestState.Declined, declined.Value!.State);
        Assert.Empty(await SocialTest.EventsAsync(context, b));
    }
}
