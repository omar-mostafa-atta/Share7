using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Competency;
using Share7.Domain.Constants;

namespace Share7.Infrastructure.Persistence.Configurations;

public class CompetencyFrameworkConfiguration : IEntityTypeConfiguration<CompetencyFramework>
{
    public void Configure(EntityTypeBuilder<CompetencyFramework> builder)
    {
        builder.ToTable("CompetencyFrameworks");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FrameworkKey).HasMaxLength(128).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(256).IsRequired();
        builder.Property(f => f.VersionLabel).HasMaxLength(64).IsRequired();
        builder.HasIndex(f => f.FrameworkKey).IsUnique();

        // The bootstrap framework, seeded rather than backfilled, because the placeholder targets
        // the migration mints need a framework to belong to before the migration runs.
        builder.HasData(
            new CompetencyFramework
            {
                Id = EducationIds.PlaceholderFramework,
                FrameworkKey = "share7.lesson_placeholder",
                Name = "Lesson placeholders (pre-framework)",
                AuthorityId = null,
                VersionLabel = "bootstrap",
                PublishedAtUtc = EvidenceContractConfiguration.SeededAtUtc,
                CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
            },

            // The framework authored targets are minted into, which starts **empty on purpose**.
            // A placeholder is a lesson wearing a target's clothes and can be generated; a real
            // claim about what a child can do has to be written by somebody who knows the subject,
            // and there is no honest way to seed one. It exists from the first deployment so that
            // the authoring surface has somewhere to write, and it fills subject by subject (§20.5).
            new CompetencyFramework
            {
                Id = AssessmentIds.Share7CoreFramework,
                FrameworkKey = "share7.core",
                Name = "Share7 authored competencies",
                AuthorityId = null,
                VersionLabel = "v1",
                PublishedAtUtc = EvidenceContractConfiguration.SeededAtUtc,
                CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
            });
    }
}

public class LearningTargetConfiguration : IEntityTypeConfiguration<LearningTarget>
{
    public void Configure(EntityTypeBuilder<LearningTarget> builder)
    {
        builder.ToTable("LearningTargets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TargetKey).HasMaxLength(200).IsRequired();
        builder.Property(t => t.TargetKindKey).HasMaxLength(64).IsRequired();

        builder.HasIndex(t => new { t.FrameworkId, t.TargetKey }).IsUnique();

        // Every aggregation and every projection filters placeholders out, so the flag is in the
        // index rather than being a scan over the whole framework.
        builder.HasIndex(t => new { t.FrameworkId, t.IsPlaceholder });

        builder.HasOne(t => t.Framework)
            .WithMany(f => f.Targets)
            .HasForeignKey(t => t.FrameworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LearningTargetTranslationConfiguration : IEntityTypeConfiguration<LearningTargetTranslation>
{
    public void Configure(EntityTypeBuilder<LearningTargetTranslation> builder)
    {
        builder.ToTable("LearningTargetTranslations");
        builder.HasKey(t => new { t.TargetId, t.LangId });

        builder.Property(t => t.Statement).HasMaxLength(1000).IsRequired();

        builder.HasOne(t => t.Target)
            .WithMany(t => t.Translations)
            .HasForeignKey(t => t.TargetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LearningTargetEdgeConfiguration : IEntityTypeConfiguration<LearningTargetEdge>
{
    public void Configure(EntityTypeBuilder<LearningTargetEdge> builder)
    {
        builder.ToTable("LearningTargetEdges");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Weight).HasPrecision(6, 4);

        // One edge of one kind between two targets. A second copy would double-count the component
        // in every aggregation that walks upward.
        builder.HasIndex(e => new { e.FromTargetId, e.ToTargetId, e.EdgeKind }).IsUnique();

        // NoAction on both ends: two cascade paths into LearningTargets is the multiple-cascade
        // shape SQL Server refuses, and an edge is cheap to clean up explicitly.
        builder.HasOne(e => e.FromTarget)
            .WithMany()
            .HasForeignKey(e => e.FromTargetId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.ToTarget)
            .WithMany()
            .HasForeignKey(e => e.ToTargetId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class LearningTargetAlignmentConfiguration : IEntityTypeConfiguration<LearningTargetAlignment>
{
    public void Configure(EntityTypeBuilder<LearningTargetAlignment> builder)
    {
        builder.ToTable("LearningTargetAlignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Note).HasMaxLength(512);

        builder.HasIndex(a => new { a.SourceTargetId, a.AlignedTargetId }).IsUnique();

        builder.HasOne(a => a.SourceTarget)
            .WithMany()
            .HasForeignKey(a => a.SourceTargetId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.AlignedTarget)
            .WithMany()
            .HasForeignKey(a => a.AlignedTargetId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class ItemTargetMappingConfiguration : IEntityTypeConfiguration<ItemTargetMapping>
{
    public void Configure(EntityTypeBuilder<ItemTargetMapping> builder)
    {
        builder.ToTable("ItemTargetMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Emphasis).HasPrecision(6, 4);

        builder.HasIndex(m => new { m.ItemId, m.TargetId }).IsUnique();

        // Reporting needs one answer to "what is this question for", so exactly one primary per
        // item — a filtered unique index rather than a rule somebody has to remember.
        builder.HasIndex(m => m.ItemId).IsUnique().HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("IX_ItemTargetMappings_ItemId_Primary");

        // The measurement read: every item that measures this target.
        builder.HasIndex(m => m.TargetId);

        builder.HasOne(m => m.Item)
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Target)
            .WithMany()
            .HasForeignKey(m => m.TargetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class NodeTargetMappingConfiguration : IEntityTypeConfiguration<NodeTargetMapping>
{
    public void Configure(EntityTypeBuilder<NodeTargetMapping> builder)
    {
        builder.ToTable("NodeTargetMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Emphasis).HasPrecision(6, 4);

        builder.HasIndex(m => new { m.CurriculumVersionId, m.NodeId, m.TargetId }).IsUnique();
        builder.HasIndex(m => m.TargetId);

        builder.HasOne(m => m.Target)
            .WithMany()
            .HasForeignKey(m => m.TargetId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK on NodeId while CurriculumNodes is a derived projection: the node rows are rebuilt
        // from the legacy tables, and a constraint here would make a rebuild order-dependent for
        // no gain. The orphan check lives in the projector, which is the only writer.
    }
}
