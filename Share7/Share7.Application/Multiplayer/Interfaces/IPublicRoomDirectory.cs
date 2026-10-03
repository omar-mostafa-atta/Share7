using Share7.Application.Common.Models;
using Share7.Application.Social;

namespace Share7.Application.Multiplayer.Interfaces;

public sealed record PublicRoomDto(Guid SessionId, long Sequence, Guid GameId, Guid? ModeId, int Players, int Capacity, bool Ranked, DateTime CreatedAtUtc);
public interface IPublicRoomDirectory
{
    Task<ServiceResult<CursorPage<PublicRoomDto>>> ReadAsync(Guid user, int protocolVersion, long after = 0, Guid? gameId = null, CancellationToken token = default);
}
