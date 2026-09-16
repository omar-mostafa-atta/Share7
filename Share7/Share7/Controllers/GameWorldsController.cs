using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// The worlds a game offers and which of them the caller owns.
/// <para>
/// Authenticated, unlike the mode read: ownership is the answer, and there is nothing useful to say
/// about a world without knowing who is asking. The client still computes the free floor itself, so
/// a failed read costs the picker its paid worlds and never costs a child the ones they always had.
/// </para>
/// </summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GameWorldsController : ControllerBase
{
    private readonly IGameWorldService _worlds;
    private readonly ICurrentUserService _currentUser;

    public GameWorldsController(IGameWorldService worlds, ICurrentUserService currentUser)
    {
        _worlds = worlds;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Every world this game offers, with <c>owned</c> resolved for the caller. An unknown game
    /// answers with an empty list, as the mode read does.
    /// </summary>
    [HttpGet("{gameKey}/worlds")]
    public async Task<IActionResult> GetWorlds(string gameKey, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var worlds = await _worlds.GetForPlayerAsync(userId, gameKey, cancellationToken);

        return Ok(worlds);
    }
}

/// <summary>Authoring which worlds exist and how each one is unlocked.</summary>
[ApiController]
[Route("api/admin/worlds")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminGameWorldsController : ControllerBase
{
    private readonly IGameWorldAdminService _worlds;

    public AdminGameWorldsController(IGameWorldAdminService worlds) => _worlds = worlds;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? gameId, CancellationToken cancellationToken) =>
        Ok(await _worlds.ListForAuthoringAsync(gameId, cancellationToken));

    [HttpGet("{worldId:guid}")]
    public async Task<IActionResult> GetForAuthoring(Guid worldId, CancellationToken cancellationToken)
    {
        var world = await _worlds.GetForAuthoringAsync(worldId, cancellationToken);

        return world is null ? NotFound(new { errors = new[] { "World not found." } }) : Ok(world);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveGameWorldRequest request, CancellationToken cancellationToken)
    {
        var result = await _worlds.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(GetForAuthoring), new { worldId = result.Value!.WorldId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>Full replace. The key and the owning game are immutable.</summary>
    [HttpPut("{worldId:guid}")]
    public async Task<IActionResult> Update(
        Guid worldId, SaveGameWorldRequest request, CancellationToken cancellationToken)
    {
        var result = await _worlds.UpdateAsync(worldId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Removes a world policy row. **Does not un-own anything** — entitlements point at products,
    /// not at this table — so deactivating is nearly always what is wanted instead.
    /// </summary>
    [HttpDelete("{worldId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, CancellationToken cancellationToken)
    {
        var result = await _worlds.DeleteAsync(worldId, cancellationToken);

        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
