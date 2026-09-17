using Microsoft.Extensions.Options;
using Share7.Application.Common.Interfaces;
using Share7.Domain.Constants;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Leaderboards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The board listing's scoping contract. A game-scoped request must still surface the global
/// boards, because a global board (<c>GameId == null</c>) belongs to every game by definition —
/// the same way a global board with <c>LangId == null</c> belongs to every language. An exact
/// match on <c>GameId</c> would leave a game whose only boards are global showing an empty
/// leaderboard, which is exactly the trap a per-game discovery screen would fall into.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LeaderboardBoardListingTests
{
    private readonly SqlServerFixture _fixture;

    public LeaderboardBoardListingTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_game_scoped_request_returns_the_games_boards_and_the_global_boards()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var (globalBoard, _) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsCompleted, gameId: null);
        var (gameBoard, _) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, gameId: path.GameId);
        var (otherGameBoard, _) = await context.CreateBoardAsync(
            LeaderboardMetrics.RunsCompleted, gameId: Guid.NewGuid());

        var service = CreateService(context, userId);

        var result = await service.GetBoardsAsync(userId, path.GameId);

        Assert.True(result.Succeeded);
        var ids = result.Value!.Select(b => b.BoardId).ToList();

        // The game's own board and every global board come back...
        Assert.Contains(globalBoard.Id, ids);
        Assert.Contains(gameBoard.Id, ids);
        // ...but a board scoped to a different game does not.
        Assert.DoesNotContain(otherGameBoard.Id, ids);
    }

    [Fact]
    public async Task An_unfiltered_request_returns_every_active_board()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var (globalBoard, _) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsCompleted, gameId: null);
        var (gameBoard, _) = await context.CreateBoardAsync(
            LeaderboardMetrics.LessonsAced, gameId: Guid.NewGuid());

        var service = CreateService(context, userId);

        var result = await service.GetBoardsAsync(userId, gameId: null);

        Assert.True(result.Succeeded);
        var ids = result.Value!.Select(b => b.BoardId).ToList();

        Assert.Contains(globalBoard.Id, ids);
        Assert.Contains(gameBoard.Id, ids);
    }

    private static LeaderboardService CreateService(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid userId)
    {
        var options = LeaderboardTestExtensions.DefaultOptions();
        var currentUser = new StubCurrentUser(userId);

        return new LeaderboardService(
            context,
            LeaderboardTestExtensions.CreateDisplayNames(context, options),
            new LanguageService(context, currentUser),
            Options.Create(options),
            Options.Create(new JwtSettings { Secret = "test-secret-not-used-for-listing" }));
    }

    private sealed class StubCurrentUser : ICurrentUserService
    {
        public StubCurrentUser(Guid userId) => UserId = userId;

        public Guid? UserId { get; }
        public string? Email => null;
        public bool IsAuthenticated => true;
        public Guid? PreferredLanguageId => LanguageIds.English;
    }
}
