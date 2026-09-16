using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Constants;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// Creates a set of competitions covering every state the event screens have to draw: one running
/// all week, one ending tonight, one scheduled for next week with a real-world prize, and one that
/// finished last week and has already paid its winners.
/// <para>
/// <b>Authored through <see cref="IPlayEventAdminService"/>, not written as rows.</b> An event is
/// three objects created as one — the event, its board and that board's single cycle — plus reward
/// rules for its prize table, and the admin service is the one place that knows how. Writing them
/// here by hand would be a second implementation of the invariants the console already enforces,
/// and a demo event that the console itself would refuse is not a useful thing to test against.
/// </para>
/// <para>
/// <b>Windows are calendar-aligned so re-running is a no-op.</b> The weekly events take the ISO week
/// and the daily one the UTC date, and both are in the key. Restarting inside the same week finds
/// every key already there; restarting next week creates next week's set, and last week's weekly
/// event has closed on its own schedule rather than being left open by a stale seed.
/// </para>
/// <para>
/// <b>The finished cup is settled here, on purpose.</b> Its window is already over when it is
/// created, and settlement would otherwise wait on the maintenance endpoint nobody calls on a
/// laptop. It goes through the ordinary settlement service, so its awards, the real-world claim and
/// the reward-engine payments are exactly the ones a real event produces — which is what lets the
/// "you won" popup and the prize-claim queue be tested at all.
/// </para>
/// <para>
/// <b>Off unless an environment asks for it,</b> like the demo players it ranks.
/// </para>
/// </summary>
internal sealed class DemoEventSeeder
{
    /// <summary>Every key this seeder writes starts with this, so it only ever settles its own events.</summary>
    private const string KeyPrefix = "demo.";

    private const string GameKey = "game.runner";
    private const string ModeKey = "runner.mode.classic";
    private const string DesertWorld = "runner.env.desert";
    private const string ForestWorld = "runner.env.forest";

    private readonly ApplicationDbContext _db;
    private readonly IPlayEventAdminService _events;
    private readonly ILeaderboardRolloverService _rollover;
    private readonly ILeaderboardSettlementService _settlement;
    private readonly ILogger _logger;

    public DemoEventSeeder(
        ApplicationDbContext db,
        IPlayEventAdminService events,
        ILeaderboardRolloverService rollover,
        ILeaderboardSettlementService settlement,
        ILogger logger)
    {
        _db = db;
        _events = events;
        _rollover = rollover;
        _settlement = settlement;
        _logger = logger;
    }

    /// <summary>
    /// Creates whichever of this period's demo events do not exist yet. Run before the demo players,
    /// so their ranked entries land on the new ladders too.
    /// </summary>
    public async Task SeedAsync(ContentSeedReport report, CancellationToken ct)
    {
        var game = await _db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.GameKey == GameKey, ct);

        var mode = game is null
            ? null
            : await _db.GameModes.AsNoTracking()
                .FirstOrDefaultAsync(m => m.GameId == game.Id && m.ModeKey == ModeKey, ct);

        if (game is null || mode is null)
        {
            _logger.LogWarning(
                "Demo events skipped: {Game} or its {Mode} mode is not in the catalogue.", GameKey, ModeKey);
            return;
        }

        var worlds = await _db.GameWorlds.AsNoTracking()
            .Where(w => w.GameId == game.Id)
            .Select(w => w.WorldKey)
            .ToListAsync(ct);

