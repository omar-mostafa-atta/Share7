using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Who may connect to the realtime transport. The backend decides; Photon asks.
/// <para>
/// Without this, anyone who learns a Photon room name can join the room itself, whatever the
/// backend's roster says (threat T1). With Photon's custom authentication pointed at
/// <see cref="AuthenticatePhotonAsync"/>, only an account the backend vouched for moments ago can
/// connect, under its real user id and its public handle.
/// </para>
/// </summary>
public interface ITransportAuthService
{
    /// <summary>A short-lived ticket for the caller to hand Photon when it connects.</summary>
    Task<ServiceResult<TransportTicketDto>> IssueTicketAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Photon's question — "is this ticket a Share7 player?" — answered in Photon's own terms.
    /// <para>
    /// Never throws for a bad ticket and never answers with anything but a
    /// <see cref="PhotonAuthResponse"/>: a refused account is result code 2, not an HTTP error.
    /// </para>
    /// </summary>
    Task<PhotonAuthResponse> AuthenticatePhotonAsync(string? ticket, string? key, CancellationToken cancellationToken = default);
}
