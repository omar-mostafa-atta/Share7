using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Social;
using Share7.Infrastructure.Identity;
namespace Share7.Infrastructure.Persistence.Configurations;
public class PlayerTeamConfiguration : IEntityTypeConfiguration<PlayerTeam>
{
    public void Configure(EntityTypeBuilder<PlayerTeam> b)
    {
        b.ToTable("PlayerTeams"); b.HasKey(t => t.Id); b.Property(t => t.RequestId).HasMaxLength(64);
        b.HasIndex(t => new { t.OwnerUserId, t.RequestId }).IsUnique();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class PlayerTeamMemberConfiguration : IEntityTypeConfiguration<PlayerTeamMember>
{
    public void Configure(EntityTypeBuilder<PlayerTeamMember> b)
    {
        b.ToTable("PlayerTeamMembers"); b.HasKey(m => new { m.TeamId, m.UserId }); b.HasIndex(m => m.UserId);
        b.HasOne<PlayerTeam>().WithMany().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
