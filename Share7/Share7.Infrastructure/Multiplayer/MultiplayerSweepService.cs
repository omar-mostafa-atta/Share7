using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// The cleanup rules. See <see cref="IMultiplayerSweepService"/> for why this is a service rather than
/// logic inside the background worker.
/// <para>
/// **Every rule that ends a session releases its seats in the same transaction.** One account holds
/// one live seat, enforced by an index, so a session that went terminal with members still seated
/// would lock those accounts out of every future match. The healing rule at the end exists for the
/// one case a transaction cannot cover: rows written before this invariant was an index.
/// </para>
/// </summary>
public class MultiplayerSweepService : IMultiplayerSweepService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMatchResultService _results;
    private readonly MultiplayerOptions _options;
    private readonly SweeperWarmup _warmup;
    private readonly ILogger<MultiplayerSweepService> _logger;

    /// <summary>
    /// Rows touched per rule per pass.
    /// <para>
    /// A bound rather than "everything that matches" so a backlog — a bad deploy, a database that
    /// was down for an hour — cannot hold one enormous transaction open and block live traffic. The
    /// pass is idempotent, so a backlog simply drains over several passes.
    /// </para>
    /// </summary>
    private const int BatchSize = 200;

    private readonly IChallengeService _challenges;
    private readonly ITournamentService _tournaments;

    public MultiplayerSweepService(
        ApplicationDbContext dbContext,
        IMatchResultService results,
        IChallengeService challenges,
        ITournamentService tournaments,
        IOptions<MultiplayerOptions> options,
        SweeperWarmup warmup,
        ILogger<MultiplayerSweepService> logger)
    {
        _dbContext = dbContext;
        _results = results;
        _challenges = challenges;
        _tournaments = tournaments;
        _options = options.Value;
        _warmup = warmup;
        _logger = logger;
    }

    public async Task<MultiplayerSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // The silence rules wait out one full window after this process starts — see SweeperWarmup.
        // A restart must not be read as every host going quiet at once.
        var silenceWindow = TimeSpan.FromSeconds(Math.Max(
            _options.SessionTimeoutSeconds,
            Math.Max(_options.CreatingTimeoutSeconds, _options.PlayerDisconnectGraceSeconds)));

        var warmingUp = _warmup.IsWarmingUp(now, silenceWindow);

        // **Order matters, though correctness does not depend on it.** Releasing disconnected
        // players before closing empty sessions means a session whose last member just timed out is
        // closed in the same pass rather than the next one. Creating-timeout runs before the
        // heartbeat sweep because a stuck Creating session also has a stale heartbeat, and
        // CreationFailed is the more specific — and more useful — reason to record.
        var failedCreating = warmingUp ? 0 : await FailStuckCreatingAsync(now, cancellationToken);
        var abandoned = warmingUp ? 0 : await AbandonSilentAsync(now, cancellationToken);
        var playersReleased = warmingUp ? 0 : await ReleaseDisconnectedPlayersAsync(now, cancellationToken);
        var closedEmpty = await CloseEmptyAsync(now, cancellationToken);
        var orphaned = await ReleaseSeatsInEndedSessionsAsync(now, cancellationToken);
        var logsPurged = await PurgeRequestLogsAsync(now, cancellationToken);
        var eventsPurged = await PurgeEventsAsync(now, cancellationToken);
        var invitationsExpired = await ExpireInvitationsAsync(now, cancellationToken);

        // Last, so a match that ended in this very pass is already terminal when its result is
        // considered. Forfeits for silence wait out the warm-up like every other silence rule: a
        // player whose result could not land because this server was down has not forfeited anything.
        var matchesDecided = await _results.DecideDueAsync(allowDeadline: !warmingUp, cancellationToken);

        // Deadlines are clock time, not silence, so challenges are settled during the warm-up too.
        var challengesSettled = await _challenges.SettleDueAsync(cancellationToken);

        // After the verdicts, so a pairing whose match was decided in this pass moves its bracket on
        // in the same pass. Deadlines are clock time, so this runs during the warm-up too.
        var tournamentsAdvanced = await _tournaments.AdvanceDueAsync(cancellationToken);

        var result = new MultiplayerSweepResult(
            failedCreating, abandoned, closedEmpty, playersReleased, logsPurged, orphaned, matchesDecided,
            eventsPurged, invitationsExpired, challengesSettled, tournamentsAdvanced);

        Count("failed_creating", failedCreating);
        Count("abandoned", abandoned);
        Count("players_released", playersReleased);
        Count("closed_empty", closedEmpty);
        Count("orphaned_seats_released", orphaned);
        Count("request_logs_purged", logsPurged);
        Count("events_purged", eventsPurged);
        Count("invitations_expired", invitationsExpired);
        Count("challenges_settled", challengesSettled);
        Count("tournaments_advanced", tournamentsAdvanced);

        if (orphaned > 0)
            _logger.LogWarning(
                "Multiplayer sweep released {Count} seat(s) still held in sessions that had already ended.", orphaned);

        if (result.Total > 0)
            _logger.LogInformation(
                "Multiplayer sweep: {FailedCreating} failed creating, {Abandoned} abandoned, "
                + "{ClosedEmpty} closed empty, {PlayersReleased} players released, {LogsPurged} logs purged.",
                failedCreating, abandoned, closedEmpty, playersReleased, logsPurged);

        return result;
    }

    /// <summary>
    /// A session that never confirmed its transport room. It holds a room name nobody can use and
    /// keeps its host locked out of starting anything else, so it is failed rather than left.
    /// </summary>
    private async Task<int> FailStuckCreatingAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-_options.CreatingTimeoutSeconds);

        var ids = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.State == MultiplayerSessionState.Creating && s.CreatedAtUtc < cutoff)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(BatchSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The state predicate is repeated in the UPDATE, not just in the SELECT above. A session that
        // was confirmed in the gap between the two must not be failed out from under a host who just
        // got their room up.
        var updated = await _dbContext.MultiplayerSessions
            .Where(s => ids.Contains(s.Id) && s.State == MultiplayerSessionState.Creating)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, MultiplayerSessionState.Failed)
                    .SetProperty(s => s.ClosedReason, SessionClosedReason.CreationFailed)
                    .SetProperty(s => s.EndedAtUtc, now)
                    .SetProperty(s => s.CurrentPlayerCount, 0),
                cancellationToken);

        await DepartMembershipsAsync(ids, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Ended(SessionClosedReason.CreationFailed, updated);
        return updated;
    }

    /// <summary>
    /// The host stopped talking. **This is the rule that makes a crashed host survivable** — without
    /// it the session would hold its room name and its members' one active membership forever.
    /// </summary>
    private async Task<int> AbandonSilentAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-_options.SessionTimeoutSeconds);

        var ids = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Failed
                        && s.State != MultiplayerSessionState.Abandoned
                        && s.LastHeartbeatAtUtc < cutoff)
            .OrderBy(s => s.LastHeartbeatAtUtc)
            .Take(BatchSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var updated = await _dbContext.MultiplayerSessions
            .Where(s => ids.Contains(s.Id)
                        && s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Failed
                        && s.State != MultiplayerSessionState.Abandoned
                        && s.LastHeartbeatAtUtc < cutoff)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, MultiplayerSessionState.Abandoned)
                    .SetProperty(s => s.ClosedReason, SessionClosedReason.Abandoned)
                    .SetProperty(s => s.EndedAtUtc, now)
                    .SetProperty(s => s.CurrentPlayerCount, 0),
                cancellationToken);

        await DepartMembershipsAsync(ids, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Ended(SessionClosedReason.Abandoned, updated);
        return updated;
    }

    /// <summary>
    /// A member who has been missing longer than the grace period gives up their seat. The heartbeat
    /// marks them Disconnected; only this promotes it to Left, so a host with a flaky connection
    /// cannot evict anybody by failing to see them for a moment.
    /// </summary>
    private async Task<int> ReleaseDisconnectedPlayersAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-_options.PlayerDisconnectGraceSeconds);

        var stale = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.Status == SessionPlayerStatus.Disconnected && p.LastSeenAtUtc < cutoff)
            .OrderBy(p => p.LastSeenAtUtc)
            .Take(BatchSize)
            .Select(p => new { p.Id, p.SessionId })
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return 0;

        var ids = stale.Select(p => p.Id).ToList();

        var released = await _dbContext.MultiplayerSessionPlayers
            .Where(p => ids.Contains(p.Id)
                        && p.Status == SessionPlayerStatus.Disconnected
                        && p.LastSeenAtUtc < cutoff)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                    .SetProperty(p => p.LeftAtUtc, now),
                cancellationToken);

        // One statement per session — see SeatCounts for why the count is never read and then written.
        foreach (var sessionId in stale.Select(p => p.SessionId).Distinct())
            await SeatCounts.RecountAsync(_dbContext, sessionId, cancellationToken);

        return released;
    }

    /// <summary>
    /// An open session nobody is in. Closes it so its room name goes back into circulation — and
    /// releases any seat still recorded against it, because a count of zero is a number and the
    /// memberships are the truth: a closed session must never keep a seat, whatever its count said.
    /// </summary>
    private async Task<int> CloseEmptyAsync(DateTime now, CancellationToken cancellationToken)
    {
        // Ordered for the same reason as every other rule here: TOP without ORDER BY takes an
        // arbitrary set, which under a sustained backlog can leave the same rows unvisited pass
        // after pass. Oldest first guarantees the queue drains.
        var ids = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.State == MultiplayerSessionState.Created && s.CurrentPlayerCount == 0)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(BatchSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var closed = await _dbContext.MultiplayerSessions
            .Where(s => ids.Contains(s.Id)
                        && s.State == MultiplayerSessionState.Created
                        && s.CurrentPlayerCount == 0)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, MultiplayerSessionState.Closed)
                    .SetProperty(s => s.ClosedReason, SessionClosedReason.Empty)
                    .SetProperty(s => s.EndedAtUtc, now),
                cancellationToken);

        await DepartMembershipsAsync(ids, now, cancellationToken, onlyEndedSessions: true);
        await transaction.CommitAsync(cancellationToken);

        Ended(SessionClosedReason.Empty, closed);
        return closed;
    }

    /// <summary>
    /// **The healing rule.** Seats still held in sessions that have already ended are released.
    /// <para>
    /// Every path that ends a session releases its seats in the same transaction, so in steady state
    /// this finds nothing. It exists for what a transaction cannot cover — rows written before one
    /// live seat per account was an index, and anything a future path gets wrong — because a seat
    /// stranded in an ended session is not a cosmetic leak: under that index it is an account that can
    /// never be seated anywhere again.
    /// </para>
    /// </summary>
    private async Task<int> ReleaseSeatsInEndedSessionsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var ids = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed
                        && (p.Session!.State == MultiplayerSessionState.Closed
                            || p.Session.State == MultiplayerSessionState.Failed
                            || p.Session.State == MultiplayerSessionState.Abandoned))
            .OrderBy(p => p.JoinedAtUtc)
            .Take(BatchSize)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        return await _dbContext.MultiplayerSessionPlayers
            .Where(p => ids.Contains(p.Id)
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                    .SetProperty(p => p.LeftAtUtc, now),
                cancellationToken);
    }

    /// <summary>
    /// Expired idempotency keys. The window is far longer than any client retry budget, so a deleted
    /// row is one nobody could still be retrying against.
    /// </summary>
    private async Task<int> PurgeRequestLogsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-_options.RequestLogRetentionHours);

        return await _dbContext.MultiplayerRequestLogs
            .Where(l => l.CreatedAtUtc < cutoff)
            .OrderBy(l => l.CreatedAtUtc)
            .Take(BatchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Party invites whose time ran out, or whose party has ended.</summary>
    private Task<int> ExpirePartyInvitationsAsync(DateTime now, CancellationToken cancellationToken) =>
        _dbContext.PartyInvitations
            .Where(i => i.State == InvitationState.Pending
                        && (i.ExpiresAtUtc <= now || i.Party!.State != PartyState.Open))
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.State, InvitationState.Expired)
                .SetProperty(i => i.AnsweredAtUtc, now), cancellationToken);

    /// <summary>
    /// Feed events past retention, whether or not they were read. A client whose cursor falls behind
    /// this is told so (<c>EVENTS_CURSOR_EXPIRED</c>) rather than handed a feed with a hole in it.
    /// </summary>
    private async Task<int> PurgeEventsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-_options.EventRetentionDays);

        return await _dbContext.PlayerEvents
            .Where(e => e.OccurredAtUtc < cutoff)
            .OrderBy(e => e.Sequence)
            .Take(BatchSize * 10)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Pending invites whose time ran out, or whose room started or ended. Reads already treat them as
    /// expired; this makes the row say so, and frees the one-pending-invite slot.
    /// </summary>
    private async Task<int> ExpireInvitationsAsync(DateTime now, CancellationToken cancellationToken) =>
        await ExpirePartyInvitationsAsync(now, cancellationToken)
        + await _dbContext.SessionInvitations
            .Where(i => i.State == InvitationState.Pending
                        && (i.ExpiresAtUtc <= now
                            || (i.Session!.State != MultiplayerSessionState.Creating
                                && i.Session.State != MultiplayerSessionState.Created)))
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.State, InvitationState.Expired)
                .SetProperty(i => i.AnsweredAtUtc, now), cancellationToken);

    /// <summary>
    /// Marks every seat in these sessions released. A terminal session with members still seated
    /// would keep those accounts from joining anything else.
    /// <para>
    /// With <paramref name="onlyEndedSessions"/>, restricted to the sessions in the list that are now
    /// actually terminal — for a rule whose UPDATE may have skipped some of them because they changed
    /// in the gap (a player joined the "empty" session).
    /// </para>
    /// </summary>
    private Task DepartMembershipsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        DateTime now,
        CancellationToken cancellationToken,
        bool onlyEndedSessions = false)
    {
        var seats = _dbContext.MultiplayerSessionPlayers
            .Where(p => sessionIds.Contains(p.SessionId)
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed);

        if (onlyEndedSessions)
            seats = seats.Where(p => p.Session!.State == MultiplayerSessionState.Closed
                                     || p.Session.State == MultiplayerSessionState.Failed
                                     || p.Session.State == MultiplayerSessionState.Abandoned);

        return seats.ExecuteUpdateAsync(set => set
                .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                .SetProperty(p => p.LeftAtUtc, now),
            cancellationToken);
    }

    private static void Count(string rule, int rows)
    {
        if (rows > 0)
            MultiplayerMetrics.Swept.Add(rows, MultiplayerMetrics.Tag("rule", rule));
    }

    private static void Ended(SessionClosedReason reason, int sessions)
    {
        if (sessions > 0)
            MultiplayerMetrics.SessionsEnded.Add(sessions, MultiplayerMetrics.Tag("reason", reason.ToString()));
    }
}
