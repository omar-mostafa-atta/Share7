using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Guidance;

namespace Share7.Infrastructure.Persistence.Configurations;

public class GuidanceFlowConfiguration : IEntityTypeConfiguration<GuidanceFlow>
{
    public void Configure(EntityTypeBuilder<GuidanceFlow> builder)
    {
        builder.ToTable("GuidanceFlows");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Key)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(f => f.Key)
            .IsUnique();

        builder.Property(f => f.Title)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(f => f.Description)
            .HasMaxLength(1024)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(f => f.Kind)
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue("Tour");

        builder.Property(f => f.Priority)
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(f => f.ReplayPolicy)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(f => f.Skippable)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(f => f.SkipAfterStep)
            .IsRequired()
            .HasDefaultValue(2);

        builder.Property(f => f.Resumable)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(f => f.IsKillSwitched)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(f => f.IsKillSwitched);

        builder.Property(f => f.ActiveVersionNumber)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(f => f.CreatedAtUtc)
            .IsRequired();

        builder.Property(f => f.UpdatedAtUtc)
            .IsRequired();

        builder.HasMany(f => f.Versions)
            .WithOne(v => v.Flow)
            .HasForeignKey(v => v.FlowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(f => f.AuditLogs)
            .WithOne(a => a.Flow)
            .HasForeignKey(a => a.FlowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class GuidanceFlowVersionConfiguration : IEntityTypeConfiguration<GuidanceFlowVersion>
{
    public void Configure(EntityTypeBuilder<GuidanceFlowVersion> builder)
    {
        builder.ToTable("GuidanceFlowVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VersionNumber)
            .IsRequired();

        builder.Property(v => v.Status)
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue("Draft");

        builder.HasIndex(v => v.Status);

        builder.HasIndex(v => new { v.FlowId, v.VersionNumber })
            .IsUnique();

        builder.Property(v => v.StepsJson)
            .IsRequired();

        builder.Property(v => v.ChangeSummary)
            .HasMaxLength(512);

        builder.Property(v => v.CreatedAtUtc)
            .IsRequired();
    }
}

public class GuidanceAuditLogConfiguration : IEntityTypeConfiguration<GuidanceAuditLog>
{
    public void Configure(EntityTypeBuilder<GuidanceAuditLog> builder)
    {
        builder.ToTable("GuidanceAuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.UserEmail)
            .HasMaxLength(256);

        builder.Property(a => a.TimestampUtc)
            .IsRequired();

        builder.HasIndex(a => new { a.FlowId, a.TimestampUtc });
        builder.HasIndex(a => a.TimestampUtc);
    }
}
