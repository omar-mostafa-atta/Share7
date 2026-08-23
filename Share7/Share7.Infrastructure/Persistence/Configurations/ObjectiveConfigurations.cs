using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Application.Common.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ObjectiveConfiguration : IEntityTypeConfiguration<Objective>
{
    public void Configure(EntityTypeBuilder<Objective> builder)
    {
        builder.ToTable("Objectives");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Key).IsRequired().HasMaxLength(128);
        builder.Property(o => o.Metric).IsRequired().HasMaxLength(48);
        builder.Property(o => o.Scope).HasMaxLength(64);
        builder.Property(o => o.IconKey).HasMaxLength(64);

        // Text, like every other enum that reaches an audit trail or an admin's SQL window.
        builder.Property(o => o.Kind)
            .HasConversion(ObjectiveEnumWire.Converter<ObjectiveKind>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.Aggregation)
            .HasConversion(ObjectiveEnumWire.Converter<LeaderboardAggregation>())
            .HasMaxLength(16)
            .IsRequired();

        // The key is the public identity — reward rules, client art and analytics all resolve
        // through it, so two objectives cannot share one.
        builder.HasIndex(o => o.Key).IsUnique();

        // The projector's read: every active objective, on every fold.
        builder.HasIndex(o => new { o.IsActive, o.Metric })
            .HasDatabaseName("IX_Objective_Active");

        // NoAction: a group and its members are authored together, and SQL Server would otherwise
        // have two cascade paths into UserObjectiveProgress — one via the objective, one via the
        // group — which it refuses outright.
        builder.HasOne(o => o.Group)
            .WithMany()
            .HasForeignKey(o => o.GroupId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(o => o.Translations)
            .WithOne(t => t.Objective!)
            .HasForeignKey(t => t.ObjectiveId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ObjectiveTranslationConfiguration : IEntityTypeConfiguration<ObjectiveTranslation>
{
    public void Configure(EntityTypeBuilder<ObjectiveTranslation> builder)
    {
        builder.ToTable("ObjectiveTranslations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Description).HasMaxLength(512);

        builder.HasIndex(t => new { t.ObjectiveId, t.LangId }).IsUnique();
    }
}

public class UserObjectiveProgressConfiguration : IEntityTypeConfiguration<UserObjectiveProgress>
{
    public void Configure(EntityTypeBuilder<UserObjectiveProgress> builder)
    {
        builder.ToTable("UserObjectiveProgress");

        // The cycle is part of the identity, which is what makes a rollover a new row rather than
        // an UPDATE that something has to remember to run at midnight.
        builder.HasKey(p => new { p.UserId, p.ObjectiveId, p.CycleKey });

        builder.Property(p => p.CycleKey).IsRequired().HasMaxLength(32);

        builder.Property(p => p.State)
            .HasConversion(ObjectiveEnumWire.Converter<ObjectiveState>())
            .HasMaxLength(16)
            .IsRequired();

        // "This player's objectives" — the read behind every progression screen.
        builder.HasIndex(p => new { p.UserId, p.State })
            .HasDatabaseName("IX_UserObjectiveProgress_User");

        builder.HasOne(p => p.Objective)
            .WithMany()
            .HasForeignKey(p => p.ObjectiveId)
            .OnDelete(DeleteBehavior.Cascade);

        // Declared here because Domain cannot see ApplicationUser. Cascade means progress goes with
        // the account and needs no entry in UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectionCheckpointConfiguration : IEntityTypeConfiguration<ProjectionCheckpoint>
{
    public void Configure(EntityTypeBuilder<ProjectionCheckpoint> builder)
    {
        builder.ToTable("ProjectionCheckpoints");
        builder.HasKey(c => c.Consumer);

        builder.Property(c => c.Consumer).HasMaxLength(64);
    }
}

public class ObjectiveGroupConfiguration : IEntityTypeConfiguration<ObjectiveGroup>
{
    public void Configure(EntityTypeBuilder<ObjectiveGroup> builder)
    {
        builder.ToTable("ObjectiveGroups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Key).IsRequired().HasMaxLength(128);
        builder.Property(g => g.SeasonKey).HasMaxLength(64);
        builder.Property(g => g.IconKey).HasMaxLength(64);

        builder.Property(g => g.Kind)
            .HasConversion(ObjectiveEnumWire.Converter<ObjectiveKind>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(g => g.CompletionMode)
            .HasConversion(ObjectiveEnumWire.Converter<GroupCompletionMode>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(g => g.Key).IsUnique();

        builder.HasMany(g => g.Translations)
            .WithOne(t => t.Group!)
            .HasForeignKey(t => t.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ObjectiveGroupTranslationConfiguration
    : IEntityTypeConfiguration<ObjectiveGroupTranslation>
{
    public void Configure(EntityTypeBuilder<ObjectiveGroupTranslation> builder)
    {
        builder.ToTable("ObjectiveGroupTranslations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Description).HasMaxLength(512);

        builder.HasIndex(t => new { t.GroupId, t.LangId }).IsUnique();
    }
}

public class UserObjectiveGroupProgressConfiguration
    : IEntityTypeConfiguration<UserObjectiveGroupProgress>
{
    public void Configure(EntityTypeBuilder<UserObjectiveGroupProgress> builder)
    {
        builder.ToTable("UserObjectiveGroupProgress");
        builder.HasKey(p => new { p.UserId, p.GroupId, p.CycleKey });

        builder.Property(p => p.CycleKey).IsRequired().HasMaxLength(32);

        builder.Property(p => p.State)
            .HasConversion(ObjectiveEnumWire.Converter<ObjectiveState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(p => new { p.UserId, p.State })
            .HasDatabaseName("IX_UserObjectiveGroupProgress_User");

        builder.HasOne(p => p.Group)
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade from the account, like every other user-keyed table that can have one — which is
        // what keeps it out of UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserStreakConfiguration : IEntityTypeConfiguration<UserStreak>
{
    public void Configure(EntityTypeBuilder<UserStreak> builder)
    {
        builder.ToTable("UserStreaks");
        builder.HasKey(s => new { s.UserId, s.StreakKey });

        builder.Property(s => s.StreakKey).IsRequired().HasMaxLength(32);
        builder.Property(s => s.LastCycleKey).IsRequired().HasMaxLength(32);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// The same SCREAMING_SNAKE text conversion <c>EnumWire</c> applies to the ledger, reused here so
/// an objective's kind and state read the same way in a SQL window as everything else does.
/// </summary>
internal static class ObjectiveEnumWire
{
    public static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TEnum, string>
        Converter<TEnum>() where TEnum : struct, Enum =>
        new(value => WireEnum.ToWire(value), text => WireEnum.FromWire<TEnum>(text));
}
