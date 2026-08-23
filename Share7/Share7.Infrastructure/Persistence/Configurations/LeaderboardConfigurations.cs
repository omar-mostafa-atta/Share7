using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Games;
using Share7.Domain.Leaderboards;
using Share7.Domain.LookUps;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// Every index here exists to keep a read an index seek. The deployment is shared IIS with no
/// Redis and a limited CPU budget, so a board page that degrades into a sort over the whole cycle
/// is not a slow page — it is an outage during the exact event that made the board interesting.
/// </summary>
public class GameResultConfiguration : IEntityTypeConfiguration<GameResult>
{
    public void Configure(EntityTypeBuilder<GameResult> builder)
    {
        builder.ToTable("GameResults");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Metric).IsRequired().HasMaxLength(48);
        builder.Property(r => r.RequestId).HasMaxLength(128);
        builder.Property(r => r.FlagReason).HasMaxLength(256);
        builder.Property(r => r.SourceType).HasConversion<int>();

        // The projector's queue: unclaimed rows in arrival order. Filtered so the index stays the
        // size of the backlog rather than the size of history, which is the difference between a
        // seek and a scan once this table is in the millions.
        builder.HasIndex(r => new { r.ProjectedAtUtc, r.OccurredAtUtc })
            .HasFilter("[ProjectedAtUtc] IS NULL")
            .HasDatabaseName("IX_GameResult_Pending");

        // Replaying one cycle's worth of history during a rebuild.
        builder.HasIndex(r => new { r.GameId, r.Metric, r.OccurredAtUtc })
            .HasDatabaseName("IX_GameResult_Replay");

        builder.HasIndex(r => new { r.UserId, r.OccurredAtUtc })
            .HasDatabaseName("IX_GameResult_User");

