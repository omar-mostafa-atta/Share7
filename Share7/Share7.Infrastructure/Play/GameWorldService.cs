using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Application.Progression.Interfaces;
using Share7.Domain.Commerce;
using Share7.Domain.Leaderboards;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Answers "which worlds may this child run in".
/// <para>
/// <b>Four ways to own a world, and only two of them are stored.</b> Free is a policy, level and
/// grade are computed from who the player is, and only a purchase or a reward writes an entitlement.
/// That split is deliberate: levelling up must not depend on a grant job having run, and a free
/// world must survive an entitlement refresh that comes back empty — the hazard the cosmetics floor
/// has already paid for once.
/// </para>
/// <para>
/// A fifth, temporary way exists: an open event that lends its world for the duration, so no child
/// is ever locked out of the competition their friends are playing.
/// </para>
/// </summary>
public class GameWorldService : IGameWorldService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;
    private readonly ILevelService _levels;

    public GameWorldService(
        ApplicationDbContext dbContext, ILanguageService languageService, ILevelService levels)
    {
        _dbContext = dbContext;
        _languageService = languageService;
        _levels = levels;
    }

    public async Task<GameWorldsResponse> GetForPlayerAsync(
        Guid userId, string gameKey, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var key = (gameKey ?? string.Empty).Trim();

        var gameId = await _dbContext.Games
            .AsNoTracking()
            .Where(g => g.GameKey == key)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (gameId is not { } id)
            return new GameWorldsResponse { GameKey = key, ServerTimeUtc = now, Worlds = [] };

        var worlds = await LoadAsync(userId, id, now, cancellationToken);

        return new GameWorldsResponse
        {
            GameKey = key,
            ServerTimeUtc = now,
            Worlds = worlds
        };
    }

    public async Task<ServiceResult<GameWorldDto>> AuthoriseAsync(
        Guid userId, Guid gameId, string worldKey, CancellationToken cancellationToken = default)
    {
        var key = (worldKey ?? string.Empty).Trim();

        if (key.Length == 0)
            return ServiceResult<GameWorldDto>.Failure(
                ApiErrors.PlayWorldUnknown, ServiceErrorKind.Validation, "No world was named.");

        var worlds = await LoadAsync(userId, gameId, DateTime.UtcNow, cancellationToken);
        var world = worlds.FirstOrDefault(w => string.Equals(w.WorldKey, key, StringComparison.Ordinal));

        if (world is null)
            return ServiceResult<GameWorldDto>.Failure(
                ApiErrors.PlayWorldUnknown,
                ServiceErrorKind.NotFound,
                $"'{key}' is not a world this game offers.");

        if (!world.Owned)
            return ServiceResult<GameWorldDto>.Failure(
                ApiErrors.PlayWorldLocked,
                ServiceErrorKind.Forbidden,
                $"'{key}' has not been unlocked on this account.",
                new Dictionary<string, object?>
                {
                    ["unlockKind"] = world.UnlockKind,
                    ["sku"] = world.Sku,
                    ["minLevel"] = world.MinLevel,
                    ["minGradeOrder"] = world.MinGradeOrder
                });

        return ServiceResult<GameWorldDto>.Success(world);
    }

    /// <summary>
    /// Every active world of one game with ownership resolved, in one pass.
    /// <para>
    /// The player's level and grade are read at most once each, and only when some world actually
    /// gates on them — the ordinary case is a game whose worlds are free, and that case costs one
    /// query.
    /// </para>
    /// </summary>
    private async Task<List<GameWorldDto>> LoadAsync(
        Guid userId, Guid gameId, DateTime now, CancellationToken cancellationToken)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var worlds = await _dbContext.GameWorlds
            .AsNoTracking()
            .Include(w => w.Translations)
            .Include(w => w.Product)
            .Where(w => w.GameId == gameId && w.IsActive)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.WorldKey)
            .ToListAsync(cancellationToken);

        if (worlds.Count == 0) return [];

        var productIds = worlds.Where(w => w.ProductId is not null).Select(w => w.ProductId!.Value).ToList();

        var owned = new HashSet<Guid>();

        // Only the sold and awarded worlds need an entitlement read at all, which is why the free
        // floor survives whatever that read returns.
        var sellable = new Dictionary<Guid, Guid>();

        if (productIds.Count > 0)
        {
            owned = (await _dbContext.Entitlements
                .AsNoTracking()
                .Where(e => e.UserId == userId && productIds.Contains(e.ProductId))
                .Select(e => e.ProductId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var offers = await _dbContext.OfferProducts
                .AsNoTracking()
                .Where(op => productIds.Contains(op.ProductId)
                             && op.Offer!.Availability == OfferAvailability.Available
                             && (op.Offer.ExpiresAtUtc == null || op.Offer.ExpiresAtUtc > now))
                .OrderBy(op => op.Offer!.SortOrder)
                .Select(op => new { op.ProductId, op.OfferId })
                .ToListAsync(cancellationToken);

            foreach (var offer in offers)
                sellable.TryAdd(offer.ProductId, offer.OfferId);
        }

        // Worlds an open event is lending right now, with the moment each loan ends.
        var lent = await _dbContext.PlayEvents
            .AsNoTracking()
            .Where(e => e.GameId == gameId
                        && e.IsActive
                        && e.CancelledAtUtc == null
                        && e.GrantsWorldForDuration
                        && e.WorldKey != null
                        && e.Cycle!.State == LeaderboardCycleState.Open)
            .Select(e => new { WorldKey = e.WorldKey!, e.Cycle!.EndsAtUtc })
            .ToListAsync(cancellationToken);

        var needsLevel = worlds.Any(w => w.UnlockKind == WorldUnlockKind.Level);
        var needsGrade = worlds.Any(w => w.UnlockKind == WorldUnlockKind.Grade);

        var level = needsLevel ? (await _levels.GetForUserAsync(userId, cancellationToken)).Level : 0;

        var gradeOrder = needsGrade
            ? await _dbContext.StudentProfiles
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => p.Grade!.Order)
                .FirstOrDefaultAsync(cancellationToken)
            : 0;

        var result = new List<GameWorldDto>(worlds.Count);

        foreach (var world in worlds)
        {
            var translation = world.Translations.FirstOrDefault(t => t.LangId == langId);

            var byPolicy = world.IsUnlockedBy(level, gradeOrder);
            var byEntitlement = world.ProductId is { } pid && owned.Contains(pid);

            // The event loan, and the only ownership with an end date on it.
            var loanEndsAtUtc = lent
                .Where(l => string.Equals(l.WorldKey, world.WorldKey, StringComparison.Ordinal))
                .Select(l => (DateTime?)l.EndsAtUtc)
                .OrderByDescending(end => end)
                .FirstOrDefault();

            result.Add(new GameWorldDto
            {
                WorldId = world.Id,
                WorldKey = world.WorldKey,
                Name = translation?.Name ?? string.Empty,
                Description = translation?.Description ?? string.Empty,
                UnlockKind = WireEnum.ToWire(world.UnlockKind).ToLowerInvariant(),
                Owned = byPolicy || byEntitlement || loanEndsAtUtc is not null,

                // Only a loan expires. A world that is owned outright reports no end date even while
                // an event happens to be lending it as well.
                OwnedUntilUtc = byPolicy || byEntitlement ? null : loanEndsAtUtc,

                ProductId = world.ProductId,
                Sku = world.Product?.Key,
                OfferId = world.ProductId is { } productId && sellable.TryGetValue(productId, out var offerId)
                    ? offerId
                    : null,
                MinLevel = world.MinLevel,
                MinGradeOrder = world.MinGradeOrder,
                SortOrder = world.SortOrder,
                IsDefault = world.IsDefault
            });
        }

        return result;
    }
}
