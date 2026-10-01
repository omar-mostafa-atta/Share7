using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Tournaments, for the players in them and for the teachers who run their class's own. A pairing is
/// played in an ordinary session: pressing play opens (or hands over) the room reserved for the pair,
/// and the match result decides it. News arrives on the player feed as
/// <c>multiplayer.tournament.*</c>.
/// <para>
/// <b>Organising here is a teacher's, for their own class only</b> — the authority is the class
/// membership, checked by the service, never a role claim. Event and open tournaments are created
/// by operators, through <c>api/admin/multiplayer/tournaments</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/multiplayer/tournaments")]
[Authorize]
public class TournamentsController : ControllerBase
{
    private readonly ITournamentService _tournaments;
    private readonly ICurrentUserService _currentUser;

    public TournamentsController(ITournamentService tournaments, ICurrentUserService currentUser)
    {
        _tournaments = tournaments;
        _currentUser = currentUser;
    }

    /// <summary>Tournaments you can enter or are in: open ones, your event's, and your class's.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        return Ok(await _tournaments.ListAsync(userId, cancellationToken));
    }

    /// <summary>
    /// The bracket or standings, every round paired so far, and <c>myMatch</c> — your pairing still to
    /// play. Reading it also settles anything due: a verdict in, a deadline passed, a finished round.
    /// </summary>
    [HttpGet("{tournamentId:guid}")]
    public async Task<IActionResult> Get(Guid tournamentId, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.GetAsync(userId, tournamentId, false, cancellationToken));

    /// <summary>Enter. Entering again answers with the same tournament.</summary>
    [HttpPost("{tournamentId:guid}/register")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Register(Guid tournamentId, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.RegisterAsync(userId, tournamentId, cancellationToken));

    /// <summary>Leave. After the start, a pairing still to play goes to your opponent.</summary>
    [HttpPost("{tournamentId:guid}/withdraw")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Withdraw(Guid tournamentId, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.WithdrawAsync(userId, tournamentId, cancellationToken));

    /// <summary>
    /// Press play on your pairing.
    /// <code>{ "transportSessionName": "…", "transportRegion": "eu", "protocolVersion": 1, "requestId": "…" }</code>
    /// <c>youHost: true</c> — create the transport room named in <c>session</c> and <c>start</c> it.
    /// <c>youHost: false</c> — your opponent opened it: join <c>session.id</c> once it reads <c>Created</c>.
    /// Use a fresh <c>requestId</c> for each game of the pairing.
    /// </summary>
    [HttpPost("{tournamentId:guid}/matches/{matchId:guid}/play")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Play(
        Guid tournamentId, Guid matchId, [FromBody] PlayTournamentMatchRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.PlayAsync(userId, tournamentId, matchId, request, cancellationToken));

    // ---- a teacher's own class ---------------------------------------------------------------------

    /// <summary>
    /// Create a tournament for a class you teach — <c>cohortId</c> is required here.
    /// <code>{ "title": "Fractions cup", "gameId": "…", "modeId": "…", "format": "Swiss", "cohortId": "…", "matchMinutes": 10 }</code>
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create([FromBody] CreateTournamentRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.CreateAsync(userId, request, asAdmin: false, cancellationToken));

    /// <summary>Start it now — the classroom case, where the teacher says go. Fewer than two entrants cancels it.</summary>
    [HttpPost("{tournamentId:guid}/start")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Start(Guid tournamentId, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.StartAsync(userId, tournamentId, asAdmin: false, cancellationToken));

    [HttpPost("{tournamentId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Cancel(Guid tournamentId, [FromBody] OrganiserReasonRequest? request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.CancelAsync(userId, tournamentId, request?.Reason, asAdmin: false, cancellationToken));

    /// <summary>Settle a pairing still being played: a winner, or a replay. A reason is required, and kept.</summary>
    [HttpPost("{tournamentId:guid}/matches/{matchId:guid}/decide")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Decide(
        Guid tournamentId, Guid matchId, [FromBody] DecideTournamentMatchRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.DecideMatchAsync(userId, tournamentId, matchId, request, asAdmin: false, cancellationToken));

    [HttpPost("{tournamentId:guid}/entries/{entrantUserId:guid}/disqualify")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Disqualify(
        Guid tournamentId, Guid entrantUserId, [FromBody] OrganiserReasonRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.DisqualifyAsync(userId, tournamentId, entrantUserId, request.Reason ?? string.Empty, asAdmin: false, cancellationToken));

    private async Task<IActionResult> Run<T>(Func<Guid, Task<ServiceResult<T>>> action)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await action(userId);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}

/// <summary>
/// Operators' tournaments: open ones, and event ones that pay the event's prize table from the final
/// placements. Every decision that changes who wins anything is written to the audit log.
/// </summary>
[ApiController]
[Route("api/admin/multiplayer/tournaments")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminTournamentsController : ControllerBase
{
    private readonly ITournamentService _tournaments;
    private readonly ICurrentUserService _currentUser;

    public AdminTournamentsController(ITournamentService tournaments, ICurrentUserService currentUser)
    {
        _tournaments = tournaments;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _tournaments.ListAllAsync(cancellationToken));

    [HttpGet("{tournamentId:guid}")]
    public async Task<IActionResult> Get(Guid tournamentId, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.GetAsync(userId, tournamentId, asAdmin: true, cancellationToken));

    /// <summary>
    /// Create one. <c>eventId</c> makes it the event's tournament: its game and mode, its rules for who
    /// may enter, and its prize table, paid by final placement. <c>cohortId</c> makes it a class's.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTournamentRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.CreateAsync(userId, request, asAdmin: true, cancellationToken));

    [HttpPost("{tournamentId:guid}/start")]
    public async Task<IActionResult> Start(Guid tournamentId, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.StartAsync(userId, tournamentId, asAdmin: true, cancellationToken));

    [HttpPost("{tournamentId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid tournamentId, [FromBody] OrganiserReasonRequest? request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.CancelAsync(userId, tournamentId, request?.Reason, asAdmin: true, cancellationToken));

    [HttpPost("{tournamentId:guid}/matches/{matchId:guid}/decide")]
    public async Task<IActionResult> Decide(
        Guid tournamentId, Guid matchId, [FromBody] DecideTournamentMatchRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.DecideMatchAsync(userId, tournamentId, matchId, request, asAdmin: true, cancellationToken));

    [HttpPost("{tournamentId:guid}/entries/{entrantUserId:guid}/disqualify")]
    public async Task<IActionResult> Disqualify(
        Guid tournamentId, Guid entrantUserId, [FromBody] OrganiserReasonRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _tournaments.DisqualifyAsync(userId, tournamentId, entrantUserId, request.Reason ?? string.Empty, asAdmin: true, cancellationToken));

    private async Task<IActionResult> Run<T>(Func<Guid, Task<ServiceResult<T>>> action)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await action(userId);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
