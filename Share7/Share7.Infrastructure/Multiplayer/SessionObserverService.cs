using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Social;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Social;
namespace Share7.Infrastructure.Multiplayer;
public sealed class SessionObserverService(ApplicationDbContext db, ISocialPolicy policy, IAuditLog audit) : ISessionObserverService
{
    public async Task<ServiceResult> SetCapabilityAsync(ObservationCapabilityInput request, CancellationToken token = default)
    {
        if (request.ContractVersion != 1 || request.ProtocolVersion < 1 || request.MaxObservers is < 1 or > 4
            || !await db.Games.AnyAsync(g => g.Id == request.GameId && g.SupportsMultiplayer, token)) return ServiceResult.Invalid("Invalid observer adapter contract.");
        var row = await db.GameObservationCapabilities.FirstOrDefaultAsync(c => c.GameId == request.GameId && c.ProtocolVersion == request.ProtocolVersion, token);
        if (row is null) { row = new() { GameId = request.GameId, ProtocolVersion = request.ProtocolVersion }; db.GameObservationCapabilities.Add(row); }
        row.ContractVersion = request.ContractVersion; row.MaxObservers = request.MaxObservers; row.Enabled = request.Enabled;
        audit.Record(new("multiplayer.observer-capability.changed", "multiplayer", "Changed an explicitly supported observer adapter.", "game", request.GameId.ToString(), new { request.ProtocolVersion, request.Enabled }));
        await db.SaveChangesAsync(token); return ServiceResult.Success();
    }
    public async Task<ServiceResult> ConfigureAsync(Guid user, Guid session, bool enabled, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token); await Lock(session, token);
        var room = await db.MultiplayerSessions.FirstOrDefaultAsync(s => s.Id == session && s.HostUserId == user, token);
        if (room is null || room.State != MultiplayerSessionState.Created || !Eligible(room)) return ServiceResult.NotFound("Observer setting unavailable.");
        if (enabled && !await db.GameObservationCapabilities.AnyAsync(c => c.GameId == room.GameId && c.ProtocolVersion == room.ProtocolVersion && c.Enabled, token))
            return ServiceResult.Conflict("This game/protocol has no approved observer adapter.");
        room.AllowObservers = enabled;
        if (!enabled) await db.SessionObservers.Where(o => o.SessionId == session).ExecuteDeleteAsync(token);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
    public async Task<ServiceResult<ObserverMembershipDto>> JoinAsync(Guid user, Guid session, int protocol, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token); await Lock(session, token);
        var room = await db.MultiplayerSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session, token);
        var now = DateTime.UtcNow;
        if (room is null || !room.AllowObservers || !Eligible(room) || room.ProtocolVersion != protocol
            || room.State is not (MultiplayerSessionState.Created or MultiplayerSessionState.Starting or MultiplayerSessionState.Running)
            || !(await policy.CanInteractAsync(user, room.HostUserId, SocialAction.Invite, token)).Allowed
            || await db.MultiplayerSessionPlayers.AnyAsync(p => p.SessionId == session && p.UserId == user, token)
            || await db.MultiplayerSessionBans.AnyAsync(b => b.SessionId == session && b.UserId == user, token)
            || await db.MultiplayerSessionPlayers.AnyAsync(p => p.SessionId == session && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed
                && db.PlayerBlocks.Any(b => (b.UserId == user && b.BlockedUserId == p.UserId) || (b.UserId == p.UserId && b.BlockedUserId == user)), token)) return Missing<ObserverMembershipDto>();
        var capability = await db.GameObservationCapabilities.AsNoTracking().FirstOrDefaultAsync(c => c.GameId == room.GameId && c.ProtocolVersion == protocol && c.Enabled, token);
        if (capability is null) return Missing<ObserverMembershipDto>();
        await db.SessionObservers.Where(o => o.UserId == user && o.ExpiresAtUtc <= now).ExecuteDeleteAsync(token);
        var existing = await db.SessionObservers.FirstOrDefaultAsync(o => o.UserId == user, token);
        if (existing is not null && existing.SessionId != session) return ServiceResult<ObserverMembershipDto>.Conflict("Leave the other observation first.");
        if (existing is null)
        {
            if (await db.SessionObservers.CountAsync(o => o.SessionId == session && o.ExpiresAtUtc > now, token) >= capability.MaxObservers)
                return ServiceResult<ObserverMembershipDto>.Conflict("Observer capacity reached.");
            existing = new() { SessionId = session, UserId = user }; db.SessionObservers.Add(existing);
        }
        existing.ExpiresAtUtc = now.AddSeconds(90);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return ServiceResult<ObserverMembershipDto>.Success(new(session, room.TransportSessionName, room.TransportRegion, protocol, "observer", existing.ExpiresAtUtc));
    }
    public async Task<ServiceResult<ObserverRosterDto>> RosterAsync(Guid user, Guid session, CancellationToken token = default)
    {
        var room = await db.MultiplayerSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session && s.HostUserId == user, token);
        if (room is null || room.State.IsTerminal()) return Missing<ObserverRosterDto>();
        var now = DateTime.UtcNow;
        if (!room.AllowObservers || !Eligible(room) || !await db.GameObservationCapabilities.AnyAsync(c => c.GameId == room.GameId && c.ProtocolVersion == room.ProtocolVersion && c.Enabled, token))
            return ServiceResult<ObserverRosterDto>.Success(new([], now));
        var ids = await db.SessionObservers.AsNoTracking().Where(o => o.SessionId == session && o.ExpiresAtUtc > now).Select(o => o.UserId).Take(4).ToListAsync(token);
        var allowed = new List<Guid>();
        foreach (var id in ids)
            if ((await policy.CanInteractAsync(id, user, SocialAction.Invite, token)).Allowed
                && !await db.MultiplayerSessionPlayers.AnyAsync(p => p.SessionId == session && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed
                    && db.PlayerBlocks.Any(b => (b.UserId == id && b.BlockedUserId == p.UserId) || (b.UserId == p.UserId && b.BlockedUserId == id)), token)) allowed.Add(id);
        return ServiceResult<ObserverRosterDto>.Success(new(allowed, now));
    }
    public async Task<ServiceResult> LeaveAsync(Guid user, Guid session, CancellationToken token = default)
    {
        await db.SessionObservers.Where(o => o.SessionId == session && o.UserId == user).ExecuteDeleteAsync(token); return ServiceResult.Success();
    }
    private static bool Eligible(MultiplayerSession room) => room.Visibility == SessionVisibility.Public && !room.IsReserved && !room.IsRanked && room.EventId == null;
    private async Task Lock(Guid id, CancellationToken token) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [MultiplayerSessions] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {id}", token);
    private static ServiceResult<T> Missing<T>() => ServiceResult<T>.NotFound("Observation unavailable.");
}
