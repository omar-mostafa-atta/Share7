using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <inheritdoc cref="IContentSeeder"/>
internal sealed class ContentSeeder : IContentSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILeaderboardRolloverService _rollover;
    private readonly ILeaderboardSettlementService _settlement;
    private readonly IPlayEventAdminService _events;
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
        ILeaderboardSettlementService settlement,
        IPlayEventAdminService events,
        IOptions<ContentSeedOptions> options,
        ILogger<ContentSeeder> logger)
    {
        _db = db;
        _users = users;
        _rollover = rollover;
        _settlement = settlement;
        _events = events;
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

                // The seeder writes content the way it was always written — into the typed tables and
                // the old recovery table. The engine's backfill then gives it everything the engine
                // needs: nodes, the recovery pool in the item bank, served-set versions. The same SQL
                // the EngineAuthoritative migration runs, and just as re-runnable.
                //
                // And, like the migration, it needs longer than the provider's thirty seconds. On a
                // database with real content this is minutes of work; at the default it times out
                // part way through, and because it runs on the path to the first request, the whole
                // host fails to start. The migration was given an hour in Program.cs for exactly
                // this reason — the seeder reaches the same SQL by a second road.
                var was = _db.Database.GetCommandTimeout();
                _db.Database.SetCommandTimeout(TimeSpan.FromHours(1));
                try
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        Share7.Infrastructure.Persistence.Migrations.EngineBackfill.Sql, cancellationToken);
                }
                finally
                {
                    _db.Database.SetCommandTimeout(was);
                }
            }

            // Events either side of the demo players: created first so the players' ranked entries
            // land on the new ladders, and settled after so last week's cup has somebody to pay.
            var demoEvents = _options.DemoEvents
                ? new DemoEventSeeder(_db, _events, _rollover, _settlement, _logger)
                : null;

            if (demoEvents is not null)
                await demoEvents.SeedAsync(report, cancellationToken);

            if (_options.DemoPlayers)
            {
                await new DemoPlayerSeeder(_db, _users, _options).SeedAsync(report, cancellationToken);
                _db.ChangeTracker.Clear();
            }

            if (demoEvents is not null)
                await demoEvents.SettleFinishedAsync(report, cancellationToken);

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
