using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;

// Aliased because Share7.Domain.Assessment.Assessment shares its name with its own namespace, so
// an unqualified `Assessment` in a generic argument binds to the namespace.
using AssessmentEntity = Share7.Domain.Assessment.Assessment;
using Share7.Domain.Assessment;

namespace Share7.Infrastructure.Persistence.Configurations;

public class AssessmentBlueprintConfiguration : IEntityTypeConfiguration<AssessmentBlueprint>
{
    public void Configure(EntityTypeBuilder<AssessmentBlueprint> builder)
    {
        builder.ToTable("AssessmentBlueprints");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BlueprintKey).HasMaxLength(128).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(256).IsRequired();
        builder.Property(b => b.SourceNote).HasMaxLength(1024);

        builder.Property(b => b.MinCoverageRatio).HasPrecision(5, 4);
        builder.Property(b => b.MinAreaCoverageRatio).HasPrecision(5, 4);

        // A revision supersedes rather than overwrites, so the pair is the identity.
        builder.HasIndex(b => new { b.BlueprintKey, b.VersionNumber }).IsUnique();

        builder.HasOne(b => b.Framework)
            .WithMany()
            .HasForeignKey(b => b.FrameworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssessmentBlueprintAreaConfiguration : IEntityTypeConfiguration<AssessmentBlueprintArea>
{
    public void Configure(EntityTypeBuilder<AssessmentBlueprintArea> builder)
    {
        builder.ToTable("AssessmentBlueprintAreas");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AreaKey).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Label).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Weight).HasPrecision(9, 4);

        builder.HasIndex(a => new { a.BlueprintId, a.AreaKey }).IsUnique();

