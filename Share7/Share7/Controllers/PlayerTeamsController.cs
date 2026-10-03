using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Social;
namespace Share7.API.Controllers;
public sealed record CreatePlayerTeamRequest(string RequestId);
public sealed record TeamInviteRequest(Guid UserId);
[ApiController, Authorize, Route("api/social/teams")]
public sealed class PlayerTeamsController(IPlayerTeamService teams, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => current.UserId is { } user ? Ok(await teams.ListAsync(user, token)) : Unauthorized();
    [HttpPost, EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(CreatePlayerTeamRequest request, CancellationToken token)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await teams.CreateAsync(user, request.RequestId, token); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
    [HttpPost("{team:guid}/invites"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Invite(Guid team, TeamInviteRequest request, CancellationToken token) => Run(user => teams.InviteAsync(user, team, request.UserId, token));
    [HttpPost("{team:guid}/accept"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Accept(Guid team, CancellationToken token) => Run(user => teams.AcceptAsync(user, team, token));
    [HttpDelete("{team:guid}/members/{target:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Remove(Guid team, Guid target, CancellationToken token) => Run(user => teams.RemoveAsync(user, team, target, token));
    [HttpDelete("{team:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Disband(Guid team, CancellationToken token) => Run(user => teams.DisbandAsync(user, team, token));
    private async Task<IActionResult> Run(Func<Guid, Task<ServiceResult>> call)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await call(user); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
