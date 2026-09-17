using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Progress;

namespace Share7.Infrastructure.Persistence.Configurations;

public class UserNodeUnlockConfiguration : IEntityTypeConfiguration<UserNodeUnlock>
{
    public void Configure(EntityTypeBuilder<UserNodeUnlock> builder)
    {
        builder.ToTable("UserNodeUnlocks");
        builder.HasKey(u => new { u.UserId, u.GameId, u.NodeType, u.NodeId });

        builder.Property(u => u.NodeType).HasConversion<int>();

        // "Everything this student has unlocked in this game" — read once per snapshot.
        builder.HasIndex(u => new { u.UserId, u.GameId });

        builder.HasOne<Domain.Games.Game>()
            .WithMany()
            .HasForeignKey(u => u.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // NodeId intentionally has no FK — it addresses four different tables. See UserNodeUnlock.
    }
}
