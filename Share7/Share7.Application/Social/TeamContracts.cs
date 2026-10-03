using Share7.Application.Common.Models;
namespace Share7.Application.Social;
public sealed record TeamMemberDto(Guid UserId, string? DisplayName, bool Accepted, bool IsOwner);
public sealed record PlayerTeamDto(Guid Id, Guid OwnerUserId, IReadOnlyList<TeamMemberDto> Members, DateTime CreatedAtUtc, bool CanManage);
public interface IPlayerTeamService
{
    Task<IReadOnlyList<PlayerTeamDto>> ListAsync(Guid user, CancellationToken token = default);
    Task<ServiceResult<PlayerTeamDto>> CreateAsync(Guid user, string requestId, CancellationToken token = default);
    Task<ServiceResult> InviteAsync(Guid user, Guid team, Guid target, CancellationToken token = default);
    Task<ServiceResult> AcceptAsync(Guid user, Guid team, CancellationToken token = default);
    Task<ServiceResult> RemoveAsync(Guid user, Guid team, Guid target, CancellationToken token = default);
    Task<ServiceResult> DisbandAsync(Guid user, Guid team, CancellationToken token = default);
}
