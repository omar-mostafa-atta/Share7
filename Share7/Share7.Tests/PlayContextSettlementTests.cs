using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Progress.Models;
using Share7.Application.Runs.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Play;
using Share7.Domain.Progress;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// What a session is actually worth, end to end: the mode and the context decide, and the server
/// pays the intersection.
/// <para>
/// **These are the tests that stop the economy being decided by the client.** Every case here is a
/// run or an attempt that looks identical on the wire except for two selector fields, and settles
/// completely differently because of them.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PlayContextSettlementTests
{
    private readonly SqlServerFixture _fixture;

    public PlayContextSettlementTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- runs ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_run_is_stamped_with_the_mode_and_context_it_started_under()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var started = await RunTestExtensions.CreateRunService(context).StartAsync(
            userId,
            new StartRunRequest
            {
                GameId = game.Id,
                ModeKey = mode.ModeKey,
                ContextKey = PlayContextTokens.FreePlay
            });

        Assert.True(started.Succeeded);

        await using var check = _fixture.CreateContext();
        var run = await check.Runs.FirstAsync(r => r.Id == started.Value!.RunId);

        // Stamped at start and never re-read: an operator editing the mode mid-run must not change
        // what that run settles as.
        Assert.Equal(mode.Id, run.ModeId);
        Assert.Equal(PlayContextKind.FreePlay, run.Context);
    }

    [Fact]
    public async Task A_run_naming_an_unknown_mode_is_refused_before_it_exists()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        await context.AddModeAsync(game.Id, isDefault: true);

        var started = await RunTestExtensions.CreateRunService(context).StartAsync(
            userId, new StartRunRequest { GameId = game.Id, ModeKey = "runner.nope" });

        Assert.False(started.Succeeded);
        Assert.Equal(ApiErrors.PlayModeUnknown.Code, started.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.Runs.CountAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task A_practice_run_pays_nothing_and_ranks_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 3);

        var mode = await context.AddModeAsync(game.Id, isDefault: true);
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(
            userId,
            new StartRunRequest
            {
                GameId = game.Id,
                ModeKey = mode.ModeKey,
                ContextKey = PlayContextTokens.Practice
            });

        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 47));

        Assert.True(settled.Succeeded);
        Assert.Empty(settled.Value!.Rewards);

        // The collected counts still come back — the results screen shows what happened, it just did
        // not pay for it.
        Assert.Equal(47, Assert.Single(settled.Value.Collected).Count);
        Assert.True(settled.Value.CapReached);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.RunPayouts.CountAsync(p => p.RunId == started.Value.RunId));
        Assert.Equal(0, await check.GameResults.CountAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task A_half_paying_profile_halves_what_the_run_earns()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 2);

        var half = await context.AddEconomyProfileAsync(50);
        var mode = await context.AddModeAsync(game.Id, isDefault: true, economyProfileId: half.Id);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id, ModeKey = mode.ModeKey });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 10));

        Assert.True(settled.Succeeded);

        // 10 coins at 2 each is 20; the profile keeps half. The payout row records 10, because the
        // ledger has to explain the number that actually moved the balance.
        Assert.Equal(10, Assert.Single(settled.Value!.Rewards).Amount);
        Assert.Equal(10, settled.Value.Balances.AmountOf(coins.Key));
    }

    [Fact]
    public async Task A_mode_that_posts_to_no_board_still_records_its_results_for_quests()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true, countsTowardRanking: false);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id, ModeKey = mode.ModeKey });

        Assert.True((await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 1))).Succeeded);

        await using var check = _fixture.CreateContext();
        var results = await check.GameResults.Where(r => r.UserId == userId).ToListAsync();

        // Written, so "play three runs" still ticks over — and marked, so no board ever folds them in.
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(r.CountsForRanking));
    }

    // ---- attempts ------------------------------------------------------------------------------

    [Fact]
    public async Task A_practice_attempt_is_graded_in_full_and_changes_no_record()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddModeAsync(path.GameId, isDefault: true);
        await context.UnlockLessonAsync(userId, path);

        var progress = RewardTestExtensions.CreateProgressService(context, userId);

        // A real attempt first, so there is a record for practice to leave alone.
        var real = await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = [new SubmittedAnswer { QuestionId = path.QuestionId, ChoiceId = await CorrectChoiceAsync(context, path.QuestionId) }]
        });

        Assert.True(real.Succeeded);
        Assert.Equal(100, real.Value!.Percent);

        var practice = await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            ContextKey = PlayContextTokens.Practice,

            // Answered wrongly on purpose: the record must not move, in either direction.
            Answers = [new SubmittedAnswer { QuestionId = path.QuestionId, ChoiceId = null }]
        });

        Assert.True(practice.Succeeded);
        Assert.Equal(0, practice.Value!.Percent);
        Assert.Single(practice.Value.Answers);

        // The record it reports is the one that already stood, and nothing was written.
        Assert.Equal(nameof(CompletionState.Aced), practice.Value.CompletionState);
        Assert.Empty(practice.Value.Rewards);

        await using var check = _fixture.CreateContext();
        var row = await check.UserLessonProgress.FirstAsync(
            p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(1, row.Attempts);
        Assert.Equal(100, row.BestPercent);
        Assert.Equal(100, row.Percent);
    }

    [Fact]
    public async Task An_event_attempt_records_its_score_for_the_event_ladder_only()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var mode = await context.AddModeAsync(path.GameId, isDefault: true);
        await context.UnlockLessonAsync(userId, path);

        var created = await PlayTest.EventAdmin(context).CreateAsync(
            PlayTest.EventRequest(
                path.GameId, mode.Id, LeaderboardMetrics.CorrectAnswers,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddDays(1)),
            Guid.NewGuid());

        Assert.True(created.Succeeded);

        var attempt = await RewardTestExtensions.CreateProgressService(context, userId).SubmitAttemptAsync(
            userId,
            new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                ModeKey = mode.ModeKey,
                ContextKey = PlayContextTokens.Event,
                EventId = created.Value!.EventId,
                Answers = [new SubmittedAnswer { QuestionId = path.QuestionId, ChoiceId = await CorrectChoiceAsync(context, path.QuestionId) }]
            });

        Assert.True(attempt.Succeeded);

        await using var check = _fixture.CreateContext();
        var result = Assert.Single(await check.GameResults.Where(r => r.UserId == userId).ToListAsync());

        Assert.Equal(LeaderboardMetrics.CorrectAnswers, result.Metric);
        Assert.Equal(1, result.Value);
        Assert.Equal(created.Value.EventId, result.EventId);
        Assert.Equal(PlayContextKind.Event, result.Context);

        // No mastery: an event entry is not evidence of having learned the lesson for the first time.
        Assert.Equal(0, await check.UserLessonProgress.CountAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task An_attempt_in_an_unknown_mode_is_refused_with_its_code()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddModeAsync(path.GameId, isDefault: true);
        await context.UnlockLessonAsync(userId, path);

        var attempt = await RewardTestExtensions.CreateProgressService(context, userId).SubmitAttemptAsync(
            userId,
            new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                ModeKey = "runner.nope",
                Answers = []
            });

        Assert.False(attempt.Succeeded);
        Assert.Equal(ApiErrors.PlayModeUnknown.Code, attempt.Error?.Code);
    }

    /// <summary>The answer key, read from the server's own row — never asserted by the payload.</summary>
    private static async Task<Guid> CorrectChoiceAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid questionId) =>
        await context.Questions
            .Where(q => q.Id == questionId)
            .Select(q => q.CorrectChoiceId)
            .FirstAsync();
}