        foreach (var request in Plan(game.Id, mode.Id, worlds, DateTime.UtcNow))
        {
            if (await _db.PlayEvents.AnyAsync(e => e.EventKey == request.EventKey, ct)) continue;

            // Guid.Empty as the author: nobody created these, and a made-up account id would send
            // the console looking for a user that does not exist.
            var created = await _events.CreateAsync(request, Guid.Empty, ct);
            _db.ChangeTracker.Clear();

            if (created.Succeeded)
                report.PlayEvents++;
            else
                _logger.LogWarning(
                    "Demo event {EventKey} was refused: {Errors}",
                    request.EventKey, string.Join(" ", created.Errors));
        }
    }

    /// <summary>
    /// Closes and settles demo events whose window is over. Run after the demo players, so the
    /// finished cup has a ladder to pay.
    /// </summary>
    public async Task SettleFinishedAsync(ContentSeedReport report, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var due = await _db.PlayEvents.AsNoTracking()
            .Where(e => e.EventKey.StartsWith(KeyPrefix)
                        && e.Cycle!.EndsAtUtc <= now
                        && e.Cycle.State != LeaderboardCycleState.Settled)
            .Select(e => new { e.EventKey, e.CycleId })
            .ToListAsync(ct);

        if (due.Count == 0) return;

        // Open -> Closed is the rollover's transition, not this seeder's. It also queues the settle
        // job, which will find the cycle already settled and succeed without doing anything.
        await _rollover.RolloverAsync(ct);
        _db.ChangeTracker.Clear();

        foreach (var cycle in due)
        {
            // A settlement that throws is logged rather than rethrown. This runs on the startup path,
            // and demo data failing to pay is not a reason for the API a developer is testing
            // against to refuse to start — the queued settle job retries it through the ordinary
            // path, with its error recorded on the job.
            try
            {
                var settled = await _settlement.SettleAsync(cycle.CycleId, ct);

                if (settled.Succeeded)
                    report.PlayEventsSettled++;
                else
                    _logger.LogWarning(
                        "Demo event {EventKey} could not be settled: {Errors}",
                        cycle.EventKey, string.Join(" ", settled.Errors));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Demo event {EventKey} threw while settling.", cycle.EventKey);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    private static IEnumerable<SavePlayEventRequest> Plan(
        Guid gameId, Guid modeId, IReadOnlyCollection<string> worlds, DateTime now)
    {
        var desert = worlds.Contains(DesertWorld) ? DesertWorld : null;
        var forest = worlds.Contains(ForestWorld) ? ForestWorld : desert;

        var thisWeek = WeekStarting(now);
        var today = now.Date;

        yield return new SavePlayEventRequest
        {
            EventKey = $"{KeyPrefix}runner.weekly.{WeekKey(thisWeek)}",
            GameId = gameId,
            ModeId = modeId,
            WorldKey = desert,
            Metric = LeaderboardMetrics.CorrectAnswers,
            Aggregation = "sum",
            StartsAtUtc = thisWeek,
            EndsAtUtc = thisWeek.AddDays(7),
            PrizeCohort = "all",
            MaxEntriesPerDay = 10,
            SortOrder = 0,
            Translations =
            [
                Name(LanguageIds.English,
                    "Weekly Brain Race",
                    "Answer as many questions as you can this week.",
                    "Play Classic runs in the Desert. Every correct answer adds to your total for the week. " +
                    "You can enter up to 10 times a day. The race closes on Monday at 00:00 UTC, and " +
                    "prizes are handed out once the results are final."),
                Name(LanguageIds.Arabic,
                    "سباق العقول الأسبوعي",
                    "أجب عن أكبر عدد ممكن من الأسئلة هذا الأسبوع.",
                    "العب جولات الوضع الكلاسيكي في الصحراء. كل إجابة صحيحة تُضاف إلى مجموعك لهذا الأسبوع. " +
                    "يمكنك المشاركة حتى 10 مرات في اليوم. ينتهي السباق يوم الاثنين الساعة 00:00 بتوقيت UTC، " +
                    "وتُمنح الجوائز بعد اعتماد النتائج.")
            ],
            PrizeTiers =
            [
                Coins(1, 1, 1000, 50, "1,000 coins and 50 gems", "1000 عملة و50 جوهرة"),
                Coins(2, 3, 500, 0, "500 coins", "500 عملة"),
                Coins(4, 10, 150, 0, "150 coins", "150 عملة")
            ]
        };

        yield return new SavePlayEventRequest
        {
            EventKey = $"{KeyPrefix}runner.daily.{today:yyyy-MM-dd}",
            GameId = gameId,
            ModeId = modeId,
            WorldKey = forest,
            Metric = LeaderboardMetrics.CorrectAnswers,
            Aggregation = "best",
            StartsAtUtc = today,
            EndsAtUtc = today.AddDays(1),
            PrizeCohort = "grade",
            MaxEntriesPerDay = 3,
            SortOrder = 10,
            Translations =
            [
                Name(LanguageIds.English,
                    "Forest Sprint",
                    "Your best run today is the one that counts.",
                    "Play Classic runs in the Forest. Only your best run of the day counts, so make it a " +
                    "good one. You can enter 3 times today. Everyone is ranked against their own grade."),
                Name(LanguageIds.Arabic,
                    "انطلاقة الغابة",
                    "أفضل جولة لك اليوم هي التي تُحتسب.",
                    "العب جولات الوضع الكلاسيكي في الغابة. تُحتسب أفضل جولة لك اليوم فقط، فاجعلها مميزة. " +
                    "يمكنك المشاركة 3 مرات اليوم. يُرتَّب كل لاعب مع زملاء صفّه.")
            ],
            PrizeTiers =
            [
                Coins(1, 1, 300, 0, "300 coins", "300 عملة"),
                Coins(2, 5, 100, 0, "100 coins", "100 عملة")
            ]
        };

        // The same cup twice: next week's, still scheduled, and last week's, already over. The
        // week in the key is what keeps the two apart, and what makes next week's copy the one that
        // opens on Monday.
        yield return Cup(gameId, modeId, forest, thisWeek.AddDays(7), sortOrder: 20);
        yield return Cup(gameId, modeId, forest, thisWeek.AddDays(-7), sortOrder: 30);
    }

    /// <summary>
    /// A week-long cup whose first prize is real. Free to enter, as every event with a real-world
    /// prize has to be.
    /// </summary>
    private static SavePlayEventRequest Cup(
        Guid gameId, Guid modeId, string? world, DateTime weekStart, int sortOrder) => new()
    {
        EventKey = $"{KeyPrefix}runner.cup.{WeekKey(weekStart)}",
        GameId = gameId,
        ModeId = modeId,
        WorldKey = world,
        Metric = LeaderboardMetrics.CorrectAnswers,
        Aggregation = "sum",
        StartsAtUtc = weekStart,
        EndsAtUtc = weekStart.AddDays(7),
        PrizeCohort = "all",
        MaxEntriesPerDay = 5,
        SortOrder = sortOrder,
        Translations =
        [
            Name(LanguageIds.English,
                "Grand Cup",
                "A week-long cup with a real prize for first place.",
                "Play Classic runs in the Forest. Every correct answer adds to your total. You can enter " +
                "up to 5 times a day. First place wins a real prize, and a grown-up at home will be " +
                "contacted to arrange it. Nothing is ever asked of you in the app."),
            Name(LanguageIds.Arabic,
                "الكأس الكبرى",
                "كأس تستمر أسبوعًا كاملًا بجائزة حقيقية للمركز الأول.",
                "العب جولات الوضع الكلاسيكي في الغابة. كل إجابة صحيحة تُضاف إلى مجموعك. يمكنك المشاركة " +
                "حتى 5 مرات في اليوم. يفوز صاحب المركز الأول بجائزة حقيقية، وسيتم التواصل مع أحد الكبار " +
                "في المنزل لترتيب استلامها. لن يُطلب منك أي شيء داخل التطبيق.")
        ],
        PrizeTiers =
        [
            new SaveEventPrizeTierRequest
            {
                FromRank = 1,
                ToRank = 1,
                Kind = "real_world",
                DeclaredValueMinor = 800_000,
                ValueCurrencyCode = "EGP",
                Quantity = 1,
                SortOrder = 0,
                Translations =
                [
                    Title(LanguageIds.English, "A tablet", "Delivered to a grown-up at home."),
                    Title(LanguageIds.Arabic, "جهاز لوحي", "يُسلَّم إلى أحد الكبار في المنزل.")
                ]
            },
            Coins(2, 10, 400, 0, "400 coins", "400 عملة")
        ]
    };

    private static SaveEventPrizeTierRequest Coins(
        int fromRank, int toRank, long coins, long gems, string english, string arabic)
    {
        var tier = new SaveEventPrizeTierRequest
        {
            FromRank = fromRank,
            ToRank = toRank,
            Kind = "in_game",
            SortOrder = fromRank,
            Grants = [new EventPrizeGrantRequest { Currency = "coins", Amount = coins }],
            Translations =
            [
                Title(LanguageIds.English, english, string.Empty),
                Title(LanguageIds.Arabic, arabic, string.Empty)
            ]
        };

        if (gems > 0)
            tier.Grants.Add(new EventPrizeGrantRequest { Currency = "gems", Amount = gems });

        return tier;
    }

    private static PlayEventTranslationRequest Name(Guid langId, string name, string description, string rules) =>
        new() { LangId = langId, Name = name, Description = description, Rules = rules };

    private static EventPrizeTierTranslationRequest Title(Guid langId, string title, string description) =>
        new() { LangId = langId, Title = title, Description = description };

    /// <summary>Monday 00:00 UTC of the ISO week containing <paramref name="now"/>.</summary>
    private static DateTime WeekStarting(DateTime now) =>
        DateTime.SpecifyKind(
            ISOWeek.ToDateTime(ISOWeek.GetYear(now), ISOWeek.GetWeekOfYear(now), DayOfWeek.Monday),
            DateTimeKind.Utc);

    /// <summary><c>2026w38</c> — the ISO week-numbering year and week, which is what the console's duplicate action increments.</summary>
    private static string WeekKey(DateTime weekStart) =>
        $"{ISOWeek.GetYear(weekStart)}w{ISOWeek.GetWeekOfYear(weekStart):D2}";
}
