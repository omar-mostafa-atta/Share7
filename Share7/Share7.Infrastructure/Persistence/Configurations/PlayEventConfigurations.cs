using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Play;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// Events, their prize tables, and what those tables paid.
/// <para>
/// The delete behaviours here are the interesting part. An event cascades to the things that only
/// describe it — its text and its tiers — and refuses to cascade into anything that records what
/// actually happened to a player. An award is evidence that a child won something; it does not
/// disappear because somebody tidied up a finished competition.
/// </para>
/// </summary>
public class PlayEventConfiguration : IEntityTypeConfiguration<PlayEvent>
{
    public void Configure(EntityTypeBuilder<PlayEvent> builder)
    {
        builder.ToTable("PlayEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventKey).IsRequired().HasMaxLength(128);
        builder.Property(e => e.WorldKey).HasMaxLength(128);
        builder.Property(e => e.BannerAddress).HasMaxLength(256);
        builder.Property(e => e.AccentColor).HasMaxLength(9);
        builder.Property(e => e.CancelReason).HasMaxLength(512);
        builder.Property(e => e.PrizeCohort).HasConversion<int>();

        builder.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.Mode)
            .WithMany()
            .HasForeignKey(e => e.ModeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.Board)
            .WithMany()
            .HasForeignKey(e => e.BoardId)
            .OnDelete(DeleteBehavior.NoAction);

        // NoAction throughout: a board is retired rather than deleted, and an event whose board
        // vanished would be a settled prize table pointing at nothing.

        builder.HasOne(e => e.Cycle)
            .WithMany()
            .HasForeignKey(e => e.CycleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.EntryProduct)
            .WithMany()
            .HasForeignKey(e => e.EntryProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.EconomyProfile)
            .WithMany()
            .HasForeignKey(e => e.EconomyProfileId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => e.EventKey)
            .IsUnique()
            .HasDatabaseName("UX_PlayEvent_Key");

        // The client listing: this game's live events, headline first.
        builder.HasIndex(e => new { e.GameId, e.IsActive, e.SortOrder })
            .HasDatabaseName("IX_PlayEvent_Listing");

        // Settlement's lookup: which event, if any, owns the cycle that just closed.
        builder.HasIndex(e => e.CycleId)
            .IsUnique()
            .HasDatabaseName("UX_PlayEvent_Cycle");
    }
}

public class PlayEventTranslationConfiguration : IEntityTypeConfiguration<PlayEventTranslation>
{
    public void Configure(EntityTypeBuilder<PlayEventTranslation> builder)
    {
        builder.ToTable("PlayEventTranslations");
        builder.HasKey(t => new { t.EventId, t.LangId });

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Rules).HasMaxLength(4000);

        builder.HasOne(t => t.Event)
            .WithMany(e => e.Translations)
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EventPrizeTierConfiguration : IEntityTypeConfiguration<EventPrizeTier>
{
    public void Configure(EntityTypeBuilder<EventPrizeTier> builder)
    {
        builder.ToTable("EventPrizeTiers");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Kind).HasConversion(EnumWire.Converter<EventPrizeKind>()).HasMaxLength(16);
        builder.Property(t => t.ValueCurrencyCode).HasMaxLength(3);

        builder.HasOne(t => t.Event)
            .WithMany(e => e.PrizeTiers)
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.RewardRule)
            .WithMany()
            .HasForeignKey(t => t.RewardRuleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(t => new { t.EventId, t.FromRank })
            .HasDatabaseName("IX_EventPrizeTier_Event");
    }
}

public class EventPrizeTierTranslationConfiguration : IEntityTypeConfiguration<EventPrizeTierTranslation>
{
    public void Configure(EntityTypeBuilder<EventPrizeTierTranslation> builder)
    {
        builder.ToTable("EventPrizeTierTranslations");
        builder.HasKey(t => new { t.TierId, t.LangId });

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);

