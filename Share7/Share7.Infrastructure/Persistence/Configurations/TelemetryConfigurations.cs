using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// The telemetry tables.
/// <para>
/// **Indexing here is a budget, not a wish list.** <c>TelemetryEvents</c> is the hottest write
/// path in the platform and takes on the order of 10^8 rows a week at a million DAU; every index
/// on it is paid for on every insert. So it carries exactly four — the idempotency key, the
/// projector's cursor, the per-user trace, and the retention sweep — and nothing that exists to
/// make a dashboard convenient. Dashboards read the rollups, which is the whole reason they exist.
/// See <c>Docs/AnalyticsArchitecture.md</c> → Rule 4.
/// </para>
/// </summary>
public class TelemetryEventConfiguration : IEntityTypeConfiguration<TelemetryEvent>
{
    public void Configure(EntityTypeBuilder<TelemetryEvent> builder)
    {
        builder.ToTable("TelemetryEvents");

        // Clustered on the insertion sequence rather than on the Guid id. A Guid primary key here
        // would make every insert a page split somewhere in the middle of the largest table in the
        // system; a monotonic bigint appends. The id is still unique — as a separate non-clustered
        // index below, which is where idempotency actually needs it.
        builder.HasKey(e => e.Sequence);
        builder.Property(e => e.Sequence).ValueGeneratedOnAdd();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(TelemetryNames.MaxNameLength);
        builder.Property(e => e.AppVersion).IsRequired().HasMaxLength(32);
        builder.Property(e => e.Platform).IsRequired().HasMaxLength(TelemetryPlatforms.MaxLength);
        builder.Property(e => e.DeviceModel).HasMaxLength(64);
        builder.Property(e => e.Locale).HasMaxLength(16);

        // Bounded rather than nvarchar(max). A payload cap is what stops one broken client from
        // making a row cost a kilobyte, and it is enforced at ingest with the same number so the
        // refusal is a clear rejection reason rather than a truncation nobody notices.
        builder.Property(e => e.ParamsJson).IsRequired().HasMaxLength(2048);

        builder.Property(e => e.Category)
            .HasConversion(EnumWire.Converter<TelemetryCategory>())
            .HasMaxLength(16)
            .IsRequired();

        // Date-only, so a stray time-of-day cannot split one day across two rollup keys — the same
        // reason DailySignalLedger stores it this way.
        builder.Property(e => e.DayUtc).HasColumnType("date");

        // **Idempotency.** The client's offline queue retries on reconnect by design, so the same
        // event arriving twice is the ordinary path. Unique on (UserId, Id) rather than Id alone:
        // the id is client-generated, and one client's Guid collision must not be able to suppress
        // another account's event.
        builder.HasIndex(e => new { e.UserId, e.Id })
            .IsUnique()
            .HasDatabaseName("UX_TelemetryEvent_Idem");

        // The per-user trace, and the only reason this table is queried by user at all. Descending
        // on time because every read of it is "most recent first".
        builder.HasIndex(e => new { e.UserId, e.ReceivedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TelemetryEvent_User");

        // The retention sweep, and the event explorer's day-scoped reads. Name second so a sweep
        // for one schema's rows within a day is a seek rather than a scan of the day.
        builder.HasIndex(e => new { e.DayUtc, e.Name })
            .HasDatabaseName("IX_TelemetryEvent_Day");

        // Sessionisation reads every event of one session in client order.
        builder.HasIndex(e => new { e.SessionId, e.ClientSeq })
            .HasDatabaseName("IX_TelemetryEvent_Session");

        // No index on GameId or RunId. Both are read only through a user or a day that is already
        // narrowed by one of the above, and an index on the write path has to earn its cost.

        // Cascade: a child's raw events go with the account, exactly like Runs — which is what
        // keeps this table out of UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // **GameId is deliberately not a foreign key.** It is a dimension, and telemetry has to
        // stay readable after a retired game is deleted from the catalogue — the same argument
        // Run.SessionId is documented with. RunId likewise: a run is swept long before the trace is.
    }
}

public class TelemetrySessionConfiguration : IEntityTypeConfiguration<TelemetrySession>
{
    public void Configure(EntityTypeBuilder<TelemetrySession> builder)
    {
        builder.ToTable("TelemetrySessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.AppVersion).IsRequired().HasMaxLength(32);
        builder.Property(s => s.Platform).IsRequired().HasMaxLength(TelemetryPlatforms.MaxLength);
        builder.Property(s => s.DayUtc).HasColumnType("date");

        // "How many sessions did this child have, and how long were they" — the user-360 read.
        builder.HasIndex(s => new { s.UserId, s.StartedAtUtc })
            .HasDatabaseName("IX_TelemetrySession_User");

        // Daily session counts and average length, per day.
        builder.HasIndex(s => s.DayUtc).HasDatabaseName("IX_TelemetrySession_Day");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TelemetryUserDayConfiguration : IEntityTypeConfiguration<TelemetryUserDay>
{
    public void Configure(EntityTypeBuilder<TelemetryUserDay> builder)
    {
        builder.ToTable("TelemetryUserDays");

        // Composite natural key. The projector's read is a primary-key lookup and the row's
        // existence *is* "this user was active that day" — the same shape DailyCurrencyLedger uses,
        // and for the same reason: an identity column here would let two rows count one day.
        builder.HasKey(d => new { d.UserId, d.DayUtc });

        builder.Property(d => d.DayUtc).HasColumnType("date");
        builder.Property(d => d.FirstSeenDayUtc).HasColumnType("date");

        // **The retention query, and the only index that matters on this table.** The nightly
        // cohort pass groups by exactly this pair; without it the pass is a full scan of the
        // largest rollup in the system every night.
        builder.HasIndex(d => new { d.FirstSeenDayUtc, d.DayIndex })
            .HasDatabaseName("IX_TelemetryUserDay_Cohort");

        // DAU for a day, and the day range every dashboard opens with.
        builder.HasIndex(d => d.DayUtc).HasDatabaseName("IX_TelemetryUserDay_Day");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TelemetryUserLifecycleConfiguration : IEntityTypeConfiguration<TelemetryUserLifecycle>
{
    public void Configure(EntityTypeBuilder<TelemetryUserLifecycle> builder)
    {
        builder.ToTable("TelemetryUserLifecycle");
        builder.HasKey(l => l.UserId);

        builder.Property(l => l.CohortDayUtc).HasColumnType("date");
        builder.Property(l => l.InstallAppVersion).IsRequired().HasMaxLength(32);
        builder.Property(l => l.InstallPlatform).IsRequired().HasMaxLength(TelemetryPlatforms.MaxLength);
        builder.Property(l => l.LastAppVersion).IsRequired().HasMaxLength(32);
        builder.Property(l => l.LastPlatform).IsRequired().HasMaxLength(TelemetryPlatforms.MaxLength);

        // Cohort sizes, the denominator of every retention percentage.
        builder.HasIndex(l => l.CohortDayUtc).HasDatabaseName("IX_TelemetryLifecycle_Cohort");

        // Churn queries: everyone last seen before a date.
        builder.HasIndex(l => l.LastSeenAtUtc).HasDatabaseName("IX_TelemetryLifecycle_LastSeen");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TelemetryDailyMetricConfiguration : IEntityTypeConfiguration<TelemetryDailyMetric>
{
    public void Configure(EntityTypeBuilder<TelemetryDailyMetric> builder)
    {
        builder.ToTable("TelemetryDailyMetrics");

        // All four parts, because the ungrouped total and every split share one table and are told
        // apart only by Dimension being empty. Empty string rather than null throughout — see the
        // entity, and note that a nullable key part here would need HasFilter(null) to constrain
        // anything, the trap SignalValuation documents.
        builder.HasKey(m => new { m.DayUtc, m.Name, m.Dimension, m.DimensionValue });

        builder.Property(m => m.DayUtc).HasColumnType("date");
        builder.Property(m => m.Name).HasMaxLength(TelemetryNames.MaxNameLength);
        builder.Property(m => m.Dimension).HasMaxLength(TelemetryDimensions.MaxLength);
        builder.Property(m => m.DimensionValue).HasMaxLength(64);

        // A trend line for one event across a range — the shape every chart in the console asks for.
        builder.HasIndex(m => new { m.Name, m.DayUtc }).HasDatabaseName("IX_TelemetryMetric_Name");
    }
}

public class TelemetryRetentionCohortConfiguration : IEntityTypeConfiguration<TelemetryRetentionCohort>
{
    public void Configure(EntityTypeBuilder<TelemetryRetentionCohort> builder)
    {
        builder.ToTable("TelemetryRetentionCohorts");
        builder.HasKey(c => new { c.CohortDayUtc, c.DayIndex });

        builder.Property(c => c.CohortDayUtc).HasColumnType("date");

        // "D7 across every cohort" — the headline number, read by day index rather than by cohort.
        builder.HasIndex(c => c.DayIndex).HasDatabaseName("IX_TelemetryCohort_DayIndex");
    }
}

public class TelemetryEventSchemaConfiguration : IEntityTypeConfiguration<TelemetryEventSchema>
{
    public void Configure(EntityTypeBuilder<TelemetryEventSchema> builder)
    {
        builder.ToTable("TelemetryEventSchemas");

        // The name is the key. A registry that allowed two rows for one name would mean ingest had
        // to choose between two categories, and the lawful basis is not a thing to guess at.
        builder.HasKey(s => s.Name);
        builder.Property(s => s.Name).HasMaxLength(TelemetryNames.MaxNameLength);

        builder.Property(s => s.Group).IsRequired().HasMaxLength(32);
        builder.Property(s => s.Description).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Dimensions).IsRequired().HasMaxLength(128);

        builder.Property(s => s.Category)
            .HasConversion(EnumWire.Converter<TelemetryCategory>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.SampleRate).HasDefaultValue(1.0);
        builder.Property(s => s.Enabled).HasDefaultValue(true);
        builder.Property(s => s.RollUpDaily).HasDefaultValue(true);

        // The console's "unrecognised events awaiting registration" list.
        builder.HasIndex(s => s.FirstSeenAtUtc)
            .HasFilter("[FirstSeenAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_TelemetrySchema_Unregistered");
    }
}
