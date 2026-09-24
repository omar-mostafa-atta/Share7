using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Domain.Constants;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Engine.Reads;
using Share7.Infrastructure.Persistence;

namespace Share7.API.Controllers;

/// <summary>
/// The education engine's operational switches and their evidence — for whoever runs the platform,
/// not for authors.
/// <para>
/// <c>read-model</c> is where the switch-over to the node tree is decided (plan step A6): run
/// <c>Curriculum:ReadModel = Shadow</c>, watch every day's counts here, and once fourteen days in a
/// row show nothing differed and nothing failed, set it to <c>Generic</c>. Falling back is the same
/// one setting.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/engine")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminEngineController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly CurriculumReadTally _tally;
    private readonly IOptionsMonitor<CurriculumReadOptions> _options;
    private readonly UnlockRepairRunner _repairs;

    public AdminEngineController(
        ApplicationDbContext db,
        CurriculumReadTally tally,
        IOptionsMonitor<CurriculumReadOptions> options,
        UnlockRepairRunner repairs)
    {
        _db = db;
        _tally = tally;
        _options = options;
        _repairs = repairs;
    }

    /// <summary>
    /// Which tables serve the game's curriculum reads, and every day's shadow comparison — newest
    /// first, per read. Counts not yet written are written first, so this is current to the request.
    /// </summary>
    [HttpGet("read-model")]
    public async Task<IActionResult> ReadModel([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        await _tally.FlushAsync(_db, cancellationToken);

        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365)));

        var checks = await _db.CurriculumReadChecks
            .AsNoTracking()
            .Where(c => c.Day >= since)
            .OrderByDescending(c => c.Day)
            .ThenBy(c => c.ReadName)
            .ToListAsync(cancellationToken);

        var options = _options.CurrentValue;

        // The run the gate asks for: consecutive days, ending today, on which reads were compared
        // and none differed or failed.
        var byDay = checks.GroupBy(c => c.Day).ToDictionary(g => g.Key, g => g.ToList());
        var cleanDays = 0;
        for (var day = DateOnly.FromDateTime(DateTime.UtcNow); byDay.TryGetValue(day, out var rows); day = day.AddDays(-1))
        {
            if (rows.Sum(r => r.Compared) == 0 || rows.Any(r => r.Differed > 0 || r.Failed > 0)) break;
            cleanDays++;
        }

        return Ok(new
        {
            readModel = options.ReadModel.ToString(),
            shadowSampleRate = options.ShadowSampleRate,
            consecutiveCleanDays = cleanDays,
            checks = checks.Select(c => new
            {
                day = c.Day,
                read = c.ReadName,
                compared = c.Compared,
                differed = c.Differed,
                failed = c.Failed,
                lastDifferenceAtUtc = c.LastDifferenceAtUtc,
                lastDifferenceSample = c.LastDifferenceSample
            })
        });
    }

    /// <summary>Unlock repairs still to do or given up on — normally empty within seconds of a change.</summary>
    [HttpGet("unlock-repairs")]
    public async Task<IActionResult> UnlockRepairs(CancellationToken cancellationToken) =>
        Ok(await _db.UnlockRepairJobs
            .AsNoTracking()
            .Where(j => j.CompletedAtUtc == null)
            .OrderBy(j => j.CreatedAtUtc)
            .Take(200)
            .Select(j => new
            {
                j.Id,
                kind = j.Kind.ToString(),
                j.NodeId,
                j.CreatedAtUtc,
                j.Attempts,
                j.LastError
            })
            .ToListAsync(cancellationToken));

    /// <summary>Works the unlock repair queue now instead of at the next sweep. Safe to repeat.</summary>
    [HttpPost("unlock-repairs/run")]
    public async Task<IActionResult> RunUnlockRepairs(CancellationToken cancellationToken) =>
        Ok(new { completed = await _repairs.RunPendingAsync(cancellationToken) });
}
