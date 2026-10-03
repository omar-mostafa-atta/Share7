using Share7.Application.Common.Models;
namespace Share7.Application.Multiplayer.Interfaces;
public sealed record ObservationCapabilityInput(Guid GameId, int ProtocolVersion, int ContractVersion, int MaxObservers, bool Enabled);
public sealed record ObserverMembershipDto(Guid SessionId, string TransportSessionName, string? TransportRegion, int ProtocolVersion,
    string Role, DateTime ExpiresAtUtc);
public sealed record ObserverRosterDto(IReadOnlyList<Guid> UserIds, DateTime ServerTimeUtc);
public interface ISessionObserverService
{
    Task<ServiceResult> SetCapabilityAsync(ObservationCapabilityInput request, CancellationToken token = default);
    Task<ServiceResult> ConfigureAsync(Guid user, Guid session, bool enabled, CancellationToken token = default);
    Task<ServiceResult<ObserverMembershipDto>> JoinAsync(Guid user, Guid session, int protocol, CancellationToken token = default);
    Task<ServiceResult<ObserverRosterDto>> RosterAsync(Guid user, Guid session, CancellationToken token = default);
    Task<ServiceResult> LeaveAsync(Guid user, Guid session, CancellationToken token = default);
}
