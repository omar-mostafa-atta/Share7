using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Authoring the mode catalogue.
/// <para>
/// <b>Two fields are immutable and the rest are not.</b> The key and the owning game are what every
/// run, result, board and event already recorded points at, so an update that moves either is
/// refused rather than cascaded — a "rename" is a new mode, and the old one is deactivated. Policy,
/// windows, prices and text all move freely, because moving them without a client release is the
/// entire reason this table exists.
/// </para>
/// </summary>
public class GameModeAdminService : IGameModeAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public GameModeAdminService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<GameModeAdminDto>> ListForAuthoringAsync(
        Guid? gameId = null, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var defaultProfileKey = await GameModeService.DefaultProfileKeyAsync(_dbContext, cancellationToken);

        var query = _dbContext.GameModes.AsNoTracking();

        if (gameId is { } id)
            query = query.Where(m => m.GameId == id);

        var modes = await query
            .Include(m => m.Translations)
            .Include(m => m.Game)
            .Include(m => m.EntitlementProduct)
            .Include(m => m.EconomyProfile)
            .OrderBy(m => m.Game!.GameKey)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.ModeKey)
            .ToListAsync(cancellationToken);

        if (modes.Count == 0) return [];

        // One grouped count for the whole page rather than a count per row: an authoring listing
        // that issues a query per mode is how a console with fifty modes becomes a slow page.
        var ids = modes.Select(m => m.Id).ToList();

        var runCounts = await _dbContext.Runs
            .AsNoTracking()
            .Where(r => r.ModeId != null && ids.Contains(r.ModeId.Value))
            .GroupBy(r => r.ModeId!.Value)
            .Select(g => new { ModeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModeId, x => x.Count, cancellationToken);

        return modes
            .Select(m => Map(m, langId, defaultProfileKey, runCounts.GetValueOrDefault(m.Id)))
            .ToList();
    }

    public async Task<GameModeAdminDto?> GetForAuthoringAsync(
        Guid modeId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var defaultProfileKey = await GameModeService.DefaultProfileKeyAsync(_dbContext, cancellationToken);

        var mode = await _dbContext.GameModes
            .AsNoTracking()
            .Include(m => m.Translations)
            .Include(m => m.Game)
            .Include(m => m.EntitlementProduct)
            .Include(m => m.EconomyProfile)
            .FirstOrDefaultAsync(m => m.Id == modeId, cancellationToken);

        if (mode is null) return null;

        var runs = await _dbContext.Runs
            .AsNoTracking()
            .CountAsync(r => r.ModeId == modeId, cancellationToken);

        return Map(mode, langId, defaultProfileKey, runs);
    }

    public async Task<ServiceResult<GameModeAdminDto>> CreateAsync(
        SaveGameModeRequest request, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, null, cancellationToken);

        if (!validated.Succeeded)
            return Propagate<GameModeAdminDto>(validated);

        var mode = new GameMode { Id = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };

        Apply(mode, request, validated.Value!);
        _dbContext.GameModes.Add(mode);

        // Before SaveChanges, so the demotion and the promotion land in one write. Two writes would
        // leave a window with no default at all, which is the state that refuses every legacy run.
        if (mode.IsDefault)
            await ClearOtherDefaultsAsync(mode.GameId, mode.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameModeAdminDto>.Success(
            (await GetForAuthoringAsync(mode.Id, cancellationToken))!);
    }

    public async Task<ServiceResult<GameModeAdminDto>> UpdateAsync(
        Guid modeId, SaveGameModeRequest request, CancellationToken cancellationToken = default)
    {
        var mode = await _dbContext.GameModes
            .Include(m => m.Translations)
            .FirstOrDefaultAsync(m => m.Id == modeId, cancellationToken);

        if (mode is null)
            return ServiceResult<GameModeAdminDto>.Failure(
                ApiErrors.PlayModeUnknown, ServiceErrorKind.NotFound, "No mode has that id.");

        var key = (request.ModeKey ?? string.Empty).Trim();

        if (!string.Equals(key, mode.ModeKey, StringComparison.Ordinal))
            return ServiceResult<GameModeAdminDto>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Conflict,
                $"A mode key is immutable once published; '{mode.ModeKey}' cannot become '{key}'. " +
                "Deactivate this mode and create the new one.");

        if (request.GameId != mode.GameId)
            return ServiceResult<GameModeAdminDto>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Conflict,
                "A mode cannot be moved to another game — its rules are one game's own type, and " +
                "every run recorded against it belongs to the game it was played in.");

        var validated = await ValidateAsync(request, modeId, cancellationToken);

        if (!validated.Succeeded)
            return Propagate<GameModeAdminDto>(validated);

        // A game must keep a default. Clearing the flag on the mode that holds it would leave every
        // client that sends no mode key unable to open a session, so the flag is refused down rather
        // than silently moved to a row nobody chose.
        if (mode.IsDefault && !request.IsDefault)
            return ServiceResult<GameModeAdminDto>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Conflict,
                "This is the game's default mode. Make another mode the default instead of clearing " +
                "the flag — a game with no default refuses every session that names no mode.");

        // Full replace: a language dropped from the payload must not linger as a stale name.
        _dbContext.GameModeTranslations.RemoveRange(mode.Translations);
        mode.Translations.Clear();

        Apply(mode, request, validated.Value!);
        mode.UpdatedAtUtc = DateTime.UtcNow;

        if (mode.IsDefault)
            await ClearOtherDefaultsAsync(mode.GameId, mode.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameModeAdminDto>.Success(
            (await GetForAuthoringAsync(mode.Id, cancellationToken))!);
    }

    public async Task<ServiceResult<GameModeDeletionImpact>> DeleteAsync(
        Guid modeId, bool force, CancellationToken cancellationToken = default)
    {
        var mode = await _dbContext.GameModes.FirstOrDefaultAsync(m => m.Id == modeId, cancellationToken);

        if (mode is null)
            return ServiceResult<GameModeDeletionImpact>.Failure(
                ApiErrors.PlayModeUnknown, ServiceErrorKind.NotFound, "No mode has that id.");

        if (mode.IsDefault)
            return ServiceResult<GameModeDeletionImpact>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Conflict,
                "A game's default mode cannot be deleted — every client that sends no mode key " +
                "resolves through it. Make another mode the default first.");

        var impact = new GameModeDeletionImpact
        {
            Runs = await _dbContext.Runs.CountAsync(r => r.ModeId == modeId, cancellationToken),
            Results = await _dbContext.GameResults.CountAsync(r => r.ModeId == modeId, cancellationToken),
            Events = await _dbContext.PlayEvents.CountAsync(e => e.ModeId == modeId, cancellationToken)
        };

        // An event points at exactly one mode and cannot be re-pointed after it has run, so this one
        // is refused outright rather than forced: deleting the mode would orphan a settled prize table.
        if (impact.Events > 0)
            return ServiceResult<GameModeDeletionImpact>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Conflict,
                $"{impact.Events} event(s) are bound to this mode. Deactivate it instead.",
                impact);

        if (!force && impact.HasHistory)
            return ServiceResult<GameModeDeletionImpact>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Conflict,
                $"This mode has {impact.Describe()}. Deleting it detaches all of it — resend with " +
                "force=true to confirm, or set isActive=false to withdraw the mode without losing history.",
                impact);

        _dbContext.GameModes.Remove(mode);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameModeDeletionImpact>.Success(impact);
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// Demotes whatever else holds the default for this game.
    /// <para>
    /// Done in the same SaveChanges as the promotion, because the filtered unique index would
    /// otherwise reject the write — and correctly so: the window between two writes is a window in
    /// which a game has no default and every legacy client is refused.
    /// </para>
    /// </summary>
    private async Task ClearOtherDefaultsAsync(Guid gameId, Guid keepModeId, CancellationToken cancellationToken)
    {
        var others = await _dbContext.GameModes
            .Where(m => m.GameId == gameId && m.IsDefault && m.Id != keepModeId)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            other.IsDefault = false;
            other.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static void Apply(GameMode mode, SaveGameModeRequest request, Validated validated)
    {
        mode.GameId = request.GameId;
        mode.ModeKey = validated.ModeKey;
        mode.Topologies = validated.Topologies;
        mode.MinPlayers = request.MinPlayers;
        mode.MaxPlayers = request.MaxPlayers;
        mode.IsActive = request.IsActive;
        mode.IsDefault = request.IsDefault;
        mode.AvailableFromUtc = request.AvailableFromUtc;
        mode.AvailableToUtc = request.AvailableToUtc;
        mode.RequiresEntitlement = request.RequiresEntitlement;
        mode.EntitlementProductId = request.RequiresEntitlement ? request.EntitlementProductId : null;
        mode.MinGradeOrder = request.MinGradeOrder;
        mode.CountsTowardMastery = request.CountsTowardMastery;
        mode.SettlesEconomy = request.SettlesEconomy;
        mode.CountsTowardRanking = request.CountsTowardRanking;
        mode.EconomyProfileId = request.EconomyProfileId;
        mode.SortOrder = request.SortOrder;

        foreach (var name in validated.Names)
        {
            mode.Translations.Add(new GameModeTranslation
            {
                ModeId = mode.Id,
                LangId = name.LangId,
                Name = name.Name,
                Description = name.Description
            });
        }
    }

    private sealed record Validated(string ModeKey, PlayTopologies Topologies, List<ModeName> Names);

    private sealed record ModeName(Guid LangId, string Name, string Description);

    private async Task<ServiceResult<Validated>> ValidateAsync(
        SaveGameModeRequest request, Guid? existingModeId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var key = (request.ModeKey ?? string.Empty).Trim();

        if (key.Length == 0)
            errors.Add("modeKey is required.");
        else if (await _dbContext.GameModes.AnyAsync(
                     m => m.ModeKey == key && (existingModeId == null || m.Id != existingModeId),
                     cancellationToken))
            return ServiceResult<Validated>.Failure(
                ApiErrors.PlayModeKeyTaken,
                ServiceErrorKind.Conflict,
                $"Another mode already uses the key '{key}'.");

        if (!await _dbContext.Games.AnyAsync(g => g.Id == request.GameId, cancellationToken))
            errors.Add($"No game has id {request.GameId}.");

        var topologies = PlayTopologyTokens.FromTokens(request.Topologies);

        if (topologies is not { } set || set == PlayTopologies.None)
        {
            errors.Add("topologies must contain at least one of: solo, versus, coop.");
            set = PlayTopologies.None;
        }
        else if (set.HasFlag(PlayTopologies.Coop))
        {
            // Declared in the schema, refused at authoring. Nothing on either side knows what a
            // shared run settles as yet, and a mode offered in a topology the platform cannot
            // account for would pay out under rules nobody has written.
            errors.Add("Co-op is not implemented yet; a mode cannot offer it.");
        }

        if (request.MinPlayers > request.MaxPlayers)
            errors.Add("minPlayers cannot be greater than maxPlayers.");

        if (set != PlayTopologies.None && !set.HasFlag(PlayTopologies.Solo) && request.MaxPlayers < 2)
            errors.Add("A mode that is not offered solo needs maxPlayers of at least 2.");

        if (set.HasFlag(PlayTopologies.Solo) && request.MinPlayers > 1 && !set.HasFlag(PlayTopologies.Versus))
            errors.Add("A solo-only mode cannot require more than one player.");

        if (request is { AvailableFromUtc: { } from, AvailableToUtc: { } to } && to <= from)
            errors.Add("availableToUtc must be after availableFromUtc.");

        if (request.RequiresEntitlement)
        {
            if (request.EntitlementProductId is not { } productId)
                errors.Add("A mode that requires an entitlement must name the product it is sold under.");
            else if (!await _dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken))
                errors.Add($"No product has id {productId}.");
        }

        if (request.EconomyProfileId is { } profileId
            && !await _dbContext.EconomyProfiles.AnyAsync(p => p.Id == profileId, cancellationToken))
            errors.Add($"No economy profile has id {profileId}.");

        // The default has to be playable by a client that knows nothing about modes: switched on,
        // always open, free, and ungated. Otherwise the compatibility path it exists to serve is
        // exactly the path it refuses.
        if (request.IsDefault)
        {
            if (!request.IsActive) errors.Add("A default mode must be active.");
            if (request.AvailableFromUtc is not null || request.AvailableToUtc is not null)
                errors.Add("A default mode cannot have an availability window.");
            if (request.RequiresEntitlement) errors.Add("A default mode cannot require an entitlement.");
            if (request.MinGradeOrder > 0) errors.Add("A default mode cannot be grade gated.");
        }

        var supplied = request.Translations ?? [];
        var names = new List<ModeName>();

        foreach (var translation in supplied)
        {
            var name = (translation.Name ?? string.Empty).Trim();

            if (name.Length == 0)
                errors.Add($"name is required for language {translation.LangId}.");
            else
                names.Add(new ModeName(translation.LangId, name, (translation.Description ?? string.Empty).Trim()));
        }

        if (supplied.Select(t => t.LangId).Distinct().Count() != supplied.Count)
            errors.Add("The same language appears more than once.");

        var languages = await _dbContext.Languages
            .Select(l => new { l.Id, l.Code })
            .ToListAsync(cancellationToken);

        foreach (var name in names.Where(n => languages.All(l => l.Id != n.LangId)))
            errors.Add($"Unknown language '{name.LangId}'.");

        var missing = languages.Where(l => names.All(n => n.LangId != l.Id)).Select(l => l.Code).ToList();

        if (missing.Count > 0)
            errors.Add($"A name is required for every language. Missing: {string.Join(", ", missing)}.");

        return errors.Count > 0
            ? ServiceResult<Validated>.Failure(
                ApiErrors.PlayModeInvalid,
                ServiceErrorKind.Validation,
                string.Join(" ", errors),
                new Dictionary<string, object?> { ["problems"] = errors })
            : ServiceResult<Validated>.Success(new Validated(key, topologies!.Value, names));
    }

    private static GameModeAdminDto Map(GameMode mode, Guid langId, string defaultProfileKey, int runCount)
    {
        var translation = mode.Translations.FirstOrDefault(t => t.LangId == langId);

        return new GameModeAdminDto
        {
            ModeId = mode.Id,
            GameId = mode.GameId,
            GameKey = mode.Game?.GameKey ?? string.Empty,
            ModeKey = mode.ModeKey,
            Topologies = PlayTopologyTokens.ToTokens(mode.Topologies),
            MinPlayers = mode.MinPlayers,
            MaxPlayers = mode.MaxPlayers,
            IsActive = mode.IsActive,
            IsDefault = mode.IsDefault,
            AvailableFromUtc = mode.AvailableFromUtc,
            AvailableToUtc = mode.AvailableToUtc,
            RequiresEntitlement = mode.RequiresEntitlement,
            EntitlementProductId = mode.EntitlementProductId,
            EntitlementSku = mode.EntitlementProduct?.Key,
            MinGradeOrder = mode.MinGradeOrder,
            CountsTowardMastery = mode.CountsTowardMastery,
            SettlesEconomy = mode.SettlesEconomy,
            CountsTowardRanking = mode.CountsTowardRanking,
            EconomyProfileId = mode.EconomyProfileId,
            EconomyProfileKey = mode.EconomyProfile?.ProfileKey ?? defaultProfileKey,
            SortOrder = mode.SortOrder,
            Name = translation?.Name ?? string.Empty,
            Description = translation?.Description ?? string.Empty,
            LangId = langId,
            Translations = mode.Translations
                .OrderBy(t => t.LangId)
                .Select(t => new GameModeTranslationRequest
                {
                    LangId = t.LangId,
                    Name = t.Name,
                    Description = t.Description
                })
                .ToList(),
            RunCount = runCount
        };
    }

    private static ServiceResult<T> Propagate<T>(ServiceResult source) =>
        new() { ErrorKind = source.ErrorKind, Errors = source.Errors, Error = source.Error, Details = source.Details };
}
