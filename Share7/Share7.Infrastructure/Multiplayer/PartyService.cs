using Microsoft.Data.SqlClient;
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

/// <summary>
/// Parties. See <see cref="IPartyService"/>. Every change to a party's membership takes the party row
/// first — the same lock order sessions use — so a leave, a removal and a join cannot interleave into
/// a count or a leader that disagrees with the members.
/// </summary>
public class PartyService : IPartyService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISocialPolicy _policy;
    private readonly IPresenceReader _presence;
    private readonly IRosterNameResolver _names;
    private readonly IPlayerEventPublisher _events;
    private readonly MultiplayerSessionService _sessions;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<PartyService> _logger;

    public PartyService(
        ApplicationDbContext dbContext,
        ISocialPolicy policy,
        IPresenceReader presence,
        IRosterNameResolver names,
        IPlayerEventPublisher events,
        MultiplayerSessionService sessions,
        IOptions<MultiplayerOptions> options,
        ILogger<PartyService> logger)
    {
        _dbContext = dbContext;
        _policy = policy;
        _presence = presence;
        _names = names;
        _events = events;
        _sessions = sessions;
        _options = options.Value;
        _logger = logger;
    }

    // ---- create / read -------------------------------------------------------------------------

    public async Task<ServiceResult<PartyDto>> CreateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (await LivePartyOfAsync(userId, cancellationToken) is { } existing)
            return await ReadAsync(existing, cancellationToken);

        var now = DateTime.UtcNow;
        var party = new Party
        {
            Id = Guid.NewGuid(),
            LeaderUserId = userId,
            State = PartyState.Open,
            MaxSize = Math.Max(2, _options.PartyMaxSize),
            MemberCount = 1,
            CreatedAtUtc = now
        };

        _dbContext.Parties.Add(party);
        _dbContext.PartyMembers.Add(new PartyMember { Id = Guid.NewGuid(), PartyId = party.Id, UserId = userId, JoinedAtUtc = now });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A double tap: the other one made the party.
            Detach();

            if (await LivePartyOfAsync(userId, cancellationToken) is { } raced)
                return await ReadAsync(raced, cancellationToken);

            throw;
        }

        return await ReadAsync(party.Id, cancellationToken);
    }

    public async Task<ServiceResult<PartyDto>> CurrentAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await LivePartyOfAsync(userId, cancellationToken) is { } partyId
            ? await ReadAsync(partyId, cancellationToken)
            : PartyNotFound<PartyDto>();

    // ---- invites -------------------------------------------------------------------------------

    public async Task<ServiceResult<PartyInvitationDto>> InviteAsync(
        Guid userId, Guid partyId, InvitePlayerRequest request, CancellationToken cancellationToken = default)
    {
        var party = await _dbContext.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partyId, cancellationToken);

        if (party is null || party.State != PartyState.Open || !await IsMemberAsync(partyId, userId, cancellationToken))
            return PartyNotFound<PartyInvitationDto>();

        if (party.LeaderUserId != userId)
            return NotLeader<PartyInvitationDto>();

        var target = request.UserId;

        if (target == userId || target == Guid.Empty)
            return ServiceResult<PartyInvitationDto>.Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Choose someone else to invite.");

        if (!(await _policy.CanInteractAsync(userId, target, SocialAction.Invite, cancellationToken)).Allowed)
            return ServiceResult<PartyInvitationDto>.Failure(ApiErrors.SocialNotAllowed, ServiceErrorKind.Forbidden, "You can only invite classmates and friends.");

        if (await IsMemberAsync(partyId, target, cancellationToken))
            return ServiceResult<PartyInvitationDto>.Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "They are already in the party.");

        var now = DateTime.UtcNow;

        await _dbContext.PartyInvitations
            .Where(i => i.PartyId == partyId && i.RecipientUserId == target && i.State == InvitationState.Pending && i.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.State, InvitationState.Expired).SetProperty(i => i.AnsweredAtUtc, now), cancellationToken);

        if (await PendingInviteAsync(partyId, target, cancellationToken) is { } existing)
            return ServiceResult<PartyInvitationDto>.Success(await MapInviteAsync(existing, cancellationToken));

        var invitation = new PartyInvitation
        {
            Id = Guid.NewGuid(),
            PartyId = partyId,
            SenderUserId = userId,
            RecipientUserId = target,
            State = InvitationState.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(Math.Max(1, _options.InvitationMinutes))
        };

        var fromName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);

        _dbContext.PartyInvitations.Add(invitation);
        _events.Stage(target, PlayerEventTypes.PartyInviteReceived, new
        {
            invitationId = invitation.Id,
            partyId,
            fromUserId = userId,
            fromDisplayName = fromName,
            expiresAtUtc = invitation.ExpiresAtUtc
        }, invitation.ExpiresAtUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            Detach();

            if (await PendingInviteAsync(partyId, target, cancellationToken) is { } raced)
                return ServiceResult<PartyInvitationDto>.Success(await MapInviteAsync(raced, cancellationToken));

            throw;
        }

        return ServiceResult<PartyInvitationDto>.Success(await MapInviteAsync(invitation, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<PartyInvitationDto>>> PendingInvitesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await _dbContext.PartyInvitations.AsNoTracking()
            .Where(i => i.RecipientUserId == userId && i.State == InvitationState.Pending && i.ExpiresAtUtc > now
                        && i.Party!.State == PartyState.Open)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(rows.Select(r => r.SenderUserId).Distinct().ToList(), cancellationToken);

        IReadOnlyList<PartyInvitationDto> list = rows.Select(r => ToDto(r, names)).ToList();
        return ServiceResult<IReadOnlyList<PartyInvitationDto>>.Success(list);
    }

    public async Task<ServiceResult<PartyDto>> AcceptInviteAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await _dbContext.PartyInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.RecipientUserId == userId, cancellationToken);

        if (invitation is null)
            return ServiceResult<PartyDto>.Failure(ApiErrors.InviteNotFound, ServiceErrorKind.NotFound, $"Invite {invitationId} was not found.");

        // A retry of an accept that went through.
        if (invitation.State == InvitationState.Accepted && await IsMemberAsync(invitation.PartyId, userId, cancellationToken))
            return await ReadAsync(invitation.PartyId, cancellationToken);

        var now = DateTime.UtcNow;

        if (invitation.State != InvitationState.Pending || invitation.ExpiresAtUtc <= now)
            return InviteNotPending<PartyDto>(invitation.State == InvitationState.Pending ? InvitationState.Expired : invitation.State);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Joining moves the player: out of whatever party they were in, into this one.
        if (await LivePartyOfAsync(userId, cancellationToken) is { } previous && previous != invitation.PartyId)
            await LeaveCoreAsync(previous, userId, "left", now, cancellationToken);

        // The whole size guarantee: one conditional UPDATE, exactly as a session's last seat.
        var admitted = await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE [Parties] SET [MemberCount] = [MemberCount] + 1 WHERE [Id] = {0} AND [State] = 'OPEN' AND [MemberCount] < [MaxSize]",
            [invitation.PartyId],
            cancellationToken);

        if (admitted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var party = await _dbContext.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == invitation.PartyId, cancellationToken);

            return party is { State: PartyState.Open }
                ? ServiceResult<PartyDto>.Failure(ApiErrors.PartyFull, ServiceErrorKind.Conflict, "The party is full.")
                : InviteNotPending<PartyDto>(InvitationState.Expired);
        }

        _dbContext.PartyMembers.Add(new PartyMember { Id = Guid.NewGuid(), PartyId = invitation.PartyId, UserId = userId, JoinedAtUtc = now });

        await _dbContext.PartyInvitations
            .Where(i => i.Id == invitationId && i.State == InvitationState.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.State, InvitationState.Accepted).SetProperty(i => i.AnsweredAtUtc, now), cancellationToken);

        await StageUpdatedAsync(invitation.PartyId, "joined", userId, exceptUserId: null, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Two accepts at once for the same player: the other one joined them.
            await transaction.RollbackAsync(cancellationToken);
            Detach();

            if (await LivePartyOfAsync(userId, cancellationToken) is { } joined)
                return await ReadAsync(joined, cancellationToken);

            throw;
        }

        return await ReadAsync(invitation.PartyId, cancellationToken);
    }

    public async Task<ServiceResult<PartyInvitationDto>> DeclineInviteAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await _dbContext.PartyInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.RecipientUserId == userId, cancellationToken);

        if (invitation is null)
            return ServiceResult<PartyInvitationDto>.Failure(ApiErrors.InviteNotFound, ServiceErrorKind.NotFound, $"Invite {invitationId} was not found.");

        var moved = await _dbContext.PartyInvitations
            .Where(i => i.Id == invitationId && i.State == InvitationState.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.State, InvitationState.Declined).SetProperty(i => i.AnsweredAtUtc, DateTime.UtcNow), cancellationToken) > 0;

        if (!moved && invitation.State != InvitationState.Declined)
            return InviteNotPending<PartyInvitationDto>(invitation.State);

        var current = await _dbContext.PartyInvitations.AsNoTracking().FirstAsync(i => i.Id == invitationId, cancellationToken);
        return ServiceResult<PartyInvitationDto>.Success(await MapInviteAsync(current, cancellationToken));
    }

    // ---- leave / remove ------------------------------------------------------------------------

    public async Task<ServiceResult> LeaveAsync(Guid userId, Guid partyId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Idempotent: leaving a party you are not in succeeds and changes nothing.
        await LeaveCoreAsync(partyId, userId, "left", DateTime.UtcNow, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<PartyDto>> RemoveAsync(Guid userId, Guid partyId, Guid memberUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var party = await LockAsync(partyId, cancellationToken);

        if (party is null || party.State != PartyState.Open || !await IsMemberAsync(partyId, userId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return PartyNotFound<PartyDto>();
        }

        if (party.LeaderUserId != userId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotLeader<PartyDto>();
        }

        if (memberUserId == userId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ServiceResult<PartyDto>.Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Leave the party instead.");
        }

        if (await LeaveCoreAsync(partyId, memberUserId, "removed", DateTime.UtcNow, cancellationToken))
        {
            // The removed player is told too: their client is still showing the party.
            _events.Stage(memberUserId, PlayerEventTypes.PartyUpdated, new { partyId, reason = "removed", userId = memberUserId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ReadAsync(partyId, cancellationToken);
    }

    /// <summary>
    /// Takes one member out, inside the caller's transaction: the party row first, then the membership,
    /// the count, the leader and — for the last one out — the party itself. False if they were not a member.
    /// </summary>
    private async Task<bool> LeaveCoreAsync(Guid partyId, Guid userId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var party = await LockAsync(partyId, cancellationToken);

        if (party is null)
            return false;

        var left = await _dbContext.PartyMembers
            .Where(m => m.PartyId == partyId && m.UserId == userId && m.LeftAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.LeftAtUtc, now), cancellationToken) > 0;

        if (!left)
            return false;

        var remaining = await _dbContext.PartyMembers.AsNoTracking()
            .Where(m => m.PartyId == partyId && m.LeftAtUtc == null)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        if (remaining.Count == 0)
        {
            await _dbContext.Parties.Where(p => p.Id == partyId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.State, PartyState.Disbanded)
                    .SetProperty(p => p.MemberCount, 0)
                    .SetProperty(p => p.DisbandedAtUtc, now), cancellationToken);

            return true;
        }

        // A leader leaving hands the party to whoever has been in it longest.
        var leader = party.LeaderUserId == userId ? remaining[0] : party.LeaderUserId;

        await _dbContext.Parties.Where(p => p.Id == partyId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.MemberCount, remaining.Count)
                .SetProperty(p => p.LeaderUserId, leader), cancellationToken);

        await StageUpdatedAsync(partyId, reason, userId, exceptUserId: userId, cancellationToken, remaining);

        if (leader != party.LeaderUserId)
            await StageUpdatedAsync(partyId, "leader_changed", leader, exceptUserId: null, cancellationToken, remaining);

        return true;
    }

    // ---- play ----------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> PlayAsync(
        Guid userId, Guid partyId, PartyPlayRequest request, CancellationToken cancellationToken = default)
    {
        var party = await _dbContext.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partyId, cancellationToken);

        if (party is null || party.State != PartyState.Open || !await IsMemberAsync(partyId, userId, cancellationToken))
            return PartyNotFound<MultiplayerSessionDto>();

        if (party.LeaderUserId != userId)
            return NotLeader<MultiplayerSessionDto>();

        var members = await LiveMembersAsync(partyId, cancellationToken);

        // Everyone must be free to come — told who is not, so the leader can wait for them.
        // A seat in the party's own current room is not "busy": that is a retried play, or the room
        // everyone is already in.
        var busy = await _dbContext.MultiplayerSessionPlayers.AsNoTracking()
            .Where(p => members.Contains(p.UserId)
                        && p.SessionId != party.CurrentSessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (busy.Count > 0)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.PartyMemberBusy,
                ServiceErrorKind.Conflict,
                "Someone in the party is still in a room.",
                new Dictionary<string, object?> { ["userIds"] = busy });

        var seats = await _dbContext.Games.AsNoTracking()
            .Where(g => g.Id == request.GameId)
            .Select(g => (int?)g.MaxPlayers)
            .FirstOrDefaultAsync(cancellationToken);

        if (seats is { } max && members.Count > max)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.ValidationFailed, ServiceErrorKind.Validation, $"This game seats {max}; the party is {members.Count}.");

        var created = await _sessions.CreateReservedAsync(userId, new CreateMultiplayerSessionRequest
        {
            GameId = request.GameId,
            ModeKey = request.ModeKey,
            CurriculumPath = request.CurriculumPath,
            TransportSessionName = request.TransportSessionName,
            TransportRegion = request.TransportRegion,
            ProtocolVersion = request.ProtocolVersion,
            Visibility = SessionVisibility.Private,
            MaxPlayers = Math.Max(members.Count, 2),
            IsRanked = false,
            RequestId = request.RequestId
        }, members, MultiplayerOperations.PartyPlay, cancellationToken);

        if (!created.Succeeded)
            return created;

        var sessionId = created.Value!.Id;

        // Once per room: a retried "play" replays the create and must not call everyone twice.
        var announced = await _dbContext.Parties
            .Where(p => p.Id == partyId && p.CurrentSessionId != sessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.CurrentSessionId, sessionId), cancellationToken) > 0;

        if (announced)
        {
            foreach (var member in members.Where(m => m != userId))
                _events.Stage(member, PlayerEventTypes.PartyPlayStarted,
                    new { partyId, sessionId, gameId = request.GameId }, DateTime.UtcNow.AddMinutes(Math.Max(1, _options.InvitationMinutes)));

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Party {PartyId} opened session {SessionId} for {Count} players.", partyId, sessionId, members.Count);
        }

        return created;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private sealed record PartyLock(Guid LeaderUserId, PartyState State);

    private Task<PartyLock?> LockAsync(Guid partyId, CancellationToken cancellationToken) =>
        _dbContext.Parties
            .FromSqlRaw("SELECT * FROM [Parties] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", partyId)
            .AsNoTracking()
            .Select(p => new PartyLock(p.LeaderUserId, p.State))
            .FirstOrDefaultAsync(cancellationToken);

    private Task<Guid?> LivePartyOfAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.PartyMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.LeftAtUtc == null && m.Party!.State == PartyState.Open)
            .Select(m => (Guid?)m.PartyId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> IsMemberAsync(Guid partyId, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.PartyMembers.AsNoTracking().AnyAsync(m => m.PartyId == partyId && m.UserId == userId && m.LeftAtUtc == null, cancellationToken);

    private Task<List<Guid>> LiveMembersAsync(Guid partyId, CancellationToken cancellationToken) =>
        _dbContext.PartyMembers.AsNoTracking()
            .Where(m => m.PartyId == partyId && m.LeftAtUtc == null)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    private Task<PartyInvitation?> PendingInviteAsync(Guid partyId, Guid recipient, CancellationToken cancellationToken) =>
        _dbContext.PartyInvitations.AsNoTracking().FirstOrDefaultAsync(
            i => i.PartyId == partyId && i.RecipientUserId == recipient && i.State == InvitationState.Pending, cancellationToken);

    /// <summary>Tells every current member what changed, so each client refreshes the party it shows.</summary>
    private async Task StageUpdatedAsync(
        Guid partyId, string reason, Guid userId, Guid? exceptUserId, CancellationToken cancellationToken, IReadOnlyList<Guid>? members = null)
    {
        foreach (var member in members ?? await LiveMembersAsync(partyId, cancellationToken))
        {
            if (member != exceptUserId)
                _events.Stage(member, PlayerEventTypes.PartyUpdated, new { partyId, reason, userId });
        }
    }

    /// <summary>
    /// The party as it stands, repaired first if an account deletion took a member (or the leader)
    /// out underneath it: the count follows the members, and a party without its leader is led by the
    /// longest-standing member — or ends, if nobody is left.
    /// </summary>
    private async Task<ServiceResult<PartyDto>> ReadAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var party = await _dbContext.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partyId, cancellationToken);

        if (party is null)
            return PartyNotFound<PartyDto>();

        var rows = await _dbContext.PartyMembers.AsNoTracking()
            .Where(m => m.PartyId == partyId && m.LeftAtUtc == null)
            .OrderBy(m => m.JoinedAtUtc)
            .ToListAsync(cancellationToken);

        if (party.State == PartyState.Open && (rows.Count != party.MemberCount || rows.All(m => m.UserId != party.LeaderUserId)))
        {
            var leader = rows.Any(m => m.UserId == party.LeaderUserId) ? party.LeaderUserId : rows.FirstOrDefault()?.UserId ?? party.LeaderUserId;

            await _dbContext.Parties.Where(p => p.Id == partyId && p.State == PartyState.Open)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.MemberCount, rows.Count)
                    .SetProperty(p => p.LeaderUserId, leader)
                    .SetProperty(p => p.State, rows.Count == 0 ? PartyState.Disbanded : PartyState.Open), cancellationToken);

            party.MemberCount = rows.Count;
            party.LeaderUserId = leader;
            if (rows.Count == 0) party.State = PartyState.Disbanded;
        }

        if (party.State != PartyState.Open)
            return PartyNotFound<PartyDto>();

        var ids = rows.Select(m => m.UserId).ToList();
        var names = await _names.ResolveAsync(ids, cancellationToken);
        var presence = await _presence.GetAsync(ids, cancellationToken);

        // The room is only worth pointing at while it is live.
        var currentSession = party.CurrentSessionId is { } sessionId
                             && await _dbContext.MultiplayerSessions.AnyAsync(
                                 s => s.Id == sessionId
                                      && s.State != MultiplayerSessionState.Closed
                                      && s.State != MultiplayerSessionState.Abandoned
                                      && s.State != MultiplayerSessionState.Failed, cancellationToken)
            ? party.CurrentSessionId
            : null;

        return ServiceResult<PartyDto>.Success(new PartyDto
        {
            Id = party.Id,
            LeaderUserId = party.LeaderUserId,
            State = party.State,
            MaxSize = party.MaxSize,
            CurrentSessionId = currentSession,
            Members = rows.Select(m => new PartyMemberDto
            {
                UserId = m.UserId,
                DisplayName = names.GetValueOrDefault(m.UserId),
                IsLeader = m.UserId == party.LeaderUserId,
                Presence = presence.GetValueOrDefault(m.UserId),
                JoinedAtUtc = DateTime.SpecifyKind(m.JoinedAtUtc, DateTimeKind.Utc)
            }).ToList(),
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    private async Task<PartyInvitationDto> MapInviteAsync(PartyInvitation invitation, CancellationToken cancellationToken) =>
        ToDto(invitation, await _names.ResolveAsync([invitation.SenderUserId], cancellationToken));

    private static PartyInvitationDto ToDto(PartyInvitation i, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = i.Id,
        PartyId = i.PartyId,
        FromUserId = i.SenderUserId,
        FromDisplayName = names.GetValueOrDefault(i.SenderUserId),
        ToUserId = i.RecipientUserId,
        State = i.State,
        CreatedAtUtc = DateTime.SpecifyKind(i.CreatedAtUtc, DateTimeKind.Utc),
        ExpiresAtUtc = DateTime.SpecifyKind(i.ExpiresAtUtc, DateTimeKind.Utc)
    };

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static ServiceResult<T> PartyNotFound<T>() =>
        ServiceResult<T>.Failure(ApiErrors.PartyNotFound, ServiceErrorKind.NotFound, "No such party, or you are not in it.");

    private static ServiceResult<T> NotLeader<T>() =>
        ServiceResult<T>.Failure(ApiErrors.NotPartyLeader, ServiceErrorKind.Forbidden, "Only the party leader can do that.");

    private static ServiceResult<T> InviteNotPending<T>(InvitationState state) =>
        ServiceResult<T>.Failure(
            ApiErrors.InviteNotPending,
            ServiceErrorKind.Conflict,
            $"The invite is {state}.",
            new Dictionary<string, object?> { ["state"] = state.ToString() });
}
