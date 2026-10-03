using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Social;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>Discovery never grants a seat: the existing atomic Join path owns admission.</summary>
public sealed class PublicRoomDirectory(ApplicationDbContext db, IOptions<MultiplayerOptions> options) : IPublicRoomDirectory
{
    public async Task<ServiceResult<CursorPage<PublicRoomDto>>> ReadAsync(Guid user, int protocolVersion, long after = 0, Guid? gameId = null, CancellationToken token = default)
    {
        if (!options.Value.EffectiveProtocolVersions.Contains(protocolVersion)) return ServiceResult<CursorPage<PublicRoomDto>>.Failure(
            ApiErrors.ProtocolVersionMismatch, ServiceErrorKind.Conflict, "Unsupported protocol.");
        if (after < 0) return ServiceResult<CursorPage<PublicRoomDto>>.Invalid("Invalid cursor.");
        var now = DateTime.UtcNow;
        var rows = await db.MultiplayerSessions.AsNoTracking().Where(s => s.DirectorySequence > after
            && s.Visibility == SessionVisibility.Public && s.State == MultiplayerSessionState.Created
            && s.ProtocolVersion == protocolVersion && !s.IsReserved && s.CurrentPlayerCount < s.MaxPlayers
            && (gameId == null || s.GameId == gameId)
            && !db.MultiplayerSessionBans.Any(b => b.SessionId == s.Id && b.UserId == user)
            && !db.MultiplayerSessionPlayers.Any(p => p.SessionId == s.Id && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed
                && db.PlayerBlocks.Any(b => (b.UserId == user && b.BlockedUserId == p.UserId) || (b.UserId == p.UserId && b.BlockedUserId == user))))
            .OrderBy(s => s.DirectorySequence).Take(51).ToListAsync(token);
        return ServiceResult<CursorPage<PublicRoomDto>>.Success(new(rows.Take(50).Select(s => new PublicRoomDto(s.Id, s.DirectorySequence,
            s.GameId, s.ModeId, s.CurrentPlayerCount, s.MaxPlayers, s.IsRanked, DateTime.SpecifyKind(s.CreatedAtUtc, DateTimeKind.Utc))).ToArray(),
            rows.Count > 50 ? rows[49].DirectorySequence : null, now));
    }
}
