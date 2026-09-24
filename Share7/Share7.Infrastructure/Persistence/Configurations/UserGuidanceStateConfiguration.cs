using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Guidance;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class UserGuidanceStateConfiguration : IEntityTypeConfiguration<UserGuidanceState>
{
    public void Configure(EntityTypeBuilder<UserGuidanceState> builder)
    {
        builder.ToTable("UserGuidanceStates");
        builder.HasKey(s => s.UserId);

        builder.Property(s => s.Generation)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(s => s.SchemaVersion)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(s => s.SessionOrdinal)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.LastSessionDayUtc)
            .HasMaxLength(10)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(s => s.StateJson)
            .IsRequired();

        builder.Property(s => s.CompletedOnboarding)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.TotalFlowsCompleted)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.UpdatedAtUtc)
            .IsRequired();

        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.HasIndex(s => s.CompletedOnboarding);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
