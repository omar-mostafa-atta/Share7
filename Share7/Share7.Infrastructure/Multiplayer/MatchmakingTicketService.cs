using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Queued matchmaking. See <see cref="IMatchmakingTicketService"/> and MultiplayerPlatform.md §7.5.
/// <para>
/// <b>Forming is single-threaded across the platform</b> (<c>sp_getapplock</c>), because two workers
/// reading the same waiting tickets would each form a match from them. Each formation is then one
/// transaction — tickets claimed, session created, every player seated — so a formation that loses a
/// race to a player's own actions (a cancel, a seat taken elsewhere) leaves nothing behind.
/// </para>
/// </summary>
public class MatchmakingTicketService : IMatchmakingTicketService
{
    private const string LockResource = "share7.matchmaking";
    private const int PassSize = 500;

    /// <summary>How long a matched ticket stays readable as "current", so a reconnecting client can find its match.</summary>
    private static readonly TimeSpan MatchedVisibleFor = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerSessionService _sessions;
    private readonly ISessionLessonMatcher _lessons;
    private readonly IPlaySelectionResolver _play;
    private readonly ILanguageService _language;
    private readonly IRatingService _ratings;
    private readonly IPlayerEventPublisher _events;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<MatchmakingTicketService> _logger;

    public MatchmakingTicketService(
        ApplicationDbContext dbContext,
        MultiplayerSessionService sessions,
        ISessionLessonMatcher lessons,
        IPlaySelectionResolver play,
        ILanguageService language,
        IRatingService ratings,
        IPlayerEventPublisher events,
        IOptions<MultiplayerOptions> options,
        ILogger<MatchmakingTicketService> logger)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _lessons = lessons;
        _play = play;
        _language = language;
        _ratings = ratings;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    // ---- enqueue -------------------------------------------------------------------------------

    public async Task<ServiceResult<MatchmakingTicketDto>> EnqueueAsync(
        Guid userId, EnqueueTicketRequest request, CancellationToken cancellationToken = default)
    {
        // Queueing again is the same intention: the ticket already searching, or the one this request made.
        if (await LiveTicketOfAsync(userId, cancellationToken) is { } live)
            return Success(await MapAsync(live, cancellationToken));

        if (request.RequestId is { Length: > 0 } requestId
            && await _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.OwnerUserId == userId && t.RequestId == requestId, cancellationToken) is { } made)
            return Success(await MapAsync(made, cancellationToken));

        if (!_options.EffectiveProtocolVersions.Contains(request.ProtocolVersion))
            return Failure(ApiErrors.ProtocolVersionMismatch, ServiceErrorKind.Validation,
                $"Protocol version {request.ProtocolVersion} is not accepted by this server.");

        var players = new List<Guid> { userId };
        Guid? partyId = null;

        if (request.PartyId is { } requestedParty)
        {
            // Ranked is queued alone: a friend in your ranked match is the easiest way to be boosted.
            if (request.Ranked)
                return Failure(ApiErrors.RankedSoloOnly, ServiceErrorKind.Conflict, "Ranked is played solo.");

            var party = await _dbContext.Parties.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == requestedParty && p.State == PartyState.Open, cancellationToken);

            var members = await _dbContext.PartyMembers.AsNoTracking()
                .Where(m => m.PartyId == requestedParty && m.LeftAtUtc == null)
                .OrderBy(m => m.JoinedAtUtc)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            if (party is null || !members.Contains(userId))
                return Failure(ApiErrors.PartyNotFound, ServiceErrorKind.NotFound, "No such party, or you are not in it.");

            if (party.LeaderUserId != userId)
                return Failure(ApiErrors.NotPartyLeader, ServiceErrorKind.Forbidden, "Only the party leader can queue the party.");

