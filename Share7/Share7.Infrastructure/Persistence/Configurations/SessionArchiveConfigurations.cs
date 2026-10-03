using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class SessionArchiveConfiguration : IEntityTypeConfiguration<SessionArchive>
{
    public void Configure(EntityTypeBuilder<SessionArchive> b)
    {
        b.ToTable("SessionArchives"); b.HasKey(a => a.Id);
        b.Property(a => a.State).HasConversion(EnumWire.Converter<MultiplayerSessionState>()).HasMaxLength(16);
        b.HasIndex(a => a.ExpiresAtUtc);
    }
}
public class SessionArchiveParticipantConfiguration : IEntityTypeConfiguration<SessionArchiveParticipant>
{
    public void Configure(EntityTypeBuilder<SessionArchiveParticipant> b)
    {
        b.ToTable("SessionArchiveParticipants"); b.HasKey(a => new { a.SessionId, a.UserId });
        b.HasOne<SessionArchive>().WithMany().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(a => a.UserId);
    }
}
