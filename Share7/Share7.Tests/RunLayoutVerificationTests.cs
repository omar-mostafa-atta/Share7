using Share7.Application.Runs.Models;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Seed verification — the one place a run is **rejected** rather than capped.
/// <para>
/// The distinction these tests exist to pin: every other bound is probabilistic, so it caps, flags
/// and pays, because a child with a bad clock trips it as readily as a script. A layout the server
/// itself generated is not a judgement about likelihood — a track either had 180 coins on it or it
/// did not — and that is what earns the right to refuse.
/// </para>
/// <para>
/// **No generator is registered in production.** These run against a deterministic stand-in so the
/// verification path is proven before the Unity track generator is ported, because the port is the
/// expensive half and shipping the machinery untested behind it would mean discovering its bugs on
/// real children's runs.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RunLayoutVerificationTests
{
    private readonly SqlServerFixture _fixture;

    public RunLayoutVerificationTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>Test 16.</summary>
    [Fact]
    public async Task A_claim_exceeding_the_seeded_layout_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);

        // The track this seed generates holds 180 coins. Not "about 180" — exactly 180.
        var runs = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 180)));

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var forged = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 500));

        Assert.False(forged.Succeeded);
        Assert.Equal("RUN_REJECTED", forged.Error?.Code);

        await using var check = _fixture.CreateContext();
        var run = await check.RunOfAsync(started.Value.RunId);

        Assert.Equal(RunState.Rejected, run.State);
        Assert.True(run.IsFlagged);
        Assert.Contains("layout_exceeded", run.FlagReason);

        // Nothing paid, and nothing left open for a retry to settle.
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Empty(await check.PayoutsOfAsync(started.Value.RunId));

        // The claim is kept, because a rejected run is the one somebody most wants to look at.
        Assert.Contains("\"count\":500", run.PickupsJson);
    }

    [Fact]
    public async Task Collecting_everything_the_layout_held_is_paid_in_full()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);

        var runs = RunTestExtensions.CreateRunService(
            context,
            options: new RunOptions { MaxPickupsPerSecond = 1_000 },
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 180)));

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var perfect = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 180));

        // The bound is the layout, not a fraction of it. A child who collects every coin on the track
        // must be paid for every coin on the track.
        Assert.True(perfect.Succeeded);
        Assert.Equal(180, Assert.Single(perfect.Value!.Rewards).Amount);
        Assert.False(perfect.Value.CapReached);
    }

    [Fact]
    public async Task A_pickup_id_the_layout_never_placed_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 10)));

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var forged = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Pickups = [new RunSignalReport { Kind = "coin", Count = 3 }],
            PickupIds = [0, 1, 4_000],
            DurationMs = 30_000
        });

        // Three coins is a modest claim; coin #4000 was never spawned. Detected exactly rather than
        // probabilistically, which is the entire value of issuing the seed.
        Assert.False(forged.Succeeded);
        Assert.Equal("RUN_REJECTED", forged.Error?.Code);
        Assert.Contains("layout_unknown_pickup", (await context.RunOfAsync(started.Value.RunId)).FlagReason);
    }

    [Fact]
    public async Task Collecting_the_same_pickup_twice_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var runs = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 10)));

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        var forged = await runs.SettleAsync(userId, started.Value!.RunId, new SubmitRunResultRequest
        {
            Pickups = [new RunSignalReport { Kind = "coin", Count = 3 }],
            PickupIds = [2, 2, 5],
            DurationMs = 30_000
        });

        Assert.False(forged.Succeeded);
        Assert.Contains("layout_duplicate_pickup", (await context.RunOfAsync(started.Value.RunId)).FlagReason);
    }

    [Fact]
    public async Task With_no_generator_registered_nothing_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 20);

        // The shipped state. Verification off, plausibility bounds only.
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 9_999));

        // Capped and flagged, never refused — the safe default, and deliberately so. A half-ported
        // generator that disagreed with the client would reject real runs from real children, which
        // is worse than the farming it was meant to stop.
        Assert.True(settled.Succeeded);
        Assert.Equal(20, Assert.Single(settled.Value!.Rewards).Amount);
        Assert.Equal(0, (await context.RunOfAsync(started.Value.RunId)).LayoutVersion);
    }

    [Fact]
    public async Task A_run_is_verified_against_the_generator_it_started_under_not_the_newest()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10_000);

        // v1 places 180 coins; the run starts under it and is stamped 1.
        var v1Only = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 180)));

        var started = await v1Only.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        Assert.Equal(1, (await context.RunOfAsync(started.Value!.RunId)).LayoutVersion);

        // A deploy lands mid-run. v2 is leaner, and the *newest* generator would call this claim
        // impossible — but the client generated its track with v1 and collected what v1 placed.
        var afterDeploy = RunTestExtensions.CreateRunService(
            context,
            options: new RunOptions { MaxPickupsPerSecond = 1_000 },
            generators:
            [
                new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 180)),
                new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 2, ("coin", 20))
            ]);

        var settled = await afterDeploy.SettleAsync(
            userId, started.Value.RunId, RunTestExtensions.Result(coins: 150));

        // Rejecting correct runs for the crime of being queued during a deploy is the failure mode
        // versioning exists to prevent.
        Assert.True(settled.Succeeded);
        Assert.Equal(150, Assert.Single(settled.Value!.Rewards).Amount);
    }

    [Fact]
    public async Task A_run_whose_generator_version_has_been_retired_settles_unverified_rather_than_failing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 50);

        var withV1 = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 1, ("coin", 10)));

        var started = await withV1.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // v1 unregistered while the result sat in the offline queue.
        var withoutV1 = RunTestExtensions.CreateRunService(
            context,
            generators: new RunTestExtensions.FixedLayoutGenerator(game.GameKey, 2, ("coin", 10)));

        var settled = await withoutV1.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 40));

        // Retiring a version costs a defence, never a child's run. Capped by the bounds instead.
        Assert.True(settled.Succeeded);
        Assert.Equal(40, Assert.Single(settled.Value!.Rewards).Amount);
    }
}
