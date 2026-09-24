using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.LookUps;
using Share7.Domain.Staff;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

// Content-team accounts (Team & Access) and Studio sign-in. See ContentStudioPhase1.md.
//
// Every table here cascades from AspNetUsers — the scope tables by way of their profile — so the
// database removes them with the account. But staff accounts are never deleted in the first place:
// both deletion paths refuse an account that has a StaffProfile, and a SuperAdmin deactivates it
// instead, which keeps the person's name resolvable on everything they did.

public class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.ToTable("StaffProfiles");
        builder.HasKey(p => p.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<StaffProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.FullName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.JobTitle).HasMaxLength(100);
        builder.Property(p => p.WorkEmail).HasMaxLength(256);
        builder.Property(p => p.InterfaceLanguage).IsRequired().HasMaxLength(5);
        builder.Property(p => p.StatusReason).HasMaxLength(500);
        builder.Property(p => p.Notes).HasMaxLength(4000);
        builder.Property(p => p.LastTwoStepCodeHash).HasMaxLength(64);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Status);

        builder.HasMany(p => p.ScopeNodes)
            .WithOne()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ScopeLanguages)
            .WithOne()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StaffScopeNodeConfiguration : IEntityTypeConfiguration<StaffScopeNode>
{
    public void Configure(EntityTypeBuilder<StaffScopeNode> builder)
    {
        builder.ToTable("StaffScopeNodes");
        builder.HasKey(s => new { s.UserId, s.NodeId });

        // No foreign key to CurriculumNodes — see StaffScopeNode.
        builder.HasIndex(s => s.NodeId);
    }
}

public class StaffScopeLanguageConfiguration : IEntityTypeConfiguration<StaffScopeLanguage>
{
    public void Configure(EntityTypeBuilder<StaffScopeLanguage> builder)
    {
        builder.ToTable("StaffScopeLanguages");
        builder.HasKey(s => new { s.UserId, s.LanguageId });

        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(s => s.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class StaffSetupTokenConfiguration : IEntityTypeConfiguration<StaffSetupToken>
{
    public void Configure(EntityTypeBuilder<StaffSetupToken> builder)
    {
        builder.ToTable("StaffSetupTokens");
        builder.HasKey(t => t.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.CreatedAtUtc });
    }
}

public class StaffSessionConfiguration : IEntityTypeConfiguration<StaffSession>
{
    public void Configure(EntityTypeBuilder<StaffSession> builder)
    {
        builder.ToTable("StaffSessions");
        builder.HasKey(s => s.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.RefreshTokenHash).IsRequired().HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(s => s.PreviousRefreshTokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(s => s.IpAddress).HasMaxLength(45);
        builder.Property(s => s.UserAgent).HasMaxLength(256);
        builder.Property(s => s.RevokedReason).HasMaxLength(40);
        builder.Property(s => s.StampHash).IsRequired().HasMaxLength(22).IsUnicode(false);

        builder.HasIndex(s => s.RefreshTokenHash).IsUnique();
        builder.HasIndex(s => s.PreviousRefreshTokenHash);
        builder.HasIndex(s => new { s.UserId, s.RevokedAtUtc });
    }
}

public class StaffSignInEventConfiguration : IEntityTypeConfiguration<StaffSignInEvent>
{
    public void Configure(EntityTypeBuilder<StaffSignInEvent> builder)
    {
        builder.ToTable("StaffSignInEvents");
        builder.HasKey(e => e.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(256);

        builder.HasIndex(e => new { e.UserId, e.OccurredAtUtc });
    }
}

public class StaffSecuritySettingsConfiguration : IEntityTypeConfiguration<StaffSecuritySettings>
{
    public void Configure(EntityTypeBuilder<StaffSecuritySettings> builder)
    {
        builder.ToTable("StaffSecuritySettings", table =>
            table.HasCheckConstraint("CK_StaffSecuritySettings_Singleton", $"[Id] = {StaffSecuritySettings.SingletonId}"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // The row exists from the migration onward, so readers never have to cope with its absence.
        builder.HasData(new StaffSecuritySettings
        {
            Id = StaffSecuritySettings.SingletonId,
            RequireTwoStep = false,
            SessionLifetimeHours = 24 * 7,
            IdleTimeoutHours = 12,
            MinimumPasswordLength = StaffSecuritySettings.MinimumPasswordLengthFloor,
            SetupLinkLifetimeHours = 72,
            UpdatedAtUtc = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
