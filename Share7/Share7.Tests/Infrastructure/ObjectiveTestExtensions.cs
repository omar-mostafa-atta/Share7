using Microsoft.Extensions.Logging.Abstractions;
using Share7.Domain.Constants;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Objectives;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

public static class ObjectiveTestExtensions
{
    /// <summary>
    /// An objective with a key unique to the calling test.
    /// <para>
    /// Objectives are global configuration and the collection shares one database, so a test that
    /// reused a key — or left one matching a metric another test raises — would count inside
    /// unrelated tests and make them pass for the wrong reason. The same discipline
    /// <c>RewardServiceTests</c> applies to reward rules.
    /// </para>
    /// </summary>
    public static async Task<Objective> CreateObjectiveAsync(
        this ApplicationDbContext context,
        string metric,
        long target,
        ObjectiveKind kind = ObjectiveKind.Daily,
        LeaderboardAggregation aggregation = LeaderboardAggregation.Sum,
        string? scope = null,
        Guid? gameId = null,
        string? key = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var objective = new Objective
        {
            Id = Guid.NewGuid(),
            Key = key ?? $"test.{Guid.NewGuid():N}"[..24],
            Kind = kind,
            Metric = metric,
            Scope = scope,
            Target = target,
            Aggregation = aggregation,
            GameId = gameId,
            IsActive = isActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Translations =
            [
                new ObjectiveTranslation
                {
                    Id = Guid.NewGuid(),
                    LangId = LanguageIds.English,
                    Name = "Test objective"
                }
            ]
        };

        context.Objectives.Add(objective);
        await context.SaveChangesAsync(cancellationToken);

        return objective;
    }

    public static ObjectiveProjector CreateProjector(ApplicationDbContext context) =>
        new(context, NullLogger<ObjectiveProjector>.Instance);

    public static ObjectiveService CreateObjectiveService(
        ApplicationDbContext context, Guid? langId = null)
    {
        var wallet = new Share7.Infrastructure.Economy.WalletService(context);

        return new ObjectiveService(
            context,
            new Share7.Infrastructure.Rewards.RewardService(
                context, wallet, new Share7.Infrastructure.Progression.LevelService(context), new Share7.Infrastructure.Commerce.EntitlementService(context)),
            wallet,
            new StubLanguageService(langId ?? LanguageIds.English));
    }

    /// <summary>
    /// Writes a result straight onto the stream, bypassing gameplay.
    /// <para>
    /// The projector's contract is with <c>GameResult</c>, not with lessons or runs — driving it
    /// through a whole attempt would test the attempt path instead, and could not produce the
    /// arbitrary metrics and scopes an objective can be authored against.
    /// </para>
    /// </summary>
    public static async Task<GameResult> AddResultAsync(
        this ApplicationDbContext context,
        Guid userId,
        string metric,
        long value,
        Guid? gameId = null,
        string? scope = null,
        DateTime? occurredAtUtc = null,
        bool isFlagged = false,
        CancellationToken cancellationToken = default)
    {
        // GameResults carries a real foreign key to Games, so a fabricated id is refused by the
        // database rather than quietly stored. A caller who does not care which game the result came
        // from still needs one to exist — the objective under test is almost never game-scoped.
        var resolvedGameId = gameId ?? (await context.CreateGameAsync(cancellationToken: cancellationToken)).Id;

        var result = new GameResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameId = resolvedGameId,
            Metric = metric,
            Scope = scope,
            Value = value,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            SourceType = GameResultSource.Session,
            SourceId = Guid.NewGuid(),
            IsFlagged = isFlagged,
            FlagReason = isFlagged ? "test" : null,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.GameResults.Add(result);
        await context.SaveChangesAsync(cancellationToken);

        return result;
    }
}
