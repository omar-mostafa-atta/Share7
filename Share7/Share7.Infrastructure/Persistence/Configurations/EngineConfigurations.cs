using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Domain.LookUps;
using Share7.Domain.Progress;
using Share7.Domain.Structure;

namespace Share7.Infrastructure.Persistence.Configurations;

public class PublishedItemSetConfiguration : IEntityTypeConfiguration<PublishedItemSet>
{
    public void Configure(EntityTypeBuilder<PublishedItemSet> builder)
    {
        builder.ToTable("PublishedItemSets");
        builder.HasKey(s => new { s.NodeId, s.Role, s.LangId });

        // Restrict: a node is retired, never deleted, and a set outlives nothing it describes.
        builder.HasOne<CurriculumNode>()
            .WithMany()
            .HasForeignKey(s => s.NodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(s => s.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ContentPublicationConfiguration : IEntityTypeConfiguration<ContentPublication>
{
    public void Configure(EntityTypeBuilder<ContentPublication> builder)
    {
        builder.ToTable("ContentPublications");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FileName).HasMaxLength(260).IsRequired();

        // Text, like the upload tables it generalises: read to explain a change.
        builder.Property(p => p.Source)
            .HasConversion(EnumWire.Converter<QuestionSetSource>())
            .HasMaxLength(32)
            .IsRequired();

        // Two concurrent publishes that both read version N and both claim N+1: the loser fails
        // here rather than recording a history that did not happen.
        builder.HasIndex(p => new { p.NodeId, p.Role, p.LangId, p.Version }).IsUnique();
        builder.HasIndex(p => p.ReleaseId).HasFilter("[ReleaseId] IS NOT NULL");

        builder.HasOne<CurriculumNode>()
            .WithMany()
            .HasForeignKey(p => p.NodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(p => p.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CurriculumReadCheckConfiguration : IEntityTypeConfiguration<CurriculumReadCheck>
{
    public void Configure(EntityTypeBuilder<CurriculumReadCheck> builder)
    {
        builder.ToTable("CurriculumReadChecks");
        builder.HasKey(c => new { c.Day, c.ReadName });
        builder.Property(c => c.ReadName).HasMaxLength(64).IsRequired();
        builder.Property(c => c.LastDifferenceSample).HasMaxLength(4000);
    }
}

public class UnlockRepairJobConfiguration : IEntityTypeConfiguration<UnlockRepairJob>
{
    public void Configure(EntityTypeBuilder<UnlockRepairJob> builder)
    {
        builder.ToTable("UnlockRepairJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Kind).HasConversion<int>();
        builder.Property(j => j.LastError).HasMaxLength(2000);

        // The sweep's read: what is still to do, oldest first.
        builder.HasIndex(j => j.CreatedAtUtc).HasFilter("[CompletedAtUtc] IS NULL");
    }
}
