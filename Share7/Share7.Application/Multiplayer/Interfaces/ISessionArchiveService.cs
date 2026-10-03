using Share7.Application.Common.Models;
using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Interfaces;

public sealed record SessionArchiveDto(Guid SessionId, Guid GameId, Guid? ModeId, DateTime? StartedAtUtc,
    DateTime EndedAtUtc, DateTime ExpiresAtUtc, MultiplayerSessionState State, int ParticipantCount,
    int? MyPlacement, bool MyWin, bool MyForfeit);

public interface ISessionArchiveService
{
    Task<int> SweepAsync(CancellationToken token = default);
    Task<ServiceResult<SessionArchiveDto>> ReadAsync(Guid user, Guid session, bool isAdmin = false, CancellationToken token = default);
}
