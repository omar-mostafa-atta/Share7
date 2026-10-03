using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Constants;
namespace Share7.API.Controllers;
public sealed record ObserveRequest(int ProtocolVersion);
[ApiController, Authorize, Route("api/multiplayer")]
public sealed class SessionObserversController(ISessionObserverService observers, ICurrentUserService current) : ControllerBase
{
    [HttpPut("admin/observer-capabilities"), Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Capability(ObservationCapabilityInput request, CancellationToken token)
    { var result = await observers.SetCapabilityAsync(request, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult(); }
    [HttpPut("sessions/{session:guid}/observers/settings"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Configure(Guid session, SwitchRequest request, CancellationToken token)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await observers.ConfigureAsync(user, session, request.Enabled, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
    [HttpPost("sessions/{session:guid}/observe"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Observe(Guid session, ObserveRequest request, CancellationToken token)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await observers.JoinAsync(user, session, request.ProtocolVersion, token); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
    [HttpGet("sessions/{session:guid}/observers")]
    public async Task<IActionResult> Roster(Guid session, CancellationToken token)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await observers.RosterAsync(user, session, token); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
    [HttpDelete("sessions/{session:guid}/observe"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Leave(Guid session, CancellationToken token)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await observers.LeaveAsync(user, session, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