        // Cascade: an area is part of its blueprint and means nothing away from it.
        builder.HasOne(a => a.Blueprint)
            .WithMany(b => b.Areas)
            .HasForeignKey(a => a.BlueprintId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssessmentBlueprintLineConfiguration : IEntityTypeConfiguration<AssessmentBlueprintLine>
{
    public void Configure(EntityTypeBuilder<AssessmentBlueprintLine> builder)
    {
        builder.ToTable("AssessmentBlueprintLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Weight).HasPrecision(9, 4);

        // One line per target per area. Two lines for the same claim would double its weight
        // silently, which is the kind of arithmetic error a coverage figure hides perfectly.
        builder.HasIndex(l => new { l.AreaId, l.TargetId }).IsUnique();

        builder.HasOne(l => l.Area)
            .WithMany(a => a.Lines)
            .HasForeignKey(l => l.AreaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a target with a blueprint line on it is load-bearing for an exam claim, and
        // deleting it should fail loudly rather than quietly shrink a paper.
        builder.HasOne(l => l.Target)
            .WithMany()
            .HasForeignKey(l => l.TargetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssessmentConfiguration : IEntityTypeConfiguration<AssessmentEntity>
{
    public void Configure(EntityTypeBuilder<AssessmentEntity> builder)
    {
        builder.ToTable("Assessments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AssessmentKey).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(256).IsRequired();

        builder.HasIndex(a => a.AssessmentKey).IsUnique();

        builder.HasOne(a => a.Blueprint)
            .WithMany()
            .HasForeignKey(a => a.BlueprintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssessmentFormConfiguration : IEntityTypeConfiguration<AssessmentForm>
{
    public void Configure(EntityTypeBuilder<AssessmentForm> builder)
    {
        builder.ToTable("AssessmentForms");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Label).HasMaxLength(256);

        builder.HasIndex(f => new { f.AssessmentId, f.FormNumber }).IsUnique();

        builder.HasOne(f => f.Assessment)
            .WithMany(a => a.Forms)
            .HasForeignKey(f => f.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Blueprint)
            .WithMany()
            .HasForeignKey(f => f.BlueprintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssessmentFormItemConfiguration : IEntityTypeConfiguration<AssessmentFormItem>
{
    public void Configure(EntityTypeBuilder<AssessmentFormItem> builder)
    {
        builder.ToTable("AssessmentFormItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Points).HasPrecision(6, 2);

        builder.HasIndex(i => new { i.FormId, i.Position }).IsUnique();

        builder.HasOne(i => i.Form)
            .WithMany(f => f.Items)
            .HasForeignKey(i => i.FormId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, and it is the whole point of the table: the promise a sealed form makes is
        // that this exact wording can be served again in five years. A cascade here would let a
        // content cleanup quietly rewrite the history of a disputed grade.
        builder.HasOne(i => i.ItemVersion)
            .WithMany()
            .HasForeignKey(i => i.ItemVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssessmentAdministrationConfiguration : IEntityTypeConfiguration<AssessmentAdministration>
{
    public void Configure(EntityTypeBuilder<AssessmentAdministration> builder)
    {
        builder.ToTable("AssessmentAdministrations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.PointsEarned).HasPrecision(9, 2);
        builder.Property(a => a.PointsAvailable).HasPrecision(9, 2);
        builder.Property(a => a.IdempotencyKey).HasMaxLength(128);

        // The learner's own history of sittings, newest first — the read every report starts from.
        builder.HasIndex(a => new { a.LearnerId, a.StartedAtUtc });

        // A replayed start must find the sitting it already created rather than opening a second
        // one. Filtered because most administrations carry no key.
        builder.HasIndex(a => new { a.LearnerId, a.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName("IX_AssessmentAdministrations_Idempotency");

        builder.HasOne(a => a.Form)
            .WithMany()
            .HasForeignKey(a => a.FormId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExamSpecificationConfiguration : IEntityTypeConfiguration<ExamSpecification>
{
    public void Configure(EntityTypeBuilder<ExamSpecification> builder)
    {
        builder.ToTable("ExamSpecifications");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SpecificationKey).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(256).IsRequired();
        builder.Property(s => s.CountryCode).HasMaxLength(2);
        builder.Property(s => s.SubjectLabel).HasMaxLength(128);

        builder.HasIndex(s => s.SpecificationKey).IsUnique();

        builder.HasOne(s => s.Authority)
            .WithMany()
            .HasForeignKey(s => s.AuthorityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExamSpecificationVersionConfiguration : IEntityTypeConfiguration<ExamSpecificationVersion>
{
    public void Configure(EntityTypeBuilder<ExamSpecificationVersion> builder)
    {
        builder.ToTable("ExamSpecificationVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VersionLabel).HasMaxLength(64).IsRequired();

        builder.HasIndex(v => new { v.ExamSpecificationId, v.VersionLabel }).IsUnique();

        builder.HasOne(v => v.ExamSpecification)
            .WithMany(s => s.Versions)
            .HasForeignKey(v => v.ExamSpecificationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Blueprint)
            .WithMany()
            .HasForeignKey(v => v.BlueprintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReportedExamOutcomeConfiguration : IEntityTypeConfiguration<ReportedExamOutcome>
{
    public void Configure(EntityTypeBuilder<ReportedExamOutcome> builder)
    {
        builder.ToTable("ReportedExamOutcomes");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.ReportedScore).HasPrecision(9, 2);
        builder.Property(o => o.ReportedGrade).HasMaxLength(32);
        builder.Property(o => o.Note).HasMaxLength(1024);
        builder.Property(o => o.BlueprintWeightedEstimateAtReport).HasPrecision(6, 4);
        builder.Property(o => o.CoverageRatioAtReport).HasPrecision(5, 4);

        // One learner reports one sitting once. A correction updates the row it already has, which
        // is what keeps the calibration sample from counting the same child twice.
        builder.HasIndex(o => new { o.LearnerId, o.ExamSpecificationVersionId }).IsUnique();

        builder.HasOne(o => o.ExamSpecificationVersion)
            .WithMany(v => v.ReportedOutcomes)
            .HasForeignKey(o => o.ExamSpecificationVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Derived on read from consent, verification and whether there is a usable result at all.
        builder.Ignore(o => o.IsCalibrationUsable);
    }
}

public class ExamProjectionConfiguration : IEntityTypeConfiguration<ExamProjection>
{
    public void Configure(EntityTypeBuilder<ExamProjection> builder)
    {
        builder.ToTable("ExamProjections");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.MethodKey).HasMaxLength(32).IsRequired();
        builder.Property(p => p.CoverageRatio).HasPrecision(5, 4);
        builder.Property(p => p.WeakestAreaCoverage).HasPrecision(5, 4);
        builder.Property(p => p.ProficiencyBandLow).HasPrecision(6, 4);
        builder.Property(p => p.ProficiencyBandHigh).HasPrecision(6, 4);
        builder.Property(p => p.OutcomeBandLow).HasPrecision(9, 2);
        builder.Property(p => p.OutcomeBandHigh).HasPrecision(9, 2);

        // One live projection per learner, exam version and method. Methods coexist here for the
        // same reason they do in the measurement layer: a new one shadows the old before anybody
        // switches to it.
        builder.HasIndex(p => new { p.LearnerId, p.ExamSpecificationVersionId, p.MethodKey })
            .IsUnique();

        builder.HasOne(p => p.ExamSpecificationVersion)
            .WithMany()
            .HasForeignKey(p => p.ExamSpecificationVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExamProjectionGapConfiguration : IEntityTypeConfiguration<ExamProjectionGap>
{
    public void Configure(EntityTypeBuilder<ExamProjectionGap> builder)
    {
        builder.ToTable("ExamProjectionGaps");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.AreaKey).HasMaxLength(64).IsRequired();
        builder.Property(g => g.AreaLabel).HasMaxLength(256).IsRequired();
        builder.Property(g => g.WeightInExam).HasPrecision(5, 4);

        builder.HasIndex(g => new { g.ExamProjectionId, g.Rank });

        builder.HasOne(g => g.ExamProjection)
            .WithMany(p => p.Gaps)
            .HasForeignKey(g => g.ExamProjectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction rather than Restrict: SQL Server refuses a second cascade path into
        // LearningTargets, and a gap is disposable anyway — the whole projection is rebuilt from
        // the blueprint and the observations whenever it is read.
        builder.HasOne(g => g.Target)
            .WithMany()
            .HasForeignKey(g => g.TargetId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
