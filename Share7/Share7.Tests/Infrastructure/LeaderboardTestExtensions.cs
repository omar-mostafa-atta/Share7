using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Leaderboards;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Rewards;
using Share7.Infrastructure.Progression;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// Fixtures for boards, cycles and results, plus the real projector graph.
/// <para>
/// No mocks here either. Ranking correctness is almost entirely a question of what the database
/// does — window functions, unique indexes under concurrency, transactional claim — so a
/// substitute for any of it would pass while production failed.
/// </para>
/// </summary>
public static class LeaderboardTestExtensions
{
    public static LeaderboardOptions DefaultOptions() => new()
    {
        Enabled = true,
        ListedByDefault = true,
        ProjectionBatchSize = 500,
        JobClaimSeconds = 300,
        JobMaxAttempts = 5
    };

    public static LeaderboardProjector CreateProjector(
        ApplicationDbContext context, LeaderboardOptions? options = null) =>
        new(context,
            CreateDisplayNames(context, options),
            NullLogger<LeaderboardProjector>.Instance);

    public static DisplayNameService CreateDisplayNames(
        ApplicationDbContext context, LeaderboardOptions? options = null) =>
        new(context, Options.Create(options ?? DefaultOptions()));

    public static LeaderboardJobRunner CreateJobRunner(
        ApplicationDbContext context, LeaderboardOptions? options = null)
    {
        var resolved = options ?? DefaultOptions();

        return new LeaderboardJobRunner(
            context,
            CreateProjector(context, resolved),
            CreateRollover(context),
            CreateSettlement(context, resolved),
            Options.Create(resolved),
            NullLogger<LeaderboardJobRunner>.Instance);
    }

    public static LeaderboardRolloverService CreateRollover(ApplicationDbContext context) =>
        new(context, NullLogger<LeaderboardRolloverService>.Instance);

    /// <summary>
    /// The real reward service behind settlement, not a stub. Whether a retried payout pays twice
    /// is the single most important thing about this code, and only the real engine — with its
    /// unique idempotency index — can answer it.
    /// </summary>
    public static LeaderboardSettlementService CreateSettlement(
        ApplicationDbContext context, LeaderboardOptions? options = null) =>
        new(context,
            CreateProjector(context, options),
            new RewardService(context, new WalletService(context), new LevelService(context)),
            NullLogger<LeaderboardSettlementService>.Instance);

    /// <summary>A board with one open cycle covering all of time unless bounded.</summary>
    public static async Task<(LeaderboardBoard Board, LeaderboardCycle Cycle)> CreateBoardAsync(
        this ApplicationDbContext context,
        string metric,
        LeaderboardAggregation aggregation = LeaderboardAggregation.Best,
        LeaderboardSortDirection sortDirection = LeaderboardSortDirection.Desc,
        Guid? gameId = null,
        string supportedCohorts = "All,Grade",
        DateTime? startsAtUtc = null,
        DateTime? endsAtUtc = null,
        LeaderboardCycleState state = LeaderboardCycleState.Open,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var board = new LeaderboardBoard
        {
            Id = Guid.NewGuid(),
            BoardKey = $"test.{metric.ToLowerInvariant()}.{Guid.NewGuid():N}"[..40],
            GameId = gameId,
            Metric = metric,
            Aggregation = aggregation,
            SortDirection = sortDirection,
            Period = LeaderboardPeriod.AllTime,
            SupportedCohorts = supportedCohorts,
            IsActive = true,
            GraceSeconds = 60,
            CreatedAtUtc = now
        };

        var cycle = new LeaderboardCycle
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StartsAtUtc = startsAtUtc ?? now.AddDays(-30),
            EndsAtUtc = endsAtUtc ?? now.AddDays(30),
            State = state,
            CreatedAtUtc = now
        };

        context.LeaderboardBoards.Add(board);
        context.LeaderboardCycles.Add(cycle);
        await context.SaveChangesAsync(cancellationToken);

        return (board, cycle);
    }

    /// <summary>One pending result, written straight in rather than through gameplay.</summary>
    public static async Task<GameResult> AddResultAsync(
        this ApplicationDbContext context,
        Guid userId,
        Guid gameId,
        string metric,
        long value,
        DateTime? occurredAtUtc = null,
        Guid? gradeId = null,
        bool isFlagged = false,
        CancellationToken cancellationToken = default)
    {
        var result = new GameResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameId = gameId,
            Metric = metric,
            Value = value,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            SourceType = GameResultSource.Attempt,
            SourceId = Guid.NewGuid(),
            GradeId = gradeId,
            IsFlagged = isFlagged,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.GameResults.Add(result);
        await context.SaveChangesAsync(cancellationToken);

        return result;
    }
}
