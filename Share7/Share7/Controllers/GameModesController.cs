using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Play.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// The modes one game offers, as the Unity mode picker reads them.
/// <para>
/// <b>Anonymous, like the grade and language reads.</b> The picker is reachable before a session
/// exists, and an anonymous response is cacheable at the edge — which is worth more than
/// personalising it, because the one personal fact it would add (does this child own the mode) is
/// already on the device.
/// </para>
/// <para>
/// <b>Unreachable is not refusal.</b> The client falls back to the modes authored in its own content
/// when this cannot be reached, so a failure here costs the picker its server-side policy and never
/// costs a child their game.
/// </para>
/// </summary>
[ApiController]
[Route("api/games")]
[AllowAnonymous]
public class GameModesController : ControllerBase
{
    private readonly IGameModeService _modes;

    public GameModesController(IGameModeService modes) => _modes = modes;

    /// <summary>
    /// Every mode <paramref name="gameKey"/> is currently offering, in picker order.
    /// <para>
    /// An unknown game answers <c>200</c> with an empty list rather than <c>404</c>: a client asking
    /// about a game only its own content catalogue knows about is an offline-content build, which is
    /// a supported state.
    /// </para>
    /// </summary>
    /// <param name="gameKey">The Unity <c>gameId</c>, e.g. <c>game.runner</c>.</param>
    /// <param name="includeScheduled">
    /// Also return modes whose window has not opened yet, so the picker can show "opens Friday".
    /// Withdrawn modes stay hidden either way.
    /// </param>
    [HttpGet("{gameKey}/modes")]
    public async Task<IActionResult> GetModes(
        string gameKey,
        [FromQuery] bool includeScheduled,
        CancellationToken cancellationToken)
    {
        var modes = await _modes.GetForGameAsync(gameKey, includeScheduled, cancellationToken);

        return Ok(modes);
    }
}