        builder.HasOne(t => t.Tier)
            .WithMany(t => t.Translations)
            .HasForeignKey(t => t.TierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EventAwardConfiguration : IEntityTypeConfiguration<EventAward>
{
    public void Configure(EntityTypeBuilder<EventAward> builder)
    {
        builder.ToTable("EventAwards");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.State).HasConversion(EnumWire.Converter<EventAwardState>()).HasMaxLength(24);
        builder.Property(a => a.Cohort).HasConversion<int>();

        builder.HasOne(a => a.Event)
            .WithMany()
            .HasForeignKey(a => a.EventId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.Tier)
            .WithMany()
            .HasForeignKey(a => a.TierId)
            .OnDelete(DeleteBehavior.NoAction);

        // **The idempotency guarantee for the whole prize path.** Settlement is a retried job, so
        // one placing has to be able to win once no matter how many times the job runs — and the
        // database is what enforces it, not a flag the retry has to trust.
        builder.HasIndex(a => new { a.EventId, a.Cohort, a.CohortKey, a.UserId })
            .IsUnique()
            .HasDatabaseName("UX_EventAward_Placing");

        // "What have I won?" — the client's own read, newest first.
        builder.HasIndex(a => new { a.UserId, a.CreatedAtUtc })
            .HasDatabaseName("IX_EventAward_User");
    }
}

public class PrizeClaimConfiguration : IEntityTypeConfiguration<PrizeClaim>
{
    public void Configure(EntityTypeBuilder<PrizeClaim> builder)
    {
        builder.ToTable("PrizeClaims");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.State).HasConversion(EnumWire.Converter<PrizeClaimState>()).HasMaxLength(24);
        builder.Property(c => c.ReviewNote).HasMaxLength(1000);

        builder.HasOne(c => c.Award)
            .WithOne(a => a.Claim)
            .HasForeignKey<PrizeClaim>(c => c.AwardId)
            .OnDelete(DeleteBehavior.NoAction);

        // One claim per award, and no second one can be opened for a prize already being handled.
        builder.HasIndex(c => c.AwardId)
            .IsUnique()
            .HasDatabaseName("UX_PrizeClaim_Award");

        // The operator queue: everything still waiting on a person, oldest first.
        builder.HasIndex(c => new { c.State, c.CreatedAtUtc })
            .HasDatabaseName("IX_PrizeClaim_Queue");

        // The sweep that forfeits a claim nobody answered.
        builder.HasIndex(c => c.ExpiresAtUtc)
            .HasDatabaseName("IX_PrizeClaim_Expiry");
    }
}

public class GameWorldConfiguration : IEntityTypeConfiguration<GameWorld>
{
    public void Configure(EntityTypeBuilder<GameWorld> builder)
    {
        builder.ToTable("GameWorlds");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.WorldKey).IsRequired().HasMaxLength(128);
        builder.Property(w => w.UnlockKind).HasConversion(EnumWire.Converter<WorldUnlockKind>()).HasMaxLength(16);

        builder.HasOne(w => w.Game)
            .WithMany()
            .HasForeignKey(w => w.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.Product)
            .WithMany()
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        // A world key is the client's own environment id, and two rows for one id would make
        // ownership depend on which the query returned.
        builder.HasIndex(w => new { w.GameId, w.WorldKey })
            .IsUnique()
            .HasDatabaseName("UX_GameWorld_Key");

        // One fallback world per game: what a session runs in when nothing was chosen or owned.
        builder.HasIndex(w => w.GameId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_GameWorld_Default");

        builder.HasIndex(w => new { w.GameId, w.IsActive, w.SortOrder })
            .HasDatabaseName("IX_GameWorld_Listing");
    }
}

public class GameWorldTranslationConfiguration : IEntityTypeConfiguration<GameWorldTranslation>
{
    public void Configure(EntityTypeBuilder<GameWorldTranslation> builder)
    {
        builder.ToTable("GameWorldTranslations");
        builder.HasKey(t => new { t.WorldId, t.LangId });

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);

        builder.HasOne(t => t.World)
            .WithMany(w => w.Translations)
            .HasForeignKey(t => t.WorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
