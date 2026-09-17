using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Admin.Interfaces;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Platform counters for the admin console's landing page.
/// </summary>
[ApiController]
[Route("api/admin/overview")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminOverviewController : ControllerBase
{
    private readonly IAdminOverviewService _overviewService;

    public AdminOverviewController(IAdminOverviewService overviewService)
    {
        _overviewService = overviewService;
    }

    /// <summary>
    /// One request for every figure on the dashboard.
    /// </summary>
    /// <remarks>
    /// The alternative — the console fetching every list endpoint and measuring the
    /// arrays — makes the admin panel the most expensive client of its own API, and
    /// gets linearly worse as the catalogue grows.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await _overviewService.GetAsync(cancellationToken));
}
