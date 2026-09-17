using Share7.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Leaderboards.Models;
using Share7.Application.Progress.Models;
using Share7.Application.Runs.Models;
using Share7.Domain.Constants;
using Share7.Domain.Economy;
using Share7.Domain.Leaderboards;
using Share7.Domain.Progression;
using Share7.Domain.Rewards;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The variable half of the economy, across both surfaces that produce it.
/// <para>
/// Every test here is a regression: each one pins behaviour that was either absent or wrong before
/// the signal pricer existed. They are grouped by the thing that was broken rather than by class,
/// because that is what somebody reading a failure needs to know.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SignalEconomyTests
{
    private readonly SqlServerFixture _fixture;

    public SignalEconomyTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- ownership: one signal, one surface, one payment --------------------------------------

    [Fact]
    public async Task A_run_is_never_paid_for_a_correct_answer()
    {
        // The whole reason SignalKinds has owners. A runner session settles a run *and* submits an
        // attempt: if both could be paid for the same right answer, one question would pay twice
        // from two transactions with two idempotency keys that cannot see each other.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(
            coins.Id, kind: SignalKinds.CorrectAnswer, gameId: game.Id, unitValue: 5);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Signals = [new RunSignalReport { Kind = SignalKinds.CorrectAnswer, Count = 10 }],
            DurationMs = 30_000
        });

        // Settles fine — a kind the surface does not own behaves exactly like an unpriced one, and
        // for the same reason: losing a child's whole run over it would be the worse outcome.
        Assert.True(settled.Succeeded);
        Assert.Empty(settled.Value!.Rewards);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task An_attempt_pays_for_the_answers_the_server_graded()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        await context.CreateValuationAsync(
            CurrencyIds.Xp, kind: SignalKinds.CorrectAnswer, unitValue: 5);

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = await CorrectAnswersAsync(context, path.LessonId)
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        var paid = Assert.Single(result.Value!.Rewards);
        Assert.Equal("signal:correct_answer", paid.EventType);
        Assert.Equal(5, Assert.Single(paid.Grants).Amount);

        // Absolute, and already including what was just paid — the same reconciliation contract the
        // rule rewards have, so the client needs no follow-up call.
        Assert.Equal(5, result.Value.Balances.Single(b => b.Currency == CurrencyKeys.Xp).Amount);
    }

    // ---- the farm that improvement-only payment closes ----------------------------------------

    [Fact]
    public async Task Replaying_a_finished_lesson_pays_no_more_xp()
    {
        // Without improvement-based payment this is the exploit: a lesson worth 5 XP a right answer
        // is worth 5 XP a right answer *every time it is replayed*, forever, with no rule and no cap
        // in the way. A Once-scoped rule is replay-proof for free; a proportional payout is not.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        await context.CreateValuationAsync(
            CurrencyIds.Xp, kind: SignalKinds.CorrectAnswer, unitValue: 5);

        var progress = RewardTestExtensions.CreateProgressService(context, userId);
        var answers = await CorrectAnswersAsync(context, path.LessonId);

        var first = await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = answers
        });

        var replay = await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = answers
        });

        Assert.True(first.Succeeded);
        Assert.True(replay.Succeeded);

        Assert.Single(first.Value!.Rewards);
        Assert.Empty(replay.Value!.Rewards);

        await using var check = _fixture.CreateContext();
        Assert.Equal(5, await check.BalanceOfAsync(userId, CurrencyIds.Xp));

        // The record the decision is made from, so a later content revision cannot resurrect the
        // payout by changing the question count underneath it.
        var row = await check.UserLessonProgress
            .AsNoTracking()
            .FirstAsync(p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(1, row.BestCorrectCount);
        Assert.Equal(2, row.Attempts);
    }

    [Fact]
    public async Task A_worse_attempt_pays_nothing_and_keeps_the_record()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        await context.CreateValuationAsync(
            CurrencyIds.Xp, kind: SignalKinds.CorrectAnswer, unitValue: 5);

        var progress = RewardTestExtensions.CreateProgressService(context, userId);

        await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = await CorrectAnswersAsync(context, path.LessonId)
        });

        var worse = await progress.SubmitAttemptAsync(userId, new SubmitAttemptRequest
        {
            GameId = path.GameId,
            LessonId = path.LessonId,
            Answers = await WrongAnswersAsync(context, path.LessonId)
        });

        Assert.True(worse.Succeeded);
        Assert.Empty(worse.Value!.Rewards);

        await using var check = _fixture.CreateContext();
        Assert.Equal(5, await check.BalanceOfAsync(userId, CurrencyIds.Xp));

        var row = await check.UserLessonProgress
            .AsNoTracking()
            .FirstAsync(p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(1, row.BestCorrectCount);
    }

    // ---- the level a run moved, and the baseline that used to miss it -------------------------

    [Fact]
    public async Task A_run_that_pays_xp_states_the_level_it_reached()
    {
        // Before the settlement carried a level, run XP moved the balance and crossed levels
        // server-side while the client's bar sat on yesterday's number until some later lesson
        // happened to refresh it.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();

        await context.CreateValuationAsync(CurrencyIds.Xp, gameId: game.Id, unitValue: 10);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // 15 coins at 10 XP each is 150 XP. The seeded curve is 25(L-1)L, so level 2 begins at 50
        // and level 3 at 150 — two rungs crossed in one settlement, which is exactly the case a
        // single-level report would have got wrong.
        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 15));

        Assert.True(settled.Succeeded);
        Assert.NotNull(settled.Value!.Level);
        Assert.Equal(150, settled.Value.Level!.Xp);
        Assert.Equal(3, settled.Value.Level.Level);
        Assert.Equal([2, 3], settled.Value.LevelsGained);
    }

    [Fact]
    public async Task A_level_crossed_by_a_variable_grant_pays_its_rule()
    {
        // The baseline fix. Level-up detection used to derive its starting point by subtracting what
        // the *rules* paid, so a level crossed by the variable half was invisible: no popup, and any
        // reward authored on that level never fired.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(CurrencyIds.Xp, gameId: game.Id, unitValue: 10);

        // Pays on every level-up, which is how a flat "coins per level" is expressed.
        await context.CreateRewardRuleAsync(RewardEventType.PlayerLevelUp, [new GrantSpec(coins.Id, 25)]);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 15));

        Assert.True(settled.Succeeded);

        await using var check = _fixture.CreateContext();

        // 150 XP crosses levels 2 and 3, so the rule pays twice — and no rule granted a single point
        // of the XP that caused it.
        Assert.Equal(50, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_replayed_result_states_the_level_but_gains_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();

        await context.CreateValuationAsync(CurrencyIds.Xp, gameId: game.Id, unitValue: 10);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var request = RunTestExtensions.Result(coins: 15, requestId: "retry-1");

        await runs.SettleAsync(userId, started.Value!.RunId, request);
        var replay = await runs.SettleAsync(userId, started.Value.RunId, request);

        Assert.True(replay.Succeeded);
        Assert.Equal(3, replay.Value!.Level!.Level);

        // A level is reached once. The offline queue retries by design, and a retried result that
        // re-celebrated would show the popup twice for one run.
        Assert.Empty(replay.Value.LevelsGained);
    }

    // ---- the metric that had no producer ------------------------------------------------------

    [Fact]
    public async Task A_settled_run_records_what_it_actually_paid()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 2);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 7));

        await using var check = _fixture.CreateContext();

        var earned = await check.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Metric == LeaderboardMetrics.CurrencyEarned)
            .ToListAsync();

        // CURRENCY_EARNED was declared, offered in the admin console, and raised by nothing. An
        // operator could author "earn 100 coins today" and watch it sit at zero forever.
        var line = Assert.Single(earned);
        Assert.Equal(14, line.Value);
        Assert.Equal(coins.Key, line.Scope);
    }

    [Fact]
    public async Task An_attempt_records_what_it_actually_paid()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        await context.CreateValuationAsync(
            CurrencyIds.Xp, kind: SignalKinds.CorrectAnswer, unitValue: 5);

        await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = await CorrectAnswersAsync(context, path.LessonId)
            });

        await using var check = _fixture.CreateContext();

        var earned = await check.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Metric == LeaderboardMetrics.CurrencyEarned)
            .ToListAsync();

        var line = Assert.Single(earned);
        Assert.Equal(5, line.Value);
        Assert.Equal(CurrencyKeys.Xp, line.Scope);
    }

    // ---- bounds that are per kind rather than per platform ------------------------------------

    [Fact]
    public async Task A_kinds_daily_allowance_spans_runs()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(
            coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 100, maxPerDay: 15);

        var runs = RunTestExtensions.CreateRunService(context);

        var first = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        await runs.SettleAsync(userId, first.Value!.RunId, RunTestExtensions.Result(coins: 10));

        var second = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var capped = await runs.SettleAsync(userId, second.Value!.RunId, RunTestExtensions.Result(coins: 10));

        Assert.True(capped.Succeeded);
        Assert.True(capped.Value!.CapReached);
        Assert.Equal("signal_daily_limit", capped.Value.CapMessage);

        await using var check = _fixture.CreateContext();

        // Ten, then the five that were left — never twenty.
        Assert.Equal(15, await check.BalanceOfAsync(userId, coins.Id));

        // And the counter it was read from is one keyed row, not a scan over payout history.
        var ledger = await check.DailySignalLedger
            .AsNoTracking()
            .FirstAsync(l => l.UserId == userId && l.SignalKind == SignalKinds.Coin);

        Assert.Equal(15, ledger.PaidCount);
    }

    [Fact]
    public async Task Distance_is_bounded_by_its_own_rate_not_the_coin_rate()
    {
        // A runner covers ten metres a second and perhaps two coins. One global per-second bound
        // has to be wrong for one of them, and being wrong here means capping every honest run.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(
            coins.Id,
            kind: SignalKinds.DistanceM,
            gameId: game.Id,
            unitValue: 1,
            maxPerRun: 100_000,
            maxPerSecond: 15);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // 60 seconds at 12 m/s. Under this kind's own bound of 15/s, and far over the platform
        // default the coin uses.
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Signals = [new RunSignalReport { Kind = SignalKinds.DistanceM, Count = 720 }],
            DurationMs = 60_000
        });

        Assert.True(settled.Succeeded);
        Assert.False(settled.Value!.CapReached);
        Assert.Equal(720, Assert.Single(settled.Value.Rewards).Amount);
    }

    [Fact]
    public async Task A_signal_the_layout_never_spawned_does_not_reject_the_run()
    {
        // The landmine. Verification compared *every* reported kind against the spawn table, so the
        // day a generator was registered, one dodge would have read as "claimed more than existed"
        // and rejected an entirely honest run.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);
        await context.CreateValuationAsync(
            coins.Id, kind: SignalKinds.NearMiss, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);

        var runs = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 180)));

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Signals =
            [
                new RunSignalReport { Kind = SignalKinds.Coin, Count = 100 },
                new RunSignalReport { Kind = SignalKinds.NearMiss, Count = 40 }
            ],
            DurationMs = 120_000
        });

        Assert.True(settled.Succeeded);
        Assert.Equal(RunState.Settled.ToString().ToUpperInvariant(), settled.Value!.State);

        // The coin half is still checked exactly, because the layout does describe it.
        var forgedStart = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var forged = await runs.SettleAsync(
            userId, forgedStart.Value!.RunId, RunTestExtensions.Result(coins: 500));

        Assert.False(forged.Succeeded);
    }

    // ---- wire compatibility -------------------------------------------------------------------

    [Fact]
    public async Task The_legacy_pickups_field_still_settles_and_is_summed_once()
    {
        // A build shipped before the rename sends `pickups`. Deleting the field would silently stop
        // paying for every coin an installed client collects, which is the failure a deprecation
        // window exists to avoid — and a build sending both must not be paid twice.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 5);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Signals = [new RunSignalReport { Kind = SignalKinds.Coin, Count = 3 }],
            Pickups = [new RunSignalReport { Kind = SignalKinds.Coin, Count = 4 }],
            DurationMs = 60_000
        });

        Assert.True(settled.Succeeded);

        // Seven claimed as one total, meeting one cap of five — not two caps of five.
        Assert.Equal(5, Assert.Single(settled.Value!.Rewards).Amount);
        Assert.Equal(7, Assert.Single(settled.Value.Collected).Count);
    }

    // ---- what must never become a ladder ------------------------------------------------------

    [Fact]
    public async Task A_board_cannot_be_authored_on_currency_earned()
    {
        await using var context = _fixture.CreateContext();
        var admin = LeaderboardTestExtensions.CreateAdminService(context);

        var refused = await admin.CreateBoardAsync(new SaveLeaderboardBoardRequest
        {
            BoardKey = $"b.{Guid.NewGuid():N}"[..16],
            Metric = LeaderboardMetrics.CurrencyEarned,
            Aggregation = nameof(LeaderboardAggregation.Sum),
            SortDirection = nameof(LeaderboardSortDirection.Desc),
            Period = nameof(LeaderboardPeriod.Weekly),
            SupportedCohorts = nameof(LeaderboardCohort.All)
        });

        // A board aggregates across every scope, so this one would add coins to XP — and XP measures
        // time spent rather than skill, compared across every age on the platform.
        Assert.False(refused.Succeeded);
    }

    // ---- retention ----------------------------------------------------------------------------

    [Fact]
    public async Task Retention_leaves_rows_a_consumer_has_not_read()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();

        var old = new GameResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameId = game.Id,
            Metric = LeaderboardMetrics.RunsSettled,
            Value = 1,
            OccurredAtUtc = DateTime.UtcNow.AddDays(-400),
            SourceType = GameResultSource.Session,
            SourceId = Guid.NewGuid(),
            ProjectedAtUtc = DateTime.UtcNow.AddDays(-400)
        };

        context.GameResults.Add(old);
        context.ProjectionCheckpoints.Add(new ProjectionCheckpoint
        {
            Consumer = ProjectionConsumers.Objectives,
            Watermark = 0,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var deleted = await LeaderboardTestExtensions.CreateRetentionService(context).SweepAsync();

        // Past the window and projected onto the boards, but the objective projector is still at
        // zero. Deleting it would not merely lose a quest step — it would make the sequence
        // discontiguous underneath a cursor that is walking it.
        Assert.Equal(0, deleted);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.GameResults.AnyAsync(r => r.Id == old.Id));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static Task<List<SubmittedAnswer>> CorrectAnswersAsync(
        ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: true);

    private static Task<List<SubmittedAnswer>> WrongAnswersAsync(
        ApplicationDbContext context, Guid lessonId) =>
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
                WrongChoiceId = q.Choices
                    .Where(c => c.Id != q.CorrectChoiceId)
                    .Select(c => c.Id)
                    .FirstOrDefault()
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
