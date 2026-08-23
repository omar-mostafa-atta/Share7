using Microsoft.EntityFrameworkCore;
using Share7.Application.Runs.Models;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The bounds a farming script hits, and the wall explaining itself when it does.
/// <para>
/// **Every one of these caps and pays rather than refusing**, and the tests assert the paying half as
/// carefully as the capping half. A child on a device with a bad clock, or one whose session dropped
/// and resumed, trips exactly the same bounds a script does — losing their run and giving them no way
/// to find out why is the failure mode these bounds are most likely to actually produce.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RunBoundsTests
{
    private readonly SqlServerFixture _fixture;

    public RunBoundsTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>Test 7.</summary>
    [Fact]
    public async Task A_run_past_the_daily_ceiling_pays_the_remainder_and_says_so()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCappedCurrencyAsync(dailyEarnCap: 30);
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(context);

        var first = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var one = await runs.SettleAsync(userId, first.Value!.RunId, RunTestExtensions.Result(coins: 25));

        var second = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var two = await runs.SettleAsync(userId, second.Value!.RunId, RunTestExtensions.Result(coins: 25));

        // 25 then 5. The ceiling clamps rather than refusing, so the second run still pays what is
        // left instead of paying nothing because it asked for too much.
        Assert.False(one.Value!.CapReached);
        Assert.Equal(25, one.Value.Balances.AmountOf(coins.Key));

        Assert.True(two.Value!.CapReached);
        Assert.Equal("daily_coin_limit", two.Value.CapMessage);
        Assert.Equal(5, Assert.Single(two.Value.Rewards).Amount);
        Assert.Equal(30, two.Value.Balances.AmountOf(coins.Key));

        await using var check = _fixture.CreateContext();

        // A third run earns nothing at all, and still settles.
        var third = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var three = await runs.SettleAsync(userId, third.Value!.RunId, RunTestExtensions.Result(coins: 25));

        Assert.True(three.Succeeded);
        Assert.Empty(three.Value!.Rewards);
        Assert.True(three.Value.CapReached);
        Assert.Equal(30, await check.BalanceOfAsync(userId, coins.Id));
    }

    /// <summary>Test 8 — the exemption the ceiling exists alongside.</summary>
    [Fact]
    public async Task Purchased_currency_does_not_consume_the_earning_ceiling()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCappedCurrencyAsync(dailyEarnCap: 50);
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        await new Share7.Infrastructure.Economy.WalletService(context).ApplyAsync(
            new Share7.Application.Economy.Models.WalletMutation
            {
                UserId = userId,
                CurrencyId = coins.Id,
                Delta = 5_000,
                TransactionType = Share7.Domain.Economy.CurrencyTransactionType.Purchase,
                SourceType = Share7.Domain.Economy.LedgerSourceType.Purchase
            });

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 40));

        // A ceiling that counted the purchase would stop a child whose parent bought coins from
        // earning any, which makes the purchase actively harmful.
        Assert.False(settled.Value!.CapReached);
        Assert.Equal(40, Assert.Single(settled.Value.Rewards).Amount);
        Assert.Equal(5_040, settled.Value.Balances.AmountOf(coins.Key));
    }

    /// <summary>Test 13.</summary>
    [Fact]
    public async Task Opening_past_the_concurrent_cap_expires_the_oldest_rather_than_failing_the_new_one()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();

        var runs = RunTestExtensions.CreateRunService(
            context, options: new RunOptions { MaxConcurrentOpenRuns = 3 });

        var opened = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
            Assert.True(started.Succeeded, "the child in front of the device must always be able to start");
            opened.Add(started.Value!.RunId);
        }

        await using var check = _fixture.CreateContext();

        var open = await check.Runs
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.State == RunState.Open)
            .Select(r => r.Id)
            .ToListAsync();

        // The three most recent survive; the two abandoned earliest gave way.
        Assert.Equal(3, open.Count);
        Assert.Equal(opened.TakeLast(3).OrderBy(id => id), open.OrderBy(id => id));

        foreach (var expired in opened.Take(2))
            Assert.Equal(RunState.Expired, (await check.RunOfAsync(expired)).State);
    }

    [Fact]
    public async Task Past_the_daily_run_limit_a_run_still_settles_but_pays_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        // A rule bonus too, to prove the wall has no door in it: a run that pays nothing for its
        // pickups must not still hand out a completion bonus.
        await context.CreateRewardRuleAsync(
            Share7.Domain.Rewards.RewardEventType.RunSettled,
            [new GrantSpec(coins.Id, 50)],
            Share7.Domain.Rewards.RewardRepeatPolicy.EveryTime,
            referenceKey: game.Id.ToString());

        var runs = RunTestExtensions.CreateRunService(context, options: new RunOptions { MaxRunsPerDay = 2 });

        for (var i = 0; i < 2; i++)
        {
            var allowed = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
            await runs.SettleAsync(userId, allowed.Value!.RunId, RunTestExtensions.Result(coins: 10));
        }

        var third = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var blocked = await runs.SettleAsync(userId, third.Value!.RunId, RunTestExtensions.Result(coins: 10));

        Assert.True(blocked.Succeeded);
        Assert.Empty(blocked.Value!.Rewards);
        Assert.True(blocked.Value.CapReached);
        Assert.Equal("daily_run_limit", blocked.Value.CapMessage);

        // 2 runs x (10 pickups + 50 bonus). The third added nothing.
        Assert.Equal(120, blocked.Value.Balances.AmountOf(coins.Key));

        // Recorded, not discarded — what was collected is still on the run for a reviewer to see.
        await using var check = _fixture.CreateContext();
        var run = await check.RunOfAsync(third.Value.RunId);
        Assert.Equal(RunState.Settled, run.State);
        Assert.True(run.IsFlagged);
        Assert.Contains("daily_run_limit", run.FlagReason);
        Assert.Contains("\"count\":10", run.PickupsJson);
    }

    [Fact]
    public async Task A_claim_faster_than_physically_possible_settles_at_the_per_second_bound()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);

        var runs = RunTestExtensions.CreateRunService(
            context, options: new RunOptions { MaxPickupsPerSecond = 20 });

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Ten seconds of real elapsed time, so the duration clamp leaves the claimed four intact and
        // the rate bound is what actually decides this rather than the test clock.
        await context.AgeRunAsync(started.Value!.RunId, seconds: 10);

        // Four seconds, 300 coins. The per-run cap is nowhere near it; this is the bound that catches it.
        var settled = await runs.SettleAsync(
            userId, started.Value.RunId, RunTestExtensions.Result(coins: 300, durationMs: 4_000));

        Assert.True(settled.Succeeded);
        Assert.Equal(80, Assert.Single(settled.Value!.Rewards).Amount);
        Assert.True(settled.Value.CapReached);
        Assert.Equal("pickup_rate_limit", settled.Value.CapMessage);

        await using var check = _fixture.CreateContext();
        Assert.Contains("rate_capped", (await check.RunOfAsync(started.Value.RunId)).FlagReason);
    }

    [Fact]
    public async Task Inflating_the_duration_does_not_buy_headroom_on_the_per_second_bound()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);

        var runs = RunTestExtensions.CreateRunService(
            context, options: new RunOptions { MaxPickupsPerSecond = 20 });

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // "It took an hour" — claimed a moment after the server itself opened the run. The duration is
        // clamped to real elapsed time first, which is what makes the rate bound worth having.
        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 5_000, durationMs: 3_600_000));

        Assert.True(Assert.Single(settled.Value!.Rewards).Amount < 200,
            "an hour's worth of headroom must not be purchasable by claiming an hour");

        await using var check = _fixture.CreateContext();
        var run = await check.RunOfAsync(started.Value.RunId);
        Assert.Contains("duration_clamped", run.FlagReason);
        Assert.Contains("rate_capped", run.FlagReason);
    }

    [Fact]
    public async Task A_kinds_daily_allowance_spans_runs_and_counts_what_was_paid_not_what_was_claimed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();

        // 10 per run, 15 per day.
        await context.CreateValuationAsync(
            coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10, maxPerDay: 15);

        var runs = RunTestExtensions.CreateRunService(context);

        var first = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Claims 50, paid 10 — the per-run cap. The day's allowance must be charged 10, not 50.
        var one = await runs.SettleAsync(userId, first.Value!.RunId, RunTestExtensions.Result(coins: 50));

        var second = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var two = await runs.SettleAsync(userId, second.Value!.RunId, RunTestExtensions.Result(coins: 50));

        Assert.Equal(10, Assert.Single(one.Value!.Rewards).Amount);
        Assert.Equal(5, Assert.Single(two.Value!.Rewards).Amount);
        Assert.Equal("pickup_daily_limit", two.Value.CapMessage);
        Assert.Equal(15, two.Value.Balances.AmountOf(coins.Key));
    }

    [Fact]
    public async Task A_short_run_is_flagged_but_not_capped_for_being_short()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(
            context, options: new RunOptions { MinRunDurationMs = 5_000, MaxPickupsPerSecond = 100 });

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 3, durationMs: 400));

        // A genuine crash or an instant fail is short and legitimate. Whether the *claim* was possible
        // in the time is the rate bound's question, and three coins in 400ms is not a stretch.
        Assert.Equal(3, Assert.Single(settled.Value!.Rewards).Amount);
        Assert.False(settled.Value.CapReached);

        await using var check = _fixture.CreateContext();
        var run = await check.RunOfAsync(started.Value.RunId);
        Assert.True(run.IsFlagged);
        Assert.Contains("run_too_short", run.FlagReason);
    }

    [Fact]
    public async Task The_cap_message_names_the_bound_that_cost_the_most()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCappedCurrencyAsync(dailyEarnCap: 5);
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 20);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Both bite: 100 claimed is capped to 20 per run, then to 5 by the account ceiling. The
        // account ceiling is the one that actually explains the shortfall.
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 100));

        Assert.Equal(5, Assert.Single(settled.Value!.Rewards).Amount);
        Assert.Equal("daily_coin_limit", settled.Value.CapMessage);
    }
}
