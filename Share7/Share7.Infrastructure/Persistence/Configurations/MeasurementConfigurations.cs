using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;
using Share7.Domain.Measurement;

// Aliased because Share7.Infrastructure.Measurement is a namespace in scope here, and an
// unqualified `Measurement` would bind to it rather than to the entity.
using MeasurementEntity = Share7.Domain.Measurement.Measurement;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> builder)
    {
        builder.ToTable("Observations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Sequence).ValueGeneratedOnAdd().UseIdentityColumn();
        builder.HasIndex(o => o.Sequence).IsUnique();

        builder.Property(o => o.Weight).HasPrecision(6, 4);
        builder.Property(o => o.ExclusionNote).HasMaxLength(512);

        // **The idempotency guard.** One response says one thing about one target, so re-running
        // the projector over work it has already done is an insert that fails rather than a second
        // copy of the same evidence quietly doubling a measurement.
        builder.HasIndex(o => new { o.LearnerResponseId, o.TargetId })
            .IsUnique()
            .HasFilter("[LearnerResponseId] IS NOT NULL")
            .HasDatabaseName("IX_Observations_Response_Target");

        // The measurement read: one learner's observations on one target.
        builder.HasIndex(o => new { o.LearnerId, o.TargetId, o.ExcludedAtUtc });

        // The statistics fold and the mis-keyed-item sweep: every observation of one item version.
        builder.HasIndex(o => o.ItemVersionId);

        // Cascade from the response: when a learner is erased their responses are hard-deleted, and
        // the interpretations of those responses are about that child just as much as the answers
        // were. They are also fully rebuildable, so nothing is lost that mattered.
        builder.HasOne(o => o.LearnerResponse)
            .WithMany()
            .HasForeignKey(o => o.LearnerResponseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Target)
            .WithMany()
            .HasForeignKey(o => o.TargetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Item)
            .WithMany()
            .HasForeignKey(o => o.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ItemStatisticsConfiguration : IEntityTypeConfiguration<ItemStatistics>
{
    public void Configure(EntityTypeBuilder<ItemStatistics> builder)
    {
        builder.ToTable("ItemStatistics");

        // Keyed by version and population, not by item: a rewritten question is not the same
        // question, and a cohort is not the world.
        builder.HasKey(s => new { s.ItemVersionId, s.Population });

        builder.Property(s => s.Population).HasMaxLength(64);
        builder.Property(s => s.DiscriminationNumerator).HasPrecision(18, 6);
        builder.Property(s => s.DiscriminationDenominator).HasPrecision(18, 6);

        // JSON rather than a child table. It is read whole, written whole, and never joined —
        // a distractor breakdown is one blob per item, not a relation.
        builder.Property(s => s.ChoiceFrequency).HasColumnType("nvarchar(max)");

        builder.HasIndex(s => s.ItemId);

        builder.HasOne(s => s.ItemVersion)
            .WithMany()
            .HasForeignKey(s => s.ItemVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Facility and MeanElapsedMs are computed from the counts on the way out; storing them
        // would put a derived value where a running aggregate belongs.
        builder.Ignore(s => s.Facility);
        builder.Ignore(s => s.MeanElapsedMs);
    }
}

public class MeasurementConfiguration : IEntityTypeConfiguration<MeasurementEntity>
{
    public void Configure(EntityTypeBuilder<MeasurementEntity> builder)
    {
        builder.ToTable("Measurements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MethodKey).HasMaxLength(32).IsRequired();
        builder.Property(m => m.Estimate).HasPrecision(6, 4);
        builder.Property(m => m.IntervalLow).HasPrecision(6, 4);
        builder.Property(m => m.IntervalHigh).HasPrecision(6, 4);

        // One row per learner, target and method. Methods coexist rather than overwrite, which is
        // what lets a new approach be shadowed against the live one before anybody trusts it.
        builder.HasIndex(m => new { m.LearnerId, m.TargetId, m.MethodKey }).IsUnique();

        builder.HasOne(m => m.Target)
            .WithMany()
            .HasForeignKey(m => m.TargetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class MasteryRuleConfiguration : IEntityTypeConfiguration<MasteryRule>
{
    public void Configure(EntityTypeBuilder<MasteryRule> builder)
    {
        builder.ToTable("MasteryRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RuleKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1024);
        builder.Property(r => r.MasteredIntervalLowAtLeast).HasPrecision(6, 4);
        builder.Property(r => r.DevelopingEstimateAtLeast).HasPrecision(6, 4);

        builder.HasIndex(r => new { r.RuleKey, r.VersionNumber }).IsUnique();

        // Seeded, like the platform evidence contract and for the same reason: a deployment with no
        // published rule can produce measurements but no verdicts, silently, and nobody notices
        // until a report is empty.
        builder.HasData(new MasteryRule
        {
            Id = MeasurementIds.DefaultMasteryRuleV1,
            RuleKey = "platform.default",
            VersionNumber = 1,

            // Eight is not a magic number, it is a floor with a reason: below it the Wilson
            // interval on any plausible proportion is wider than the distance between the bands,
            // so every verdict would be indistinguishable from every other one.
            MinObservations = 8,

            // Practice counts toward a learner-facing verdict. It does not count toward an exam
            // projection, and that distinction lives in the projection's own gate rather than here
            // — a child grinding the questions they got wrong is learning, and a report that
            // refused to notice would be useless to them.
            MinStrength = EvidenceStrength.Practice,

            MasteredIntervalLowAtLeast = 0.80m,
            DevelopingEstimateAtLeast = 0.50m,

            Description =
                "Mastered when at least 8 admitted observations put the lower bound of the 95% "
                + "Wilson interval at or above 0.80. Developing when the point estimate is at or "
                + "above 0.50. Insufficient below 8 observations, which is a stated absence of "
                + "evidence rather than a low score.",

            PublishedAtUtc = EvidenceContractConfiguration.SeededAtUtc,
            CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
        });
    }
}

public class MasteryVerdictConfiguration : IEntityTypeConfiguration<MasteryVerdict>
{
    public void Configure(EntityTypeBuilder<MasteryVerdict> builder)
    {
        builder.ToTable("MasteryVerdicts");
        builder.HasKey(v => v.Id);

        builder.HasIndex(v => new { v.LearnerId, v.TargetId }).IsUnique();

        builder.HasOne(v => v.Target)
            .WithMany()
            .HasForeignKey(v => v.TargetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.MasteryRule)
            .WithMany()
            .HasForeignKey(v => v.MasteryRuleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade: a verdict is a judgement about a measurement and is meaningless without it.
        builder.HasOne(v => v.Measurement)
            .WithMany()
            .HasForeignKey(v => v.MeasurementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
