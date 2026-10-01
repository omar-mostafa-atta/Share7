using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Progress.Models;
using Share7.Domain.Constants;
using Share7.Domain.Feed;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// "Beat my score by Friday", decided from real graded attempts — and live duels in a room kept for
/// the two players.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ChallengeTests
{
    private readonly SqlServerFixture _fixture;

    public ChallengeTests(SqlServerFixture fixture) => _fixture = fixture;

    private sealed record Duel(Guid Challenger, Guid Recipient, CurriculumPathFixture Path);

    /// <summary>Two classmates, a four-question lesson both can play.</summary>
    private async Task<Duel> DuelAsync(bool recipientUnlocked = true)
    {
        await using var context = _fixture.CreateContext();
        var (challenger, recipient) = await SocialTest.ClassmatesAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddQuestionsAsync(path.LessonId, 3);
        await context.UnlockLessonAsync(challenger, path);

        if (recipientUnlocked)
            await context.UnlockLessonAsync(recipient, path);

        return new Duel(challenger, recipient, path);
    }

    /// <summary>A real graded attempt: <paramref name="correct"/> of the lesson's four questions right.</summary>
    private async Task AttemptAsync(Duel duel, Guid userId, int correct)
    {
        await using var context = _fixture.CreateContext();

        var questions = await context.Questions
            .Where(q => q.LessonId == duel.Path.LessonId && q.LangId == LanguageIds.English)
            .OrderBy(q => q.RowNumber)
            .Select(q => new { q.Id, q.CorrectChoiceId, Wrong = q.Choices.Where(c => c.Id != q.CorrectChoiceId).Select(c => c.Id).First() })
            .ToListAsync();

        var result = await RewardTestExtensions.CreateProgressService(context, userId).SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = duel.Path.GameId,
            LessonId = duel.Path.LessonId,
            Answers = [.. questions.Select((q, index) => new SubmittedAnswer
            {
                QuestionId = q.Id,
                ChoiceId = index < correct ? q.CorrectChoiceId : q.Wrong
            })]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private SendChallengeRequest Challenge(Duel duel) =>
        new() { UserId = duel.Recipient, GameId = duel.Path.GameId, LessonId = duel.Path.LessonId };

    /// <summary>Sent, accepted, and ready for the recipient's attempts.</summary>
    private async Task<ChallengeDto> AcceptedAsync(Duel duel)
    {
        await using var context = _fixture.CreateContext();
        var challenges = SocialTest.Challenges(context);

        var sent = await challenges.SendAsync(duel.Challenger, Challenge(duel));
        Assert.True(sent.Succeeded, sent.Error?.Code);

        var accepted = await challenges.AcceptAsync(duel.Recipient, sent.Value!.Id);
        Assert.Equal(ChallengeState.Accepted, accepted.Value!.State);

        return accepted.Value;
    }

    /// <summary>Moves the deadline to just after everything graded so far, so the next settle ends it.</summary>
    private async Task CloseWindowAsync(Guid challengeId)
    {
        await using var context = _fixture.CreateContext();
        var deadline = DateTime.UtcNow.AddMilliseconds(-5);

        await context.Challenges.Where(c => c.Id == challengeId)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.DeadlineUtc, deadline));
    }

    private async Task<ChallengeDto> SettledAsync(Guid challengeId, Guid viewer)
    {
        await using var context = _fixture.CreateContext();
        var challenges = SocialTest.Challenges(context);

        await challenges.SettleDueAsync();

        return (await challenges.ListAsync(viewer)).Value!.Single(c => c.Id == challengeId);
    }

    [Fact]
    public async Task Beating_the_bar_wins_the_moment_the_attempt_is_graded()
    {
        var duel = await DuelAsync();
        await AttemptAsync(duel, duel.Challenger, correct: 2);

        var challenge = await AcceptedAsync(duel);
        Assert.Equal(50, challenge.BarPercent);

        await AttemptAsync(duel, duel.Recipient, correct: 3);

        var settled = await SettledAsync(challenge.Id, duel.Recipient);

        Assert.Equal(ChallengeState.Completed, settled.State);
        Assert.Equal(ChallengeOutcome.RecipientWon, settled.Outcome);
        Assert.Equal(75, settled.RecipientBestPercent);

        await using var context = _fixture.CreateContext();
        var toRecipient = Assert.Single(await SocialTest.EventsAsync(context, duel.Recipient, PlayerEventTypes.ChallengeCompleted));
        var toChallenger = Assert.Single(await SocialTest.EventsAsync(context, duel.Challenger, PlayerEventTypes.ChallengeCompleted));
        Assert.True(toRecipient.Payload.GetProperty("youWon").GetBoolean());
        Assert.False(toChallenger.Payload.GetProperty("youWon").GetBoolean());
    }

    [Fact]
    public async Task A_bar_that_holds_to_the_deadline_is_the_challengers_win()
    {
        var duel = await DuelAsync();
        await AttemptAsync(duel, duel.Challenger, correct: 3);
        var challenge = await AcceptedAsync(duel);

        await AttemptAsync(duel, duel.Recipient, correct: 1);

        // Not beaten, not over: it stays open, showing the best so far.
        var running = await SettledAsync(challenge.Id, duel.Recipient);
        Assert.Equal(ChallengeState.Accepted, running.State);
        Assert.Equal(25, running.RecipientBestPercent);

        await CloseWindowAsync(challenge.Id);
        var settled = await SettledAsync(challenge.Id, duel.Challenger);

        Assert.Equal(ChallengeOutcome.ChallengerWon, settled.Outcome);
    }

    [Fact]
    public async Task Matching_the_bar_exactly_is_a_draw()
    {
        var duel = await DuelAsync();
        await AttemptAsync(duel, duel.Challenger, correct: 2);
        var challenge = await AcceptedAsync(duel);

        await AttemptAsync(duel, duel.Recipient, correct: 2);
        await CloseWindowAsync(challenge.Id);

        Assert.Equal(ChallengeOutcome.Draw, (await SettledAsync(challenge.Id, duel.Recipient)).Outcome);
    }

    [Fact]
    public async Task Attempts_before_accepting_do_not_count_and_an_untried_challenge_crowns_nobody()
    {
        var duel = await DuelAsync();
        await AttemptAsync(duel, duel.Challenger, correct: 2);

        await using (var context = _fixture.CreateContext())
        {
            var sent = (await SocialTest.Challenges(context).SendAsync(duel.Challenger, Challenge(duel))).Value!;

            // A perfect score — before taking the challenge on.
            await AttemptAsync(duel, duel.Recipient, correct: 4);

            await SocialTest.Challenges(context).AcceptAsync(duel.Recipient, sent.Id);
            Assert.Equal(ChallengeState.Accepted, (await SettledAsync(sent.Id, duel.Recipient)).State);

            await CloseWindowAsync(sent.Id);
            var settled = await SettledAsync(sent.Id, duel.Recipient);

            Assert.Equal(ChallengeState.Expired, settled.State);
            Assert.Equal(ChallengeOutcome.None, settled.Outcome);
        }
    }

    [Fact]
    public async Task A_challenge_needs_a_score_behind_it_a_classmate_and_a_lesson_they_can_play()
    {
        var duel = await DuelAsync();

        await using var context = _fixture.CreateContext();
        var challenges = SocialTest.Challenges(context);

        Assert.Equal("CHALLENGE_NO_SCORE", (await challenges.SendAsync(duel.Challenger, Challenge(duel))).Error?.Code);

        await AttemptAsync(duel, duel.Challenger, correct: 2);

        var stranger = await TestData.CreateUserAsync(context);
        var toStranger = await challenges.SendAsync(duel.Challenger, new SendChallengeRequest
        {
            UserId = stranger, GameId = duel.Path.GameId, LessonId = duel.Path.LessonId
        });

        Assert.Equal("SOCIAL_NOT_ALLOWED", toStranger.Error?.Code);

        var locked = await DuelAsync(recipientUnlocked: false);
        await AttemptAsync(locked, locked.Challenger, correct: 2);

        Assert.Equal("CHALLENGE_LESSON_LOCKED",
            (await SocialTest.Challenges(context).SendAsync(locked.Challenger, Challenge(locked))).Error?.Code);
    }

    [Fact]
    public async Task Sending_again_returns_the_one_open_challenge_and_an_accepted_one_cannot_be_pulled()
    {
        var duel = await DuelAsync();
        await AttemptAsync(duel, duel.Challenger, correct: 2);

        await using var context = _fixture.CreateContext();
        var challenges = SocialTest.Challenges(context);

        var first = (await challenges.SendAsync(duel.Challenger, Challenge(duel))).Value!;
        var again = (await challenges.SendAsync(duel.Challenger, Challenge(duel))).Value!;
        Assert.Equal(first.Id, again.Id);
        Assert.Single(await SocialTest.EventsAsync(context, duel.Recipient, PlayerEventTypes.ChallengeReceived));

        await challenges.AcceptAsync(duel.Recipient, first.Id);

        var pulled = await challenges.CancelAsync(duel.Challenger, first.Id);
        Assert.Equal("CHALLENGE_NOT_OPEN", pulled.Error?.Code);
    }

    [Fact]
    public async Task A_live_challenge_is_a_room_kept_for_the_two_and_an_invite_to_it()
    {
        await using var context = _fixture.CreateContext();
        var (challenger, recipient) = await SocialTest.ClassmatesAsync(context);
        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var stranger = await TestData.CreateUserAsync(context);

        var live = await SocialTest.Challenges(context).SendLiveAsync(challenger, new SendLiveChallengeRequest
        {
            UserId = recipient,
            GameId = curriculum.GameId,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            ProtocolVersion = 1
        });

        Assert.True(live.Succeeded, live.Error?.Code);
        var sessionId = live.Value!.Session.Id;
        Assert.Equal(challenger, live.Value.Session.HostUserId);
        Assert.True((await MultiplayerTest.ReadSessionAsync(context, sessionId)).IsReserved);

        var heard = Assert.Single(await SocialTest.EventsAsync(context, recipient, PlayerEventTypes.InviteReceived));
        Assert.Equal("live_challenge", heard.Text("kind"));

        await MultiplayerTest.Sessions(context).StartAsync(challenger, sessionId, new StartMultiplayerSessionRequest());

        Assert.Equal("SESSION_RESERVED", (await MultiplayerTest.Sessions(context)
            .JoinAsync(stranger, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 })).Error?.Code);

        var seated = await SocialTest.Invitations(context)
            .AcceptAsync(recipient, live.Value.Invitation.Id, new AcceptInvitationRequest { ProtocolVersion = 1 });

        Assert.True(seated.Succeeded, seated.Error?.Code);
        Assert.Equal(2, seated.Value!.CurrentPlayerCount);
    }

    [Fact]
    public async Task A_stranger_cannot_be_challenged_live_and_no_room_is_left_behind()
    {
        await using var context = _fixture.CreateContext();
        var challenger = await TestData.CreateUserAsync(context);
        var stranger = await TestData.CreateUserAsync(context);
        var curriculum = await TestData.CreateCurriculumPathAsync(context);

        var refused = await SocialTest.Challenges(context).SendLiveAsync(challenger, new SendLiveChallengeRequest
        {
            UserId = stranger,
            GameId = curriculum.GameId,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            ProtocolVersion = 1
        });

        Assert.Equal("SOCIAL_NOT_ALLOWED", refused.Error?.Code);
        Assert.False(await context.MultiplayerSessionPlayers.AnyAsync(p => p.UserId == challenger));
    }
}
