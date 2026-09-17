using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Share7.Application.Telemetry;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Telemetry;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The telemetry pipeline, against a real database.
/// <para>
/// **A regression suite, not a coverage exercise.** Every case here pins a rule whose failure is
/// silent: a clamp that stops clamping puts events in a cohort they do not belong to, a replay
/// guard that stops guarding doubles every count, and a privacy denylist that stops refusing puts
/// a child's identifier in a table that keeps it for ninety days. None of those break a build —
/// they produce numbers that look plausible and are wrong.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TelemetryPipelineTests
{
    private readonly SqlServerFixture _fixture;

    public TelemetryPipelineTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- ingest ------------------------------------------------------------------------------

    [Fact]
    public async Task Identity_is_stamped_from_the_caller_and_cannot_be_supplied()
    {
        // Rule 1. There is no user id on the request type at all, so the only thing this can assert
        // is that what lands is the caller's — but that absence is the guarantee, and a reflection
        // check below makes sure nobody adds a field to undo it.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await IngestAsync(context, userId, Batch(Event("session_start")));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Accepted);

        var stored = await context.TelemetryEvents.SingleAsync(e => e.UserId == userId);
        Assert.Equal(userId, stored.UserId);
    }

    [Fact]
    public void The_request_carries_no_identity_field()
    {
        // The contract, enforced by reflection so a later convenience cannot erode it — the same
        // device RunsController's own test uses for "no client-named amounts".
        var forbidden = new[] { "userid", "user", "username", "email", "token", "deviceid" };

        var properties = typeof(TelemetryBatchRequest).GetProperties()
            .Concat(typeof(TelemetryEventDto).GetProperties())
            .Concat(typeof(TelemetryContextDto).GetProperties())
            .Select(p => p.Name.ToLowerInvariant());

        foreach (var property in properties)
            Assert.DoesNotContain(property, forbidden);
    }

    [Fact]
    public async Task A_replayed_batch_stores_nothing_twice()
    {
        // The offline queue retries on reconnect BY DESIGN, so a replay is the ordinary path rather
        // than an anomaly. Without idempotency every dropped connection inflates every count.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var batch = Batch(Event("session_start"), Event("screen_view"));

        var first = await IngestAsync(context, userId, batch);
        var second = await IngestAsync(context, userId, batch);

        Assert.Equal(2, first.Value!.Accepted);
        Assert.Equal(0, second.Value!.Accepted);
        Assert.Equal(2, second.Value.Duplicates);

        Assert.Equal(2, await context.TelemetryEvents.CountAsync(e => e.UserId == userId));
    }

    [Fact]
    public async Task A_wrong_device_clock_is_clamped_rather_than_believed()
    {
        // The failure this prevents is silent and total: an unclamped 2019 timestamp lands the
        // event in a cohort that has nothing to do with the child who produced it, and every
        // retention figure that cohort feeds is then quietly wrong.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var ancient = Event("session_start");
        ancient.OccurredAtUtc = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var future = Event("screen_view");
        future.OccurredAtUtc = DateTime.UtcNow.AddYears(5);

        await IngestAsync(context, userId, Batch(ancient, future));

        var stored = await context.TelemetryEvents
            .Where(e => e.UserId == userId)
            .ToListAsync();

        var floor = DateTime.UtcNow.AddDays(-15);

        Assert.All(stored, e =>
        {
            Assert.True(e.OccurredAtUtc >= floor, "a backdated event was not clamped forward");
            Assert.True(e.OccurredAtUtc <= e.ReceivedAtUtc, "a future event was not clamped back");
        });
    }

    [Fact]
    public async Task An_identifier_shaped_parameter_is_refused_and_the_rest_of_the_batch_survives()
    {
        // Refused, not stripped: a silently dropped field is a gap discovered months later, by
        // which time the build that produced it is on a million devices.
        //
        // And the batch is NOT failed wholesale — one malformed event on a shipped build must not
        // block every event queued behind it, permanently, because the client would retry forever.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var leaky = Event("screen_view");
        leaky.Params = new Dictionary<string, JsonElement>
        {
            ["user_email"] = JsonDocument.Parse("\"child@example.test\"").RootElement
        };

        var clean = Event("session_start");

        var result = await IngestAsync(context, userId, Batch(leaky, clean));

        Assert.Equal(1, result.Value!.Accepted);
        Assert.Single(result.Value.Rejected);
        Assert.Equal(TelemetryRejectReasons.ForbiddenParam, result.Value.Rejected[0].Reason);

        Assert.Equal(1, await context.TelemetryEvents.CountAsync(e => e.UserId == userId));
    }

    [Fact]
    public void A_wallet_balance_is_refused_for_a_correctness_reason_not_a_privacy_one()
    {
        // The rule already written on CommerceAnalyticsEvents: a balance in this stream is a second
        // record of what a child owns, derived from a client that is not authoritative about it.
        Assert.False(TelemetryPrivacy.IsAllowedParameter("coin_balance", out _));
        Assert.False(TelemetryPrivacy.IsAllowedParameter("wallet", out _));

        // But the movement is fine — it describes the transaction, not the account.
        Assert.True(TelemetryPrivacy.IsAllowedParameter("amount", out _));
        Assert.True(TelemetryPrivacy.IsAllowedParameter("price", out _));
    }

    [Fact]
    public void The_denylist_matches_tokens_and_not_substrings()
    {
        // The first version of this matched substrings and refused `tournament` because it contains
        // `name`. A denylist that refuses correct events is one somebody eventually disables.
        Assert.True(TelemetryPrivacy.IsAllowedParameter("tournament", out _));
        Assert.True(TelemetryPrivacy.IsAllowedParameter("screen", out _));
        Assert.False(TelemetryPrivacy.IsAllowedParameter("user_name", out _));
        Assert.False(TelemetryPrivacy.IsAllowedParameter("name", out _));
    }

    [Fact]
    public async Task An_unregistered_name_is_stored_but_never_rolled_up()
    {
        // Rule 6, both halves. Losing data because a client shipped ahead of a registry row is the
        // worse failure; letting a typo create a permanent metric is the other one.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var name = $"never_registered_{Guid.NewGuid():N}"[..40];

        await IngestAsync(context, userId, Batch(Event(name)));

        var stored = await context.TelemetryEvents.SingleAsync(e => e.Name == name);
        Assert.True(stored.IsUnregistered);

        await ProjectAsync(context);

        Assert.False(await context.TelemetryDailyMetrics.AnyAsync(m => m.Name == name));

        // …and it surfaces for a human rather than disappearing.
        var schema = await context.TelemetryEventSchemas.SingleAsync(s => s.Name == name);
        Assert.NotNull(schema.FirstSeenAtUtc);
        Assert.False(schema.RollUpDaily);
    }

    // ---- projection --------------------------------------------------------------------------

    [Fact]
    public async Task The_projector_folds_sessions_days_and_lifecycle_in_one_pass()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var sessionId = Guid.NewGuid();

        var end = Event("session_end");
        end.Params = new Dictionary<string, JsonElement>
        {
            ["duration_s"] = JsonDocument.Parse("310").RootElement
        };

        await IngestAsync(
            context, userId,
            Batch(sessionId, Event("session_start"), Event("run_started"), Event("attempt_submitted"), end));

        await ProjectAsync(context);

        var session = await context.TelemetrySessions.SingleAsync(s => s.Id == sessionId);
        Assert.Equal(4, session.EventCount);
        Assert.NotNull(session.EndedAtUtc);

        var day = await context.TelemetryUserDays.SingleAsync(d => d.UserId == userId);
        Assert.Equal(1, day.SessionCount);
        Assert.Equal(1, day.RunCount);
        Assert.Equal(1, day.AttemptCount);

        // Day zero of their own cohort: DayIndex is what makes the retention query a group-by.
        Assert.Equal(0, day.DayIndex);
        Assert.Equal(day.DayUtc, day.FirstSeenDayUtc);

        var lifecycle = await context.TelemetryUserLifecycle.SingleAsync(l => l.UserId == userId);
        Assert.Equal(1, lifecycle.TotalSessions);
        Assert.Equal(4, lifecycle.TotalEvents);
    }

    [Fact]
    public async Task Re_projecting_the_same_events_does_not_double_count()
    {
        // The per-row LastSequence guard. Two app instances read the same watermark, and without
        // this both add the same batch — which is a doubling nothing else in the system would catch.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start"), Event("screen_view")));
        await ProjectAsync(context);

        var before = await context.TelemetryUserDays.SingleAsync(d => d.UserId == userId);
        var events = before.EventCount;

        // Rewind the cursor and run it again — exactly what a second instance would do.
        var checkpoint = await context.ProjectionCheckpoints
            .SingleAsync(c => c.Consumer == ProjectionConsumers.Telemetry);

        checkpoint.Watermark = 0;
        await context.SaveChangesAsync();

        await ProjectAsync(context);

        var after = await context.TelemetryUserDays.SingleAsync(d => d.UserId == userId);
        Assert.Equal(events, after.EventCount);
    }

    [Fact]
    public async Task The_projector_will_not_read_inside_the_safety_lag()
    {
        // The identity-gap guard. Sequence is an identity column, so a higher value can commit
        // before a lower one; a projector that watermarked at MAX(Sequence) would skip the
        // straggler permanently. The lag is what makes that impossible rather than unlikely.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start")));

        var folded = await ProjectAsync(context, safetyLagSeconds: 3600);

        Assert.Equal(0, folded);
        Assert.False(await context.TelemetryUserDays.AnyAsync(d => d.UserId == userId));
    }

    [Fact]
    public async Task The_install_cohort_never_moves_once_written()
    {
        // If it could move, every historical cohort a user belongs to would silently change size —
        // and last month's retention number would stop matching last month's report.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start")));
        await ProjectAsync(context);

        var cohort = (await context.TelemetryUserLifecycle.SingleAsync(l => l.UserId == userId))
            .CohortDayUtc;

        // A later batch, backdated as far as the clamp allows.
        var late = Event("screen_view");
        late.OccurredAtUtc = DateTime.UtcNow.AddDays(-10);

        await IngestAsync(context, userId, Batch(late));
        await ProjectAsync(context);

        var after = await context.TelemetryUserLifecycle.SingleAsync(l => l.UserId == userId);
        Assert.Equal(cohort, after.CohortDayUtc);
    }

    [Fact]
    public async Task A_sampled_event_is_scaled_back_up_in_the_daily_count()
    {
        // Otherwise turning sampling on reads downstream as a collapse in usage, and the series
        // can never be honestly compared across the change.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var sampled = Event("screen_view");
        sampled.SampleRate = 0.25;

        await IngestAsync(context, userId, Batch(sampled));
        await ProjectAsync(context);

        var metric = await context.TelemetryDailyMetrics
            .Where(m => m.Name == "screen_view" && m.Dimension == string.Empty)
            .OrderByDescending(m => m.DayUtc)
            .FirstAsync();

        Assert.Equal(4, metric.Count);
    }

    [Fact]
    public async Task Unique_users_stays_null_until_the_nightly_pass_computes_it()
    {
        // Null and zero are different answers. A zero here would claim nobody did the thing.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start")));
        await ProjectAsync(context);

        var metric = await context.TelemetryDailyMetrics
            .Where(m => m.Name == "session_start" && m.Dimension == string.Empty)
            .OrderByDescending(m => m.DayUtc)
            .FirstAsync();

        Assert.Null(metric.UniqueUsers);

        await Rollups(context).RunNightlyAsync(CancellationToken.None);

        await context.Entry(metric).ReloadAsync();

        // Non-null is the assertion, not an exact figure. Every test in this collection shares one
        // database and several of them emit session_start, so the honest count for the day is
        // "however many accounts those tests created" — pinning it to 1 would make this test fail
        // whenever an unrelated one is added, which is how a suite stops being trusted.
        Assert.NotNull(metric.UniqueUsers);
        Assert.True(metric.UniqueUsers >= 1);
    }

    [Fact]
    public async Task The_nightly_pass_builds_a_cohort_the_headline_can_read()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start")));
        await ProjectAsync(context);
        await Rollups(context).RunNightlyAsync(CancellationToken.None);

        var today = DateTime.UtcNow.Date;

        var cell = await context.TelemetryRetentionCohorts
            .SingleOrDefaultAsync(c => c.CohortDayUtc == today && c.DayIndex == 0);

        Assert.NotNull(cell);
        Assert.True(cell!.CohortSize >= 1);
        Assert.True(cell.RetainedUsers >= 1);
    }

    // ---- retention ---------------------------------------------------------------------------

    [Fact]
    public async Task The_sweep_never_deletes_an_event_the_projector_has_not_folded()
    {
        // Deleting an unfolded event loses it from every rollup permanently — no re-run fixes it,
        // because the source row is gone. So the sweep does less work while the projector is behind
        // rather than racing ahead of it.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start")));

        // Backdate past every retention window, but leave the watermark where it is.
        await context.TelemetryEvents
            .Where(e => e.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DayUtc, DateTime.UtcNow.Date.AddDays(-400)));

        var checkpoint = await context.ProjectionCheckpoints
            .SingleOrDefaultAsync(c => c.Consumer == ProjectionConsumers.Telemetry);

        if (checkpoint is not null)
        {
            checkpoint.Watermark = 0;
            await context.SaveChangesAsync();
        }

        var deleted = await new TelemetryRetentionService(
            context, Options(), NullLogger<TelemetryRetentionService>.Instance)
            .SweepAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.True(await context.TelemetryEvents.AnyAsync(e => e.UserId == userId));
    }

    [Fact]
    public async Task Deleting_an_account_takes_its_telemetry_with_it()
    {
        // Cascade rather than a manual purge, which is what keeps these tables out of
        // UserOwnedData.ManuallyPurged — and what makes an erasure request actually complete.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        await IngestAsync(context, userId, Batch(Event("session_start")));
        await ProjectAsync(context);

        var user = await context.Users.SingleAsync(u => u.Id == userId);
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        Assert.False(await context.TelemetryEvents.AnyAsync(e => e.UserId == userId));
        Assert.False(await context.TelemetryUserDays.AnyAsync(d => d.UserId == userId));
        Assert.False(await context.TelemetryUserLifecycle.AnyAsync(l => l.UserId == userId));
        Assert.False(await context.TelemetrySessions.AnyAsync(s => s.UserId == userId));
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static IOptions<TelemetryOptions> Options(int safetyLagSeconds = 0) =>
        Microsoft.Extensions.Options.Options.Create(new TelemetryOptions
        {
            // Zero by default so a test does not have to wait out the production lag. The one test
            // that cares about the lag sets it explicitly.
            SafetyLagSeconds = safetyLagSeconds,
            MaxBacklogDays = 14
        });

    private static async Task<Share7.Application.Common.Models.ServiceResult<TelemetryBatchResponse>>
        IngestAsync(ApplicationDbContext context, Guid userId, TelemetryBatchRequest request)
    {
        var schemas = new TelemetrySchemaService(context, new NoopSchemaCache());
        await schemas.SeedAsync(CancellationToken.None);

        var service = new TelemetryIngestService(
            context,
            new DirectSchemaCache(context),
            Options(),
            NullLogger<TelemetryIngestService>.Instance);

        return await service.IngestAsync(userId, request, CancellationToken.None);
    }

    private static TelemetryRollupService Rollups(ApplicationDbContext context, int safetyLagSeconds = 0) =>
        new(context, Options(safetyLagSeconds), NullLogger<TelemetryRollupService>.Instance);

    private static Task<int> ProjectAsync(ApplicationDbContext context, int safetyLagSeconds = 0) =>
        Rollups(context, safetyLagSeconds).ProjectAsync(CancellationToken.None);

    private static TelemetryBatchRequest Batch(params TelemetryEventDto[] events) =>
        Batch(Guid.NewGuid(), events);

    private static TelemetryBatchRequest Batch(Guid sessionId, params TelemetryEventDto[] events) =>
        new()
        {
            SessionId = sessionId,
            Context = new TelemetryContextDto
            {
                AppVersion = "1.0.0",
                Platform = "android",
                DeviceModel = "TEST-1",
                Locale = "en"
            },
            Events = [.. events]
        };

    private static TelemetryEventDto Event(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        OccurredAtUtc = DateTime.UtcNow,
        ClientSeq = 1,
        SampleRate = 1.0
    };

    /// <summary>Reads the registry straight from the context, so a test sees its own seed immediately.</summary>
    private sealed class DirectSchemaCache : ITelemetrySchemaCache
    {
        private readonly ApplicationDbContext _context;

        public DirectSchemaCache(ApplicationDbContext context) => _context = context;

        public async Task<IReadOnlyDictionary<string, TelemetryEventSchema>> GetAllAsync(
            CancellationToken cancellationToken) =>
            await _context.TelemetryEventSchemas
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Name, StringComparer.Ordinal, cancellationToken);

        public void Invalidate()
        {
        }
    }

    private sealed class NoopSchemaCache : ITelemetrySchemaCache
    {
        public Task<IReadOnlyDictionary<string, TelemetryEventSchema>> GetAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, TelemetryEventSchema>>(
                new Dictionary<string, TelemetryEventSchema>());

        public void Invalidate()
        {
        }
    }
}
