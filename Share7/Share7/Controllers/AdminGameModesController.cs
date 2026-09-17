using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Authoring the mode catalogue.
/// <para>
/// Every write here reaches shipped clients without a release, which is the entire reason modes are
/// rows rather than content: a mode that turns out to be broken is withdrawn with
/// <c>isActive: false</c> in seconds, and a client built three months ago stops offering it on its
/// next catalogue read.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/modes")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminGameModesController : ControllerBase
{
    private readonly IGameModeAdminService _modes;

    public AdminGameModesController(IGameModeAdminService modes) => _modes = modes;

    /// <summary>
    /// Every mode in the authoring shape — all translations, inactive included. Filterable to one game.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? gameId, CancellationToken cancellationToken)
    {
        var modes = await _modes.ListForAuthoringAsync(gameId, cancellationToken);

        return Ok(modes);
    }

    /// <summary>One mode with its names in every language — the read an edit form fills from.</summary>
    [HttpGet("{modeId:guid}")]
    public async Task<IActionResult> GetForAuthoring(Guid modeId, CancellationToken cancellationToken)
    {
        var mode = await _modes.GetForAuthoringAsync(modeId, cancellationToken);

        return mode is null ? NotFound(new { errors = new[] { "Mode not found." } }) : Ok(mode);
    }

    /// <summary>
    /// Registers a mode. The key must be unique and must equal the Unity definition's
    /// <c>modeId</c> — that equality is the whole join between the two catalogues.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(SaveGameModeRequest request, CancellationToken cancellationToken)
    {
        var result = await _modes.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(GetForAuthoring), new { modeId = result.Value!.ModeId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Replaces a mode wholesale, translations included.
    /// <para>
    /// The key and the owning game are immutable and an update that moves either is refused: every
    /// run, result and event already recorded points at them.
    /// </para>
    /// </summary>
    [HttpPut("{modeId:guid}")]
    public async Task<IActionResult> Update(
        Guid modeId, SaveGameModeRequest request, CancellationToken cancellationToken)
    {
        var result = await _modes.UpdateAsync(modeId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Deletes a mode. Refused while an event is bound to it, refused for a game's default, and
    /// refused with a breakdown while it has runs or results unless <paramref name="force"/> is set.
    /// Withdrawing with <c>isActive: false</c> is the reversible alternative.
    /// </summary>
    [HttpDelete("{modeId:guid}")]
    public async Task<IActionResult> Delete(
        Guid modeId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        var result = await _modes.DeleteAsync(modeId, force, cancellationToken);

        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToApiErrorResult();
    }
}
