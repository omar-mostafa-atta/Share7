using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Progression.Models;

namespace Share7.API.Controllers;

/// <summary>
/// The caller's own progression — today their level, later their quests, achievements and streak.
/// <para>
/// **One snapshot, not one endpoint per feature.** The home screen wants all of it at once, and a
/// field added to this response is not a breaking change where an added round trip is. This is the
/// same reason <c>POST /api/progress/attempts</c> returns rewards, balances and unlocks together
/// rather than making the client go and fetch each.
/// </para>
/// <para>
/// **Everything here is derived, and derived server-side.** There is no write route: a level is a
/// function of an XP balance that only the reward engine can move, so there is nothing for a client
/// to submit and nothing it could inflate.
/// </para>
/// </summary>
[ApiController]
[Route("api/progression")]
[Authorize]
public class ProgressionController : ControllerBase
{
    private readonly ILevelService _levels;
    private readonly ICurrentUserService _currentUser;

    public ProgressionController(ILevelService levels, ICurrentUserService currentUser)
    {
        _levels = levels;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Where the caller stands.
    /// <code>
    /// {
    ///   "level": {
    ///     "level": 4,
    ///     "xp": 380,
    ///     "xpIntoLevel": 80,
    ///     "xpForNextLevel": 200,
    ///     "xpToNextLevel": 120,
    ///     "isMaxLevel": false
    ///   },
    ///   "serverTimeUtc": "2026-08-23T19:40:00Z"
    /// }
    /// </code>
    /// <para>
    /// <c>xpForNextLevel</c> is the **width of the current band**, not the next absolute threshold —
    /// <c>xpIntoLevel / xpForNextLevel</c> fills a progress bar directly.
    /// </para>
    /// <para>
    /// A player with no XP, or a deployment with no curve authored, reads as level 1 with an empty
    /// band and <c>isMaxLevel: true</c>. That is deliberate: a missing curve is a configuration gap
    /// and must not fail the call, but the client must not draw a bar that can never fill either.
    /// </para>
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var level = await _levels.GetForUserAsync(userId, cancellationToken);

        return Ok(new ProgressionSnapshotDto
        {
            Level = level,
            ServerTimeUtc = DateTime.UtcNow
        });
    }
}
