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

        // One live rematch per ended match: the second player to ask is handed the first one's room.
        // Live only, so a rematch whose host vanished before confirming it does not spend the match's
        // one chance.
        builder.HasIndex(s => s.RematchOfSessionId)
            .IsUnique()
            .HasFilter("[RematchOfSessionId] IS NOT NULL AND " + MultiplayerFilters.SessionIsLive)
            .HasDatabaseName("UQ_MultiplayerSession_RematchOf");

        // One live room per tournament pairing: the second of the pair to press play is handed the
        // first one's room. Live only, so a room that never started gives the pairing back.
        builder.HasIndex(s => s.TournamentMatchId)
            .IsUnique()
            .HasFilter("[TournamentMatchId] IS NOT NULL AND " + MultiplayerFilters.SessionIsLive)
            .HasDatabaseName("UQ_MultiplayerSession_TournamentMatch");


        // The matchmaking candidate query, in key order. LessonId is last because it is the only
        // optional filter — omitting it still leaves a usable index prefix, which is what lets one
        // index serve both the filtered and the unfiltered search.
        builder.HasIndex(s => new { s.GameId, s.State, s.Visibility, s.IsRanked, s.ProtocolVersion, s.LessonId })
            .IncludeProperties(s => new { s.CurrentPlayerCount, s.MaxPlayers, s.LastHeartbeatAtUtc, s.CreatedAtUtc })
            .HasDatabaseName("IX_MultiplayerSession_Matchmaking");

        // The subject-scoped candidate query: same game, state, visibility, ranked flag and protocol
        // as the index above, then the four columns that decide whether two children can actually
        // play together — the subject they chose, the language their questions are in, the mode, and
        // the event. LessonId is not in it: a subject-scoped session has none until its roster forms.
        builder.HasIndex(s => new
            {
                s.GameId, s.State, s.Visibility, s.IsRanked, s.ProtocolVersion,
                s.SubjectId, s.LangId, s.ModeId, s.EventId
            })
            .IncludeProperties(s => new
            {
                s.CurrentPlayerCount, s.MaxPlayers, s.LastHeartbeatAtUtc, s.CreatedAtUtc, s.LessonId
            })
            .HasDatabaseName("IX_MultiplayerSession_SubjectMatchmaking");

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

