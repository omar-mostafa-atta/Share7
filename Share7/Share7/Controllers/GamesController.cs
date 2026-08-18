using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Games.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// The game catalog as the Unity client sees it. Field names mirror
/// <c>MiniGameDefinitionSO</c>, and the backend is authoritative for the values — if the
/// ScriptableObject and this disagree about player counts, this wins.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GamesController : ControllerBase
{
    private readonly IGameService _gameService;

    public GamesController(IGameService gameService)
    {
        _gameService = gameService;
    }

    /// <summary>Active games only, with names in the caller's content language.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var games = await _gameService.GetAllAsync(includeInactive: false, cancellationToken);
        return Ok(games);
    }

    /// <summary>One game by id. Returns inactive games too — a client holding a stale id should learn it is disabled.</summary>
    [HttpGet("{gameId:guid}")]
    public async Task<IActionResult> GetById(Guid gameId, CancellationToken cancellationToken)
    {
        var game = await _gameService.GetByIdAsync(gameId, cancellationToken);
        return game is null ? NotFound() : Ok(game);
    }
}
