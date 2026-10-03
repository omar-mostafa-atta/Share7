using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Common.Interfaces;
using Share7.Application.Multiplayer.Interfaces;

namespace Share7.API.Controllers;

[ApiController, Authorize, Route("api/multiplayer/rooms")]
public sealed class PublicRoomsController(IPublicRoomDirectory directory, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(int protocolVersion, long after = 0, Guid? gameId = null, CancellationToken token = default)
    {
        if (current.UserId is not { } user) return Unauthorized();
        var result = await directory.ReadAsync(user, protocolVersion, after, gameId, token);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