        // Belt and braces over the attempt path's own idempotency: even if a caller somehow
        // emitted twice for one submission, the second insert cannot land. Filtered because a null
        // RequestId is not a collision.
        builder.HasIndex(r => new { r.UserId, r.RequestId, r.Metric })
            .IsUnique()
            .HasFilter("[RequestId] IS NOT NULL")
            .HasDatabaseName("UX_GameResult_Submission");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction: retiring a game must not erase the history of what children did in it, and the
        // cascade from AspNetUsers already reaches this table by one path.
        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(r => r.GameId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class LeaderboardBoardConfiguration : IEntityTypeConfiguration<LeaderboardBoard>
{
    public void Configure(EntityTypeBuilder<LeaderboardBoard> builder)
    {
        builder.ToTable("LeaderboardBoards");
        builder.HasKey(b => b.Id);

        // 110, not 128: settlement builds "{BoardKey}:{band}" and feeds it to a reward rule's
        // 128-character ReferenceKey. Capping the input is cheaper than discovering the overflow
        // when a prize fails to pay.
        builder.Property(b => b.BoardKey).IsRequired().HasMaxLength(110);
        builder.Property(b => b.Metric).IsRequired().HasMaxLength(48);
        builder.Property(b => b.SupportedCohorts).IsRequired().HasMaxLength(128);

        builder.Property(b => b.SortDirection).HasConversion<int>();
        builder.Property(b => b.Aggregation).HasConversion<int>();
        builder.Property(b => b.Period).HasConversion<int>();

        builder.HasIndex(b => b.BoardKey).IsUnique();

        builder.HasIndex(b => new { b.IsActive, b.GameId })
            .HasDatabaseName("IX_LeaderboardBoard_Listing");

        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(b => b.GameId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class LeaderboardBoardTranslationConfiguration : IEntityTypeConfiguration<LeaderboardBoardTranslation>
{
    public void Configure(EntityTypeBuilder<LeaderboardBoardTranslation> builder)
    {
        builder.ToTable("LeaderboardBoardTranslations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Description).HasMaxLength(512);

        builder.HasIndex(t => new { t.BoardId, t.LangId }).IsUnique();

        builder.HasOne(t => t.Board)
            .WithMany(b => b.Translations)
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class LeaderboardCycleConfiguration : IEntityTypeConfiguration<LeaderboardCycle>
{
    public void Configure(EntityTypeBuilder<LeaderboardCycle> builder)
    {
        builder.ToTable("LeaderboardCycles");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.State).HasConversion<int>();

        // "Which cycle is open for this board right now", which every ingestion runs.
        builder.HasIndex(c => new { c.BoardId, c.State, c.StartsAtUtc })
            .HasDatabaseName("IX_LeaderboardCycle_Board_State");

        // The rollover job's sweep: cycles whose window has moved on, across all boards.
        builder.HasIndex(c => new { c.State, c.EndsAtUtc })
            .HasDatabaseName("IX_LeaderboardCycle_Rollover");

        // A board cannot have two windows starting at the same instant. This is what makes cycle
        // generation safe to run from two workers at once — the loser collides instead of
        // creating a duplicate window that would split a week's ranking in half.
        builder.HasIndex(c => new { c.BoardId, c.StartsAtUtc })
            .IsUnique()
            .HasDatabaseName("UX_LeaderboardCycle_Window");

        builder.HasOne(c => c.Board)
            .WithMany(b => b.Cycles)
            .HasForeignKey(c => c.BoardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeaderboardEntryConfiguration : IEntityTypeConfiguration<LeaderboardEntry>
{
    public void Configure(EntityTypeBuilder<LeaderboardEntry> builder)
    {
        builder.ToTable("LeaderboardEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Cohort).HasConversion<int>();
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(48);
        builder.Property(e => e.AvatarKey).HasMaxLength(64);

        // One row per player per cohort per cycle. Also the projector's upsert target.
        builder.HasIndex(e => new { e.CycleId, e.Cohort, e.CohortKey, e.UserId })
            .IsUnique()
            .HasDatabaseName("UX_LeaderboardEntry_Member");

        // The page read, and the only index that matters for latency. Rank is materialised so this
        // is a seek to an offset and a forward scan of one page — never a sort.
        builder.HasIndex(e => new { e.CycleId, e.Cohort, e.CohortKey, e.Rank })
            .HasDatabaseName("IX_LeaderboardEntry_Page");

        // The reindex job's ordering: value first, then the tie-break, in one covering index so
        // recomputing ranks does not sort.
        builder.HasIndex(e => new { e.CycleId, e.Cohort, e.CohortKey, e.Value, e.AchievedAtUtc })
            .HasDatabaseName("IX_LeaderboardEntry_Ordering");

        builder.HasOne(e => e.Cycle)
            .WithMany(c => c.Entries)
            .HasForeignKey(e => e.CycleId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction: the cascade from AspNetUsers already reaches here through the cycle's board,
        // and SQL Server refuses two cascade paths into one table. Account deletion clears these
        // explicitly instead.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class LeaderboardSettlementConfiguration : IEntityTypeConfiguration<LeaderboardSettlement>
{
    public void Configure(EntityTypeBuilder<LeaderboardSettlement> builder)
    {
        builder.ToTable("LeaderboardSettlements");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Cohort).HasConversion<int>();
        builder.Property(s => s.RewardReferenceKey).HasMaxLength(128);

        // **The idempotency guarantee for payment.** The settlement job is retried by design, so
        // one row per (cycle, cohort, player) is what stops a retry paying a child twice.
        builder.HasIndex(s => new { s.CycleId, s.Cohort, s.CohortKey, s.UserId })
            .IsUnique()
            .HasDatabaseName("UX_LeaderboardSettlement_Award");

        builder.HasIndex(s => new { s.UserId, s.CreatedAtUtc })
            .HasDatabaseName("IX_LeaderboardSettlement_User");

        builder.HasOne(s => s.Cycle)
            .WithMany()
            .HasForeignKey(s => s.CycleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class LeaderboardJobConfiguration : IEntityTypeConfiguration<LeaderboardJob>
{
    public void Configure(EntityTypeBuilder<LeaderboardJob> builder)
    {
        builder.ToTable("LeaderboardJobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Kind).HasConversion<int>();
        builder.Property(j => j.State).HasConversion<int>();
        builder.Property(j => j.LastError).HasMaxLength(1024);

        // The claim query: due, unclaimed, oldest first.
        builder.HasIndex(j => new { j.State, j.RunAfterUtc })
            .HasDatabaseName("IX_LeaderboardJob_Claimable");

        // At most one outstanding job of a kind per cycle, so a read that lazily enqueues work
        // cannot pile up a thousand duplicate reindexes under load. Filtered to the states where
        // that matters — completed jobs are history and may repeat.
        builder.HasIndex(j => new { j.Kind, j.CycleId })
            .IsUnique()
            .HasFilter("[State] IN (0, 1) AND [CycleId] IS NOT NULL")
            .HasDatabaseName("UX_LeaderboardJob_Outstanding");
    }
}

public class PlayerDisplayNameConfiguration : IEntityTypeConfiguration<PlayerDisplayName>
{
    public void Configure(EntityTypeBuilder<PlayerDisplayName> builder)
    {
        builder.ToTable("PlayerDisplayNames");
        builder.HasKey(n => n.UserId);

        builder.Property(n => n.Handle).IsRequired().HasMaxLength(48);
        builder.Property(n => n.Source).HasConversion<int>();

        // Unique so a row identifies a player without exposing their account id, and so the
        // generator's collision retry has something to collide with.
        builder.HasIndex(n => n.Handle).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeaderboardMetricBoundConfiguration : IEntityTypeConfiguration<LeaderboardMetricBound>
{
    public void Configure(EntityTypeBuilder<LeaderboardMetricBound> builder)
    {
        builder.ToTable("LeaderboardMetricBounds");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Metric).IsRequired().HasMaxLength(48);

        // One bound per game and metric. A second row for the same pair would make which limit
        // applies depend on row order, which is not a thing an operator can reason about.
        builder.HasIndex(b => new { b.GameId, b.Metric })
            .IsUnique()
            .HasDatabaseName("UX_LeaderboardMetricBound_Scope");

        // The ingestion lookup, which runs on the gameplay request.
        builder.HasIndex(b => new { b.Metric, b.Enabled })
            .HasDatabaseName("IX_LeaderboardMetricBound_Lookup");

        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(b => b.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
