using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Progression.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// The level curve — how much XP each level costs. **Admin only**, for the same reason reward rules
/// are: a player who could edit this could reach any level they liked.
/// <para>
/// The curve is data, so retuning progression is a request rather than a release. What each level
/// *pays* is separate and lives in the reward rules, keyed on <c>PLAYER_LEVEL_UP</c> with the level
/// as the reference — so "level 10 gives 200 coins" is authored in the existing admin UI and needs
/// nothing here.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/progression/levels")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminLevelCurveController : ControllerBase
{
    private readonly ILevelService _levels;

    public AdminLevelCurveController(ILevelService levels)
    {
        _levels = levels;
    }

    /// <summary>
    /// The authored curve, ascending.
    /// <code>{ "levels": [ { "level": 1, "cumulativeXp": 0 }, { "level": 2, "cumulativeXp": 50 } ] }</code>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var curve = await _levels.GetCurveAsync(cancellationToken);

        return Ok(new { levels = curve });
    }

    /// <summary>
    /// Replaces the **whole** curve.
    /// <code>{ "levels": [ { "level": 1, "cumulativeXp": 0 }, { "level": 2, "cumulativeXp": 50 } ] }</code>
    /// <para>
    /// Whole-curve replacement rather than per-rung edits because every rule worth enforcing is a
    /// property of the set: it must start at level 1, level 1 must start at 0 XP, levels must be
    /// contiguous, and thresholds must strictly increase. None of those can be checked one row at a
    /// time, and a per-rung endpoint would let an operator leave the curve briefly invalid — which
    /// is exactly the moment somebody levels up.
    /// </para>
    /// <para>
    /// **Shortening the curve demotes nobody.** Levels are derived, never stored, so a player above
    /// the new maximum simply reads as the new maximum and their XP is untouched — if the curve
    /// grows back, so do they. For the same reason, thresholds are stored cumulatively: level N
    /// always means "has earned at least X", so editing what level 12 costs cannot reshuffle who is
    /// level 30.
    /// </para>
    /// <para>
    /// Levels already **paid for** are unaffected by any of this. A <c>PLAYER_LEVEL_UP</c> reward is
    /// recorded against the level it paid for and is idempotent on it, so re-tuning the curve does
    /// not re-pay anyone who crosses the same rung again.
    /// </para>
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Replace(
        [FromBody] ReplaceLevelCurveRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _levels.ReplaceCurveAsync(request, cancellationToken);

        if (!result.Succeeded)
            return result.ToErrorResult();

        return Ok(new { levels = result.Value });
    }
}
