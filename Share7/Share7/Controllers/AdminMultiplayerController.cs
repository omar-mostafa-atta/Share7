using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Constants;
using Share7.Domain.Multiplayer;

namespace Share7.API.Controllers;

/// <summary>
/// Operator tooling for multiplayer sessions. Read-mostly; the one mutation is a forced close.
/// <para>
/// **Separate from the player-facing controller on purpose.** Everything here answers for *all*
/// sessions rather than the caller's own, so the privilege boundary is the route and the role
/// attribute rather than a flag threaded through a shared service — a check that lives in a
/// parameter is one that eventually gets passed the wrong value.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/multiplayer")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminMultiplayerController : ControllerBase
{
    private readonly IMultiplayerAdminService _admin;

    public AdminMultiplayerController(IMultiplayerAdminService admin)
    {
        _admin = admin;
    }

    /// <summary>
    /// Sessions matching the filter, newest first.
    /// <code>
    /// {
    ///   "sessions": [ { "id": "…", "state": "Running", "closedReason": null,
    ///                   "lastHeartbeatAtUtc": "…", "currentPlayerCount": 2, … } ],
    ///   "totalMatching": 143,
    ///   "serverTimeUtc": "…"
    /// }
    /// </code>
    /// <para>
    /// Two fields the player-facing shape omits are the reason to come here: <c>closedReason</c>
    /// answers "why did this match vanish", and <c>lastHeartbeatAtUtc</c> against <c>serverTimeUtc</c>
    /// tells you whether a session is genuinely live or merely has not been swept yet.
    /// </para>
    /// <para>
    /// Capped at 500 rows and defaulting to 100. <c>totalMatching</c> is counted before the cap, so
    /// a truncated answer is visibly truncated rather than looking like the whole picture.
    /// </para>
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? gameId,
        [FromQuery] MultiplayerSessionState? state,
        [FromQuery] DateTime? olderThanUtc,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await _admin.ListAsync(
            new MultiplayerAdminQuery
            {
                GameId = gameId,
                State = state,
                OlderThanUtc = olderThanUtc,
                Limit = limit
            },
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The full roster of any session, **including members who have already left** — who departed and
    /// when is the substance of most support questions, and a closed session would otherwise read as
    /// empty.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/players")]
    public async Task<IActionResult> GetPlayers(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _admin.GetPlayersAsync(sessionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Forces a session closed, recording <c>AdminClosed</c>, and releases every seat in it.
    /// <para>
    /// Idempotent and absorbing like every other close: a session that already ended keeps the reason
    /// and the time it ended with. **An operator can end a match; they cannot rewrite the history of
    /// one that already finished.**
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/close")]
    public async Task<IActionResult> Close(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _admin.CloseAsync(sessionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
