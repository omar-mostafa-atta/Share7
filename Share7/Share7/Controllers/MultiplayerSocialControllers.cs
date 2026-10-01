using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Social;

namespace Share7.API.Controllers;

/// <summary>
/// The player's feed: everything that happens to them outside a room — invites, results, and next
/// challenges — as an ordered, replayable list read by long-poll.
/// </summary>
[ApiController]
[Route("api/multiplayer/events")]
[Authorize]
public class PlayerFeedController : ControllerBase
{
    private readonly IPlayerEventFeed _feed;
    private readonly ICurrentUserService _currentUser;

    public PlayerFeedController(IPlayerEventFeed feed, ICurrentUserService currentUser)
    {
        _feed = feed;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Events after <paramref name="after"/>, in order; with none, waits up to <paramref name="wait"/>
    /// seconds (max 25) for one.
    /// <code>
    /// GET /api/multiplayer/events?after=1842&amp;wait=25
    /// → { "events": [ { "sequence": 1843, "eventId": "…", "type": "multiplayer.invite.received",
    ///                   "version": 1, "occurredAtUtc": "…Z", "expiresAtUtc": "…Z", "payload": { … } } ],
    ///     "nextAfter": 1843, "serverTimeUtc": "…Z" }
    /// </code>
    /// <para>
    /// At least once — de-duplicate by <c>eventId</c> — and in order. Persist <c>nextAfter</c> and send
    /// it next time; start from <c>0</c>. <c>410 EVENTS_CURSOR_EXPIRED</c>: re-read state (sessions,
    /// invites) and continue from <c>details.latest</c>. Keeping this poll running is also what shows the
    /// player as online to their classmates.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Read([FromQuery] long after = 0, [FromQuery] int wait = 0, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        try
        {
            var result = await _feed.ReadAsync(userId, after, wait, cancellationToken);
            return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client hung up mid-wait. Nobody is listening for an answer.
            return new EmptyResult();
        }
    }
}

/// <summary>Asking classmates and friends into a room, and answering.</summary>
[ApiController]
[Route("api/multiplayer")]
[Authorize]
public class MultiplayerInvitesController : ControllerBase
{
    private readonly ISessionInvitationService _invitations;
    private readonly ICurrentUserService _currentUser;

    public MultiplayerInvitesController(ISessionInvitationService invitations, ICurrentUserService currentUser)
    {
        _invitations = invitations;
        _currentUser = currentUser;
    }

