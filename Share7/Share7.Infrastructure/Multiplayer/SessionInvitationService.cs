using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Social;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

public class SessionInvitationService : ISessionInvitationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerSessionService _sessions;
    private readonly ISocialPolicy _policy;
    private readonly IRosterNameResolver _names;
    private readonly IPlayerEventPublisher _events;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<SessionInvitationService> _logger;

    public SessionInvitationService(
        ApplicationDbContext dbContext,
        MultiplayerSessionService sessions,
        ISocialPolicy policy,
        IRosterNameResolver names,
        IPlayerEventPublisher events,
        IOptions<MultiplayerOptions> options,
        ILogger<SessionInvitationService> logger)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _policy = policy;
        _names = names;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    // ---- invite ------------------------------------------------------------------------------------

    public Task<ServiceResult<SessionInvitationDto>> InviteAsync(
        Guid userId, Guid sessionId, InvitePlayerRequest request, CancellationToken cancellationToken = default) =>
        InviteCoreAsync(userId, sessionId, request.UserId, InviteKinds.Invite, cancellationToken);

    /// <summary>The invite itself; <paramref name="kind"/> tells the recipient's client how to present it.</summary>
    internal async Task<ServiceResult<SessionInvitationDto>> InviteCoreAsync(
        Guid userId, Guid sessionId, Guid target, string kind, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.MultiplayerSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null || !await HeldSeatAsync(userId, sessionId, cancellationToken))
            return Failure(ApiErrors.SessionNotFound, ServiceErrorKind.NotFound, $"Session {sessionId} was not found.");

        // Asking someone into a room you are not in would hand them a room with a stranger's host.
        if (!await SeatedAsync(userId, sessionId, cancellationToken))
            return Failure(ApiErrors.NotSessionMember, ServiceErrorKind.Forbidden, "Only a player seated in the room can invite.");

        if (session.State is not (MultiplayerSessionState.Creating or MultiplayerSessionState.Created))
            return Failure(ApiErrors.SessionInvalidTransition, ServiceErrorKind.Conflict,
                $"Session {sessionId} is {session.State}; invites are for rooms that have not started.");

        if (target == userId || target == Guid.Empty)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Choose someone else to invite.");

        var permission = await _policy.CanInteractAsync(userId, target, SocialAction.Invite, cancellationToken);

        if (!permission.Allowed)
        {
            _logger.LogInformation("Invite into {SessionId} refused ({Reason}).", sessionId, permission.Reason);
            return Failure(ApiErrors.SocialNotAllowed, ServiceErrorKind.Forbidden, "You can only invite classmates and friends.");
        }

        if (await _dbContext.MultiplayerSessionBans.AnyAsync(b => b.SessionId == sessionId && b.UserId == target, cancellationToken))
            return Failure(ApiErrors.SessionRemoved, ServiceErrorKind.Forbidden, "The host removed that player from this room.");

        if (session.IsReserved
            && !await _dbContext.MultiplayerSessionReservations.AnyAsync(r => r.SessionId == sessionId && r.UserId == target, cancellationToken))
            return Failure(ApiErrors.SessionReserved, ServiceErrorKind.Forbidden, "This room is kept for the players of the match before it.");

        if (await SeatedAsync(target, sessionId, cancellationToken))
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "That player is already in this room.");

        var now = DateTime.UtcNow;

        // A pending invite that has quietly run out still holds the one-pending slot; retire it first.
        await _dbContext.SessionInvitations
            .Where(i => i.SessionId == sessionId && i.RecipientUserId == target
                        && i.State == InvitationState.Pending && i.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.State, InvitationState.Expired)
                .SetProperty(i => i.AnsweredAtUtc, now), cancellationToken);

        if (await PendingForAsync(sessionId, target, cancellationToken) is { } existing)
            return ServiceResult<SessionInvitationDto>.Success(await MapAsync(existing, session, cancellationToken));

        var invitation = new SessionInvitation
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            SenderUserId = userId,
            RecipientUserId = target,
            State = InvitationState.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(Math.Max(1, _options.InvitationMinutes))
        };

        var fromName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);

        _dbContext.SessionInvitations.Add(invitation);

        // With the invite, in one SaveChanges: the invite and its notification exist together or not at all.
        _events.Stage(target, PlayerEventTypes.InviteReceived, new
        {
            invitationId = invitation.Id,
            kind,
            sessionId,
            gameId = session.GameId,
            modeId = session.ModeId,
            fromUserId = userId,
            fromDisplayName = fromName,
            expiresAtUtc = invitation.ExpiresAtUtc
        }, invitation.ExpiresAtUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // Someone else in the room invited the same player a moment earlier. Theirs is the invite.
            Detach();

            if (await PendingForAsync(sessionId, target, cancellationToken) is { } raced)
                return ServiceResult<SessionInvitationDto>.Success(await MapAsync(raced, session, cancellationToken));

            throw;
        }

        _logger.LogInformation("Invite {InvitationId} into session {SessionId} sent.", invitation.Id, sessionId);

        return ServiceResult<SessionInvitationDto>.Success(await MapAsync(invitation, session, cancellationToken));
    }

    // ---- read ------------------------------------------------------------------------------------

    public async Task<ServiceResult<IReadOnlyList<SessionInvitationDto>>> PendingAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await _dbContext.SessionInvitations
            .AsNoTracking()
            .Include(i => i.Session)
            .Where(i => i.RecipientUserId == userId
                        && i.State == InvitationState.Pending
                        && i.ExpiresAtUtc > now
                        && (i.Session!.State == MultiplayerSessionState.Creating || i.Session.State == MultiplayerSessionState.Created))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(rows.Select(r => r.SenderUserId).Distinct().ToList(), cancellationToken);

        IReadOnlyList<SessionInvitationDto> list = rows.Select(r => ToDto(r, r.Session!, names, now)).ToList();

        return ServiceResult<IReadOnlyList<SessionInvitationDto>>.Success(list);
    }

    // ---- answer ----------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> AcceptAsync(
        Guid userId, Guid invitationId, AcceptInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await _dbContext.SessionInvitations.AsNoTracking()
            .Include(i => i.Session)
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.RecipientUserId == userId, cancellationToken);

        if (invitation?.Session is not { } session)
            return InviteNotFound<MultiplayerSessionDto>(invitationId);

        // A retry of an accept that went through: the seat is the answer.
        if (invitation.State == InvitationState.Accepted && await SeatedAsync(userId, session.Id, cancellationToken))
            return await _sessions.GetAsync(userId, session.Id, cancellationToken);

        var now = DateTime.UtcNow;

        if (invitation.State == InvitationState.Pending && (invitation.ExpiresAtUtc <= now || session.State.IsTerminal()))
        {
            await RetireAsync(invitationId, InvitationState.Expired, now, cancellationToken);
            return NotPending<MultiplayerSessionDto>(InvitationState.Expired);
        }

        if (invitation.State != InvitationState.Pending)
            return NotPending<MultiplayerSessionDto>(invitation.State);

        // The ordinary seat step — capacity, removals, reservations, one live seat — decides. An invite
        // gets past a private room's join-code rule (it is the host's side asking), and nothing else.
        var seated = await _sessions.SeatAsync(userId, session.Id, request.ProtocolVersion, cancellationToken);

        if (!seated.Succeeded)
        {
            // Already in this very room (an accept whose answer was lost) is success, not a refusal.
            if (seated.Error?.Code == ApiErrors.AlreadyInSession.Code && await SeatedAsync(userId, session.Id, cancellationToken))
                seated = await _sessions.GetAsync(userId, session.Id, cancellationToken);
            else
                return seated;
        }

        if (await RetireAsync(invitationId, InvitationState.Accepted, now, cancellationToken))
        {
            var byName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);

            _events.Stage(invitation.SenderUserId, PlayerEventTypes.InviteAccepted, new
            {
                invitationId,
                sessionId = session.Id,
                byUserId = userId,
                byDisplayName = byName
            }, now.AddMinutes(Math.Max(1, _options.InvitationMinutes)));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return seated;
    }

    public async Task<ServiceResult<SessionInvitationDto>> DeclineAsync(
        Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await _dbContext.SessionInvitations.AsNoTracking()
            .Include(i => i.Session)
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.RecipientUserId == userId, cancellationToken);

        if (invitation?.Session is null)
            return InviteNotFound<SessionInvitationDto>(invitationId);

        // No event to the sender. A child does not need a notification that a classmate said no; their
        // invite simply lapses.
        if (!await RetireAsync(invitationId, InvitationState.Declined, DateTime.UtcNow, cancellationToken)
            && invitation.State != InvitationState.Declined)
            return NotPending<SessionInvitationDto>(invitation.State);

        return await ReloadAsync(invitationId, cancellationToken);
    }

    public async Task<ServiceResult<SessionInvitationDto>> CancelAsync(
        Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await _dbContext.SessionInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.SenderUserId == userId, cancellationToken);

        if (invitation is null)
            return InviteNotFound<SessionInvitationDto>(invitationId);

        var now = DateTime.UtcNow;

        if (await RetireAsync(invitationId, InvitationState.Cancelled, now, cancellationToken))
        {
            _events.Stage(invitation.RecipientUserId, PlayerEventTypes.InviteCancelled,
                new { invitationId, sessionId = invitation.SessionId }, invitation.ExpiresAtUtc);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (invitation.State != InvitationState.Cancelled)
        {
            return NotPending<SessionInvitationDto>(invitation.State);
        }

        return await ReloadAsync(invitationId, cancellationToken);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Moves a still-pending invite to a final state. False when it was no longer pending.</summary>
    private async Task<bool> RetireAsync(Guid invitationId, InvitationState to, DateTime now, CancellationToken cancellationToken) =>
        await _dbContext.SessionInvitations
            .Where(i => i.Id == invitationId && i.State == InvitationState.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.State, to)
                .SetProperty(i => i.AnsweredAtUtc, now), cancellationToken) > 0;

    private Task<SessionInvitation?> PendingForAsync(Guid sessionId, Guid recipient, CancellationToken cancellationToken) =>
        _dbContext.SessionInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.SessionId == sessionId && i.RecipientUserId == recipient
                                                           && i.State == InvitationState.Pending, cancellationToken);

    private Task<bool> SeatedAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers.AsNoTracking().AnyAsync(
            p => p.SessionId == sessionId && p.UserId == userId
                                          && p.Status != SessionPlayerStatus.Left
                                          && p.Status != SessionPlayerStatus.Removed,
            cancellationToken);

    /// <summary>Ever held a seat and not removed — the session's own read rule.</summary>
    private Task<bool> HeldSeatAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers.AsNoTracking().AnyAsync(
            p => p.SessionId == sessionId && p.UserId == userId
                                          && !_dbContext.MultiplayerSessionBans.Any(b => b.SessionId == sessionId && b.UserId == userId),
            cancellationToken);

    private async Task<ServiceResult<SessionInvitationDto>> ReloadAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SessionInvitations.AsNoTracking()
            .Include(i => i.Session)
            .FirstAsync(i => i.Id == invitationId, cancellationToken);

        return ServiceResult<SessionInvitationDto>.Success(await MapAsync(row, row.Session!, cancellationToken));
    }

    private async Task<SessionInvitationDto> MapAsync(SessionInvitation invitation, MultiplayerSession session, CancellationToken cancellationToken)
    {
        var names = await _names.ResolveAsync([invitation.SenderUserId], cancellationToken);
        return ToDto(invitation, session, names, DateTime.UtcNow);
    }

    private static SessionInvitationDto ToDto(
        SessionInvitation invitation, MultiplayerSession session, IReadOnlyDictionary<Guid, string> names, DateTime now) => new()
    {
        Id = invitation.Id,
        SessionId = invitation.SessionId,
        GameId = session.GameId,
        ModeId = session.ModeId,
        FromUserId = invitation.SenderUserId,
        FromDisplayName = names.GetValueOrDefault(invitation.SenderUserId),
        ToUserId = invitation.RecipientUserId,

        // Reported as it effectively stands: a pending invite past its time is expired to the reader.
        State = invitation.State == InvitationState.Pending && invitation.ExpiresAtUtc <= now
            ? InvitationState.Expired
            : invitation.State,
        CreatedAtUtc = DateTime.SpecifyKind(invitation.CreatedAtUtc, DateTimeKind.Utc),
        ExpiresAtUtc = DateTime.SpecifyKind(invitation.ExpiresAtUtc, DateTimeKind.Utc),
        ServerTimeUtc = now
    };

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static ServiceResult<SessionInvitationDto> Failure(ApiErrorCode code, ServiceErrorKind kind, string message) =>
        ServiceResult<SessionInvitationDto>.Failure(code, kind, message);

    private static ServiceResult<T> InviteNotFound<T>(Guid invitationId) =>
        ServiceResult<T>.Failure(ApiErrors.InviteNotFound, ServiceErrorKind.NotFound, $"Invite {invitationId} was not found.");

    private static ServiceResult<T> NotPending<T>(InvitationState state) =>
        ServiceResult<T>.Failure(
            ApiErrors.InviteNotPending,
            ServiceErrorKind.Conflict,
            $"The invite is {state}.",
            new Dictionary<string, object?> { ["state"] = state.ToString() });
}

/// <summary>What an invite is for, as the recipient's client presents it.</summary>
internal static class InviteKinds
{
    /// <summary>"Come and play in my room."</summary>
    public const string Invite = "invite";

    /// <summary>"I challenge you" — a room reserved for the two of you.</summary>
    public const string LiveChallenge = "live_challenge";
}
