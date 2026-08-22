using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// The filtered-index predicates for the multiplayer tables, written once.
/// <para>
/// **These strings are load-bearing and they are the easiest thing in this domain to get silently
/// wrong.** They are raw SQL, so they must name the *stored* form of each enum — <c>EnumWire</c>
/// writes <c>SCREAMING_SNAKE</c>, so the token is <c>'CLOSED'</c> and never <c>'Closed'</c>. A
/// predicate that matches nothing produces an index that constrains nothing: every service-level
/// test still passes, and the double-join and duplicate-room defences are simply gone.
/// <c>MultiplayerIndexTests</c> exists to make that failure loud.
/// </para>
/// <para>
/// Written as a chain of <c>&lt;&gt;</c> comparisons rather than <c>NOT IN</c>, because that is what
/// SQL Server's documented grammar for a filtered predicate actually admits — <c>NOT IN</c> happens
/// to work today and is not worth depending on.
/// </para>
/// </summary>
internal static class MultiplayerFilters
{
    /// <summary>
    /// A session still holding its transport name and join code. Terminal sessions release both, so
    /// a room name can be reused once the match it named is over.
    /// </summary>
    public const string SessionIsLive =
        "[State] <> 'CLOSED' AND [State] <> 'ABANDONED' AND [State] <> 'FAILED'";

    /// <summary>
    /// A membership still occupying a seat. Left and Removed release the slot, which is what lets a
    /// child rejoin a session they left without colliding with their own historical row.
    /// </summary>
    public const string PlayerIsSeated =
        "[Status] <> 'LEFT' AND [Status] <> 'REMOVED'";
}

