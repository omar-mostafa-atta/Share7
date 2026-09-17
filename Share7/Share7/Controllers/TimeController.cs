using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Commerce.Models;

namespace Share7.API.Controllers;

/// <summary>
/// The server's authoritative UTC clock. Nothing more — deliberately not a "time service".
/// </summary>
[ApiController]
[Route("api/time")]
public class TimeController : ControllerBase
{
    /// <summary>
    /// <code>{ "utcNow": "2026-08-14T20:00:00Z" }</code>
    /// <para>
    /// **Anonymous**, because a clock is not a secret and a client may need it before it has a
    /// token — for instance to decide whether a cached offer has expired while signed out.
    /// </para>
    /// <para>
    /// This machine is the one that decides whether an offer has expired: `expiresAtUtc` is compared
    /// against this clock, never against the device's. A client with a skewed clock should trust
    /// this and the `canPurchase` flag the shop already returns.
    /// </para>
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new ServerTimeResponse { UtcNow = DateTime.UtcNow });
}
