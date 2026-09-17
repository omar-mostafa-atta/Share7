using Microsoft.EntityFrameworkCore;
using Share7.Domain.Leaderboards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The projector's contract. Almost every test here is really the same assertion from a different
/// angle: **a leaderboard number must be a pure function of the results behind it.** If replaying,
/// rebuilding or crashing mid-batch can change a rank, then no rank on the platform means
/// anything.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LeaderboardProjectionTests
{
    private readonly SqlServerFixture _fixture;

    public LeaderboardProjectionTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_result_becomes_a_ranked_entry()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1);

        var projector = LeaderboardTestExtensions.CreateProjector(context);
        Assert.Equal(1, await projector.ProjectPendingAsync(100));
        await projector.ReindexCycleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();
        var entry = await check.LeaderboardEntries.SingleAsync(
            e => e.CycleId == cycle.Id && e.Cohort == LeaderboardCohort.All);

        Assert.Equal(userId, entry.UserId);
        Assert.Equal(1, entry.Value);
        Assert.Equal(1, entry.Rank);

        // The row carries a generated handle, never anything the account was registered with.
        Assert.NotEmpty(entry.DisplayName);
        Assert.DoesNotContain("@", entry.DisplayName);
    }

    [Fact]
    public async Task Re_running_the_projector_over_the_same_result_changes_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, LeaderboardAggregation.Sum);

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1);

        var projector = LeaderboardTestExtensions.CreateProjector(context);

        await projector.ProjectPendingAsync(100);
        await projector.ProjectPendingAsync(100);
        await projector.ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();
        var entry = await check.LeaderboardEntries.SingleAsync(e => e.CycleId == cycle.Id
            && e.Cohort == LeaderboardCohort.All);

        // Sum is the aggregation that cannot tell a replay from a real second result by looking at
        // the total, which is why the claim lives on the source row rather than on the entry.
        Assert.Equal(1, entry.Value);
    }

    [Fact]
    public async Task A_worse_result_does_not_lower_a_rank()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonBestPercent, LeaderboardAggregation.Best);

        var projector = LeaderboardTestExtensions.CreateProjector(context);

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonBestPercent, 90);
        await projector.ProjectPendingAsync(100);

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonBestPercent, 20);
        await projector.ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();
        var entry = await check.LeaderboardEntries.SingleAsync(e => e.CycleId == cycle.Id
            && e.Cohort == LeaderboardCohort.All);

        Assert.Equal(90, entry.Value);
    }

    [Fact]
    public async Task Equal_scores_rank_by_who_got_there_first()
    {
        await using var context = _fixture.CreateContext();
        var early = await TestData.CreateUserAsync(context);
        var late = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonBestPercent);

        var baseline = DateTime.UtcNow.AddHours(-5);

        await context.AddResultAsync(
            late, path.GameId, LeaderboardMetrics.LessonBestPercent, 100, baseline.AddHours(2));
        await context.AddResultAsync(
            early, path.GameId, LeaderboardMetrics.LessonBestPercent, 100, baseline);

        var projector = LeaderboardTestExtensions.CreateProjector(context);
        await projector.ProjectPendingAsync(100);
        await projector.ReindexCycleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();
        var ranked = await check.LeaderboardEntries
            .Where(e => e.CycleId == cycle.Id && e.Cohort == LeaderboardCohort.All)
            .OrderBy(e => e.Rank)
            .ToListAsync();

        // Never by user id: arbitrary, and it looks rigged to whichever child always loses ties.
        Assert.Equal(early, ranked[0].UserId);
        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(late, ranked[1].UserId);
        Assert.Equal(2, ranked[1].Rank);
    }

    [Fact]
    public async Task A_grade_cohort_row_is_written_alongside_the_open_one()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        var gradeId = Guid.NewGuid();
        await context.AddResultAsync(
            userId, path.GameId, LeaderboardMetrics.LessonsAced, 1, gradeId: gradeId);

        await LeaderboardTestExtensions.CreateProjector(context).ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();
        var entries = await check.LeaderboardEntries
            .Where(e => e.CycleId == cycle.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Cohort == LeaderboardCohort.All && e.CohortKey == Guid.Empty);
        Assert.Contains(entries, e => e.Cohort == LeaderboardCohort.Grade && e.CohortKey == gradeId);
    }

    [Fact]
    public async Task A_grade_the_result_did_not_carry_produces_no_grade_row()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1);

        await LeaderboardTestExtensions.CreateProjector(context).ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();
        var entries = await check.LeaderboardEntries.Where(e => e.CycleId == cycle.Id).ToListAsync();

        Assert.Single(entries);
        Assert.Equal(LeaderboardCohort.All, entries[0].Cohort);
    }

    [Fact]
    public async Task A_flagged_result_is_kept_and_not_ranked()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        var flagged = await context.AddResultAsync(
            userId, path.GameId, LeaderboardMetrics.LessonsAced, 999, isFlagged: true);

        await LeaderboardTestExtensions.CreateProjector(context).ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();

        // Kept, because rejecting a child's run outright over a bad clock or a dropped connection
        // is not a defensible anti-cheat posture. Reviewable, and reversible.
        Assert.True(await check.GameResults.AnyAsync(r => r.Id == flagged.Id));
        Assert.Empty(await check.LeaderboardEntries.Where(e => e.CycleId == cycle.Id).ToListAsync());
    }

    [Fact]
    public async Task A_result_outside_the_cycle_window_is_not_projected_into_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var now = DateTime.UtcNow;
        var (_, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced,
            startsAtUtc: now.AddDays(-2),
            endsAtUtc: now.AddDays(-1));

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1, now);

        await LeaderboardTestExtensions.CreateProjector(context).ProjectPendingAsync(100);

        await using var check = _fixture.CreateContext();
        Assert.Empty(await check.LeaderboardEntries.Where(e => e.CycleId == cycle.Id).ToListAsync());

        // Still claimed, so it does not sit in the pending queue forever being re-read.
        Assert.False(await check.GameResults.AnyAsync(r => r.ProjectedAtUtc == null && !r.IsFlagged));
    }

    [Fact]
    public async Task Rebuilding_a_cycle_reproduces_identical_ranks()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, LeaderboardAggregation.Sum);

        var players = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var userId = await TestData.CreateUserAsync(context);
            players.Add(userId);

            for (var n = 0; n <= i; n++)
                await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1);
        }

        var projector = LeaderboardTestExtensions.CreateProjector(context);
        await projector.ProjectPendingAsync(500);
        await projector.ReindexCycleAsync(cycle.Id);

        var before = await SnapshotAsync(cycle.Id);

        // The disaster-recovery path, and the proof that entries are genuinely derived: if this
        // came back different, the entry table would be holding state the results cannot explain.
        await projector.RebuildCycleAsync(cycle.Id);
        await projector.ProjectPendingAsync(500);
        await projector.ReindexCycleAsync(cycle.Id);

        Assert.Equal(before, await SnapshotAsync(cycle.Id));
        Assert.Equal(5, before.Count);
    }

    [Fact]
    public async Task A_hidden_player_keeps_their_rank()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var (_, cycle) = await context.CreateBoardAsync(LeaderboardMetrics.LessonsAced);

        var displayNames = LeaderboardTestExtensions.CreateDisplayNames(context);
        await displayNames.EnsureHandleAsync(userId);
        await displayNames.SetHiddenAsync(userId, true);

        await context.AddResultAsync(userId, path.GameId, LeaderboardMetrics.LessonsAced, 1);

        var projector = LeaderboardTestExtensions.CreateProjector(context);
        await projector.ProjectPendingAsync(100);
        await projector.ReindexCycleAsync(cycle.Id);

        await using var check = _fixture.CreateContext();
        var entry = await check.LeaderboardEntries.SingleAsync(
            e => e.CycleId == cycle.Id && e.Cohort == LeaderboardCohort.All);

        // Hiding is not forfeiting: excluded from listings, still on the ladder.
        Assert.True(entry.IsHidden);
        Assert.Equal(1, entry.Rank);
    }

    [Fact]
    public async Task A_guardians_decision_is_not_the_childs_to_reverse()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var displayNames = LeaderboardTestExtensions.CreateDisplayNames(context);
        await displayNames.EnsureHandleAsync(userId);

        var row = await context.PlayerDisplayNames.SingleAsync(n => n.UserId == userId);
        row.IsHiddenByGuardian = true;
        await context.SaveChangesAsync();

        Assert.False(await displayNames.SetHiddenAsync(userId, false));
        Assert.True(await displayNames.IsHiddenAsync(userId));
    }

    [Fact]
    public async Task Handles_are_unique_and_disclose_nothing()
    {
        await using var context = _fixture.CreateContext();
        var displayNames = LeaderboardTestExtensions.CreateDisplayNames(context);

        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
            ids.Add(await TestData.CreateUserAsync(context));

        var handles = await displayNames.EnsureHandlesAsync(ids);

        Assert.Equal(ids.Count, handles.Values.Distinct().Count());
        Assert.All(handles.Values, h => Assert.DoesNotContain("@", h));

        // Asking twice returns the same handle rather than minting a second one.
        Assert.Equal(handles[ids[0]], await displayNames.EnsureHandleAsync(ids[0]));
    }

    private async Task<List<(Guid UserId, long Value, int Rank)>> SnapshotAsync(Guid cycleId)
    {
        await using var context = _fixture.CreateContext();

        return await context.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.CycleId == cycleId && e.Cohort == LeaderboardCohort.All)
            .OrderBy(e => e.Rank)
            .Select(e => new ValueTuple<Guid, long, int>(e.UserId, e.Value, e.Rank))
            .ToListAsync();
    }
}
