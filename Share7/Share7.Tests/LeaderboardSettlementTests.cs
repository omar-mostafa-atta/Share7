using Microsoft.EntityFrameworkCore;
using Share7.Domain.Leaderboards;
using Share7.Domain.Rewards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Settlement, and the one property that matters most about it: **running it twice pays once.**
/// <para>
/// The job table delivers at-least-once and a shared host can kill a worker between the grant and
/// the record of it, so every test here is really asking whether a retry is safe. Paying a child
/// twice for third place is a defect nobody reports.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LeaderboardSettlementTests
{
    private readonly SqlServerFixture _fixture;

    public LeaderboardSettlementTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Settling_freezes_final_ranks_and_pays_the_prize_band()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        var (board, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, LeaderboardAggregation.Sum);

        // The prize table, authored as data through the engine that already exists.
        await context.CreateRewardRuleAsync(
            RewardEventType.LeaderboardSettled,
            [new GrantSpec(coins.Id, 100)],
            referenceKey: $"{board.BoardKey}:1");

        var winner = await TestData.CreateUserAsync(context);
        var runnerUp = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(winner, path.GameId, LeaderboardMetrics.LessonsAced, 5);
        await context.AddResultAsync(runnerUp, path.GameId, LeaderboardMetrics.LessonsAced, 2);

        await CloseAsync(context, cycle.Id);
        var settled = await LeaderboardTestExtensions.CreateSettlement(context).SettleAsync(cycle.Id);

        Assert.True(settled.Succeeded, string.Join("; ", settled.Errors));

        await using var check = _fixture.CreateContext();

        var frozen = await check.LeaderboardSettlements
            .Where(s => s.CycleId == cycle.Id && s.Cohort == LeaderboardCohort.All)
            .OrderBy(s => s.FinalRank)
            .ToListAsync();

        Assert.Equal(2, frozen.Count);
        Assert.Equal(winner, frozen[0].UserId);
        Assert.Equal(1, frozen[0].FinalRank);

        Assert.Equal(LeaderboardCycleState.Settled, (await check.LeaderboardCycles.SingleAsync(c => c.Id == cycle.Id)).State);

        // First place was paid; second place had no rule and is still marked done, because
        // "nobody authored a prize for rank 2" is a finished placing, not an outstanding debt.
        Assert.Equal(100, await check.BalanceOfAsync(winner, coins.Id));
        Assert.Equal(0, await check.BalanceOfAsync(runnerUp, coins.Id));
        Assert.All(frozen, s => Assert.True(s.RewardIssued));
    }

    [Fact]
    public async Task Settling_twice_pays_once()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        var (board, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, LeaderboardAggregation.Sum);

        await context.CreateRewardRuleAsync(
            RewardEventType.LeaderboardSettled,
            [new GrantSpec(coins.Id, 100)],
            referenceKey: $"{board.BoardKey}:1");

        var winner = await TestData.CreateUserAsync(context);
        await context.AddResultAsync(winner, path.GameId, LeaderboardMetrics.LessonsAced, 5);

        await CloseAsync(context, cycle.Id);

        var settlement = LeaderboardTestExtensions.CreateSettlement(context);

        await settlement.SettleAsync(cycle.Id);
        await settlement.SettleAsync(cycle.Id);
        await settlement.SettleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();

        // The whole point of the retry machinery. Three runs, one prize.
        Assert.Equal(100, await check.BalanceOfAsync(winner, coins.Id));
        Assert.Single(await check.LeaderboardSettlements.Where(s => s.CycleId == cycle.Id).ToListAsync());
    }

    [Fact]
    public async Task Every_band_a_rank_falls_in_pays()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        var (board, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, LeaderboardAggregation.Sum);

        // "A bigger prize for a better rank", expressed as composing rules rather than branching.
        await context.CreateRewardRuleAsync(
            RewardEventType.LeaderboardSettled, [new GrantSpec(coins.Id, 100)],
            referenceKey: $"{board.BoardKey}:1");

        await context.CreateRewardRuleAsync(
            RewardEventType.LeaderboardSettled, [new GrantSpec(coins.Id, 10)],
            referenceKey: $"{board.BoardKey}:top10");

        var winner = await TestData.CreateUserAsync(context);
        await context.AddResultAsync(winner, path.GameId, LeaderboardMetrics.LessonsAced, 5);

        await CloseAsync(context, cycle.Id);
        await LeaderboardTestExtensions.CreateSettlement(context).SettleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();
        Assert.Equal(110, await check.BalanceOfAsync(winner, coins.Id));
    }

    [Fact]
    public async Task A_hidden_player_is_still_settled_and_still_paid()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        var (board, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, LeaderboardAggregation.Sum);

        await context.CreateRewardRuleAsync(
            RewardEventType.LeaderboardSettled, [new GrantSpec(coins.Id, 100)],
            referenceKey: $"{board.BoardKey}:1");

        var shy = await TestData.CreateUserAsync(context);

        var displayNames = LeaderboardTestExtensions.CreateDisplayNames(context);
        await displayNames.EnsureHandleAsync(shy);
        await displayNames.SetHiddenAsync(shy, true);

        await context.AddResultAsync(shy, path.GameId, LeaderboardMetrics.LessonsAced, 5);

        await CloseAsync(context, cycle.Id);
        await LeaderboardTestExtensions.CreateSettlement(context).SettleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();

        // Opting out of being *listed* is not opting out of having competed. Quietly withholding a
        // child's prize because they asked not to be shown would be a nasty surprise.
        Assert.Equal(100, await check.BalanceOfAsync(shy, coins.Id));
    }

    [Fact]
    public async Task An_open_cycle_cannot_be_settled()
    {
        await using var context = _fixture.CreateContext();
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        var result = await LeaderboardTestExtensions.CreateSettlement(context).SettleAsync(cycle.Id);

        // Ranks have to stop moving before prizes are cut, or settlement races the last second of
        // play.
        Assert.False(result.Succeeded);
    }

    /// <summary>Closes a cycle the way rollover does, so settlement sees a real closed window.</summary>
    private static async Task CloseAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid cycleId)
    {
        var cycle = await context.LeaderboardCycles.SingleAsync(c => c.Id == cycleId);

        cycle.State = LeaderboardCycleState.Closed;
        cycle.ClosedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }
}
