using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

[ApiController, Authorize, Route("api/multiplayer/history")]
public sealed class SessionArchiveController(ISessionArchiveService archive, ICurrentUserService current) : ControllerBase
{
    [HttpGet("{session:guid}")]
    public async Task<IActionResult> Read(Guid session, CancellationToken token)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await archive.ReadAsync(id, session, false, token); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
    [HttpGet("admin/{session:guid}"), Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<IActionResult> AdminRead(Guid session, CancellationToken token)
    {
        if (current.UserId is not { } id) return Unauthorized();
        var result = await archive.ReadAsync(id, session, true, token); return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
