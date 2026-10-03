using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Identity;
namespace Share7.Infrastructure.Persistence.Configurations;
public class GameObservationCapabilityConfiguration : IEntityTypeConfiguration<GameObservationCapability>
{
    public void Configure(EntityTypeBuilder<GameObservationCapability> b)
    {
        b.ToTable("GameObservationCapabilities"); b.HasKey(c => new { c.GameId, c.ProtocolVersion });
        b.HasOne<Domain.Games.Game>().WithMany().HasForeignKey(c => c.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class SessionObserverConfiguration : IEntityTypeConfiguration<SessionObserver>
{
    public void Configure(EntityTypeBuilder<SessionObserver> b)
    {
        b.ToTable("SessionObservers"); b.HasKey(o => new { o.SessionId, o.UserId }); b.HasIndex(o => o.UserId).IsUnique(); b.HasIndex(o => o.ExpiresAtUtc);
        b.HasOne<MultiplayerSession>().WithMany().HasForeignKey(o => o.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