/// <summary>
/// The running intersection of what every seated player can play.
/// <para>
/// Cascades from the session — the set describes a match and is meaningless once it is gone — and
/// restricts from the lesson, so removing a lesson from the curriculum cannot silently empty the
/// candidate set of a match in progress.
/// </para>
/// </summary>
public class MultiplayerSessionEligibleLessonConfiguration
    : IEntityTypeConfiguration<MultiplayerSessionEligibleLesson>
{
    public void Configure(EntityTypeBuilder<MultiplayerSessionEligibleLesson> builder)
    {
        builder.ToTable("MultiplayerSessionEligibleLessons");
        builder.HasKey(l => new { l.SessionId, l.LessonId });

        builder.HasOne(l => l.Session)
            .WithMany(s => s.EligibleLessons)
            .HasForeignKey(l => l.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Lesson)
            .WithMany()
            .HasForeignKey(l => l.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Which lesson should this match play" — the set of one session, least-practised first.
        builder.HasIndex(l => new { l.SessionId, l.SummedBestPercent })
            .HasDatabaseName("IX_SessionEligibleLesson_Pick");

        // The candidate search asks the mirror-image question: which sessions could play a lesson
        // this caller can. Without this it is a scan of every live session's set.
        builder.HasIndex(l => l.LessonId)
            .HasDatabaseName("IX_SessionEligibleLesson_Lesson");
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

        // **One account, one live seat — across every session.** The two indexes above hold within a
        // session; nothing held across them, so two joins (or a join and a create) for the same
        // account landing in different sessions at the same moment both passed the service's read
        // and seated one child twice — typically a retried matchmake, leaving a ghost in somebody
        // else's lobby. The second insert now dies here and is answered ALREADY_IN_SESSION.
        //
        // This makes a standing invariant load-bearing: **a session that ends must release its
        // seats.** Every path that ends one does so in the same transaction, and the sweeper's
        // healing rule catches anything that did not — because a seat stranded in an ended session
        // is now an account that can never be seated again.
        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasFilter(MultiplayerFilters.PlayerIsSeated)
            .HasDatabaseName("UQ_SessionPlayer_OneLiveSeat");

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

/// <summary>
/// A host's removal of one account from one session.
/// <para>
/// Cascades from the session — a ban is about one room and ends with it. The account FK is
/// **NoAction** for the same reason the seat's is: sessions already cascade from the host's account,
/// and SQL Server refuses a second cascade path into this table. The banned account's rows are purged
/// explicitly instead (<c>UserOwnedData.ManuallyPurged</c>).
/// </para>
/// </summary>
public class MultiplayerSessionBanConfiguration : IEntityTypeConfiguration<MultiplayerSessionBan>
{
    public void Configure(EntityTypeBuilder<MultiplayerSessionBan> builder)
    {
        builder.ToTable("MultiplayerSessionBans");

        // One row per (session, account): banning twice is the same ban, which is what makes a
        // retried removal idempotent at the index rather than by a lookup.
        builder.HasKey(b => new { b.SessionId, b.UserId });

        builder.HasOne(b => b.Session)
            .WithMany()
            .HasForeignKey(b => b.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // The account-deletion purge's query, and matchmaking's "not into a room I was removed from".
        builder.HasIndex(b => b.UserId)
            .HasDatabaseName("IX_SessionBan_User");
    }
}

public class MultiplayerSessionReservationConfiguration : IEntityTypeConfiguration<MultiplayerSessionReservation>
{
    public void Configure(EntityTypeBuilder<MultiplayerSessionReservation> builder)
    {
        builder.ToTable("MultiplayerSessionReservations");

        // The key is also the seat step's lookup: (session, account) inside the capacity UPDATE.
        builder.HasKey(r => new { r.SessionId, r.UserId });

        builder.HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction and purged by list, for the bans' reason: a second cascade path from the account.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => r.UserId)
            .HasDatabaseName("IX_SessionReservation_User");
    }
}

/// <summary>
/// The verdict on one match. Keyed by the session, and **not a foreign key to it** — a result is the
/// durable record of a match and outlives the session row once old sessions are archived.
/// </summary>
public class MatchResultConfiguration : IEntityTypeConfiguration<MatchResult>
{
    public void Configure(EntityTypeBuilder<MatchResult> builder)
    {
        builder.ToTable("MatchResults");

        // The primary key is the whole idempotency story: two requests deciding the same match at
        // once both try to insert this row, and exactly one can.
        builder.HasKey(r => r.SessionId);

        builder.Property(r => r.State)
            .HasConversion(EnumWire.Converter<MatchResultState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.WinRuleJson).HasMaxLength(MatchWinRule.MaxJsonLength);
        builder.Property(r => r.DecidedBy).IsRequired().HasMaxLength(32);

        // "Matches decided recently" — operations and, later, analytics.
        builder.HasIndex(r => r.DecidedAtUtc).HasDatabaseName("IX_MatchResult_DecidedAt");
    }
}

/// <summary>
/// One participant's place. Cascades from the result, and from the account: a child's competitive
/// history is deleted with them, never anonymised — the same ruling the leaderboards made.
/// </summary>
public class MatchPlacementConfiguration : IEntityTypeConfiguration<MatchPlacement>
{
    public void Configure(EntityTypeBuilder<MatchPlacement> builder)
    {
        builder.ToTable("MatchPlacements");
        builder.HasKey(p => new { p.SessionId, p.UserId });

        builder.Property(p => p.FlagReason).HasMaxLength(128);
        builder.Property(p => p.ValuesJson).IsRequired().HasMaxLength(1024);

        builder.HasOne(p => p.Result)
            .WithMany(r => r.Placements)
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One cascade path only — results have no account foreign key — so this can cascade.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // "My recent matches".
        builder.HasIndex(p => p.UserId).HasDatabaseName("IX_MatchPlacement_User");
    }
}

/// <summary>
/// A graded attempt attributed to a match. Pointer to the session, not a foreign key: the session
/// already cascades from its host's account, and a second path into this table is one SQL Server
/// refuses.
/// </summary>
public class MatchAttemptScoreConfiguration : IEntityTypeConfiguration<MatchAttemptScore>
{
    public void Configure(EntityTypeBuilder<MatchAttemptScore> builder)
    {
        builder.ToTable("MatchAttemptScores");
        builder.HasKey(s => s.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The verdict's read: one match's scores per player, earliest first.
        builder.HasIndex(s => new { s.SessionId, s.UserId, s.SubmittedAtUtc })
            .HasDatabaseName("IX_MatchAttemptScore_Session");
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
