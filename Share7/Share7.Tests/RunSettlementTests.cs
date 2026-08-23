using Microsoft.EntityFrameworkCore;
using Share7.Application.Runs.Models;
using Share7.Domain.Rewards;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Run settlement against a real database — the BC-COM-04 gate.
/// <para>
/// The invariant every test here circles is one sentence: **the client reports counts and the server
/// decides amounts.** A test that passed because the client was believed would not look any different
/// from one that passed because the server re-valued the run, so each of these fixes what the server
/// was told apart from what it paid.
/// </para>
/// <para>
/// Reward rules are global configuration and this collection shares one database, so every rule below
/// is scoped by <c>referenceKey</c> to its own test's game. A rule left matching every game would pay
/// out inside unrelated tests and make them pass for the wrong reason.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RunSettlementTests
{
    private readonly SqlServerFixture _fixture;

    public RunSettlementTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- starting ------------------------------------------------------------------------------

    /// <summary>Test 4.</summary>
    [Fact]
    public async Task Start_retried_with_the_same_request_id_returns_the_same_run_and_seed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var request = new StartRunRequest { GameId = game.Id, RequestId = "start-once" };

        var first = await runs.StartAsync(userId, request);
        var retry = await runs.StartAsync(userId, request);

        Assert.True(first.Succeeded);
        Assert.True(retry.Succeeded);

        // The seed matters as much as the id: the client generates its track from it, so two seeds
        // for one run would mean the track on screen is not the track the server can check.
        Assert.Equal(first.Value!.RunId, retry.Value!.RunId);
        Assert.Equal(first.Value.Seed, retry.Value.Seed);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.Runs.CountAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task Start_issues_a_different_seed_to_each_run()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var seeds = new List<long>();

        for (var i = 0; i < 5; i++)
        {
            var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
            Assert.True(started.Succeeded);
            seeds.Add(started.Value!.Seed);
        }

        Assert.Equal(seeds.Count, seeds.Distinct().Count());
        Assert.All(seeds, seed => Assert.True(seed >= 0, "a seed must survive a signed JSON reader"));
    }

    [Fact]
    public async Task Start_is_refused_for_a_retired_game()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync(isActive: false);

        var started = await RunTestExtensions
            .CreateRunService(context)
            .StartAsync(userId, new StartRunRequest { GameId = game.Id });

        Assert.False(started.Succeeded);
        Assert.Equal("GAME_INACTIVE", started.Error?.Code);
    }

    // ---- the run must exist --------------------------------------------------------------------

    /// <summary>
    /// Test 1 — the rule the whole aggregate exists for. A result for a run nobody started has no
    /// server-known start time, so nothing it claims is bounded by anything.
    /// </summary>
    [Fact]
    public async Task A_result_for_a_run_that_was_never_started_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var settled = await RunTestExtensions
            .CreateRunService(context)
            .SettleAsync(userId, Guid.NewGuid(), RunTestExtensions.Result(coins: 47));

        Assert.False(settled.Succeeded);
        Assert.Equal("RUN_NOT_FOUND", settled.Error?.Code);
    }

    [Fact]
    public async Task A_result_for_someone_elses_run_is_refused_as_not_found()
    {
        await using var context = _fixture.CreateContext();
        var owner = await TestData.CreateUserAsync(context);
        var stranger = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(owner, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(stranger, started.Value!.RunId, RunTestExtensions.Result(coins: 5));

        // Answered identically to "no such run" on purpose — a distinct refusal would turn this route
        // into an oracle for other people's run ids.
        Assert.False(settled.Succeeded);
        Assert.Equal("RUN_NOT_FOUND", settled.Error?.Code);
    }

    [Fact]
    public async Task An_expired_run_is_refused_and_does_not_pay()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, unitValue: 1);

        var started = await RunTestExtensions
            .CreateRunService(context)
            .StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Reach past the service to age the run: the alternative is a test that sleeps for an hour.
        await context.Runs
            .Where(r => r.Id == started.Value!.RunId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));

        // A fresh context for the settle, because that is what production does — one scoped
        // DbContext per request. Reusing the one that started the run would hand the service its own
        // still-tracked entity, complete with the expiry this test just overwrote underneath it.
        await using var settling = _fixture.CreateContext();

        var settled = await RunTestExtensions
            .CreateRunService(settling)
            .SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 47));

        Assert.False(settled.Succeeded);
        Assert.Equal("RUN_EXPIRED", settled.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(RunState.Expired, (await check.Runs.SingleAsync(r => r.Id == started.Value.RunId)).State);
    }

    // ---- valuation -----------------------------------------------------------------------------

    [Fact]
    public async Task The_server_decides_the_amount_from_the_valuation_row()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        // 3 coins each. The client says "47 collected" and never says what that is worth.
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 3);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 47));

        Assert.True(settled.Succeeded);

        var reward = Assert.Single(settled.Value!.Rewards);
        Assert.Equal("pickup:coin", reward.Source);
        Assert.Equal(141, reward.Amount);
        Assert.Equal(coins.Key, reward.Currency);

        // Collected is a count of things, not a balance — 47 touched, 141 paid.
        Assert.Equal(47, Assert.Single(settled.Value.Collected).Count);
        Assert.Equal(141, settled.Value.Balances.AmountOf(coins.Key));
    }

    [Fact]
    public async Task A_game_specific_price_wins_over_the_platform_default()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        // A default for the platform, and a richer price for this one harder game.
        await context.CreateValuationAsync(coins.Id, kind: "coin", gameId: null, unitValue: 1);
        await context.CreateValuationAsync(coins.Id, kind: "coin", gameId: game.Id, unitValue: 10);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 4));

        Assert.Equal(40, Assert.Single(settled.Value!.Rewards).Amount);
    }

    /// <summary>Test 10.</summary>
    [Fact]
    public async Task An_unpriced_pickup_kind_pays_zero_and_does_not_fail_the_run()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, kind: "coin", gameId: game.Id, unitValue: 2);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Pickups =
            [
                new RunPickupReport { Kind = "coin", Count = 5 },
                new RunPickupReport { Kind = "mg147_starfish", Count = 99 }
            ],
            DurationMs = 30_000
        });

        // An unpriced kind is a design oversight to notice in the payout data, not a reason to lose a
        // child's whole run.
        Assert.True(settled.Succeeded);
        Assert.Equal(RunState.Settled.ToString().ToUpperInvariant(), settled.Value!.State);
        Assert.Equal(10, Assert.Single(settled.Value.Rewards).Amount);

        // Still counted and still recorded, so a price added later is provably owed.
        Assert.Contains(settled.Value.Collected, c => c.Kind == "mg147_starfish" && c.Count == 99);
    }

    /// <summary>Test 5.</summary>
    [Fact]
    public async Task Collecting_more_than_max_per_run_settles_at_the_cap_flags_and_still_pays()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 20);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 47));

        Assert.True(settled.Succeeded);
        Assert.Equal(20, Assert.Single(settled.Value!.Rewards).Amount);

        // The client must be able to say *why* it paid 20 after showing 47. Paying less in silence is
        // how a child learns the game is unfair.
        Assert.True(settled.Value.CapReached);
        Assert.Equal("pickup_limit", settled.Value.CapMessage);

        await using var check = _fixture.CreateContext();
        var run = await check.Runs.SingleAsync(r => r.Id == started.Value.RunId);
        Assert.True(run.IsFlagged);
        Assert.Contains("pickup_capped", run.FlagReason);

        // Flagged for review, capped, and *paid* — never thrown away with an error.
        Assert.Equal(RunState.Settled, run.State);

        // Gross and net both survive, so "the cap ate 27" is answerable from data.
        var payout = Assert.Single(await check.PayoutsOfAsync(run.Id));
        Assert.Equal(47, payout.CollectedCount);
        Assert.Equal(20, payout.PaidCount);
        Assert.Equal(47, payout.GrossAmount);
        Assert.Equal(27, payout.CappedAmount);
        Assert.Equal(20, payout.NetAmount);
    }

    [Fact]
    public async Task One_kind_split_across_entries_meets_one_cap()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 20);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Splitting one kind across entries must not buy a second helping of MaxPerRun.
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Pickups =
            [
                new RunPickupReport { Kind = "coin", Count = 19 },
                new RunPickupReport { Kind = "coin", Count = 19 }
            ],
            DurationMs = 30_000
        });

        Assert.Equal(20, Assert.Single(settled.Value!.Rewards).Amount);
    }

    // ---- what the client is not trusted about ---------------------------------------------------

    /// <summary>Test 6.</summary>
    [Fact]
    public async Task A_duration_longer_than_real_elapsed_time_is_clamped_not_trusted()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Four hours of play, claimed moments after the server itself opened the run.
        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 1, durationMs: 14_400_000));

        Assert.True(settled.Succeeded);

        await using var check = _fixture.CreateContext();
        var run = await check.Runs.SingleAsync(r => r.Id == started.Value.RunId);

        // The run was *started* here, so elapsed time is something the server knows rather than
        // something it is told. An unclamped duration is a free multiplier on every per-second bound.
        Assert.True(run.DurationMs < 60_000, $"expected a clamped duration, got {run.DurationMs}ms");
        Assert.True(run.IsFlagged);
        Assert.Contains("duration_clamped", run.FlagReason);
    }

    /// <summary>Test 9.</summary>
    [Fact]
    public async Task Double_reward_is_applied_by_the_server_and_only_when_declared()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var withModifier = await TestData.CreateUserAsync(context);
        var without = await TestData.CreateUserAsync(context);
        var runs = RunTestExtensions.CreateRunService(context);

        var a = await runs.StartAsync(withModifier, new StartRunRequest { GameId = game.Id });
        var doubled = await runs.SettleAsync(
            withModifier,
            a.Value!.RunId,
            RunTestExtensions.Result(coins: 10, modifiers: RunTestExtensions.DoubleReward(5)));

        var b = await runs.StartAsync(without, new StartRunRequest { GameId = game.Id });
        var plain = await runs.SettleAsync(without, b.Value!.RunId, RunTestExtensions.Result(coins: 10));

        // The client declared that the modifier ran. It never declared what its own payout should be —
        // the ×2 is applied here, to an already-capped count.
        Assert.Equal(20, Assert.Single(doubled.Value!.Rewards).Amount);
        Assert.Equal(10, Assert.Single(plain.Value!.Rewards).Amount);
    }

    [Fact]
    public async Task An_unrecognised_modifier_is_ignored_rather_than_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var settled = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Pickups = [new RunPickupReport { Kind = "coin", Count = 10 }],
            Modifiers = [new RunModifierReport { Kind = "quintuple_everything", DurationSeconds = 30 }],
            DurationMs = 60_000
        });

        // An older server must not fail a run produced by a newer client, and ignoring a modifier can
        // only ever pay less.
        Assert.True(settled.Succeeded);
        Assert.Equal(10, Assert.Single(settled.Value!.Rewards).Amount);
    }

    // ---- idempotency ---------------------------------------------------------------------------

    /// <summary>Test 2.</summary>
    [Fact]
    public async Task A_second_result_for_a_settled_run_replays_the_settlement_without_paying_again()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 2);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var first = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 47, requestId: "result-once"));

        // The offline queue retries on reconnect by design, so a replay is the ordinary path — not an
        // edge case — and paying it again would mint currency on every dropped response.
        var replay = await runs.SettleAsync(
            userId, started.Value.RunId, RunTestExtensions.Result(coins: 47, requestId: "result-once"));

        Assert.True(replay.Succeeded);
        Assert.Equal(94, Assert.Single(first.Value!.Rewards).Amount);
        Assert.Equal(94, Assert.Single(replay.Value!.Rewards).Amount);
        Assert.Equal(94, replay.Value.Balances.AmountOf(coins.Key));

        await using var check = _fixture.CreateContext();
        Assert.Equal(94, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Single(await check.PayoutsOfAsync(started.Value.RunId));
    }

    [Fact]
    public async Task A_replay_that_claims_more_does_not_get_more()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 10));

        // The settled state is what refuses this, not the request id — a modified client that
        // re-posts an inflated claim without one gets the stored answer just the same.
        var greedy = await runs.SettleAsync(userId, started.Value.RunId, RunTestExtensions.Result(coins: 9_999));

        Assert.True(greedy.Succeeded);
        Assert.Equal(10, greedy.Value!.Balances.AmountOf(coins.Key));

        await using var check = _fixture.CreateContext();
        Assert.Equal(10, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_request_id_already_spent_on_another_run_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);
        var first = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var second = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        await runs.SettleAsync(userId, first.Value!.RunId, RunTestExtensions.Result(coins: 5, requestId: "shared"));
        var reused = await runs.SettleAsync(userId, second.Value!.RunId, RunTestExtensions.Result(coins: 5, requestId: "shared"));

        // One key, one run. Either a client bug or an attempt to have one key pay twice — neither
        // should move a balance.
        Assert.False(reused.Succeeded);
        Assert.Equal("RUN_REQUEST_ID_REUSED", reused.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(5, await check.BalanceOfAsync(userId, coins.Id));
    }

    /// <summary>Test 12.</summary>
    [Fact]
    public async Task Balances_are_absolute_and_already_include_the_rewards()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);

        var first = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var one = await runs.SettleAsync(userId, first.Value!.RunId, RunTestExtensions.Result(coins: 30));

        var second = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var two = await runs.SettleAsync(userId, second.Value!.RunId, RunTestExtensions.Result(coins: 12));

        // Rewards are deltas; balances are the total that already contains them. A client that added
        // the delta to the balance would show 84 after the first run.
        Assert.Equal(30, Assert.Single(one.Value!.Rewards).Amount);
        Assert.Equal(30, one.Value.Balances.AmountOf(coins.Key));

        Assert.Equal(12, Assert.Single(two.Value!.Rewards).Amount);
        Assert.Equal(42, two.Value.Balances.AmountOf(coins.Key));

        // Assigning them twice is the same as assigning them once, which is what makes the reconciler
        // safe to run on every response.
        Assert.Equal(42, await context.BalanceOfAsync(userId, coins.Id));
    }

    // ---- reward rules, the fixed half -----------------------------------------------------------

    [Fact]
    public async Task A_run_settled_rule_pays_a_fixed_bonus_alongside_the_pickups()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        // Scoped to this test's game: RUN_SETTLED rules are global configuration on a shared database.
        var rule = await context.CreateRewardRuleAsync(
            RewardEventType.RunSettled,
            [new GrantSpec(coins.Id, 25)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: game.Id.ToString());

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 10));

        Assert.True(settled.Succeeded);

        // Two mechanisms, one wallet: a variable payout that scales with what was collected, and a
        // fixed bonus a rule can express. The source is how the results screen tells them apart.
        Assert.Equal(2, settled.Value!.Rewards.Count);
        Assert.Equal(10, settled.Value.Rewards.Single(r => r.Source == "pickup:coin").Amount);
        Assert.Equal(25, settled.Value.Rewards.Single(r => r.Source == $"rule:{rule.Id}").Amount);
        Assert.Equal(35, settled.Value.Balances.AmountOf(coins.Key));

        await using var check = _fixture.CreateContext();
        Assert.Equal(35, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(2, (await check.PayoutsOfAsync(started.Value.RunId)).Count);
    }

    [Fact]
    public async Task A_lesson_rule_does_not_fire_for_a_run()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 500)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: game.Id.ToString());

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 3));

        // Events are not interchangeable. A run raises RUN_SETTLED and nothing else.
        Assert.Equal(3, settled.Value!.Balances.AmountOf(coins.Key));
    }

    // ---- the transaction ------------------------------------------------------------------------

    /// <summary>Test 11.</summary>
    [Fact]
    public async Task Grant_payout_and_daily_ledger_all_commit_together()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 4);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 25));

        await using var check = _fixture.CreateContext();

        // A grant without its payout rows is currency nobody can explain; a ledger increment without
        // its grant robs the child. All three, or none.
        Assert.Equal(100, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(100, Assert.Single(await check.PayoutsOfAsync(started.Value.RunId)).NetAmount);

        var daily = await check.DailyOfAsync(userId, coins.Id);
        Assert.NotNull(daily);
        Assert.Equal(100, daily!.EarnedAmount);
        Assert.Equal(1, daily.RunCount);
    }

    /// <summary>Test 11, the half that matters.</summary>
    [Fact]
    public async Task A_settlement_that_throws_partway_leaves_nothing_behind()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();

        await context.CreateValuationAsync(coins.Id, kind: "coin", gameId: game.Id, unitValue: 5);
        await context.CreateValuationAsync(gems.Id, kind: "gem", gameId: game.Id, unitValue: 50);

        var started = await RunTestExtensions
            .CreateRunService(context)
            .StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Rigged to die after the first currency is credited — the state a torn settlement would
        // leave behind, if it could leave one.
        var rigged = RunTestExtensions.CreateRunService(
            context, new RunTestExtensions.ThrowingWallet(context, throwOnCall: 2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rigged.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
            {
                Pickups =
                [
                    new RunPickupReport { Kind = "coin", Count = 10 },
                    new RunPickupReport { Kind = "gem", Count = 2 }
                ],
                DurationMs = 30_000
            }));

        await using var check = _fixture.CreateContext();

        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(0, await check.BalanceOfAsync(userId, gems.Id));
        Assert.Empty(await check.PayoutsOfAsync(started.Value!.RunId));
        Assert.Null(await check.DailyOfAsync(userId, coins.Id));

        // Still Open, so the client's retry settles it properly rather than finding it half-paid.
        Assert.Equal(RunState.Open, (await check.Runs.SingleAsync(r => r.Id == started.Value.RunId)).State);
    }

    [Fact]
    public async Task The_daily_counter_accumulates_across_runs()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);

        for (var i = 0; i < 3; i++)
        {
            var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
            await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 7));
        }

        await using var check = _fixture.CreateContext();
        var daily = await check.DailyOfAsync(userId, coins.Id);

        // The counter phase 2's ceiling will read. It has to be accurate before anything can be
        // refused on the strength of it.
        Assert.Equal(21, daily!.EarnedAmount);
        Assert.Equal(3, daily.RunCount);
    }

    [Fact]
    public async Task Purchased_currency_does_not_reach_the_daily_earning_counter()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await new Share7.Infrastructure.Economy.WalletService(context).ApplyAsync(
            new Share7.Application.Economy.Models.WalletMutation
            {
                UserId = userId,
                CurrencyId = coins.Id,
                Delta = 5_000,
                TransactionType = Share7.Domain.Economy.CurrencyTransactionType.Purchase,
                SourceType = Share7.Domain.Economy.LedgerSourceType.Purchase
            });

        await using var check = _fixture.CreateContext();

        // A ceiling that counted purchases would stop a child whose parent bought coins from earning
        // any — which makes the purchase actively harmful. That is why the counter lives in
        // settlement rather than inside the wallet.
        Assert.Equal(5_000, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Null(await check.DailyOfAsync(userId, coins.Id));
    }
}
