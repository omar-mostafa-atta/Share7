using Microsoft.EntityFrameworkCore;
using Share7.Application.Progression.Models;
using Share7.Domain.Constants;
using Share7.Domain.Progress;
using Share7.Domain.Progression;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Progression;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Levels, and the XP they are derived from, against a real database.
/// <para>
/// The curve is global configuration shared by every test in the collection, so each test here
/// authors the whole curve it needs rather than assuming the seeded one — the same discipline
/// <c>RewardServiceTests</c> applies to reward rules, and for the same reason.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LevelProgressionTests
{
    private readonly SqlServerFixture _fixture;

    public LevelProgressionTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- the seed --------------------------------------------------------------------------

    [Fact]
    public async Task Xp_is_seeded_and_is_not_spendable()
    {
        // The whole level derivation rests on this one flag: a spendable balance falls, and a level
        // derived from a falling number falls with it.
        await using var context = _fixture.CreateContext();

        var xp = await context.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CurrencyIds.Xp);

        Assert.NotNull(xp);
        Assert.Equal("xp", xp!.Key);
        Assert.True(xp.Enabled);
        Assert.False(xp.IsSpendable);
        Assert.False(xp.IsHard);
    }

    [Fact]
    public async Task Existing_currencies_stay_spendable_after_the_migration()
    {
        // The backfill. IsSpendable arrived with a false column default, so without the explicit
        // UPDATE in the migration every currency that predates it — coins above all — would have
        // silently become unspendable.
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var stored = await context.Currencies.AsNoTracking().FirstAsync(c => c.Id == coins.Id);

        Assert.True(stored.IsSpendable);
    }

    // ---- derivation ------------------------------------------------------------------------

    [Fact]
    public async Task Level_is_the_highest_rung_at_or_below_the_balance()
    {
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));
        var levels = new LevelService(context);

        Assert.Equal(1, (await levels.DescribeAsync(0)).Level);
        Assert.Equal(1, (await levels.DescribeAsync(99)).Level);
        // Landing exactly on a threshold reaches that level.
        Assert.Equal(2, (await levels.DescribeAsync(100)).Level);
        Assert.Equal(2, (await levels.DescribeAsync(299)).Level);
        Assert.Equal(3, (await levels.DescribeAsync(300)).Level);
        Assert.Equal(3, (await levels.DescribeAsync(9_999)).Level);
    }

    [Fact]
    public async Task The_band_is_the_width_of_the_current_level_not_the_next_threshold()
    {
        // What a progress bar needs: xpIntoLevel / xpForNextLevel, with no second subtraction on
        // the client.
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));

        var at150 = await new LevelService(context).DescribeAsync(150);

        Assert.Equal(2, at150.Level);
        Assert.Equal(150, at150.Xp);
        Assert.Equal(50, at150.XpIntoLevel);
        Assert.Equal(200, at150.XpForNextLevel);
        Assert.Equal(150, at150.XpToNextLevel);
        Assert.False(at150.IsMaxLevel);
    }

    [Fact]
    public async Task Max_level_reports_an_empty_band_rather_than_a_bar_that_cannot_fill()
    {
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100));

        var top = await new LevelService(context).DescribeAsync(5_000);

        Assert.Equal(2, top.Level);
        Assert.True(top.IsMaxLevel);
        Assert.Equal(0, top.XpForNextLevel);
        Assert.Equal(0, top.XpToNextLevel);
    }

    [Fact]
    public async Task No_curve_reads_as_level_one_rather_than_throwing()
    {
        // A missing curve is a configuration gap. It must not take down a results screen.
        await using var context = _fixture.CreateContext();
        await ClearCurveAsync(context);

        var described = await new LevelService(context).DescribeAsync(4_242);

        Assert.Equal(1, described.Level);
        Assert.Equal(4_242, described.Xp);
        Assert.True(described.IsMaxLevel);
    }

    // ---- crossing --------------------------------------------------------------------------

    [Fact]
    public async Task Crossing_several_levels_at_once_reports_every_one()
    {
        // Each level is a separate reward event; collapsing them to "you reached 4" would silently
        // drop whatever levels 2 and 3 were configured to pay.
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300), (4, 600));

        var crossed = await new LevelService(context).LevelsCrossedAsync(0, 700);

        Assert.Equal([2, 3, 4], crossed);
    }

    [Fact]
    public async Task Starting_exactly_on_a_threshold_does_not_re_award_it()
    {
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));

        var crossed = await new LevelService(context).LevelsCrossedAsync(100, 299);

        Assert.Empty(crossed);
    }

    [Fact]
    public async Task Losing_xp_never_un_levels_anybody()
    {
        // The ladder only climbs — the same call CompletionState and LeaderboardAggregation.Best
        // already made, for the same reason.
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));

        Assert.Empty(await new LevelService(context).LevelsCrossedAsync(400, 50));
    }

    // ---- authoring -------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 0)]      // does not start at level 1
    [InlineData(1, 50)]     // level 1 does not start at 0 XP
    public async Task A_curve_that_does_not_start_at_level_one_and_zero_xp_is_refused(
        int firstLevel, long firstXp)
    {
        await using var context = _fixture.CreateContext();

        var result = await new LevelService(context).ReplaceCurveAsync(new ReplaceLevelCurveRequest
        {
            Levels = [Entry(firstLevel, firstXp), Entry(firstLevel + 1, firstXp + 100)]
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_curve_that_does_not_strictly_increase_is_refused()
    {
        // Two levels starting at the same XP cannot be told apart, and the derivation would return
        // whichever the sort happened to put first.
        await using var context = _fixture.CreateContext();

        var result = await new LevelService(context).ReplaceCurveAsync(new ReplaceLevelCurveRequest
        {
            Levels = [Entry(1, 0), Entry(2, 100), Entry(3, 100)]
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_curve_with_a_gap_is_refused()
    {
        await using var context = _fixture.CreateContext();

        var result = await new LevelService(context).ReplaceCurveAsync(new ReplaceLevelCurveRequest
        {
            Levels = [Entry(1, 0), Entry(2, 100), Entry(4, 300)]
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Shortening_the_curve_leaves_xp_untouched_and_demotes_nobody_below_the_new_cap()
    {
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300), (4, 600));

        var levels = new LevelService(context);
        Assert.Equal(4, (await levels.DescribeAsync(700)).Level);

        await ReplaceCurveAsync(context, (1, 0), (2, 100));

        // A fresh service, because the curve is memoised for the life of one request.
        var after = await new LevelService(_fixture.CreateContext()).DescribeAsync(700);

        Assert.Equal(2, after.Level);
        Assert.Equal(700, after.Xp);
        Assert.True(after.IsMaxLevel);
    }

    // ---- paying ----------------------------------------------------------------------------

    [Fact]
    public async Task Earning_xp_through_a_rule_levels_the_player_up_and_pays_the_level_rule()
    {
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));

        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        // 120 XP for completing this lesson: enough to reach level 2 and nothing more.
        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(CurrencyIds.Xp, 120)],
            referenceKey: path.LessonId.ToString());

        // What level 2 is worth. Authored as data, keyed on the level.
        await context.CreateRewardRuleAsync(
            RewardEventType.PlayerLevelUp,
            [new GrantSpec(coins.Id, 25)],
            referenceKey: "2");

        var rewards = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Completed));

        Assert.Contains(rewards, r => r.EventType == "PLAYER_LEVEL_UP");

        await using var check = _fixture.CreateContext();
        Assert.Equal(120, await check.BalanceOfAsync(userId, CurrencyIds.Xp));
        Assert.Equal(25, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_level_rule_is_paid_once_however_many_times_the_attempt_is_replayed()
    {
        // A level is reached once, ever — so both repeat policies collapse to the same key.
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));

        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted,
            [new GrantSpec(CurrencyIds.Xp, 120)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        await context.CreateRewardRuleAsync(
            RewardEventType.PlayerLevelUp,
            [new GrantSpec(coins.Id, 25)],
            referenceKey: "2");

        await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, attemptNumber: 1));
        await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, attemptNumber: 2));

        await using var check = _fixture.CreateContext();

        // The XP rule pays every time, so the balance climbs past level 2 and on toward 3 …
        Assert.Equal(240, await check.BalanceOfAsync(userId, CurrencyIds.Xp));
        // … but level 2 was reached once, and paid once.
        Assert.Equal(25, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_grant_spanning_two_levels_pays_both()
    {
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300));

        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(CurrencyIds.Xp, 350)],
            referenceKey: path.LessonId.ToString());

        // No reference key: pays on every level-up, which is how a flat per-level reward is said.
        await context.CreateRewardRuleAsync(
            RewardEventType.PlayerLevelUp,
            [new GrantSpec(coins.Id, 25)]);

        await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Completed));

        await using var check = _fixture.CreateContext();
        Assert.Equal(50, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_level_rule_that_grants_xp_cannot_loop()
    {
        // The structural half of the guarantee: authoring refuses such a rule, and evaluation
        // strips XP from level rules so one that predates the check still cannot re-trigger itself.
        await using var context = _fixture.CreateContext();
        await ReplaceCurveAsync(context, (1, 0), (2, 100), (3, 300), (4, 600));

        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(CurrencyIds.Xp, 120)],
            referenceKey: path.LessonId.ToString());

        // Written straight to the database, bypassing the admin validation that would refuse it.
        await context.CreateRewardRuleAsync(
            RewardEventType.PlayerLevelUp,
            [new GrantSpec(CurrencyIds.Xp, 500)]);

        await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Completed));

        await using var check = _fixture.CreateContext();

        // Only the lesson's XP landed. The level rule's XP was stripped, so the balance did not
        // run away up the curve.
        Assert.Equal(120, await check.BalanceOfAsync(userId, CurrencyIds.Xp));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static LevelThresholdEntryRequest Entry(int level, long cumulativeXp) =>
        new() { Level = level, CumulativeXp = cumulativeXp };

    private static async Task ReplaceCurveAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        params (int Level, long CumulativeXp)[] rungs)
    {
        var result = await new LevelService(context).ReplaceCurveAsync(new ReplaceLevelCurveRequest
        {
            Levels = [.. rungs.Select(r => Entry(r.Level, r.CumulativeXp))]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private static async Task ClearCurveAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context)
    {
        context.LevelThresholds.RemoveRange(await context.LevelThresholds.ToListAsync());
        await context.SaveChangesAsync();
    }
}
