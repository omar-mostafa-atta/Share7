using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;

namespace Share7.API.Controllers;

/// <summary>
/// Multiplayer lobbies. Photon Fusion owns the live connection; this surface owns everything Fusion
/// cannot arbitrate — who may join, how many fit, who is host, and whether the session still exists.
/// <para>
/// **No route accepts a user id.** The caller is always the access token's subject, which is what
/// makes spoofing an identity, impersonating a host, or joining on another child's behalf impossible
/// rather than merely refused.
/// </para>
/// <para>
/// Refusals use the <c>{ code, messageKey, details }</c> envelope, the same one commerce returns.
/// Map on <c>code</c> and never on the message — the backend does not send display prose.
/// </para>
/// </summary>
[ApiController]
[Route("api/multiplayer")]
[Authorize]
public class MultiplayerController : ControllerBase
{
    private readonly IMultiplayerSessionService _sessions;
    private readonly IMatchmakingService _matchmaking;
    private readonly ICurrentUserService _currentUser;

    public MultiplayerController(
        IMultiplayerSessionService sessions,
        IMatchmakingService matchmaking,
        ICurrentUserService currentUser)
    {
        _sessions = sessions;
        _matchmaking = matchmaking;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Creates a session and seats the caller as its host, in state <c>Creating</c>.
    /// <code>
    /// { "gameId": "…", "transportSessionName": "r7f3a91c", "transportRegion": "eu",
    ///   "visibility": "Private", "maxPlayers": 2, "isRanked": false, "protocolVersion": 1,
    ///   "curriculumPath": { "gradeId": "…", "lessonId": "…" }, "requestId": "…" }
    /// </code>
    /// <para>
    /// **The session is not joinable yet.** Bring the Photon room up, then call <c>start</c> to move
    /// it to <c>Created</c> — that confirmation is what stops players being seated into a room that
    /// never materialised. A session left in <c>Creating</c> is failed automatically after
    /// <c>CreatingTimeoutSeconds</c>.
    /// </para>
    /// <para>
    /// <c>maxPlayers</c> is a preference and is clamped to the game catalog's own maximum;
    /// <c>joinCode</c> comes back populated only for a private session. Refusals:
    /// <c>GAME_NOT_FOUND</c>, <c>GAME_NOT_MULTIPLAYER</c>, <c>ALREADY_IN_SESSION</c>,
    /// <c>TRANSPORT_NAME_TAKEN</c>, <c>PROTOCOL_VERSION_MISMATCH</c>.
    /// </para>
    /// </summary>
    [HttpPost("sessions")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMultiplayerSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.CreateAsync(userId, request, cancellationToken);

        return result.Succeeded
            ? Created($"/api/multiplayer/sessions/{result.Value!.Id}", result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Host only. Carries the session forward, and which move it makes depends on where it is:
    /// <list type="bullet">
    /// <item><c>Creating → Created</c> — the transport room is up; the session opens for joins.</item>
    /// <item><c>Created → Running</c> — commit to start; joins close and <c>startedAtUtc</c> is set.</item>
    /// </list>
    /// <para>
    /// One route for both because the client's intent is the same either way ("I am ready"), and a
    /// client that has lost track of which half it is on would otherwise have to guess.
    /// </para>
    /// <para>
    /// Refusals: <c>NOT_SESSION_HOST</c>, <c>SESSION_INVALID_TRANSITION</c>,
    /// <c>SESSION_BELOW_MIN_PLAYERS</c>.
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/start")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Start(
        Guid sessionId,
        [FromBody] StartMultiplayerSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.StartAsync(userId, sessionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Seats the caller and returns the session with its full roster.
    /// <para>
    /// Capacity is settled by a single conditional UPDATE, so two clients racing for the last seat
    /// cannot both be seated — one gets <c>SESSION_FULL</c>. That refusal **does not consume the
    /// request id**: retry the same id once a seat frees up and it will be evaluated fresh.
    /// </para>
    /// <para>
    /// Refusals: <c>SESSION_NOT_FOUND</c>, <c>SESSION_FULL</c>, <c>SESSION_CLOSED</c>,
    /// <c>ALREADY_IN_SESSION</c>, <c>PROTOCOL_VERSION_MISMATCH</c>.
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/join")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Join(
        Guid sessionId,
        [FromBody] JoinMultiplayerSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.JoinAsync(userId, sessionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Releases the caller's seat. **Idempotent** — leaving twice, or leaving a session that has
    /// already ended, returns 200 either way.
    /// <para>
    /// If the caller was the host, authority passes to the lowest remaining seat; if nobody remains,
    /// the session closes as <c>Empty</c> and its transport name is released immediately.
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/leave")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Leave(
        Guid sessionId,
        [FromBody] LeaveMultiplayerSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.LeaveAsync(userId, sessionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Host only. Ends the session and marks every remaining membership as departed.
    /// <para>
    /// **Idempotent and absorbing.** Closing an already-ended session returns 200 with the record as
    /// it stands and does not move <c>endedAtUtc</c> or rewrite why it ended.
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/close")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Close(
        Guid sessionId,
        [FromBody] CloseMultiplayerSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.CloseAsync(userId, sessionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Host only. The check-in that keeps a session alive, and the roster the server reconciles
    /// against.
    /// <code>
    /// { "connectedUserIds": ["…", "…"], "state": "Running", "requestId": "…" }
    /// </code>
    /// <para>
    /// **Obey the <c>state</c> that comes back.** It is authoritative: if it says <c>Abandoned</c>,
    /// this session was swept for missed heartbeats and the client should tear down rather than keep
    /// trying. <c>nextHeartbeatInSeconds</c> is likewise the server's cadence, not a suggestion — it
    /// is returned every time so it can be changed without shipping a client.
    /// </para>
    /// <para>
    /// <c>connectedUserIds</c> asserts **presence, never membership**: an id that is not already
    /// seated is ignored. Members the host omits are marked missing and keep their seat for
    /// <c>PlayerDisconnectGraceSeconds</c>; only the sweeper ever removes them.
    /// </para>
    /// <para>
    /// A former host gets <c>NOT_SESSION_HOST</c> here — that is how a host which dropped out and
    /// came back learns it no longer holds authority.
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(
        Guid sessionId,
        [FromBody] HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.HeartbeatAsync(userId, sessionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Moves authority to another member.
    /// <code>
    /// { "toUserId": "…", "reason": "HostUnreachable", "requestId": "…" }
    /// </code>
    /// <para>
    /// Two callers are accepted. The **current host**, handing over deliberately, may name any
    /// seated member. Any **other member** may claim authority once the host has been unseen for
    /// <c>HostClaimGraceSeconds</c> — but only for themselves, so this cannot be used to install a
    /// third party.
    /// </para>
    /// <para>
    /// Photon may elect a new host without telling anyone; this is how the backend is informed and
    /// how it arbitrates. Simultaneous claims are settled by a row-version guard: exactly one
    /// commits, and the losers get <c>HOST_STILL_ACTIVE</c> with the winner's id under
    /// <c>details</c> — re-read, do not retry blindly.
    /// </para>
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/host-transfer")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> TransferHost(
        Guid sessionId,
        [FromBody] TransferHostRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.TransferHostAsync(userId, sessionId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Find a session to play, or start one.
    /// <code>
    /// { "gameId": "…", "protocolVersion": 1, "isRanked": false, "maxPlayers": 2,
    ///   "curriculumPath": { "lessonId": "…" }, "createIfNoneFound": true,
    ///   "transportSessionName": "r7f3a91c", "transportRegion": "eu", "requestId": "…" }
    /// </code>
    /// <para>
    /// Answers <c>{ "outcome": "Joined" | "Created" | "NoMatch", "session": { … } }</c>. Nearly-full
    /// public sessions are offered first — they are the shortest wait — and sessions whose host has
    /// stopped heartbeating are never offered at all.
    /// </para>
    /// <para>
    /// <c>transportSessionName</c> is only needed if it comes to creating one, but send it whenever
    /// <c>createIfNoneFound</c> is true: there is no second round trip to ask for it. Only
    /// <c>lessonId</c> narrows the search in v1; the rest of <c>curriculumPath</c> is carried onto a
    /// session this call creates and ignored when joining one.
    /// </para>
    /// </summary>
    [HttpPost("matchmaking")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Matchmake(
        [FromBody] MatchmakeRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _matchmaking.MatchmakeAsync(userId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// One session, for a member of it.
    /// <para>
    /// **A non-member gets 404, not 403.** 403 would confirm the id exists, which is enough to probe
    /// for live sessions.
    /// </para>
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.GetAsync(userId, sessionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The roster — every seated member with their slot, host flag, status and last-seen time. This
    /// is what the host maps onto its realtime peers. Members only; same 404 rule as above.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/players")]
    public async Task<IActionResult> GetPlayers(Guid sessionId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _sessions.GetPlayersAsync(userId, sessionId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Sessions the caller is a member of, newest first. Empty array when they are in none.
    /// <para>
    /// **A recovery route, not a lobby browser** — "where am I?" after a crash or a reinstall. It
    /// never returns a session the caller is not in; Photon's own room list serves discovery.
    /// </para>
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? gameId,
        [FromQuery] MultiplayerSessionState? state,
        [FromQuery] SessionVisibility? visibility,
        [FromQuery] bool? isRanked,
        [FromQuery] Guid? lessonId,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var query = new MultiplayerSessionQuery
        {
            GameId = gameId,
            State = state,
            Visibility = visibility,
            IsRanked = isRanked,
            LessonId = lessonId
        };

        var result = await _sessions.ListForUserAsync(userId, query, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
