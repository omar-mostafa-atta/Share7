using Microsoft.EntityFrameworkCore;
using Share7.Domain.Leaderboards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The anti-cheat posture, which is one rule stated four ways: **flag, never reject.**
/// <para>
/// Every bound that catches a modified client also catches a child with a wrong clock or a dropped
/// connection. So the row is always kept, always reviewable, and only ever held out of ranking —
/// a leaderboard that silently deletes a genuine run has done more damage than the cheat it was
/// guarding against.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LeaderboardPlausibilityTests
{
    private readonly SqlServerFixture _fixture;

    public LeaderboardPlausibilityTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_impossible_value_is_flagged_and_kept()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await BoundAsync(context, LeaderboardMetrics.LessonBestPercent, maxValue: 100);

        var reason = await new Share7.Infrastructure.Leaderboards.PlausibilityGuard(context)
            .ReasonToFlagAsync(userId, path.GameId, LeaderboardMetrics.LessonBestPercent, 4000, DateTime.UtcNow);

        Assert.NotNull(reason);
        Assert.Contains("4000", reason);
    }

    [Fact]
    public async Task A_believable_value_is_not_flagged()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await BoundAsync(context, LeaderboardMetrics.LessonBestPercent, maxValue: 100);

        Assert.Null(await new Share7.Infrastructure.Leaderboards.PlausibilityGuard(context)
            .ReasonToFlagAsync(userId, path.GameId, LeaderboardMetrics.LessonBestPercent, 90, DateTime.UtcNow));
    }

    [Fact]
    public async Task A_metric_with_no_bound_is_unbounded()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        // The right default for a platform expecting new mini-games: an unauthored bound must not
        // silently flag every result the first game to raise a new metric produces.
        Assert.Null(await new Share7.Infrastructure.Leaderboards.PlausibilityGuard(context)
            .ReasonToFlagAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 999_999, DateTime.UtcNow));
    }

    [Fact]
    public async Task Too_many_results_in_a_day_is_flagged_even_when_each_looks_fine()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await BoundAsync(context, LeaderboardMetrics.LessonsAced, maxResultsPerDay: 3);

        for (var i = 0; i < 3; i++)
            await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1);

        // Each result is individually plausible; the rate is not. This is the exploit a value
        // ceiling cannot see.
        var reason = await new Share7.Infrastructure.Leaderboards.PlausibilityGuard(context)
            .ReasonToFlagAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1, DateTime.UtcNow);

        Assert.NotNull(reason);
    }

    [Fact]
    public async Task A_future_timestamp_is_flagged()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await BoundAsync(context, LeaderboardMetrics.LessonsAced, maxValue: 10);

        var reason = await new Share7.Infrastructure.Leaderboards.PlausibilityGuard(context)
            .ReasonToFlagAsync(
                userId, path.GameId, LeaderboardMetrics.LessonsAced, 1, DateTime.UtcNow.AddDays(1));

        Assert.NotNull(reason);
        Assert.Contains("future", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clearing_a_flag_puts_the_player_back_on_the_board()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        var flagged = await context.AddResultAsync(
            userId, path.GameId, LeaderboardMetrics.LessonsAced, 5, isFlagged: true);

        var projector = LeaderboardTestExtensions.CreateProjector(context);
        await projector.ProjectPendingAsync(100);

        Assert.Empty(await context.LeaderboardEntries.Where(e => e.CycleId == cycle.Id).ToListAsync());

        // A reviewer decides it was genuine. It re-enters through the ordinary projection path
        // rather than by writing an entry directly — one implementation of what a rank means.
        var admin = CreateAdmin(context);
        Assert.True((await admin.ResolveFlagAsync(flagged.Id, legitimate: true)).Succeeded);

        await projector.ProjectPendingAsync(100);
        await projector.ReindexCycleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();
        var entry = await check.LeaderboardEntries.SingleAsync(
            e => e.CycleId == cycle.Id && e.Cohort == LeaderboardCohort.All);

        Assert.Equal(5, entry.Value);
        Assert.Equal(1, entry.Rank);
    }

    [Fact]
    public async Task Upholding_a_flag_keeps_the_row_out_of_ranking_and_out_of_the_bin()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        var flagged = await context.AddResultAsync(
            userId, path.GameId, LeaderboardMetrics.LessonsAced, 9999, isFlagged: true);

        var admin = CreateAdmin(context);
        Assert.True((await admin.ResolveFlagAsync(flagged.Id, legitimate: false)).Succeeded);

        await LeaderboardTestExtensions.CreateProjector(context).ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();

        // Excluded, and still there. A judgement can be revisited; deleting the evidence would
        // make that impossible.
        Assert.True(await check.GameResults.AnyAsync(r => r.Id == flagged.Id && r.IsFlagged));
        Assert.Empty(await check.LeaderboardEntries.Where(e => e.CycleId == cycle.Id).ToListAsync());
    }

    [Fact]
    public async Task The_review_queue_shows_handles_not_real_names()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddResultAsync(
            userId, path.GameId, LeaderboardMetrics.LessonsAced, 9999, isFlagged: true);

        var queue = await CreateAdmin(context).GetFlaggedAsync(50);

        var row = Assert.Single(queue.Value!);

        // Knowing which child it is contributes nothing to judging whether the number is real, and
        // a review queue is exactly the screen left open on a shared desk.
        Assert.NotEmpty(row.DisplayName);
        Assert.DoesNotContain("@", row.DisplayName);
        Assert.NotNull(row.FlagReason is null ? row.Metric : row.Metric);
    }

    [Fact]
    public async Task A_bound_that_limits_nothing_is_refused()
    {
        await using var context = _fixture.CreateContext();

        var result = await CreateAdmin(context).SaveBoundAsync(new()
        {
            Metric = LeaderboardMetrics.LessonsAced
        });

        // A row that looks like protection and is not is worse than no row.
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_bound_on_a_metric_nothing_raises_is_refused()
    {
        await using var context = _fixture.CreateContext();

        var result = await CreateAdmin(context).SaveBoundAsync(new()
        {
            Metric = "NOT_A_REAL_METRIC",
            MaxValue = 10
        });

        Assert.False(result.Succeeded);
    }

    private static Share7.Infrastructure.Leaderboards.LeaderboardAdminService CreateAdmin(
        Share7.Infrastructure.Persistence.ApplicationDbContext context) =>
        new(context,
            LeaderboardTestExtensions.CreateProjector(context),
            LeaderboardTestExtensions.CreateRollover(context),
            LeaderboardTestExtensions.CreateSettlement(context),
            LeaderboardTestExtensions.CreateDisplayNames(context));

    private static async Task BoundAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        string metric,
        long? maxValue = null,
        int? maxResultsPerDay = null,
        long? maxValuePerDay = null)
    {
        context.LeaderboardMetricBounds.Add(new LeaderboardMetricBound
        {
            Id = Guid.NewGuid(),
            GameId = null,
            Metric = metric,
            MaxValue = maxValue,
            MaxResultsPerDay = maxResultsPerDay,
            MaxValuePerDay = maxValuePerDay,
            Enabled = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }
}
