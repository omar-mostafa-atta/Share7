using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Photon's custom-authentication callback. **Photon calls this, not the game.** Point the Photon
/// dashboard's custom authentication at <c>https://&lt;api&gt;/api/multiplayer/transport/photon-auth</c>,
/// with a <c>key</c> parameter equal to <c>Multiplayer:PhotonAuthKey</c>.
/// <para>
/// **Anonymous and exempt from rate limiting, deliberately.** Photon calls from a handful of its own
/// servers on behalf of every player, so a per-address limit would throttle the whole player base at
/// once. The shared key and the ticket's signature are checked before anything touches the database.
/// </para>
/// <para>
/// Always 200 with Photon's own reply shape: <c>{ "ResultCode": 1, "UserId", "Nickname" }</c> to let
/// the player in, <c>2</c> to refuse, <c>3</c> for a malformed call.
/// </para>
/// </summary>
[ApiController]
[Route("api/multiplayer/transport/photon-auth")]
[AllowAnonymous]
[DisableRateLimiting]
public class PhotonAuthController : ControllerBase
{
    private readonly ITransportAuthService _transport;

    public PhotonAuthController(ITransportAuthService transport) => _transport = transport;

    /// <summary>The client sent its auth values as query parameters (Photon's default).</summary>
    [HttpGet]
    public async Task<ActionResult<PhotonAuthResponse>> Get(
        [FromQuery] string? ticket,
        [FromQuery] string? key,
        CancellationToken cancellationToken) =>
        Ok(await _transport.AuthenticatePhotonAsync(ticket, key, cancellationToken));

    /// <summary>The client sent its auth values as post data; Photon's dashboard key still arrives in the query.</summary>
    [HttpPost]
    public async Task<ActionResult<PhotonAuthResponse>> Post(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PhotonAuthRequest? body,
        [FromQuery] string? ticket,
        [FromQuery] string? key,
        CancellationToken cancellationToken) =>
        Ok(await _transport.AuthenticatePhotonAsync(body?.Ticket ?? ticket, key, cancellationToken));
}
