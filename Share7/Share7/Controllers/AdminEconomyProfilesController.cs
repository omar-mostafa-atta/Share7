using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Play.Interfaces;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Pricing profiles: how much of what a session earns is actually paid.
/// <para>
/// A mode or an event points at one, and the platform default is what everything that names none
/// settles under — which is why the default cannot be deleted or quietly unset.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/economy-profiles")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminEconomyProfilesController : ControllerBase
{
    private readonly IEconomyProfileAdminService _profiles;

    public AdminEconomyProfilesController(IEconomyProfileAdminService profiles) => _profiles = profiles;

    /// <summary>Every profile, default first, each with how many modes and events use it.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await _profiles.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        SaveEconomyProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _profiles.CreateAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    [HttpPut("{profileId:guid}")]
    public async Task<IActionResult> Update(
        Guid profileId, SaveEconomyProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _profiles.UpdateAsync(profileId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>Refused while anything settles under it, and refused for the platform default.</summary>
    [HttpDelete("{profileId:guid}")]
    public async Task<IActionResult> Delete(Guid profileId, CancellationToken cancellationToken)
    {
        var result = await _profiles.DeleteAsync(profileId, cancellationToken);

        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
