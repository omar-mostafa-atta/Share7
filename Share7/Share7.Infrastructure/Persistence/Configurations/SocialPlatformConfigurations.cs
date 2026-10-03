using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Social;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class SocialPrivacyConfiguration : IEntityTypeConfiguration<SocialPrivacy>
{
    public void Configure(EntityTypeBuilder<SocialPrivacy> b)
    {
        b.ToTable("SocialPrivacy"); b.HasKey(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class OfficialProfileConfiguration : IEntityTypeConfiguration<OfficialProfile>
{
    public void Configure(EntityTypeBuilder<OfficialProfile> b)
    {
        b.ToTable("OfficialProfiles"); b.HasKey(x => x.UserId); b.Property(x => x.Sequence).UseIdentityColumn();
        b.HasIndex(x => x.Sequence).IsUnique(); b.HasIndex(x => new { x.Discoverable, x.Sequence });
        b.Property(x => x.DisplayNameEn).HasMaxLength(80); b.Property(x => x.DisplayNameAr).HasMaxLength(80);
        b.Property(x => x.TitleEn).HasMaxLength(80); b.Property(x => x.TitleAr).HasMaxLength(80);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class ShowcaseContentConfiguration : IEntityTypeConfiguration<ShowcaseContent>
{
    public void Configure(EntityTypeBuilder<ShowcaseContent> b)
    {
        b.ToTable("ShowcaseContents"); b.HasKey(x => x.Key); b.Property(x => x.Key).HasMaxLength(80);
        b.Property(x => x.AssetKey).HasMaxLength(160); b.Property(x => x.FallbackKey).HasMaxLength(80);
        b.HasIndex(x => new { x.Enabled, x.Kind, x.Key });
        b.HasOne<ShowcaseContent>().WithMany().HasForeignKey(x => x.FallbackKey).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Domain.Commerce.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class PlayerShowcaseConfiguration : IEntityTypeConfiguration<PlayerShowcase>
{
    public void Configure(EntityTypeBuilder<PlayerShowcase> b)
    {
        b.ToTable("PlayerShowcases"); b.HasKey(x => x.UserId); b.Property(x => x.SelectionJson).HasMaxLength(3000);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class OfficialFollowConfiguration : IEntityTypeConfiguration<OfficialFollow>
{
    public void Configure(EntityTypeBuilder<OfficialFollow> b)
    {
        b.ToTable("OfficialFollows"); b.HasKey(x => new { x.UserId, x.FollowedUserId });
        b.HasIndex(x => new { x.FollowedUserId, x.UserId });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.FollowedUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class OfficialActivityConfiguration : IEntityTypeConfiguration<OfficialActivity>
{
    public void Configure(EntityTypeBuilder<OfficialActivity> b)
    {
        b.ToTable("OfficialActivities"); b.HasKey(x => x.Sequence); b.Property(x => x.Sequence).UseIdentityColumn();
        b.Property(x => x.PublicationKey).HasMaxLength(64); b.Property(x => x.TitleEn).HasMaxLength(160); b.Property(x => x.TitleAr).HasMaxLength(160);
        b.HasIndex(x => new { x.UserId, x.PublicationKey }).IsUnique(); b.HasIndex(x => new { x.UserId, x.Sequence });
        b.HasIndex(x => x.ExpiresAtUtc);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class PlayerMuteConfiguration : IEntityTypeConfiguration<PlayerMute>
{
    public void Configure(EntityTypeBuilder<PlayerMute> b)
    {
        b.ToTable("PlayerMutes"); b.HasKey(x => new { x.UserId, x.MutedUserId });
        b.HasIndex(x => x.MutedUserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.MutedUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class PlayerReportConfiguration : IEntityTypeConfiguration<PlayerReport>
{
    public void Configure(EntityTypeBuilder<PlayerReport> b)
    {
        b.ToTable("PlayerReports"); b.HasKey(x => x.Id); b.Property(x => x.Sequence).UseIdentityColumn();
        b.HasIndex(x => x.Sequence).IsUnique(); b.HasIndex(x => new { x.State, x.Sequence });
        b.HasIndex(x => new { x.UserId, x.RequestId }).IsUnique(); b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        b.Property(x => x.RequestId).HasMaxLength(64); b.Property(x => x.DecisionCode).HasMaxLength(40);
        b.Property(x => x.EvidenceJson).HasMaxLength(4000);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReportedUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
public class SocialRestrictionConfiguration : IEntityTypeConfiguration<SocialRestriction>
{
    public void Configure(EntityTypeBuilder<SocialRestriction> b)
    {
        b.ToTable("SocialRestrictions"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.RevokedAtUtc, x.ExpiresAtUtc });
        b.Property(x => x.AppealCode).HasMaxLength(40);
        b.Property(x => x.AppealResolutionCode).HasMaxLength(48);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class InboxReadConfiguration : IEntityTypeConfiguration<InboxRead>
{
    public void Configure(EntityTypeBuilder<InboxRead> b)
    {
        b.ToTable("InboxReads"); b.HasKey(x => new { x.UserId, x.EventId }); b.HasIndex(x => x.ReadAtUtc);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class InboxPreferenceConfiguration : IEntityTypeConfiguration<InboxPreference>
{
    public void Configure(EntityTypeBuilder<InboxPreference> b)
    {
        b.ToTable("InboxPreferences"); b.HasKey(x => new { x.UserId, x.Category }); b.Property(x => x.Category).HasMaxLength(24);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
