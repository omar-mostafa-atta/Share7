using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Games;

namespace Share7.Infrastructure.Persistence.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("Games");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.GameKey).IsRequired().HasMaxLength(64);
        builder.HasIndex(g => g.GameKey).IsUnique();

        builder.Property(g => g.MinPlayers).HasDefaultValue(1);
        builder.Property(g => g.MaxPlayers).HasDefaultValue(2);
        builder.Property(g => g.ReadyTimeoutSeconds).HasDefaultValue(20f);
        builder.Property(g => g.SupportsSinglePlayer).HasDefaultValue(true);
        builder.Property(g => g.SupportsMultiplayer).HasDefaultValue(true);
        builder.Property(g => g.UseLobby).HasDefaultValue(true);
        builder.Property(g => g.UseMatchmaking).HasDefaultValue(true);
        builder.Property(g => g.IsActive).HasDefaultValue(true);
    }
}
