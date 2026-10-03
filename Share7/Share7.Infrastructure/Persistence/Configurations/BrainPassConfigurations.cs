using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.BrainPass;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class BrainPassSeasonConfiguration : IEntityTypeConfiguration<BrainPassSeason>
{
    public void Configure(EntityTypeBuilder<BrainPassSeason> b)
    {
        b.ToTable("BrainPassSeasons"); b.HasKey(x => x.Id); b.Property(x => x.Key).HasMaxLength(64); b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.NameEn).HasMaxLength(80); b.Property(x => x.NameAr).HasMaxLength(80); b.Property(x => x.ObjectiveGroupKey).HasMaxLength(64);
        b.HasIndex(x => new { x.State, x.StartsAtUtc, x.EndsAtUtc }); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Domain.Commerce.Product>().WithMany().HasForeignKey(x => x.PremiumProductId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class BrainPassTierConfiguration : IEntityTypeConfiguration<BrainPassTier>
{
    public void Configure(EntityTypeBuilder<BrainPassTier> b)
    {
        b.ToTable("BrainPassTiers"); b.HasKey(x => new { x.SeasonId, x.Number, x.Track });
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Domain.Rewards.RewardRule>().WithMany().HasForeignKey(x => x.RewardRuleId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class BrainPassXpRuleConfiguration : IEntityTypeConfiguration<BrainPassXpRule>
{
    public void Configure(EntityTypeBuilder<BrainPassXpRule> b)
    {
        b.ToTable("BrainPassXpRules"); b.HasKey(x => new { x.SeasonId, x.Metric }); b.Property(x => x.Metric).HasMaxLength(64);
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class BrainPassProgressConfiguration : IEntityTypeConfiguration<BrainPassProgress>
{
    public void Configure(EntityTypeBuilder<BrainPassProgress> b)
    {
        b.ToTable("BrainPassProgress"); b.HasKey(x => new { x.SeasonId, x.UserId });
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class BrainPassCreditConfiguration : IEntityTypeConfiguration<BrainPassCredit>
{
    public void Configure(EntityTypeBuilder<BrainPassCredit> b)
    {
        b.ToTable("BrainPassCredits"); b.HasKey(x => new { x.SeasonId, x.ResultId }); b.HasIndex(x => new { x.UserId, x.SeasonId });
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class BrainPassSourceConfiguration : IEntityTypeConfiguration<BrainPassSource>
{
    public void Configure(EntityTypeBuilder<BrainPassSource> b)
    {
        b.ToTable("BrainPassSources"); b.HasKey(x => new { x.SeasonId, x.UserId, x.Metric, x.SourceId }); b.Property(x => x.Metric).HasMaxLength(64);
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class BrainPassDailyConfiguration : IEntityTypeConfiguration<BrainPassDaily>
{
    public void Configure(EntityTypeBuilder<BrainPassDaily> b)
    {
        b.ToTable("BrainPassDaily"); b.HasKey(x => new { x.SeasonId, x.UserId, x.Metric, x.DayUtc }); b.Property(x => x.Metric).HasMaxLength(64);
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class BrainPassClaimConfiguration : IEntityTypeConfiguration<BrainPassClaim>
{
    public void Configure(EntityTypeBuilder<BrainPassClaim> b)
    {
        b.ToTable("BrainPassClaims"); b.HasKey(x => new { x.SeasonId, x.UserId, x.Tier, x.Track }); b.Property(x => x.RewardsJson).HasMaxLength(8000);
        b.HasOne<BrainPassSeason>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
