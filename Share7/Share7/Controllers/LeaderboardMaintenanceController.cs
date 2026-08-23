using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;

namespace Share7.API.Controllers;

/// <summary>
/// The externally-pingable way to drive deferred leaderboard work.
/// <para>
/// **This exists because shared IIS cannot be trusted to run a background loop.** App pools
/// recycle and idle workers are shut down, so an in-process timer is a hope rather than a
/// schedule. An uptime pinger or a cron job hitting this endpoint every minute is the one
/// mechanism that keeps running when the process does not, and it costs nothing when there is no
/// work: the job table comes back empty and the request returns immediately.
/// </para>
/// <para>
/// Authorised by a shared key rather than a bearer token because the caller is a machine with no
/// account. The key is compared in fixed time and the endpoint reports nothing about why it
/// refused — an unauthenticated route that distinguishes "wrong key" from "no key" is an oracle.
/// </para>
/// </summary>
[ApiController]
[Route("api/leaderboards/maintenance")]
[AllowAnonymous]
public class LeaderboardMaintenanceController : ControllerBase
{
    private readonly ILeaderboardJobRunner _jobs;
    private readonly ILeaderboardRolloverService _rollover;
    private readonly LeaderboardOptions _options;

    public LeaderboardMaintenanceController(
        ILeaderboardJobRunner jobs,
        ILeaderboardRolloverService rollover,
        IOptions<LeaderboardOptions> options)
    {
        _jobs = jobs;
        _rollover = rollover;
        _options = options.Value;
    }

    /// <summary>
    /// Rolls cycles over and drains the job queue. Safe to call as often as you like and safe to
    /// call from several pingers at once — work is claimed under a lease, so two callers cannot run
    /// the same job.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Run(
        [FromHeader(Name = "X-Maintenance-Key")] string? key,
        [FromQuery] int maxJobs = 5,
        CancellationToken cancellationToken = default)
    {
        // No key configured means the endpoint is closed, not open. An unset secret must never
        // degrade into an unauthenticated write surface.
        if (string.IsNullOrWhiteSpace(_options.MaintenanceKey) || !Matches(key))
            return Unauthorized();

        if (!_options.Enabled)
            return Ok(new { enabled = false, rolledOver = 0, jobsCompleted = 0 });

        var rolledOver = await _rollover.RolloverAsync(cancellationToken);
        var completed = await _jobs.RunDueAsync(Math.Clamp(maxJobs, 1, 50), cancellationToken);

        return Ok(new { enabled = true, rolledOver, jobsCompleted = completed });
    }

    private bool Matches(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(_options.MaintenanceKey!));
    }
}
