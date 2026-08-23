using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Games.Interfaces;
using Share7.Application.Games.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Managing the game catalog. A game is one row with a display name and description per
/// language — not one row per language, which would give the same game two ids and split its
/// progress in half.
/// </summary>
[ApiController]
[Route("api/admin/games")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminGamesController : ControllerBase
{
    private readonly IGameService _gameService;
    private readonly IGameAdminService _gameAdminService;

    public AdminGamesController(IGameService gameService, IGameAdminService gameAdminService)
    {
        _gameService = gameService;
        _gameAdminService = gameAdminService;
    }

    /// <summary>Every game, disabled ones included.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var games = await _gameService.GetAllAsync(includeInactive: true, cancellationToken);
        return Ok(games);
    }

    /// <summary>
    /// One game with its names in <b>every</b> language — the read an edit form fills from.
    /// <para>
    /// <c>GET /api/games/{id}</c> deliberately cannot serve this. It resolves one translation from
    /// the caller's content language, which is right for the Unity client and wrong for authoring:
    /// <c>PUT</c> here is a full replace, so a form filled from that read would send a single
    /// language back and silently delete the others.
    /// </para>
    /// </summary>
    [HttpGet("{gameId:guid}")]
    public async Task<IActionResult> GetForAuthoring(Guid gameId, CancellationToken cancellationToken)
    {
        var game = await _gameAdminService.GetForAuthoringAsync(gameId, cancellationToken);

        return game is null ? NotFound(new { errors = new[] { "Game not found." } }) : Ok(game);
    }

    /// <summary>
    /// Registers a game. Requires a unique <c>gameKey</c> and a displayName for every
    /// configured language.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(SaveGameRequest request, CancellationToken cancellationToken)
    {
        var result = await _gameAdminService.CreateAsync(request, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GamesController.GetById), "Games", new { gameId = result.Value!.GameId }, result.Value)
            : result.ToErrorResult();
    }

    /// <summary>Replaces a game wholesale, translations included. Set <c>isActive: false</c> here to retire one.</summary>
    [HttpPut("{gameId:guid}")]
    public async Task<IActionResult> Update(Guid gameId, SaveGameRequest request, CancellationToken cancellationToken)
    {
        var result = await _gameAdminService.UpdateAsync(gameId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>
    /// Deletes a game <b>and every student's progress and unlocks for it</b>. Refused with 409
    /// and a breakdown while any progress exists, unless <paramref name="force"/> is set.
    /// Deactivating is the reversible alternative.
    /// </summary>
    [HttpDelete("{gameId:guid}")]
    public async Task<IActionResult> Delete(Guid gameId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        var result = await _gameAdminService.DeleteAsync(gameId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }
}
