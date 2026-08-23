using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Games.Interfaces;
using Share7.Application.Games.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Games;

public class GameService : IGameService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public GameService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<GameDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.Games.AsNoTracking();
        if (!includeInactive)
            query = query.Where(g => g.IsActive);

        return await query
            .OrderBy(g => g.GameKey)
            .Select(Projection(langId))
            .ToListAsync(cancellationToken);
    }

    public async Task<GameDto?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        return await _dbContext.Games
            .AsNoTracking()
            .Where(g => g.Id == gameId)
            .Select(Projection(langId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Shared projection so the list and single-item reads cannot drift.
    /// <para>
    /// Returns an <see cref="Expression"/> rather than being a plain method: a method call
    /// inside <c>Select</c> is not translatable, so EF would materialize the entity and run it
    /// client-side — where <c>Translations</c> is empty because it was never included, and every
    /// name silently comes back blank.
    /// </para>
    /// </summary>
    private static Expression<Func<Domain.Games.Game, GameDto>> Projection(Guid langId) => g => new GameDto
    {
        GameId = g.Id,
        GameKey = g.GameKey,
        DisplayName = g.Translations.Where(t => t.LangId == langId).Select(t => t.DisplayName).FirstOrDefault() ?? string.Empty,
        Description = g.Translations.Where(t => t.LangId == langId).Select(t => t.Description).FirstOrDefault() ?? string.Empty,
        LangId = langId,
        LobbyScene = g.LobbyScene,
        GameplayScene = g.GameplayScene,
        LobbySceneAddress = g.LobbySceneAddress,
        GameplaySceneAddress = g.GameplaySceneAddress,
        MinPlayers = g.MinPlayers,
        MaxPlayers = g.MaxPlayers,
        ReadyTimeoutSeconds = g.ReadyTimeoutSeconds,
        SupportsSinglePlayer = g.SupportsSinglePlayer,
        SupportsMultiplayer = g.SupportsMultiplayer,
        UseLobby = g.UseLobby,
        UseMatchmaking = g.UseMatchmaking,
        IsActive = g.IsActive
    };
}
