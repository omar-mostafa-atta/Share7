using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Play;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// The mode catalogue's schema. Two of these indexes are invariants rather than optimisations —
/// one default per game, and one default economy profile — and they are enforced in the database
/// because "exactly one" is not a thing application code can guarantee against two writers.
/// </summary>
public class GameModeConfiguration : IEntityTypeConfiguration<GameMode>
{
    public void Configure(EntityTypeBuilder<GameMode> builder)
    {
        builder.ToTable("GameModes");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.ModeKey).IsRequired().HasMaxLength(128);

        // Stored as the bitfield, not as text: matchmaking filters on it, and a flags set spelled
        // out as a comma-joined string cannot be answered by an index.
        builder.Property(m => m.Topologies).HasConversion<int>();

        builder.HasOne(m => m.Game)
            .WithMany()
            .HasForeignKey(m => m.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // A mode is meaningless without its game, and deleting a game already destroys its progress.

        builder.HasOne(m => m.EntitlementProduct)
            .WithMany()
            .HasForeignKey(m => m.EntitlementProductId)
            .OnDelete(DeleteBehavior.NoAction);

        // NoAction, not Cascade: retiring a product must never delete the mode that was sold with
        // it, because runs and boards key on that mode. The authoring path clears the reference.

        builder.HasOne(m => m.EconomyProfile)
            .WithMany()
            .HasForeignKey(m => m.EconomyProfileId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(m => m.ModeKey)
            .IsUnique()
            .HasDatabaseName("UX_GameMode_Key");

        // Exactly one default per game. The compatibility path — a client that sends no modeKey —
        // resolves through this, so two defaults would price identical runs differently depending
        // on which row the query happened to return first.
        builder.HasIndex(m => m.GameId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_GameMode_Default");

        // The client read: one game's offered modes, in picker order.
        builder.HasIndex(m => new { m.GameId, m.IsActive, m.SortOrder })
            .HasDatabaseName("IX_GameMode_Offered");
    }
}

public class GameModeTranslationConfiguration : IEntityTypeConfiguration<GameModeTranslation>
{
    public void Configure(EntityTypeBuilder<GameModeTranslation> builder)
    {
        builder.ToTable("GameModeTranslations");
        builder.HasKey(t => new { t.ModeId, t.LangId });

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);

        builder.HasOne(t => t.Mode)
            .WithMany(m => m.Translations)
            .HasForeignKey(t => t.ModeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EconomyProfileConfiguration : IEntityTypeConfiguration<EconomyProfile>
{
    public void Configure(EntityTypeBuilder<EconomyProfile> builder)
    {
        builder.ToTable("EconomyProfiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.ProfileKey).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);

        builder.HasIndex(p => p.ProfileKey)
            .IsUnique()
            .HasDatabaseName("UX_EconomyProfile_Key");

        // One fallback, or none: every mode that names no profile reads this row, and two of them
        // would make the platform's default depend on query order.
        builder.HasIndex(p => p.IsDefault)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_EconomyProfile_Default");
    }
}
