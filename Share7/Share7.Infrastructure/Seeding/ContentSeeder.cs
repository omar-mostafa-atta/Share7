using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <inheritdoc cref="IContentSeeder"/>
internal sealed class ContentSeeder : IContentSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILeaderboardRolloverService _rollover;
    private readonly ContentSeedOptions _options;
    private readonly ILogger<ContentSeeder> _logger;

    /// <summary>
    /// One run at a time, process-wide.
    /// <para>
    /// The admin endpoint can be called twice before the first call has committed, and two seeders
    /// racing would both see an empty table and both insert — the natural-key checks read before the
    /// other transaction writes, so they cannot arbitrate this. A gate is cheaper than making every
    /// one of forty insert paths idempotent under concurrency.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public ContentSeeder(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ILeaderboardRolloverService rollover,
        IOptions<ContentSeedOptions> options,
        ILogger<ContentSeeder> logger)
    {
        _db = db;
        _users = users;
        _rollover = rollover;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ContentSeedReport> SeedAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return new ContentSeedReport { Skipped = true };

        await Gate.WaitAsync(cancellationToken);

        try
        {
            var report = new ContentSeedReport();
            var clock = Stopwatch.StartNew();

            if (_options.Platform)
            {
                await new PlatformCatalogueSeeder(_db).SeedAsync(report, cancellationToken);

                // Boards are useless without an open window, and the rollover service is what opens
                // one. Calling it here rather than duplicating its window arithmetic keeps the
                // seeded boards on exactly the path every later board takes.
                report.LeaderboardCycles += await _rollover.RolloverAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            if (_options.Curriculum)
            {
                await new CurriculumSeeder(_db, _options).SeedAsync(report, cancellationToken);
                _db.ChangeTracker.Clear();
            }

            if (_options.DemoPlayers)
            {
                await new DemoPlayerSeeder(_db, _users, _options).SeedAsync(report, cancellationToken);
                _db.ChangeTracker.Clear();
            }

            report.Elapsed = clock.Elapsed;

            if (report.WroteAnything)
                _logger.LogInformation("Content seed wrote {Report}", report.ToString());
            else
                _logger.LogInformation("Content seed found nothing to do; the catalogues are already populated.");

            return report;
        }
        finally
        {
            Gate.Release();
        }
    }
}
