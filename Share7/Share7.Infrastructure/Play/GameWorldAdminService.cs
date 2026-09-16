using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Authoring the world catalogue.
/// <para>
/// <b>Deleting a row here does not take a world away from anyone.</b> Entitlements point at products,
/// not at this table, so removing a policy row only removes the platform's ability to describe or
/// offer the world — which is why the listing is a poor way to retire one and <c>isActive</c> is the
/// supported move.
/// </para>
/// </summary>
public class GameWorldAdminService : IGameWorldAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public GameWorldAdminService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<GameWorldAdminDto>> ListForAuthoringAsync(
        Guid? gameId = null, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.GameWorlds.AsNoTracking();

        if (gameId is { } id) query = query.Where(w => w.GameId == id);

        var worlds = await query
            .Include(w => w.Translations)
            .Include(w => w.Game)
            .Include(w => w.Product)
            .OrderBy(w => w.Game!.GameKey)
            .ThenBy(w => w.SortOrder)
            .ThenBy(w => w.WorldKey)
            .ToListAsync(cancellationToken);

        return worlds.Select(w => Map(w, langId)).ToList();
    }

    public async Task<GameWorldAdminDto?> GetForAuthoringAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var world = await _dbContext.GameWorlds
            .AsNoTracking()
            .Include(w => w.Translations)
            .Include(w => w.Game)
            .Include(w => w.Product)
            .FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);

        return world is null ? null : Map(world, langId);
    }

    public async Task<ServiceResult<GameWorldAdminDto>> CreateAsync(
        SaveGameWorldRequest request, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, null, cancellationToken);

        if (!validated.Succeeded) return Propagate<GameWorldAdminDto>(validated);

        var world = new GameWorld { Id = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };

        Apply(world, request, validated.Value!);
        _dbContext.GameWorlds.Add(world);

        if (world.IsDefault) await ClearOtherDefaultsAsync(world.GameId, world.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameWorldAdminDto>.Success(
            (await GetForAuthoringAsync(world.Id, cancellationToken))!);
    }

    public async Task<ServiceResult<GameWorldAdminDto>> UpdateAsync(
        Guid worldId, SaveGameWorldRequest request, CancellationToken cancellationToken = default)
    {
        var world = await _dbContext.GameWorlds
            .Include(w => w.Translations)
            .FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);

        if (world is null)
            return ServiceResult<GameWorldAdminDto>.Failure(
                ApiErrors.PlayWorldUnknown, ServiceErrorKind.NotFound, "No world has that id.");

        var key = (request.WorldKey ?? string.Empty).Trim();

        if (!string.Equals(key, world.WorldKey, StringComparison.Ordinal) || request.GameId != world.GameId)
            return ServiceResult<GameWorldAdminDto>.Failure(
                ApiErrors.PlayWorldInvalid,
                ServiceErrorKind.Conflict,
                "A world's key and game are immutable — entitlements, events and recorded runs all " +
                "name them. Deactivate this row and author the new world instead.");

        var validated = await ValidateAsync(request, worldId, cancellationToken);

        if (!validated.Succeeded) return Propagate<GameWorldAdminDto>(validated);

        if (world.IsDefault && !request.IsDefault)
            return ServiceResult<GameWorldAdminDto>.Failure(
                ApiErrors.PlayWorldInvalid,
                ServiceErrorKind.Conflict,
                "This is the game's fallback world. Make another world the default instead of " +
                "clearing the flag — a game with no fallback has nothing to run when no world is chosen.");

        _dbContext.GameWorldTranslations.RemoveRange(world.Translations);
        world.Translations.Clear();

        Apply(world, request, validated.Value!);
        world.UpdatedAtUtc = DateTime.UtcNow;

        if (world.IsDefault) await ClearOtherDefaultsAsync(world.GameId, world.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameWorldAdminDto>.Success(
            (await GetForAuthoringAsync(world.Id, cancellationToken))!);
    }

    public async Task<ServiceResult> DeleteAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var world = await _dbContext.GameWorlds.FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);

        if (world is null)
            return ServiceResult.Failure(
                ApiErrors.PlayWorldUnknown, ServiceErrorKind.NotFound, "No world has that id.");

        if (world.IsDefault)
            return ServiceResult.Failure(
                ApiErrors.PlayWorldInvalid,
                ServiceErrorKind.Conflict,
                "A game's fallback world cannot be deleted. Make another world the default first.");

        var pinned = await _dbContext.PlayEvents
            .CountAsync(e => e.GameId == world.GameId && e.WorldKey == world.WorldKey, cancellationToken);

        if (pinned > 0)
            return ServiceResult.Failure(
                ApiErrors.PlayWorldInvalid,
                ServiceErrorKind.Conflict,
                $"{pinned} event(s) are pinned to this world. Deactivate it instead.");

        _dbContext.GameWorlds.Remove(world);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- helpers

    private async Task ClearOtherDefaultsAsync(Guid gameId, Guid keepWorldId, CancellationToken cancellationToken)
    {
        var others = await _dbContext.GameWorlds
            .Where(w => w.GameId == gameId && w.IsDefault && w.Id != keepWorldId)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            other.IsDefault = false;
            other.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static void Apply(GameWorld world, SaveGameWorldRequest request, Validated validated)
    {
        world.GameId = request.GameId;
        world.WorldKey = validated.WorldKey;
        world.UnlockKind = validated.UnlockKind;

        // Cleared for the kinds that compute ownership: a level-gated world holding a product id
        // would look sold in the console and be free in the game.
        world.ProductId = validated.UnlockKind is WorldUnlockKind.Purchase or WorldUnlockKind.Reward
            ? request.ProductId
            : null;

        world.MinLevel = validated.UnlockKind == WorldUnlockKind.Level ? request.MinLevel : 0;
        world.MinGradeOrder = validated.UnlockKind == WorldUnlockKind.Grade ? request.MinGradeOrder : 0;
        world.SortOrder = request.SortOrder;
        world.IsActive = request.IsActive;
        world.IsDefault = request.IsDefault;

        foreach (var name in validated.Names)
        {
            world.Translations.Add(new GameWorldTranslation
            {
                WorldId = world.Id,
                LangId = name.LangId,
                Name = name.Name,
                Description = name.Description
            });
        }
    }

    private sealed record Validated(string WorldKey, WorldUnlockKind UnlockKind, List<WorldName> Names);

    private sealed record WorldName(Guid LangId, string Name, string Description);

    private async Task<ServiceResult<Validated>> ValidateAsync(
        SaveGameWorldRequest request, Guid? existingWorldId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var key = (request.WorldKey ?? string.Empty).Trim();

        if (key.Length == 0)
            errors.Add("worldKey is required.");
        else if (await _dbContext.GameWorlds.AnyAsync(
                     w => w.GameId == request.GameId
                          && w.WorldKey == key
                          && (existingWorldId == null || w.Id != existingWorldId),
                     cancellationToken))
            errors.Add($"This game already has a world keyed '{key}'.");

        if (!await _dbContext.Games.AnyAsync(g => g.Id == request.GameId, cancellationToken))
            errors.Add($"No game has id {request.GameId}.");

        if (!WireEnum.TryFromWire<WorldUnlockKind>(request.UnlockKind, out var unlockKind))
        {
            errors.Add("unlockKind must be one of: free, purchase, level, grade, reward.");
            unlockKind = WorldUnlockKind.Free;
        }

        if (unlockKind is WorldUnlockKind.Purchase or WorldUnlockKind.Reward)
        {
            if (request.ProductId is not { } productId)
                errors.Add("A world that is bought or awarded must name the product it is granted as.");
            else if (!await _dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken))
                errors.Add($"No product has id {productId}.");
        }

        if (unlockKind == WorldUnlockKind.Level && request.MinLevel <= 0)
            errors.Add("A level-gated world needs a minLevel above zero.");

        if (unlockKind == WorldUnlockKind.Grade && request.MinGradeOrder <= 0)
            errors.Add("A grade-gated world needs a minGradeOrder above zero.");

        // The fallback has to be reachable by everybody, always: it is what a session runs in when
        // nothing was chosen, including the very first session an account ever plays.
        if (request.IsDefault && unlockKind != WorldUnlockKind.Free)
            errors.Add("A game's fallback world must be free.");

        if (request.IsDefault && !request.IsActive)
            errors.Add("A game's fallback world must be active.");

        var supplied = request.Translations ?? [];
        var names = new List<WorldName>();

        foreach (var translation in supplied)
        {
            var name = (translation.Name ?? string.Empty).Trim();

            if (name.Length == 0)
                errors.Add($"name is required for language {translation.LangId}.");
            else
                names.Add(new WorldName(translation.LangId, name, (translation.Description ?? string.Empty).Trim()));
        }

        if (supplied.Select(t => t.LangId).Distinct().Count() != supplied.Count)
            errors.Add("The same language appears more than once.");

        var languages = await _dbContext.Languages.Select(l => new { l.Id, l.Code }).ToListAsync(cancellationToken);

        foreach (var name in names.Where(n => languages.All(l => l.Id != n.LangId)))
            errors.Add($"Unknown language '{name.LangId}'.");

        var missing = languages.Where(l => names.All(n => n.LangId != l.Id)).Select(l => l.Code).ToList();

        if (missing.Count > 0)
            errors.Add($"A name is required for every language. Missing: {string.Join(", ", missing)}.");

        return errors.Count > 0
            ? ServiceResult<Validated>.Failure(
                ApiErrors.PlayWorldInvalid,
                ServiceErrorKind.Validation,
                string.Join(" ", errors),
                new Dictionary<string, object?> { ["problems"] = errors })
            : ServiceResult<Validated>.Success(new Validated(key, unlockKind, names));
    }

    private static GameWorldAdminDto Map(GameWorld world, Guid langId)
    {
        var translation = world.Translations.FirstOrDefault(t => t.LangId == langId);

        return new GameWorldAdminDto
        {
            WorldId = world.Id,
            GameId = world.GameId,
            GameKey = world.Game?.GameKey ?? string.Empty,
            WorldKey = world.WorldKey,
            UnlockKind = WireEnum.ToWire(world.UnlockKind).ToLowerInvariant(),
            ProductId = world.ProductId,
            Sku = world.Product?.Key,
            MinLevel = world.MinLevel,
            MinGradeOrder = world.MinGradeOrder,
            SortOrder = world.SortOrder,
            IsActive = world.IsActive,
            IsDefault = world.IsDefault,
            Name = translation?.Name ?? string.Empty,
            Description = translation?.Description ?? string.Empty,
            LangId = langId,
            Translations = world.Translations
                .OrderBy(t => t.LangId)
                .Select(t => new GameWorldTranslationRequest
                {
                    LangId = t.LangId,
                    Name = t.Name,
                    Description = t.Description
                })
                .ToList()
        };
    }

    private static ServiceResult<T> Propagate<T>(ServiceResult source) =>
        new() { ErrorKind = source.ErrorKind, Errors = source.Errors, Error = source.Error, Details = source.Details };
}
