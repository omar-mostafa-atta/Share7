using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.BrainPass;
using Share7.Application.Common.Interfaces;
using Share7.Application.Common.Models;
using Share7.Domain.BrainPass;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

[ApiController, Authorize, Route("api/brain-pass")]
public sealed class BrainPassController(IBrainPassService pass, ICurrentUserService current) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken token)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await pass.CurrentAsync(user, token);
        return result.Succeeded ? Ok(new { available = result.Value is not null, season = result.Value }) : result.ToApiErrorResult();
    }
    [HttpGet("{season:guid}")]
    public Task<IActionResult> Read(Guid season, CancellationToken token) => Run(id => pass.ReadAsync(id, season, token));
    [HttpPost("{season:guid}/tiers/{tier:int}/{track}/claim"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Claim(Guid season, int tier, BrainPassTrack track, CancellationToken token) => Run(id => pass.ClaimAsync(id, season, tier, track, token));
    private async Task<IActionResult> Run<T>(Func<Guid, Task<ServiceResult<T>>> call)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await call(id); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}

[ApiController, Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin), Route("api/admin/brain-pass")]
public sealed class AdminBrainPassController(IBrainPassAdminService pass) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await pass.ListAsync(token));
    [HttpPost, EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(BrainPassSeasonInput request, CancellationToken token) => Answer(await pass.SaveAsync(null, request, token));
    [HttpPut("{season:guid}"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Save(Guid season, BrainPassSeasonInput request, CancellationToken token) => Answer(await pass.SaveAsync(season, request, token));
    [HttpPost("{season:guid}/publish"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Publish(Guid season, SeasonVersionRequest request, CancellationToken token) => Answer(await pass.PublishAsync(season, request.ExpectedVersion, token));
    [HttpPost("{season:guid}/disable"), EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Disable(Guid season, CancellationToken token)
    {
        var result = await pass.DisableAsync(season, token); return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
    private IActionResult Answer<T>(ServiceResult<T> result) => result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
}
public sealed record SeasonVersionRequest(int ExpectedVersion);
