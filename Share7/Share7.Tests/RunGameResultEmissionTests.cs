using Microsoft.EntityFrameworkCore;
using Share7.Application.Runs.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Economy;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The seam between a settled run and everything that ranks or counts it — BC-PRG-00 §1.
/// <para>
/// Before this existed the <c>Run</c> aggregate held a complete, server-validated record of
/// gameplay that **nothing downstream could see**: no board could rank a run and no objective could
/// count one, however well either was built. Every test here fixes one property of that emission,
/// and the recurring theme is that what gets raised is what was <b>settled</b>, never what was
/// reported.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RunGameResultEmissionTests
{
    private readonly SqlServerFixture _fixture;

    public RunGameResultEmissionTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_settled_run_raises_results_the_projector_can_see()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(durationMs: 90_000));

        Assert.True(settled.Succeeded);

        var results = await ResultsForAsync(userId, game.Id);

        Assert.Contains(results, r => r.Metric == LeaderboardMetrics.RunsSettled && r.Value == 1);
        Assert.Contains(results, r => r.Metric == LeaderboardMetrics.RunsCompleted && r.Value == 1);
        Assert.Contains(results, r => r.Metric == LeaderboardMetrics.RunSeconds && r.Value == 90);
        Assert.Contains(results, r => r.Metric == LeaderboardMetrics.BestRunSeconds && r.Value == 90);

        // The run, not the lesson — so a consumer can read SourceId without guessing which it is.
        Assert.All(results, r => Assert.Equal(GameResultSource.Session, r.SourceType));
        Assert.All(results, r => Assert.Equal(started.Value.RunId, r.SourceId));
    }

    [Fact]
    public async Task A_failed_run_still_counts_as_played()
    {
        // "Play three runs today" must not depend on how well they went.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        await runs.SettleAsync(
            userId,
            started.Value!.RunId,
            RunTestExtensions.Result(outcome: nameof(RunOutcome.Failed)));

        var results = await ResultsForAsync(userId, game.Id);

        Assert.Contains(results, r => r.Metric == LeaderboardMetrics.RunsSettled);
        Assert.DoesNotContain(results, r => r.Metric == LeaderboardMetrics.RunsCompleted);
    }

    [Fact]
    public async Task Pickups_are_raised_as_settled_not_as_reported()
    {
        // The cap defeated through a side door is the whole risk here: a claim of 500 that settled
        // at 50 must not pay a "collect 500" objective.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var currency = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(currency.Id, gameId: game.Id, unitValue: 1, maxPerRun: 50);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 500));

        var pickups = (await ResultsForAsync(userId, game.Id))
            .Single(r => r.Metric == LeaderboardMetrics.PickupsCollected);

        Assert.Equal(50, pickups.Value);
        Assert.Equal(SignalKinds.Coin, pickups.Scope);
    }

    [Fact]
    public async Task Duration_is_the_server_bounded_figure_not_the_clients()
    {
        // A client claiming an hour on a run that started seconds ago must not top a time-played
        // ladder. The run clamps it; this asserts the clamped value is what gets raised.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(durationMs: 3_600_000));

        var seconds = (await ResultsForAsync(userId, game.Id))
            .Single(r => r.Metric == LeaderboardMetrics.RunSeconds);

        Assert.True(
            seconds.Value < 3_600,
            $"Expected the server-bounded duration, got the client's {seconds.Value}s.");
    }

    [Fact]
    public async Task A_flagged_run_raises_flagged_results()
    {
        // Projection already excludes flagged results, and objectives will apply the same rule — so
        // a run held for review must not rank or advance a quest while it sits there.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var currency = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(currency.Id, gameId: game.Id, unitValue: 1, maxPerRun: 10);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // Over the per-run cap: settled at the cap, flagged, and still paid.
        await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 5_000));

        await using var check = _fixture.CreateContext();
        var run = await check.Runs.AsNoTracking().SingleAsync(r => r.Id == started.Value.RunId);

        Assert.True(run.IsFlagged, "Expected the over-cap run to be flagged.");

        var results = await ResultsForAsync(userId, game.Id);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(r.IsFlagged, $"{r.Metric} should have been flagged."));
    }

    [Fact]
    public async Task Settling_twice_with_one_request_id_raises_results_once()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var result = RunTestExtensions.Result(durationMs: 30_000, requestId: "settle-once");

        await runs.SettleAsync(userId, started.Value!.RunId, result);
        await runs.SettleAsync(userId, started.Value.RunId, result);

        var settledCount = (await ResultsForAsync(userId, game.Id))
            .Count(r => r.Metric == LeaderboardMetrics.RunsSettled);

        Assert.Equal(1, settledCount);
    }

    [Fact]
    public async Task Sequence_orders_the_stream_a_second_consumer_reads()
    {
        // The reason the column exists: Id is a random Guid, so "everything since I last looked" is
        // not expressible without this.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var runs = RunTestExtensions.CreateRunService(context);

        for (var i = 0; i < 2; i++)
        {
            var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
            await runs.SettleAsync(
                userId, started.Value!.RunId, RunTestExtensions.Result(durationMs: 10_000));
        }

        var sequences = (await ResultsForAsync(userId, game.Id))
            .Select(r => r.Sequence)
            .ToList();

        Assert.NotEmpty(sequences);
        Assert.Equal(sequences.Count, sequences.Distinct().Count());
        Assert.All(sequences, s => Assert.True(s > 0, "Sequence should be database-assigned."));
    }

    private async Task<List<GameResult>> ResultsForAsync(Guid userId, Guid gameId)
    {
        await using var check = _fixture.CreateContext();

        return await check.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.GameId == gameId)
            .OrderBy(r => r.Sequence)
            .ToListAsync();
    }
}
