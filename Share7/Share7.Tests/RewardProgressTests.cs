using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Progress.Models;
using Share7.Domain.Constants;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Rewards through the real attempt endpoint rather than the engine alone.
/// <para>
/// This is where the authority model is actually proved: the only inputs are the ones the Unity
/// client sends, and none of them is an amount. Testing the engine directly cannot show that,
/// because the engine has no client-supplied input to ignore.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RewardProgressTests
{
    private readonly SqlServerFixture _fixture;

    public RewardProgressTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_attempt_credits_the_wallet_and_returns_authoritative_balances()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 10), new GrantSpec(gems.Id, 1)],
            referenceKey: path.LessonId.ToString());

        var answers = await PerfectRunAsync(context, path.LessonId);

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("Aced", result.Value!.CompletionState);

        var reward = Assert.Single(result.Value.Rewards);
        Assert.Equal("LESSON_ACED", reward.EventType);
        Assert.Equal(10, reward.Grants.Single(g => g.Currency == coins.Key).Amount);
        Assert.Equal(1, reward.Grants.Single(g => g.Currency == gems.Key).Amount);

        // The attempt response is a reconciliation point: absolute balances, already including
        // what it just paid, so the client needs no follow-up call.
        Assert.Equal(10, result.Value.Balances.Single(b => b.Currency == coins.Key).Amount);
        Assert.Equal(1, result.Value.Balances.Single(b => b.Currency == gems.Key).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(10, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(1, await check.BalanceOfAsync(userId, gems.Id));
    }

    [Fact]
    public async Task The_client_cannot_influence_what_a_reward_is_worth()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 10)],
            referenceKey: path.LessonId.ToString());

        // Every question answered with a real but wrong choice. There is no longer any field in
        // which to claim otherwise — the payload only says what was picked.
        var answers = await WrongRunAsync(context, path.LessonId);

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        Assert.Equal(0, result.Value!.CorrectCount);
        Assert.Equal("Uncompleted", result.Value.CompletionState);
        Assert.Empty(result.Value.Rewards);

        // Graded per question, with the right answer alongside, so a review screen needs no regrade.
        Assert.Equal(5, result.Value.Answers.Count);
        Assert.All(result.Value.Answers, a =>
        {
            Assert.False(a.IsCorrect);
            Assert.NotNull(a.ChoiceId);
            Assert.NotEqual(a.CorrectChoiceId, a.ChoiceId);
        });
        Assert.Equal(0, result.Value.UnrecognisedAnswers);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task The_score_is_the_servers_own_count_of_right_answers()
    {
        // A partial run: some right, some wrong, and the server arrives at the number itself.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);

        var right = await PerfectRunAsync(context, path.LessonId);
        var wrong = await WrongRunAsync(context, path.LessonId);
        var answers = right.Take(3).Concat(wrong.Skip(3)).ToList();

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(3, result.Value!.CorrectCount);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(60, result.Value.Percent);
        Assert.Equal("Completed", result.Value.CompletionState);
        Assert.Equal(3, result.Value.Answers.Count(a => a.IsCorrect));
    }

    [Fact]
    public async Task A_question_left_out_of_the_payload_counts_as_wrong()
    {
        // A run shows every question, so not reaching one is not the same as getting it right.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);

        var answers = await PerfectRunAsync(context, path.LessonId);

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers.Take(2).ToList()   // two answered, three never sent
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Value!.CorrectCount);
        Assert.Equal(5, result.Value.TotalCount);

        // The skipped ones are still graded and returned, visible by their null choice.
        Assert.Equal(5, result.Value.Answers.Count);
        Assert.Equal(3, result.Value.Answers.Count(a => a.ChoiceId is null && !a.IsCorrect));
    }

    [Fact]
    public async Task Answering_one_question_twice_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);

        var answers = await PerfectRunAsync(context, path.LessonId);
        answers.Add(answers[0]);

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
    }

    [Fact]
    public async Task Ids_the_server_does_not_recognise_earn_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 10)],
            referenceKey: path.LessonId.ToString());

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                // Invented ids — the shape of a client trying to buy a completion reward, and also
                // what a badly stale question cache looks like.
                Answers =
                [
                    new SubmittedAnswer { QuestionId = Guid.NewGuid(), ChoiceId = Guid.NewGuid() },
                    new SubmittedAnswer { QuestionId = Guid.NewGuid(), ChoiceId = Guid.NewGuid() },
                    new SubmittedAnswer { QuestionId = Guid.NewGuid(), ChoiceId = Guid.NewGuid() }
                ]
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(0, result.Value!.CorrectCount);
        Assert.Equal("Uncompleted", result.Value.CompletionState);
        Assert.Empty(result.Value.Rewards);

        // Reported rather than refused, so the client can tell a stale cache from a bad run.
        Assert.Equal(3, result.Value.UnrecognisedAnswers);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_resubmitted_run_carrying_the_same_request_id_is_paid_once()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 30)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        var answers = await PerfectRunAsync(context, path.LessonId);
        var progress = RewardTestExtensions.CreateProgressService(context, userId);

        var request = new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = answers,
            RequestId = "run-1"
        };

        var first = await progress.SubmitAttemptAsync(userId, request);
        var retry = await progress.SubmitAttemptAsync(userId, request);

        Assert.True(first.Succeeded && retry.Succeeded);

        // The retry re-records progress — attempts go up, which is correct, the run really was
        // submitted twice — but the reward replays instead of paying again.
        Assert.Equal(2, retry.Value!.Attempts);
        Assert.Equal(
            Assert.Single(first.Value!.Rewards).TransactionId,
            Assert.Single(retry.Value.Rewards).TransactionId);
        Assert.Equal(30, retry.Value.Balances.Single(b => b.Currency == coins.Key).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(30, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Single(await check.RewardTransactionsOfAsync(userId));
    }

    [Fact]
    public async Task Without_a_request_id_a_resubmitted_run_is_paid_as_a_fresh_attempt()
    {
        // Documents the known gap rather than hiding it: with no submission identity to key on,
        // the server cannot tell a retry from a genuine replay, and an EVERY_TIME rule pays both.
        // The fix is the client sending requestId — see SubmitAttemptRequest.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 30)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        var answers = await PerfectRunAsync(context, path.LessonId);
        var progress = RewardTestExtensions.CreateProgressService(context, userId);

        var request = new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = answers
        };

        await progress.SubmitAttemptAsync(userId, request);
        await progress.SubmitAttemptAsync(userId, request);

        await using var check = _fixture.CreateContext();
        Assert.Equal(60, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_once_rule_pays_the_first_completion_and_nothing_after_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 45)],
            referenceKey: path.LessonId.ToString());

        var answers = await PerfectRunAsync(context, path.LessonId);
        var progress = RewardTestExtensions.CreateProgressService(context, userId);

        var request = new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = answers
        };

        var first = await progress.SubmitAttemptAsync(userId, request);
        var second = await progress.SubmitAttemptAsync(userId, request);

        Assert.Single(first.Value!.Rewards);
        Assert.Empty(second.Value!.Rewards);

        // Balances stay authoritative on a replay that earned nothing, which is what lets the
        // client assign them unconditionally.
        Assert.Equal(45, second.Value.Balances.Single(b => b.Currency == coins.Key).Amount);
    }

    [Fact]
    public async Task The_balance_reported_by_an_attempt_matches_the_ledger()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted,
            [new GrantSpec(coins.Id, 4)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        var answers = await PerfectRunAsync(context, path.LessonId);
        var progress = RewardTestExtensions.CreateProgressService(context, userId);

        AttemptResultDto? last = null;

        for (var run = 0; run < 3; run++)
        {
            var result = await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers,
                RequestId = $"run-{run}"
            });

            last = result.Value;
        }

        await using var check = _fixture.CreateContext();
        var summed = (await check.LedgerOfAsync(userId)).Sum(e => e.Amount);

        Assert.Equal(12, summed);
        Assert.Equal(summed, last!.Balances.Single(b => b.Currency == coins.Key).Amount);
        Assert.Equal(summed, await check.BalanceOfAsync(userId, coins.Id));
    }

    /// <summary>A perfect run: every question answered with the choice that is actually right.</summary>
    private static Task<List<SubmittedAnswer>> PerfectRunAsync(ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: true);

    /// <summary>
    /// A run where every question was answered with a real choice that happens to be wrong — the
    /// ordinary way to fail, as opposed to sending nonsense ids.
    /// </summary>
    private static Task<List<SubmittedAnswer>> WrongRunAsync(ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: false);

    private static async Task<List<SubmittedAnswer>> AnswersAsync(
        ApplicationDbContext context,
        Guid lessonId,
        bool correct)
    {
        var questions = await context.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.LangId == LanguageIds.English && q.IsActive)
            .Select(q => new
            {
                q.Id,
                q.CorrectChoiceId,
                WrongChoiceId = q.Choices.Where(c => c.Id != q.CorrectChoiceId).Select(c => c.Id).FirstOrDefault()
            })
            .ToListAsync();

        return questions
            .Select(q => new SubmittedAnswer
            {
                QuestionId = q.Id,
                ChoiceId = correct ? q.CorrectChoiceId : q.WrongChoiceId
            })
            .ToList();
    }
}
