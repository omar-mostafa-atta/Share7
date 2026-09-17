using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Progress;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ProgressRequestLogConfiguration : IEntityTypeConfiguration<ProgressRequestLog>
{
    public void Configure(EntityTypeBuilder<ProgressRequestLog> builder)
    {
        builder.ToTable("ProgressRequestLogs");

        // Composite, scoped per user: the key is the concurrency guard as well as the lookup.
        // Two simultaneous retries race to insert it; one commits and the other takes the unique
        // violation and replays, which is what makes this correct without a lock.
        builder.HasKey(l => new { l.UserId, l.RequestId });

        builder.Property(l => l.RequestId).HasMaxLength(128);
        builder.Property(l => l.Operation).IsRequired().HasMaxLength(32);
        builder.Property(l => l.ResponseJson).IsRequired();

        // The retention sweep's query.
        builder.HasIndex(l => l.CreatedAtUtc)
            .HasDatabaseName("IX_ProgressRequestLog_Retention");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
