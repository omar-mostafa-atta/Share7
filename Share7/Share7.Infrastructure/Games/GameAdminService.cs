using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Games.Interfaces;
using Share7.Application.Games.Models;
using Share7.Domain.Games;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Games;

public class GameAdminService : IGameAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGameService _gameService;

    public GameAdminService(ApplicationDbContext dbContext, IGameService gameService)
    {
        _dbContext = dbContext;
        _gameService = gameService;
    }

    public async Task<ServiceResult<GameDto>> CreateAsync(SaveGameRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, null, cancellationToken);
        if (!validation.Succeeded)
            return Propagate<GameDto>(validation);

        var game = new Game { Id = Guid.NewGuid() };
        Apply(game, request, validation.Value!);

        _dbContext.Games.Add(game);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameDto>.Success((await _gameService.GetByIdAsync(game.Id, cancellationToken))!);
    }

    public async Task<ServiceResult<GameDto>> UpdateAsync(Guid gameId, SaveGameRequest request, CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

        if (game is null)
            return ServiceResult<GameDto>.NotFound("Game not found.");

        var validation = await ValidateAsync(request, gameId, cancellationToken);
        if (!validation.Succeeded)
            return Propagate<GameDto>(validation);

        // Full replace: drop the existing translations and rebuild from the request, so a
        // language removed from the payload does not linger as a stale name.
        _dbContext.GameTranslations.RemoveRange(game.Translations);
        game.Translations.Clear();

        Apply(game, request, validation.Value!);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameDto>.Success((await _gameService.GetByIdAsync(game.Id, cancellationToken))!);
    }

    public async Task<ServiceResult<GameDeletionImpact>> DeleteAsync(Guid gameId, bool force, CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games.FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        if (game is null)
            return ServiceResult<GameDeletionImpact>.NotFound("Game not found.");

        var impact = new GameDeletionImpact
        {
            LessonProgressRows = await _dbContext.UserLessonProgress.CountAsync(p => p.GameId == gameId, cancellationToken),
            QuestionProgressRows = await _dbContext.UserQuestionProgress.CountAsync(p => p.GameId == gameId, cancellationToken),
            Unlocks = await _dbContext.UserNodeUnlocks.CountAsync(u => u.GameId == gameId, cancellationToken),
            Students = await _dbContext.UserLessonProgress
                .Where(p => p.GameId == gameId)
                .Select(p => p.UserId)
                .Distinct()
                .CountAsync(cancellationToken)
        };

        if (!force && impact.HasProgress)
            return ServiceResult<GameDeletionImpact>.Conflict(
                $"This game has {impact.Describe()}. Deleting it destroys all of that — resend with " +
                "force=true to confirm, or set isActive=false instead to hide it without losing anything.",
                impact);

        // Progress and unlocks cascade from Games.
        _dbContext.Games.Remove(game);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GameDeletionImpact>.Success(impact);
    }

    // ------------------------------------------------------------- helpers

    private static void Apply(Game game, SaveGameRequest request, List<GameName> names)
    {
        game.GameKey = request.GameKey.Trim();
        game.LobbyScene = request.LobbyScene;
        game.GameplayScene = request.GameplayScene;
        game.MinPlayers = request.MinPlayers;
        game.MaxPlayers = request.MaxPlayers;
        game.ReadyTimeoutSeconds = request.ReadyTimeoutSeconds;
        game.SupportsSinglePlayer = request.SupportsSinglePlayer;
        game.SupportsMultiplayer = request.SupportsMultiplayer;
        game.UseLobby = request.UseLobby;
        game.UseMatchmaking = request.UseMatchmaking;
        game.IsActive = request.IsActive;

        foreach (var name in names)
        {
            game.Translations.Add(new GameTranslation
            {
                GameId = game.Id,
                LangId = name.LangId,
                DisplayName = name.DisplayName,
                Description = name.Description
            });
        }
    }

    /// <summary>
    /// Checks the key is free, the player range is coherent with the declared modes, and that
    /// there is a name for every configured language.
    /// </summary>
    private async Task<ServiceResult<List<GameName>>> ValidateAsync(
        SaveGameRequest request, Guid? existingGameId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var key = (request.GameKey ?? string.Empty).Trim();
        if (key.Length == 0)
            errors.Add("gameKey is required.");
        else if (await _dbContext.Games.AnyAsync(
                     g => g.GameKey.ToLower() == key.ToLower() && (existingGameId == null || g.Id != existingGameId),
                     cancellationToken))
            errors.Add($"Another game already uses the key '{key}'.");

        if (request.MinPlayers > request.MaxPlayers)
            errors.Add("minPlayers cannot be greater than maxPlayers.");

        if (!request.SupportsSinglePlayer && !request.SupportsMultiplayer)
            errors.Add("A game must support single-player, multiplayer, or both.");

        // A single-player-only game with maxPlayers > 1 (or the reverse) would have matchmaking
        // and the declared modes disagreeing about the same game.
        if (!request.SupportsMultiplayer && request.MaxPlayers > 1)
            errors.Add("maxPlayers must be 1 when the game does not support multiplayer.");

        if (!request.SupportsSinglePlayer && request.MinPlayers < 2)
            errors.Add("minPlayers must be at least 2 when the game does not support single-player.");

        var supplied = request.Translations ?? [];
        var names = new List<GameName>();

        foreach (var translation in supplied)
        {
            var displayName = (translation.DisplayName ?? string.Empty).Trim();
            if (displayName.Length == 0)
                errors.Add($"displayName is required for language {translation.LangId}.");
            else
                names.Add(new GameName(translation.LangId, displayName, (translation.Description ?? string.Empty).Trim()));
        }

        if (supplied.Select(t => t.LangId).Distinct().Count() != supplied.Count)
            errors.Add("The same language appears more than once.");

        var languages = await _dbContext.Languages.Select(l => new { l.Id, l.Code }).ToListAsync(cancellationToken);

        foreach (var name in names.Where(n => languages.All(l => l.Id != n.LangId)))
            errors.Add($"Unknown language '{name.LangId}'.");

        var missing = languages.Where(l => names.All(n => n.LangId != l.Id)).Select(l => l.Code).ToList();
        if (missing.Count > 0)
            errors.Add($"A displayName is required for every language. Missing: {string.Join(", ", missing)}.");

        return errors.Count > 0
            ? ServiceResult<List<GameName>>.Invalid([.. errors])
            : ServiceResult<List<GameName>>.Success(names);
    }

    private static ServiceResult<T> Propagate<T>(ServiceResult source) =>
        new() { ErrorKind = source.ErrorKind, Errors = source.Errors };

    private sealed record GameName(Guid LangId, string DisplayName, string Description);
}