public class MultiplayerSessionConfiguration : IEntityTypeConfiguration<MultiplayerSession>
{
    public void Configure(EntityTypeBuilder<MultiplayerSession> builder)
    {
        builder.ToTable("MultiplayerSessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TransportSessionName).IsRequired().HasMaxLength(64);
        builder.Property(s => s.TransportRegion).HasMaxLength(16);
        builder.Property(s => s.JoinCode).HasMaxLength(8);
        builder.Property(s => s.CurriculumPathJson).HasMaxLength(512);

        builder.Property(s => s.State)
            .HasConversion(EnumWire.Converter<MultiplayerSessionState>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.Visibility)
            .HasConversion(EnumWire.Converter<SessionVisibility>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.ClosedReason)
            .HasConversion(EnumWire.Converter<SessionClosedReason>())
            .HasMaxLength(32);

        // Optimistic concurrency for state transitions and host migration. Two members claiming a
        // vacant host slot both read this value; exactly one write commits, and the loser is told to
        // re-read rather than being allowed to overwrite.
        builder.Property(s => s.RowVersion).IsRowVersion();

        // **The duplicate-room defence.** Two clients that mint the same Photon room name at the
        // same instant cannot both commit — the second takes a unique violation and is answered
        // TRANSPORT_NAME_TAKEN. Filtered to live sessions so the name returns to the pool when the
        // match ends.
        builder.HasIndex(s => s.TransportSessionName)
            .IsUnique()
            .HasFilter(MultiplayerFilters.SessionIsLive)
            .HasDatabaseName("UQ_MultiplayerSession_Transport");

        // The same guarantee for the human-typable code. A null code is not a collision, hence the
        // extra IS NOT NULL — without it every public session would collide with every other.
        builder.HasIndex(s => s.JoinCode)
            .IsUnique()
            .HasFilter("[JoinCode] IS NOT NULL AND " + MultiplayerFilters.SessionIsLive)
            .HasDatabaseName("UQ_MultiplayerSession_JoinCode");

        // The matchmaking candidate query, in key order. LessonId is last because it is the only
        // optional filter — omitting it still leaves a usable index prefix, which is what lets one
        // index serve both the filtered and the unfiltered search.
        builder.HasIndex(s => new { s.GameId, s.State, s.Visibility, s.IsRanked, s.ProtocolVersion, s.LessonId })
            .IncludeProperties(s => new { s.CurrentPlayerCount, s.MaxPlayers, s.LastHeartbeatAtUtc, s.CreatedAtUtc })
            .HasDatabaseName("IX_MultiplayerSession_Matchmaking");

        // The sweeper's only query: everything non-terminal that has stopped heartbeating.
        builder.HasIndex(s => new { s.State, s.LastHeartbeatAtUtc })
            .HasDatabaseName("IX_MultiplayerSession_Sweep");

        // Restrict: a game with sessions against it cannot be deleted, or their history stops
        // resolving. Deactivating the catalog entry is the supported move — the same rule offers
        // follow for products.
        builder.HasOne(s => s.Game)
            .WithMany()
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        // Declared here because Domain cannot see ApplicationUser.
        //
        // **Cascade, and it is the only cascade from AspNetUsers that reaches this domain's rows.**
        // A deleted account takes the sessions it hosted with it, and those take their memberships.
        // The membership FK below is therefore deliberately not a cascade: two cascade paths into
        // MultiplayerSessionPlayers is something SQL Server refuses outright at migration time.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.HostUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MultiplayerSessionPlayerConfiguration : IEntityTypeConfiguration<MultiplayerSessionPlayer>
{
    public void Configure(EntityTypeBuilder<MultiplayerSessionPlayer> builder)
    {
        builder.ToTable("MultiplayerSessionPlayers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status)
            .HasConversion(EnumWire.Converter<SessionPlayerStatus>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(p => p.RowVersion).IsRowVersion();

        // **The double-join defence.** Not a validation — a structural impossibility. Two copies of
        // the same join arriving together both pass any SELECT-based check; the second one dies here
        // and is answered ALREADY_IN_SESSION.
        builder.HasIndex(p => new { p.SessionId, p.UserId })
            .IsUnique()
            .HasFilter(MultiplayerFilters.PlayerIsSeated)
            .HasDatabaseName("UQ_SessionPlayer_Active");

        // Seats are exclusive for the same reason, by the same mechanism.
        builder.HasIndex(p => new { p.SessionId, p.Slot })
            .IsUnique()
            .HasFilter(MultiplayerFilters.PlayerIsSeated)
            .HasDatabaseName("UQ_SessionPlayer_Slot");

        // "Which session am I in?" — the recovery lookup after a crash or reinstall, and the
        // one-active-membership check on every create and join.
        builder.HasIndex(p => new { p.UserId, p.Status })
            .HasDatabaseName("IX_SessionPlayer_User");

        builder.HasOne(p => p.Session)
            .WithMany(s => s.Players)
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // **NoAction, not Cascade** — see the session's host FK above for why the database will not
        // accept a second cascade path here. The account's own rows are removed explicitly instead,
        // which is why MultiplayerSessionPlayer is listed in UserOwnedData.ManuallyPurged; that purge
        // runs inside the deletion transaction and before the user row goes, so this constraint is
        // already satisfied by the time it is checked.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class MultiplayerRequestLogConfiguration : IEntityTypeConfiguration<MultiplayerRequestLog>
{
    public void Configure(EntityTypeBuilder<MultiplayerRequestLog> builder)
    {
        builder.ToTable("MultiplayerRequestLogs");

        // Composite, and scoped per user on purpose: one child's idempotency key must not be able to
        // replay — or block — another child's operation.
        builder.HasKey(l => new { l.UserId, l.RequestId });

        builder.Property(l => l.RequestId).HasMaxLength(128);
        builder.Property(l => l.Operation).IsRequired().HasMaxLength(32);
        builder.Property(l => l.ResponseJson).IsRequired();

        // The retention sweep's query.
        builder.HasIndex(l => l.CreatedAtUtc)
            .HasDatabaseName("IX_MultiplayerRequestLog_Retention");

        // **SessionId is deliberately not a foreign key.** It is a diagnostic pointer, and an FK
        // would introduce a second cascade path from AspNetUsers into this table — the same
        // constraint that shapes the two FKs above.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