    /// <summary>
    /// A seated player invites someone from their connections list into the room, before it starts.
    /// <code>{ "userId": "…" }</code>
    /// The recipient gets <c>multiplayer.invite.received</c> on their feed. Inviting twice returns the
    /// one pending invite. Refusals: <c>SOCIAL_NOT_ALLOWED</c> (not a classmate or friend — or blocked,
    /// which is never distinguished), <c>NOT_SESSION_MEMBER</c>, <c>SESSION_INVALID_TRANSITION</c>,
    /// <c>SESSION_RESERVED</c>, <c>SESSION_REMOVED</c>, <c>VALIDATION_FAILED</c>, <c>SESSION_NOT_FOUND</c>.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/invites")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Invite(Guid sessionId, [FromBody] InvitePlayerRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _invitations.InviteAsync(userId, sessionId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Invites waiting for the caller's answer.</summary>
    [HttpGet("invites")]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _invitations.PendingAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Accept, and take a seat. <code>{ "protocolVersion": 1 }</code> Answers with the session, exactly
    /// as a join does; then join the Photon room. Refusals: <c>INVITE_NOT_FOUND</c>,
    /// <c>INVITE_NOT_PENDING</c>, and every refusal a join can give.
    /// </summary>
    [HttpPost("invites/{invitationId:guid}/accept")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Accept(Guid invitationId, [FromBody] AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _invitations.AcceptAsync(userId, invitationId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Decline. The sender is not notified.</summary>
    [HttpPost("invites/{invitationId:guid}/decline")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Decline(Guid invitationId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _invitations.DeclineAsync(userId, invitationId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>The sender withdraws an invite. The recipient gets <c>multiplayer.invite.cancelled</c>.</summary>
    [HttpPost("invites/{invitationId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Cancel(Guid invitationId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _invitations.CancelAsync(userId, invitationId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}

/// <summary>
/// Who the player can play with, and who they have blocked. No search, no discovery, no real names:
/// the list is exactly the classmates and friends <c>ISocialPolicy</c> allows.
/// </summary>
[ApiController]
[Route("api/social")]
[Authorize]
public class SocialController : ControllerBase
{
    private readonly ISocialService _social;
    private readonly IFriendService _friends;
    private readonly ICurrentUserService _currentUser;

    public SocialController(ISocialService social, IFriendService friends, ICurrentUserService currentUser)
    {
        _social = social;
        _friends = friends;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Classmates and friends, with their public handle and whether they can play right now.
    /// <code>[ { "userId": "…", "displayName": "SwiftFalcon418", "relation": "Classmate", "presence": "Online" } ]</code>
    /// </summary>
    [HttpGet("connections")]
    public async Task<IActionResult> Connections(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _social.ConnectionsAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    [HttpGet("blocks")]
    public async Task<IActionResult> Blocks(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _social.BlocksAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Block a player: <c>{ "userId": "…" }</c>. Either direction keeps the two apart everywhere —
    /// matchmaking, invites, challenges, presence — and the blocked player is never told. Idempotent.
    /// </summary>
    [HttpPost("blocks")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Block([FromBody] BlockPlayerRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _social.BlockAsync(userId, request.UserId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>
    /// The caller's friend code, to read out or show to someone they know. <c>SOCIAL_CONSENT_REQUIRED</c>
    /// for a player under 18 whose guardian has not turned on playing with friends.
    /// </summary>
    [HttpGet("friend-code")]
    public async Task<IActionResult> FriendCode(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.GetCodeAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>A new code; the old one stops working. For a code that went somewhere it should not have.</summary>
    [HttpPost("friend-code/rotate")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> RotateFriendCode(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.RotateCodeAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Add a friend by their code: <c>{ "code": "K3F9QA2M" }</c>. They get
    /// <c>social.friend_request.received</c> and must accept; if they had already asked you, this makes
    /// the friendship. Every unusable code is <c>FRIEND_CODE_NOT_FOUND</c>. Limited like join codes.
    /// </summary>
    [HttpPost("friends")]
    [EnableRateLimiting(RateLimitPolicies.JoinCode)]
    public async Task<IActionResult> AddFriend([FromBody] AddFriendRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.AddByCodeAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    [HttpDelete("friends/{friendUserId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> RemoveFriend(Guid friendUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.RemoveAsync(userId, friendUserId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>Friend requests waiting for the caller's answer.</summary>
    [HttpGet("friend-requests")]
    public async Task<IActionResult> FriendRequests(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.RequestsAsync(userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    [HttpPost("friend-requests/{requestId:guid}/accept")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> AcceptFriend(Guid requestId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.AcceptAsync(userId, requestId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Decline. The sender is not told.</summary>
    [HttpPost("friend-requests/{requestId:guid}/decline")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> DeclineFriend(Guid requestId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _friends.DeclineAsync(userId, requestId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    [HttpDelete("blocks/{blockedUserId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Unblock(Guid blockedUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _social.UnblockAsync(userId, blockedUserId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}

/// <summary>
/// Parties: a few classmates or friends who stay together between matches. The leader invites, picks
/// the game, and "play" opens a room reserved for exactly the party.
/// </summary>
[ApiController]
[Route("api/multiplayer/parties")]
[Authorize]
public class PartiesController : ControllerBase
{
    private readonly IPartyService _parties;
    private readonly ICurrentUserService _currentUser;

    public PartiesController(IPartyService parties, ICurrentUserService currentUser)
    {
        _parties = parties;
        _currentUser = currentUser;
    }

    /// <summary>Your party — created, with you leading it, if you have none.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        await Run(userId => _parties.CreateAsync(userId, cancellationToken));

    /// <summary>Your party, with each member's handle and presence; <c>PARTY_NOT_FOUND</c> if you have none.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) =>
        await Run(userId => _parties.CurrentAsync(userId, cancellationToken));

    /// <summary>The leader invites a classmate or friend: <c>{ "userId": "…" }</c>.</summary>
    [HttpPost("{partyId:guid}/invites")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Invite(Guid partyId, [FromBody] InvitePlayerRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _parties.InviteAsync(userId, partyId, request, cancellationToken));

    /// <summary>Party invites waiting for your answer.</summary>
    [HttpGet("invites")]
    public async Task<IActionResult> Invites(CancellationToken cancellationToken) =>
        await Run(userId => _parties.PendingInvitesAsync(userId, cancellationToken));

    /// <summary>Join — leaving any party you were in. <c>PARTY_FULL</c> when there is no room.</summary>
    [HttpPost("invites/{invitationId:guid}/accept")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Accept(Guid invitationId, CancellationToken cancellationToken) =>
        await Run(userId => _parties.AcceptInviteAsync(userId, invitationId, cancellationToken));

    [HttpPost("invites/{invitationId:guid}/decline")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Decline(Guid invitationId, CancellationToken cancellationToken) =>
        await Run(userId => _parties.DeclineInviteAsync(userId, invitationId, cancellationToken));

    /// <summary>Leave. A leader leaving hands over to the longest-standing member.</summary>
    [HttpPost("{partyId:guid}/leave")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Leave(Guid partyId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _parties.LeaveAsync(userId, partyId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>The leader removes a member: <c>{ "userId": "…" }</c>.</summary>
    [HttpPost("{partyId:guid}/remove")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Remove(Guid partyId, [FromBody] RemovePlayerRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _parties.RemoveAsync(userId, partyId, request.UserId, cancellationToken));

    /// <summary>
    /// The leader opens a room reserved for the party — the same body as a create:
    /// <code>{ "gameId": "…", "modeKey": "…", "transportSessionName": "r7f3a91f", "protocolVersion": 1, "requestId": "…" }</code>
    /// Everyone else gets <c>multiplayer.party.play_started</c> and joins once the room is <c>Created</c>.
    /// <c>PARTY_MEMBER_BUSY</c> (with <c>details.userIds</c>) while anyone is still in a room.
    /// </summary>
    [HttpPost("{partyId:guid}/play")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Play(Guid partyId, [FromBody] PartyPlayRequest request, CancellationToken cancellationToken) =>
        await Run(userId => _parties.PlayAsync(userId, partyId, request, cancellationToken));

    private async Task<IActionResult> Run<T>(Func<Guid, Task<Application.Common.Models.ServiceResult<T>>> action)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await action(userId);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
