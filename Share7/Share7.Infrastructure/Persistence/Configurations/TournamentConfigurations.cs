using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.ToTable("Tournaments");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(80);
        builder.Property(t => t.CancelReason).HasMaxLength(200);

        builder.Property(t => t.Format)
            .HasConversion(EnumWire.Converter<TournamentFormat>())
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(t => t.State)
            .HasConversion(EnumWire.Converter<TournamentState>())
            .HasMaxLength(16)
            .IsRequired();

        // Game, mode, event and class are read and validated by the service rather than tied by
        // foreign keys, like a ticket's: a tournament is history, and must never be the reason an
        // operator cannot retire a mode or archive a class.
        builder.HasMany(t => t.Entries).WithOne(e => e.Tournament).HasForeignKey(e => e.TournamentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Matches).WithOne(m => m.Tournament).HasForeignKey(m => m.TournamentId).OnDelete(DeleteBehavior.Cascade);

        // The sweeper's read: what is due to start, and what is running.
        builder.HasIndex(t => new { t.State, t.StartsAtUtc }).HasDatabaseName("IX_Tournament_State");

        // One non-cancelled bracket owns an event's prize table, including after it completes.
        builder.HasIndex(t => t.EventId).IsUnique()
            .HasFilter("[EventId] IS NOT NULL AND [State] <> 'CANCELLED'")
            .HasDatabaseName("UQ_Tournament_Event");
        builder.HasIndex(t => t.CohortId).HasDatabaseName("IX_Tournament_Cohort");
    }
}

public class TournamentEntryConfiguration : IEntityTypeConfiguration<TournamentEntry>
{
    public void Configure(EntityTypeBuilder<TournamentEntry> builder)
    {
        builder.ToTable("TournamentEntries");

        // One entry per player per tournament: registering twice finds the first.
        builder.HasKey(e => new { e.TournamentId, e.UserId });

        builder.Property(e => e.State)
            .HasConversion(EnumWire.Converter<TournamentEntryState>())
            .HasMaxLength(16)
            .IsRequired();

        // An entry is about the player, so it goes with their account.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

        // "My tournaments".
        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_TournamentEntry_User");
    }
}

public class TournamentMatchConfiguration : IEntityTypeConfiguration<TournamentMatch>
{
    public void Configure(EntityTypeBuilder<TournamentMatch> builder)
    {
        builder.ToTable("TournamentMatches");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.State)
            .HasConversion(EnumWire.Converter<TournamentMatchState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(m => m.Outcome).HasMaxLength(16);

        // One pairing per slot: a round written twice — two servers advancing at once — cannot commit.
        builder.HasIndex(m => new { m.TournamentId, m.Round, m.Position })
            .IsUnique()
            .HasDatabaseName("UQ_TournamentMatch_Slot");

        // The two sides, for "my match" and for blanking an erased account's side.
        builder.HasIndex(m => m.PlayerAUserId).HasDatabaseName("IX_TournamentMatch_PlayerA");
        builder.HasIndex(m => m.PlayerBUserId).HasDatabaseName("IX_TournamentMatch_PlayerB");
    }
}
