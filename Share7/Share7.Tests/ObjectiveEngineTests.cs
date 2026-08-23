using Microsoft.EntityFrameworkCore;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Domain.Rewards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The objective engine against a real database.
/// <para>
/// One engine is every quest and every achievement, so the tests that matter are the ones that fix
/// its edges: what counts, what does not, what a cycle boundary does, and the difference between
/// finishing something and being paid for it.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ObjectiveEngineTests
{
    private readonly SqlServerFixture _fixture;

    public ObjectiveEngineTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- counting ------------------------------------------------------------------------------

    [Fact]
    public async Task Results_accumulate_until_the_target_completes_the_objective()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 3);
        var projector = ObjectiveTestExtensions.CreateProjector(context);

        for (var i = 0; i < 2; i++)
            await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        await projector.ProjectForUserAsync(userId);
        Assert.Equal(ObjectiveState.InProgress, await StateOfAsync(userId, objective.Id));

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await projector.ProjectForUserAsync(userId);

        Assert.Equal(ObjectiveState.Completed, await StateOfAsync(userId, objective.Id));
    }

    [Fact]
    public async Task Projecting_twice_over_the_same_results_counts_them_once()
    {
        // The property that lets the inline pass and the batch pass overlap freely. A Sum counter
        // cannot tell a replay from a genuine second result by looking at its total, which is why
        // LastSequence lives on the row.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 10);
        var projector = ObjectiveTestExtensions.CreateProjector(context);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 4);

        await projector.ProjectForUserAsync(userId);
        await projector.ProjectForUserAsync(userId);
        await ObjectiveTestExtensions.CreateProjector(_fixture.CreateContext())
            .ProjectPendingAsync();

        Assert.Equal(4, await ValueOfAsync(userId, objective.Id));
    }

    [Fact]
    public async Task A_flagged_result_never_counts()
    {
        // A run held back for review must not advance a quest while it sits there — the same
        // exclusion leaderboard projection already applies.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 5);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 5, isFlagged: true);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        Assert.Equal(0, await ValueOfAsync(userId, objective.Id));
    }

    [Fact]
    public async Task Scope_narrows_a_metric_to_one_sub_dimension()
    {
        // What makes "collect 200 coins" and "collect 20 gems" two rows over one metric rather than
        // two metrics — the thing that has to survive 200 mini-games.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var coins = await context.CreateObjectiveAsync(
            LeaderboardMetrics.PickupsCollected, target: 100, scope: "coin");

        await context.AddResultAsync(userId, LeaderboardMetrics.PickupsCollected, 30, scope: "coin");
        await context.AddResultAsync(userId, LeaderboardMetrics.PickupsCollected, 70, scope: "gem");

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        Assert.Equal(30, await ValueOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task Best_aggregation_keeps_the_high_water_mark()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var objective = await context.CreateObjectiveAsync(
            LeaderboardMetrics.BestRunSeconds,
            target: 300,
            aggregation: LeaderboardAggregation.Best);

        await context.AddResultAsync(userId, LeaderboardMetrics.BestRunSeconds, 200);
        await context.AddResultAsync(userId, LeaderboardMetrics.BestRunSeconds, 90);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        // A worse run is a no-op, never a demotion — the call CompletionState and
        // LeaderboardAggregation.Best both already made.
        Assert.Equal(200, await ValueOfAsync(userId, objective.Id));
    }

    [Fact]
    public async Task A_retired_objective_stops_counting()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var objective = await context.CreateObjectiveAsync(
            LeaderboardMetrics.RunsSettled, target: 5, isActive: false);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 5);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        Assert.Null(await RowOrNullAsync(userId, objective.Id));
    }

    // ---- cycles --------------------------------------------------------------------------------

    [Fact]
    public async Task Yesterdays_daily_and_todays_are_different_rows()
    {
        // The reason there is no rollover job: the cycle is part of the key, so midnight needs
        // nothing to run and nothing can fail to run.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 3);

        await context.AddResultAsync(
            userId, LeaderboardMetrics.RunsSettled, 1, occurredAtUtc: DateTime.UtcNow.AddDays(-1));
        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        await using var check = _fixture.CreateContext();
        var rows = await check.UserObjectiveProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ObjectiveId == objective.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Value));
    }

    [Fact]
    public void An_achievement_counts_under_one_cycle_forever()
    {
        var today = ObjectiveCycle.KeyFor(ObjectiveKind.Achievement, DateTime.UtcNow);
        var nextYear = ObjectiveCycle.KeyFor(ObjectiveKind.Achievement, DateTime.UtcNow.AddYears(1));

        Assert.Equal(ObjectiveCycle.AllTime, today);
        Assert.Equal(today, nextYear);
    }

    [Fact]
    public void A_weekly_cycle_uses_iso_weeks()
    {
        // A naive day-of-year division puts a Sunday in whichever week the arithmetic lands on, so
        // a child's weekly quest would reset a day early once a year — the kind of bug nobody finds
        // deliberately.
        var sunday = new DateTime(2027, 1, 3, 12, 0, 0, DateTimeKind.Utc);
        var monday = new DateTime(2027, 1, 4, 12, 0, 0, DateTimeKind.Utc);

        Assert.NotEqual(
            ObjectiveCycle.KeyFor(ObjectiveKind.Weekly, sunday),
            ObjectiveCycle.KeyFor(ObjectiveKind.Weekly, monday));
    }

    // ---- claiming ------------------------------------------------------------------------------

    [Fact]
    public async Task Claiming_a_completed_objective_pays_its_rule_once()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 1);

        await context.CreateRewardRuleAsync(
            RewardEventType.ObjectiveCompleted,
            [new GrantSpec(currency.Id, 25)],
            referenceKey: objective.Key);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var service = ObjectiveTestExtensions.CreateObjectiveService(_fixture.CreateContext());

        var first = await service.ClaimAsync(userId, objective.Key, "claim-1");
        Assert.True(first.Succeeded);

        // Nothing left to collect — the same answer whether it was already claimed or never
        // finished, which is deliberate.
        var second = await ObjectiveTestExtensions
            .CreateObjectiveService(_fixture.CreateContext())
            .ClaimAsync(userId, objective.Key, "claim-2");

        Assert.False(second.Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.Equal(25, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task An_unfinished_objective_cannot_be_claimed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 5);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var claim = await ObjectiveTestExtensions
            .CreateObjectiveService(_fixture.CreateContext())
            .ClaimAsync(userId, objective.Key);

        Assert.False(claim.Succeeded);
    }

    [Fact]
    public async Task A_claimed_objective_stops_counting()
    {
        // Counting into a claimed row would let a finished quest creep past its target and look
        // collectable again.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 1);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        await ObjectiveTestExtensions
            .CreateObjectiveService(_fixture.CreateContext())
            .ClaimAsync(userId, objective.Key);

        await using var more = _fixture.CreateContext();
        await more.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 5);
        await ObjectiveTestExtensions.CreateProjector(more).ProjectForUserAsync(userId);

        await using var check = _fixture.CreateContext();
        var row = await check.UserObjectiveProgress
            .AsNoTracking()
            .FirstAsync(p => p.UserId == userId && p.ObjectiveId == objective.Id);

        Assert.Equal(ObjectiveState.Claimed, row.State);
        Assert.Equal(1, row.Value);
    }

    [Fact]
    public async Task A_newly_authored_objective_backfills_over_existing_results()
    {
        // What makes launching an achievement not require every child to replay their history.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(userId, LeaderboardMetrics.LessonsAced, 4);

        var objective = await context.CreateObjectiveAsync(
            LeaderboardMetrics.LessonsAced, target: 3, kind: ObjectiveKind.Achievement);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        Assert.Equal(ObjectiveState.Completed, await StateOfAsync(userId, objective.Id));
    }

    // ---- streaks -------------------------------------------------------------------------------

    /// <summary>
    /// The regression test for a streak that sat behind the objective guard: a deployment can have
    /// no quests authored at all, and a child who played today still played today.
    /// </summary>
    [Fact]
    public async Task A_day_played_counts_toward_the_streak_with_no_objectives_authored()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var streak = await StreakOrNullAsync(userId);
        Assert.NotNull(streak);
        Assert.Equal(1, streak!.Current);
        Assert.Equal(1, streak.Best);
    }

    [Fact]
    public async Task Consecutive_days_extend_the_streak_with_no_objectives_authored()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(
            userId, LeaderboardMetrics.RunsSettled, 1, occurredAtUtc: DateTime.UtcNow.AddDays(-1));
        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var streak = await StreakOrNullAsync(userId);
        Assert.Equal(2, streak!.Current);
        Assert.Equal(2, streak.Best);
    }

    /// <summary>
    /// Guards the bound the streak-only pass reads under. It starts at the last counted day rather
    /// than at the whole history, and that day has to be skipped rather than recounted — a second
    /// projection over the same results must leave the number alone.
    /// </summary>
    [Fact]
    public async Task Projecting_twice_counts_a_day_once_with_no_objectives_authored()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        var projector = ObjectiveTestExtensions.CreateProjector(context);
        await projector.ProjectForUserAsync(userId);
        await projector.ProjectForUserAsync(userId);

        var streak = await StreakOrNullAsync(userId);
        Assert.Equal(1, streak!.Current);
    }

    /// <summary>
    /// The same forgiveness the objective-backed path applies, on the path that has no objectives:
    /// one missed day spends a freeze rather than breaking the count.
    /// </summary>
    [Fact]
    public async Task A_missed_day_spends_a_freeze_with_no_objectives_authored()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(
            userId, LeaderboardMetrics.RunsSettled, 1, occurredAtUtc: DateTime.UtcNow.AddDays(-2));
        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var streak = await StreakOrNullAsync(userId);
        Assert.Equal(2, streak!.Current);
        Assert.Equal(1, streak.FreezesRemaining);
    }

    /// <summary>
    /// The objective-backed path still folds the streak. Authoring a quest must not be what turns
    /// the count on, and must not be what turns it off either.
    /// </summary>
    [Fact]
    public async Task A_streak_still_folds_when_objectives_are_authored()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 3);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var streak = await StreakOrNullAsync(userId);
        Assert.Equal(1, streak!.Current);
    }

    /// <summary>A flagged result is not a day played, on this path as on every other.</summary>
    [Fact]
    public async Task A_flagged_result_does_not_start_a_streak()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1, isFlagged: true);

        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        Assert.Null(await StreakOrNullAsync(userId));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<UserStreak?> StreakOrNullAsync(Guid userId)
    {
        await using var check = _fixture.CreateContext();

        return await check.UserStreaks
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.StreakKey == StreakKeys.Daily);
    }

    private async Task<UserObjectiveProgress?> RowOrNullAsync(Guid userId, Guid objectiveId)
    {
        await using var check = _fixture.CreateContext();

        return await check.UserObjectiveProgress
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ObjectiveId == objectiveId);
    }

    private async Task<ObjectiveState> StateOfAsync(Guid userId, Guid objectiveId) =>
        (await RowOrNullAsync(userId, objectiveId))?.State ?? ObjectiveState.InProgress;

    private async Task<long> ValueOfAsync(Guid userId, Guid objectiveId) =>
        (await RowOrNullAsync(userId, objectiveId))?.Value ?? 0;
}
