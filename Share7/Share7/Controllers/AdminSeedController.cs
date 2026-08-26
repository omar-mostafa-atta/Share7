using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Admin.Interfaces;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Runs the content seeder on demand.
/// <para>
/// <b>Why an endpoint and not just a startup hook.</b> Seeding a fresh deployment means writing six
/// figures of rows; doing that on the startup path holds the first request behind it and, on a host
/// that recycles app pools, can be interrupted halfway and restarted from the top on every recycle.
/// An explicit call is bounded, observable, and repeatable — the seeder is idempotent, so a retry
/// after a timeout resumes rather than duplicates.
/// </para>
/// <para>
/// <b>Guarded three ways.</b> Admin or Super Admin only; refused unless <c>ContentSeed:Enabled</c>
/// is set for the environment; and serialised process-wide by the seeder itself, so a double-tap
/// cannot produce two concurrent runs.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/seed")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminSeedController : ControllerBase
{
    private readonly IContentSeeder _seeder;

    public AdminSeedController(IContentSeeder seeder) => _seeder = seeder;

    /// <summary>
    /// Seeds everything the configuration enables and returns what was written.
    /// <para>
    /// Returns 409 when the seeder is switched off, rather than 200 with an empty report: "nothing
    /// to do" and "not allowed to do anything" are different answers and an operator watching this
    /// call needs to tell them apart.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ContentSeedReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Seed(CancellationToken cancellationToken)
    {
        var report = await _seeder.SeedAsync(cancellationToken);

        if (report.Skipped)
        {
            return Conflict(new
            {
                error = "SEEDING_DISABLED",
                message = "Set ContentSeed:Enabled to true for this environment before seeding."
            });
        }

        return Ok(report);
    }
}