            players = [userId, .. members.Where(m => m != userId)];
            partyId = requestedParty;
        }

        var seated = await _dbContext.MultiplayerSessionPlayers.AsNoTracking()
            .Where(p => players.Contains(p.UserId) && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed)
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (seated.Count > 0)
            return players.Count == 1
                ? Failure(ApiErrors.AlreadyInSession, ServiceErrorKind.Conflict, "The caller already holds a seat in a session that has not ended.")
                : ServiceResult<MatchmakingTicketDto>.Failure(ApiErrors.PartyMemberBusy, ServiceErrorKind.Conflict,
                    "Someone in the party is still in a room.", new Dictionary<string, object?> { ["userIds"] = seated });

        // Every player passes the mode's own gate — entitlement, grade, event eligibility — before
        // anyone is queued, so a party never discovers at seat time that one of them cannot play.
        PlaySelection? play = null;

        foreach (var player in players)
        {
            var selection = await _play.ResolveAsync(player, new PlaySelectionRequest
            {
                GameId = request.GameId,
                ModeKey = request.ModeKey,
                ContextKey = request.EventId is null ? null : PlayContextTokens.Event,
                EventId = request.EventId,
                PlayerCount = 2
            }, cancellationToken);

            if (!selection.Succeeded)
                return new ServiceResult<MatchmakingTicketDto>
                {
                    ErrorKind = selection.ErrorKind,
                    Errors = selection.Errors,
                    Error = selection.Error,
                    Details = players.Count == 1 ? selection.Details : new Dictionary<string, object?> { ["userId"] = player }
                };

            play ??= selection.Value;
        }

        if (play?.ModeId is not { } modeId)
            return Failure(ApiErrors.PlayModeUnknown, ServiceErrorKind.NotFound, "Queued matchmaking needs a game with modes.");

        if (request.Ranked && !await _dbContext.GameModes.AnyAsync(m => m.Id == modeId && m.Ranked, cancellationToken))
            return Failure(ApiErrors.ModeNotRanked, ServiceErrorKind.Conflict, "This mode is not offered as ranked.");

        var langId = await _language.ResolveCurrentAsync(cancellationToken);
        var lessonId = request.CurriculumPath?.LessonId;
        var subjectId = lessonId is null ? request.CurriculumPath?.SubjectId : null;

        // A subject ticket carries the lessons every one of its players can play; matching intersects them.
        HashSet<Guid>? shared = null;

        if (subjectId is { } subject)
        {
            foreach (var player in players)
            {
                var eligible = (await _lessons.EligibleLessonsAsync(player, request.GameId, subject, langId, cancellationToken))
                    .Select(l => l.LessonId)
                    .ToHashSet();

                shared = shared is null ? eligible : [.. shared.Intersect(eligible)];
            }

            if (shared is not { Count: > 0 })
                return Failure(ApiErrors.PlayNoSharedLesson, ServiceErrorKind.Conflict,
                    players.Count == 1
                        ? "You have no unlocked lessons with questions in this subject yet."
                        : "The party has no lesson in this subject that everyone can play.");
        }

        var rating = request.Ranked
            ? (await _ratings.RatingsAsync([userId], modeId, cancellationToken))[userId]
            : RatingModel.Initial;

        var now = DateTime.UtcNow;

        var ticket = new MatchmakingTicket
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            PartyId = partyId,
            Size = players.Count,
            GameId = request.GameId,
            ModeId = modeId,
            EventId = play.EventId,
            IsRanked = request.Ranked,
            ProtocolVersion = request.ProtocolVersion,
            LangId = langId,
            SubjectId = subjectId,
            LessonId = lessonId,
            Region = string.IsNullOrWhiteSpace(request.TransportRegion) ? null : request.TransportRegion.Trim(),
            RatingMu = rating.Mu,
            RatingSigma = rating.Sigma,
            State = TicketState.Searching,
            EnqueuedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(Math.Max(30, _options.TicketSeconds)),
            RequestId = request.RequestId is { Length: > 0 } id ? id : null
        };

        foreach (var player in players)
            ticket.Members.Add(new MatchmakingTicketMember { TicketId = ticket.Id, UserId = player, IsLive = true });

        foreach (var lesson in shared ?? [])
            ticket.Lessons.Add(new MatchmakingTicketLesson { TicketId = ticket.Id, LessonId = lesson });

        _dbContext.MatchmakingTickets.Add(ticket);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Someone on this ticket is already searching — a double tap, or a party member who queued alone.
            Detach();

            if (await LiveTicketOfAsync(userId, cancellationToken) is { } raced)
                return Success(await MapAsync(raced, cancellationToken));

            var searching = await _dbContext.MatchmakingTicketMembers.AsNoTracking()
                .Where(m => players.Contains(m.UserId) && m.IsLive)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            return ServiceResult<MatchmakingTicketDto>.Failure(ApiErrors.PartyMemberBusy, ServiceErrorKind.Conflict,
                "Someone in the party is already searching.", new Dictionary<string, object?> { ["userIds"] = searching });
        }

        _logger.LogInformation("Ticket {TicketId} queued ({Kind}, {Size} player(s)).", ticket.Id, ticket.IsRanked ? "ranked" : "casual", ticket.Size);
        MultiplayerMetrics.Tickets.Add(1, MultiplayerMetrics.Tag("outcome", "queued"), MultiplayerMetrics.Tag("ranked", ticket.IsRanked));

        return Success(await MapAsync(ticket, cancellationToken));
    }

    // ---- read / cancel -------------------------------------------------------------------------

    public async Task<ServiceResult<MatchmakingTicketDto>> CurrentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow - MatchedVisibleFor;

        var ticket = await _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members)
            .Where(t => t.Members.Any(m => m.UserId == userId)
                        && (t.State == TicketState.Searching || (t.State == TicketState.Matched && t.MatchedAtUtc >= since)))
            .OrderByDescending(t => t.EnqueuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return ticket is null
            ? Failure(ApiErrors.TicketNotFound, ServiceErrorKind.NotFound, "You are not searching.")
            : Success(await MapAsync(ticket, cancellationToken));
    }

    public async Task<ServiceResult<MatchmakingTicketDto>> CancelAsync(Guid userId, Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.Members.Any(m => m.UserId == userId), cancellationToken);

        if (ticket is null)
            return Failure(ApiErrors.TicketNotFound, ServiceErrorKind.NotFound, "No such ticket.");

        if (!await EndAsync(ticketId, TicketState.Cancelled, "cancelled", DateTime.UtcNow, cancellationToken)
            && ticket.State != TicketState.Cancelled)
            return ServiceResult<MatchmakingTicketDto>.Failure(ApiErrors.TicketNotSearching, ServiceErrorKind.Conflict,
                $"The ticket is {ticket.State}.", new Dictionary<string, object?> { ["state"] = ticket.State.ToString() });

        var current = await _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members).FirstAsync(t => t.Id == ticketId, cancellationToken);
        return Success(await MapAsync(current, cancellationToken));
    }

    // ---- the worker pass -----------------------------------------------------------------------

    public async Task<int> FormMatchesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            if (!await TryLockAsync(cancellationToken))
                return 0;

            try
            {
                var now = DateTime.UtcNow;

                await ExpireAsync(now, cancellationToken);
                await RequeueAsync(now, cancellationToken);
                return await FormAsync(now, cancellationToken);
            }
            finally
            {
                await UnlockAsync();
            }
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Takes the platform-wide formation lock for this connection, without waiting. False if another pass holds it.</summary>
    private async Task<bool> TryLockAsync(CancellationToken cancellationToken)
    {
        var command = _dbContext.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.Add(new SqlParameter("@Resource", LockResource));
            command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
            command.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
            command.Parameters.Add(new SqlParameter("@LockTimeout", 0));
            var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
            command.Parameters.Add(result);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return result.Value is int code && code >= 0;
        }
    }

    private async Task UnlockAsync()
    {
        var command = _dbContext.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.CommandText = "sp_releaseapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.Add(new SqlParameter("@Resource", LockResource));
            command.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Tickets that searched too long: ended, and their players told so they can try something else.</summary>
    private async Task ExpireAsync(DateTime now, CancellationToken cancellationToken)
    {
        var due = await _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members)
            .Where(t => t.State == TicketState.Searching && t.ExpiresAtUtc <= now)
            .Take(PassSize)
            .ToListAsync(cancellationToken);

        foreach (var ticket in due)
        {
            if (!await EndAsync(ticket.Id, TicketState.Expired, "expired", now, cancellationToken))
                continue;

            foreach (var member in ticket.Members)
                _events.Stage(member.UserId, PlayerEventTypes.MatchmakingExpired, new { ticketId = ticket.Id }, now.AddMinutes(10));

            MultiplayerMetrics.Tickets.Add(1, MultiplayerMetrics.Tag("outcome", "expired"), MultiplayerMetrics.Tag("ranked", ticket.IsRanked));
        }

        if (due.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A formed match whose host never brought the room up fails in <c>Creating</c>. Everyone else
    /// goes back to searching — keeping their place in the queue — and the host's ticket ends: an
    /// unresponsive host must not be matched straight back into the next room.
    /// </summary>
    private async Task RequeueAsync(DateTime now, CancellationToken cancellationToken)
    {
        var stranded = await _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members)
            .Where(t => t.State == TicketState.Matched
                        && _dbContext.MultiplayerSessions.Any(s => s.Id == t.SessionId && s.State == MultiplayerSessionState.Failed))
            .Take(PassSize)
            .ToListAsync(cancellationToken);

        foreach (var ticket in stranded)
        {
            if (ticket.Members.Any(m => m.UserId == ticket.HostUserId))
            {
                await _dbContext.MatchmakingTickets
                    .Where(t => t.Id == ticket.Id && t.State == TicketState.Matched)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(t => t.State, TicketState.Cancelled)
                        .SetProperty(t => t.EndReason, "host_no_show")
                        .SetProperty(t => t.EndedAtUtc, now), cancellationToken);
                continue;
            }

            var requeued = await _dbContext.MatchmakingTickets
                .Where(t => t.Id == ticket.Id && t.State == TicketState.Matched)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.State, TicketState.Searching)
                    .SetProperty(t => t.SessionId, (Guid?)null)
                    .SetProperty(t => t.HostUserId, (Guid?)null)
                    .SetProperty(t => t.MatchedAtUtc, (DateTime?)null)
                    .SetProperty(t => t.EndReason, "host_no_show")
                    .SetProperty(t => t.ExpiresAtUtc, t => t.ExpiresAtUtc > now.AddSeconds(60) ? t.ExpiresAtUtc : now.AddSeconds(60)), cancellationToken) > 0;

            if (!requeued)
                continue;

            try
            {
                await _dbContext.MatchmakingTicketMembers
                    .Where(m => m.TicketId == ticket.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(m => m.IsLive, true), cancellationToken);
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627)
            {
                // They already queued again on their own; that newer ticket is the one that counts.
                await EndAsync(ticket.Id, TicketState.Cancelled, "requeued_elsewhere", now, cancellationToken);
                continue;
            }

            foreach (var member in ticket.Members)
                _events.Stage(member.UserId, PlayerEventTypes.MatchmakingRequeued, new { ticketId = ticket.Id, reason = "host_no_show" }, now.AddMinutes(10));
        }

        if (stranded.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> FormAsync(DateTime now, CancellationToken cancellationToken)
    {
        var searching = await _dbContext.MatchmakingTickets.AsNoTracking()
            .Include(t => t.Members).Include(t => t.Lessons)
            .Where(t => t.State == TicketState.Searching)
            .OrderBy(t => t.EnqueuedAtUtc)
            .Take(PassSize)
            .ToListAsync(cancellationToken);

        if (searching.Count == 0)
            return 0;

        // Blocks between anyone waiting: a blocked pair is never put in one match.
        var everyone = searching.SelectMany(t => t.Members.Select(m => m.UserId)).Distinct().ToList();

        var blocked = (await _dbContext.PlayerBlocks.AsNoTracking()
                .Where(b => everyone.Contains(b.UserId) && everyone.Contains(b.BlockedUserId))
                .Select(b => new { b.UserId, b.BlockedUserId })
                .ToListAsync(cancellationToken))
            .SelectMany(b => new[] { (b.UserId, b.BlockedUserId), (b.BlockedUserId, b.UserId) })
            .ToHashSet();

        var formed = 0;

        foreach (var pool in searching.GroupBy(t => (t.GameId, t.ModeId, t.EventId, t.IsRanked, t.ProtocolVersion, t.LangId, t.LessonId, t.SubjectId)))
        {
            try
            {
                formed += await FormPoolAsync(pool.ToList(), blocked, now, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One bad pool must not stall every other game's queue.
                Detach();
                _logger.LogError(exception, "Forming matches for mode {ModeId} failed; retried next pass.", pool.Key.ModeId);
            }
        }

        return formed;
    }

    private async Task<int> FormPoolAsync(
        List<MatchmakingTicket> pool, HashSet<(Guid, Guid)> blocked, DateTime now, CancellationToken cancellationToken)
    {
        var first = pool[0];

        var mode = await _dbContext.GameModes.AsNoTracking()
            .Where(m => m.Id == first.ModeId)
            .Select(m => new { m.MaxPlayers, GameMax = m.Game!.MaxPlayers })
            .FirstOrDefaultAsync(cancellationToken);

        if (mode is null)
            return 0;

        var capacity = Math.Max(2, Math.Min(mode.MaxPlayers, Math.Max(1, mode.GameMax)));
        var remaining = new List<MatchmakingTicket>(pool);
        var formed = 0;

        // Casual groups go into an open room with space for all of them first: that is somebody's
        // lobby filling up, which is the shortest wait for everyone in it.
        if (!first.IsRanked)
        {
            foreach (var ticket in remaining.ToList())
            {
                if (await PlaceInLobbyAsync(ticket, blocked, now, cancellationToken))
                {
                    remaining.Remove(ticket);
                    formed++;
                }
            }
        }

        while (remaining.Count > 0)
        {
            var anchor = remaining[0];
            var waited = (now - anchor.EnqueuedAtUtc).TotalSeconds;

            var group = new List<MatchmakingTicket> { anchor };
            var size = anchor.Size;
            var lessons = anchor.Lessons.Select(l => l.LessonId).ToHashSet();

            var candidates = remaining.Skip(1)
                .Where(c => !first.IsRanked || Math.Abs(c.RatingMu - anchor.RatingMu) <= Math.Max(Band(anchor, now), Band(c, now)))
                .OrderBy(c => first.IsRanked ? Math.Abs(c.RatingMu - anchor.RatingMu) : 0)
                .ThenBy(c => c.EnqueuedAtUtc);

            foreach (var candidate in candidates)
            {
                if (size + candidate.Size > capacity || Blocked(group, candidate, blocked))
                    continue;

                if (first.SubjectId is not null)
                {
                    var together = lessons.Intersect(candidate.Lessons.Select(l => l.LessonId)).ToHashSet();
                    if (together.Count == 0)
                        continue;

                    lessons = together;
                }

                group.Add(candidate);
                size += candidate.Size;

                if (size == capacity)
                    break;
            }

            var fillAfter = first.IsRanked ? _options.RankedFillAfterSeconds : _options.CasualFillAfterSeconds;
            var minimum = first.IsRanked ? 2 : 1;
            var ready = size == capacity || (waited >= fillAfter && size >= minimum);

            if (ready && await FormMatchAsync(group, capacity, lessons, now, cancellationToken))
            {
                formed++;
                remaining.RemoveAll(group.Contains);
            }
            else
            {
                remaining.Remove(anchor);
            }
        }

        return formed;
    }

    /// <summary>
    /// How far apart two ranked ratings may be for this ticket, widening with its wait so nobody waits
    /// forever for a perfect opponent.
    /// </summary>
    private double Band(MatchmakingTicket ticket, DateTime now) =>
        Math.Min(_options.RankedBandMax,
            _options.RankedBandBase + _options.RankedBandGrowthPerSecond * Math.Max(0, (now - ticket.EnqueuedAtUtc).TotalSeconds));

    private static bool Blocked(IEnumerable<MatchmakingTicket> group, MatchmakingTicket candidate, HashSet<(Guid, Guid)> blocked) =>
        group.SelectMany(t => t.Members).Any(a => candidate.Members.Any(b => blocked.Contains((a.UserId, b.UserId))));

    /// <summary>
    /// One match: tickets claimed, the session created with everyone seated, everyone told — in one
    /// transaction, so a formation that loses any race leaves nothing behind.
    /// </summary>
    private async Task<bool> FormMatchAsync(
        List<MatchmakingTicket> group, int capacity, HashSet<Guid> lessons, DateTime now, CancellationToken cancellationToken)
    {
        var anchor = group[0];
        var ids = group.Select(t => t.Id).ToList();
        var players = group.SelectMany(t => t.Members.Select(m => m.UserId)).ToList();

        // The longest-waiting ticket's owner hosts.
        players.Remove(anchor.OwnerUserId);
        players.Insert(0, anchor.OwnerUserId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await _dbContext.MatchmakingTickets
            .Where(t => ids.Contains(t.Id) && t.State == TicketState.Searching)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.State, TicketState.Matched)
                .SetProperty(t => t.MatchedAtUtc, now), cancellationToken);

        if (claimed != group.Count)
        {
            // Someone cancelled in the gap. The rest are picked up again next pass.
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        // Subject matches play a lesson every ticket shares; the session narrows from the host's set.
        var match = new MultiplayerSessionService.FormedMatch(
            players,
            anchor.GameId,
            anchor.ModeId,
            anchor.EventId,
            Rated: anchor.IsRanked,
            Public: !anchor.IsRanked,
            MaxPlayers: anchor.IsRanked ? players.Count : capacity,
            anchor.ProtocolVersion,
            anchor.LangId,
            anchor.SubjectId,
            anchor.LessonId,
            TransportSessionName: $"mm{Guid.NewGuid():N}"[..24],
            anchor.Region);

        ServiceResult<MultiplayerSession> created;

        try
        {
            created = await _sessions.CreateFormedAsync(match, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(cancellationToken);
            Detach();
            await DropSeatedElsewhereAsync(group, now, cancellationToken);
            return false;
        }

        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            Detach();
            _logger.LogInformation("A match for mode {ModeId} could not be formed: {Reason}.", anchor.ModeId, created.Error?.Code);
            return false;
        }

        var session = created.Value!;
        await ConfirmMatchedAsync(group, session, now, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        MultiplayerMetrics.Tickets.Add(group.Count, MultiplayerMetrics.Tag("outcome", "matched"), MultiplayerMetrics.Tag("ranked", anchor.IsRanked));
        _logger.LogInformation("Formed session {SessionId} from {Tickets} ticket(s), {Players} player(s).", session.Id, group.Count, players.Count);

        return true;
    }

    /// <summary>A casual ticket placed into somebody's open public room with space for all of its players.</summary>
    private async Task<bool> PlaceInLobbyAsync(MatchmakingTicket ticket, HashSet<(Guid, Guid)> blocked, DateTime now, CancellationToken cancellationToken)
    {
        var fresh = now.AddSeconds(-_options.SessionTimeoutSeconds);
        var players = ticket.Members.Select(m => m.UserId).ToList();
        var lessons = ticket.Lessons.Select(l => l.LessonId).ToList();

        var lobbies = _dbContext.MultiplayerSessions.AsNoTracking()
            .Where(s => s.GameId == ticket.GameId
                        && s.ModeId == ticket.ModeId
                        && s.EventId == ticket.EventId
                        && s.State == MultiplayerSessionState.Created
                        && s.Visibility == SessionVisibility.Public
                        && !s.IsRanked && !s.IsRated && !s.IsReserved
                        && s.ProtocolVersion == ticket.ProtocolVersion
                        && s.LastHeartbeatAtUtc > fresh
                        && s.CurrentPlayerCount + ticket.Size <= s.MaxPlayers
                        && !_dbContext.MultiplayerSessionBans.Any(b => b.SessionId == s.Id && players.Contains(b.UserId)));

        if (ticket.LessonId is { } lessonId)
            lobbies = lobbies.Where(s => s.LessonId == lessonId);
        else if (ticket.SubjectId is { } subjectId)
            lobbies = lobbies.Where(s => s.SubjectId == subjectId && s.LangId == ticket.LangId
                                         && (s.LessonId == null
                                             ? s.EligibleLessons.Any(l => lessons.Contains(l.LessonId))
                                             : lessons.Contains(s.LessonId.Value)));

        var candidates = await lobbies
            .OrderByDescending(s => s.CurrentPlayerCount)
            .ThenBy(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.HostUserId,
                Seated = s.Players.Where(p => p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed)
                    .Select(p => p.UserId).ToList()
            })
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var lobby in candidates)
        {
            if (lobby.Seated.Any(seated => players.Any(p => blocked.Contains((seated, p)))))
                continue;

            // Blocks with players not themselves searching are not in the pass's set; ask directly.
            if (await _dbContext.PlayerBlocks.AnyAsync(
                    b => (players.Contains(b.UserId) && lobby.Seated.Contains(b.BlockedUserId))
                         || (lobby.Seated.Contains(b.UserId) && players.Contains(b.BlockedUserId)), cancellationToken))
                continue;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var claimed = await _dbContext.MatchmakingTickets
                .Where(t => t.Id == ticket.Id && t.State == TicketState.Searching)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.State, TicketState.Matched).SetProperty(t => t.MatchedAtUtc, now), cancellationToken);

            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            bool seated;

            try
            {
                seated = await _sessions.SeatGroupAsync(lobby.Id, players, ticket.ProtocolVersion, cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                await transaction.RollbackAsync(cancellationToken);
                Detach();
                await DropSeatedElsewhereAsync([ticket], now, cancellationToken);
                return false;
            }

            if (!seated)
            {
                await transaction.RollbackAsync(cancellationToken);
                Detach();
                continue;
            }

            var session = await _dbContext.MultiplayerSessions.AsNoTracking().FirstAsync(s => s.Id == lobby.Id, cancellationToken);
            await ConfirmMatchedAsync([ticket], session, now, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            MultiplayerMetrics.Tickets.Add(1, MultiplayerMetrics.Tag("outcome", "placed"), MultiplayerMetrics.Tag("ranked", false));
            return true;
        }

        return false;
    }

    /// <summary>Records the match on its tickets, frees the players to queue again later, and tells each of them.</summary>
    private async Task ConfirmMatchedAsync(IReadOnlyList<MatchmakingTicket> tickets, MultiplayerSession session, DateTime now, CancellationToken cancellationToken)
    {
        var ids = tickets.Select(t => t.Id).ToList();

        await _dbContext.MatchmakingTickets
            .Where(t => ids.Contains(t.Id))
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.SessionId, session.Id)
                .SetProperty(t => t.HostUserId, session.HostUserId), cancellationToken);

        await _dbContext.MatchmakingTicketMembers
            .Where(m => ids.Contains(m.TicketId))
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.IsLive, false), cancellationToken);

        foreach (var ticket in tickets)
        foreach (var member in ticket.Members)
        {
            _events.Stage(member.UserId, PlayerEventTypes.MatchFound, new
            {
                ticketId = ticket.Id,
                sessionId = session.Id,
                hostUserId = session.HostUserId,
                youHost = member.UserId == session.HostUserId,
                transportSessionName = session.TransportSessionName,
                transportRegion = session.TransportRegion,
                rated = session.IsRated
            }, now.AddMinutes(10));
        }
    }

    /// <summary>
    /// A formation hit the one-live-seat index: somebody got a seat elsewhere while they were searching
    /// (a sync matchmake, an invite). Their ticket ends; everyone else stays in the queue.
    /// </summary>
    private async Task DropSeatedElsewhereAsync(IReadOnlyList<MatchmakingTicket> tickets, DateTime now, CancellationToken cancellationToken)
    {
        var players = tickets.SelectMany(t => t.Members.Select(m => m.UserId)).ToList();

        var seated = await _dbContext.MultiplayerSessionPlayers.AsNoTracking()
            .Where(p => players.Contains(p.UserId) && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        foreach (var ticket in tickets.Where(t => t.Members.Any(m => seated.Contains(m.UserId))))
            await EndAsync(ticket.Id, TicketState.Cancelled, "seated_elsewhere", now, cancellationToken);
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Ends a searching ticket and frees its players. False when it was no longer searching.</summary>
    private async Task<bool> EndAsync(Guid ticketId, TicketState to, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var ended = await _dbContext.MatchmakingTickets
            .Where(t => t.Id == ticketId && t.State == TicketState.Searching)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.State, to)
                .SetProperty(t => t.EndReason, reason)
                .SetProperty(t => t.EndedAtUtc, now), cancellationToken) > 0;

        if (ended)
            await _dbContext.MatchmakingTicketMembers
                .Where(m => m.TicketId == ticketId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.IsLive, false), cancellationToken);

        return ended;
    }

    private Task<MatchmakingTicket?> LiveTicketOfAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.MatchmakingTickets.AsNoTracking().Include(t => t.Members)
            .Where(t => t.State == TicketState.Searching && t.Members.Any(m => m.UserId == userId && m.IsLive))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<MatchmakingTicketDto> MapAsync(MatchmakingTicket ticket, CancellationToken cancellationToken)
    {
        var session = ticket.SessionId is { } sessionId
            ? await _dbContext.MultiplayerSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.TransportSessionName, s.TransportRegion })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new MatchmakingTicketDto
        {
            Id = ticket.Id,
            State = ticket.State,
            Ranked = ticket.IsRanked,
            GameId = ticket.GameId,
            ModeId = ticket.ModeId,
            PartyId = ticket.PartyId,
            Players = ticket.Members.Select(m => m.UserId).ToList(),
            EnqueuedAtUtc = DateTime.SpecifyKind(ticket.EnqueuedAtUtc, DateTimeKind.Utc),
            ExpiresAtUtc = DateTime.SpecifyKind(ticket.ExpiresAtUtc, DateTimeKind.Utc),
            SessionId = ticket.SessionId,
            HostUserId = ticket.HostUserId,
            TransportSessionName = session?.TransportSessionName,
            TransportRegion = session?.TransportRegion,
            EndReason = ticket.EndReason,
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static ServiceResult<MatchmakingTicketDto> Success(MatchmakingTicketDto dto) =>
        ServiceResult<MatchmakingTicketDto>.Success(dto);

    private static ServiceResult<MatchmakingTicketDto> Failure(ApiErrorCode code, ServiceErrorKind kind, string message) =>
        ServiceResult<MatchmakingTicketDto>.Failure(code, kind, message);
}
