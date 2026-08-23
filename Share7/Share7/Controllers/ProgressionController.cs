using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using System.ComponentModel.DataAnnotations;
using Share7.API.Extensions;
using Share7.Application.Common.Models;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Objectives.Models;
using Share7.Domain.Objectives;
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
    private readonly IObjectiveService _objectives;
    private readonly ICurrentUserService _currentUser;

    public ProgressionController(
        ILevelService levels,
        IObjectiveService objectives,
        ICurrentUserService currentUser)
    {
        _levels = levels;
        _objectives = objectives;
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
        var objectives = await _objectives.GetForUserAsync(userId, cancellationToken);
        var streak = await _objectives.GetStreakAsync(userId, cancellationToken);

        return Ok(new ProgressionSnapshotDto
        {
            Level = level,
            Daily = Kind(objectives, ObjectiveKind.Daily),
            Weekly = Kind(objectives, ObjectiveKind.Weekly),
            Achievements = Kind(objectives, ObjectiveKind.Achievement),
            Streak = new StreakDto
            {
                Current = streak.Current,
                Best = streak.Best,
                FreezesRemaining = streak.FreezesRemaining
            },
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Collects a completed objective.
    /// <code>
    /// POST /api/progression/objectives/daily.lessons.complete.3/claim
    /// { "requestId": "one-id-per-claim" }
    /// </code>
    /// <para>
    /// Returns the same shape the attempt and run responses use: <c>rewards</c> are deltas to
    /// animate, <c>balances</c> are absolute totals that already include them. **Assign the
    /// balances; never add the rewards.**
    /// </para>
    /// <para>
    /// <c>409</c> when there is nothing to collect — not finished, already collected, or the claim
    /// window has closed. One answer for all three on purpose: they are the same fact to a caller,
    /// and the payout is idempotent on the objective and its cycle regardless.
    /// </para>
    /// <para>
    /// **The client never states an amount here.** It names an objective; the server decides
    /// whether it was earned and what it pays.
    /// </para>
    /// </summary>
    [HttpPost("objectives/{key}/claim")]
    public async Task<IActionResult> Claim(
        string key,
        [FromBody] ClaimObjectiveRequest? request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _objectives.ClaimAsync(
            userId, key, request?.RequestId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    private static IReadOnlyList<ObjectiveDto> Kind(
        IReadOnlyList<ObjectiveDto> objectives, ObjectiveKind kind)
    {
        var wire = WireEnum.ToWire(kind);

        return [.. objectives.Where(o => string.Equals(o.Kind, wire, StringComparison.Ordinal))];
    }
}

/// <summary>The claim body. Carries an idempotency key and deliberately nothing else.</summary>
public class ClaimObjectiveRequest
{
    /// <summary>Client-minted, reused unchanged on every retry of this claim. Optional.</summary>
    [MaxLength(128)]
    public string? RequestId { get; set; }
}
