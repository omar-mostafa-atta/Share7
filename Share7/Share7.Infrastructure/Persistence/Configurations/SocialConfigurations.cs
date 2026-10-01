using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Domain.Social;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class PlayerEventConfiguration : IEntityTypeConfiguration<PlayerEvent>
{
    public void Configure(EntityTypeBuilder<PlayerEvent> builder)
    {
        builder.ToTable("PlayerEvents");

        // The identity is the cursor. Ever-increasing across all recipients, which is all a
        // per-recipient order needs: a recipient's events are a subsequence of it.
        builder.HasKey(e => e.Sequence);
        builder.Property(e => e.Sequence).UseIdentityColumn();

        builder.HasIndex(e => e.EventId).IsUnique().HasDatabaseName("UQ_PlayerEvent_EventId");

        builder.Property(e => e.Type).IsRequired().HasMaxLength(64);
        builder.Property(e => e.PayloadJson).IsRequired().HasMaxLength(4000);

        // The feed's only query: this recipient's events after a cursor, in order, still current.
        builder.HasIndex(e => new { e.RecipientUserId, e.Sequence })
            .IncludeProperties(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_PlayerEvent_Feed");

        // Retention's delete.
        builder.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("IX_PlayerEvent_Occurred");

        // One path from the account, so the database removes a deleted player's feed itself.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PlayerBlockConfiguration : IEntityTypeConfiguration<PlayerBlock>
{
    public void Configure(EntityTypeBuilder<PlayerBlock> builder)
    {
        builder.ToTable("PlayerBlocks");

        // Blocking twice is the same block.
        builder.HasKey(b => new { b.UserId, b.BlockedUserId });

        // "Has anyone blocked me" — the other half of either-direction.
        builder.HasIndex(b => b.BlockedUserId).HasDatabaseName("IX_PlayerBlock_Blocked");

        // Two foreign keys to the same account table cannot both cascade, so both are NoAction and
        // the rows are purged by UserOwnedData, on either column.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.BlockedUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class PlayerPresenceConfiguration : IEntityTypeConfiguration<PlayerPresence>
{
    public void Configure(EntityTypeBuilder<PlayerPresence> builder)
    {
        builder.ToTable("PlayerPresence");
        builder.HasKey(p => p.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SessionInvitationConfiguration : IEntityTypeConfiguration<SessionInvitation>
{
    public void Configure(EntityTypeBuilder<SessionInvitation> builder)
    {
        builder.ToTable("SessionInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.State)
            .HasConversion(EnumWire.Converter<InvitationState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne(i => i.Session)
            .WithMany()
            .HasForeignKey(i => i.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, and purged by UserOwnedData on both columns: the session already cascades from
        // its host, and SQL Server allows one cascade path into a table.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.SenderUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.RecipientUserId).OnDelete(DeleteBehavior.NoAction);

        // One pending invite per room and recipient — the collapse of repeated presses and of two
        // players inviting the same friend.
        builder.HasIndex(i => new { i.SessionId, i.RecipientUserId })
            .IsUnique()
            .HasFilter("[State] = 'PENDING'")
            .HasDatabaseName("UQ_SessionInvitation_Pending");

        // "My invites" — the recovery read after a reinstall.
        builder.HasIndex(i => new { i.RecipientUserId, i.State }).HasDatabaseName("IX_SessionInvitation_Recipient");
        builder.HasIndex(i => i.SenderUserId).HasDatabaseName("IX_SessionInvitation_Sender");
    }
}

public class ChallengeConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> builder)
    {
        builder.ToTable("Challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.State)
            .HasConversion(EnumWire.Converter<ChallengeState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.Outcome)
            .HasConversion(EnumWire.Converter<ChallengeOutcome>())
            .HasMaxLength(16)
            .IsRequired();

        // Two accounts named, neither cascading; purged by UserOwnedData on both columns.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.ChallengerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.RecipientUserId).OnDelete(DeleteBehavior.NoAction);

        // One open challenge per pair and lesson: sending it again returns the one already out.
        builder.HasIndex(c => new { c.ChallengerUserId, c.RecipientUserId, c.LessonId })
            .IsUnique()
            .HasFilter("[State] IN ('PENDING', 'ACCEPTED')")
            .HasDatabaseName("UQ_Challenge_Open");

        builder.HasIndex(c => new { c.RecipientUserId, c.State }).HasDatabaseName("IX_Challenge_Recipient");

        // The settlement pass: open challenges, soonest deadline first.
        builder.HasIndex(c => new { c.State, c.DeadlineUtc }).HasDatabaseName("IX_Challenge_Due");
    }
}

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendships");
        builder.HasKey(f => new { f.UserId, f.FriendUserId });

        builder.HasIndex(f => f.FriendUserId).HasDatabaseName("IX_Friendship_Friend");

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.FriendUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class PlayerFriendCodeConfiguration : IEntityTypeConfiguration<PlayerFriendCode>
{
    public void Configure(EntityTypeBuilder<PlayerFriendCode> builder)
    {
        builder.ToTable("PlayerFriendCodes");
        builder.HasKey(c => c.UserId);

        builder.Property(c => c.Code).IsRequired().HasMaxLength(12);
        builder.HasIndex(c => c.Code).IsUnique().HasDatabaseName("UQ_PlayerFriendCode_Code");

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        builder.ToTable("FriendRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.State)
            .HasConversion(EnumWire.Converter<FriendRequestState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.SenderUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.RecipientUserId).OnDelete(DeleteBehavior.NoAction);

        // Entering the same code twice is the same request.
        builder.HasIndex(r => new { r.SenderUserId, r.RecipientUserId })
            .IsUnique()
            .HasFilter("[State] = 'PENDING'")
            .HasDatabaseName("UQ_FriendRequest_Pending");

        builder.HasIndex(r => new { r.RecipientUserId, r.State }).HasDatabaseName("IX_FriendRequest_Recipient");
    }
}

public class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("Parties");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.State)
            .HasConversion(EnumWire.Converter<PartyState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasMany(p => p.Members).WithOne(m => m.Party).HasForeignKey(m => m.PartyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PartyMemberConfiguration : IEntityTypeConfiguration<PartyMember>
{
    public void Configure(EntityTypeBuilder<PartyMember> builder)
    {
        builder.ToTable("PartyMembers");
        builder.HasKey(m => m.Id);

        // The only path from an account into this table, so it can cascade.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);

        // One live party per account — the guarantee, not just the read that usually spots it first.
        builder.HasIndex(m => m.UserId)
            .IsUnique()
            .HasFilter("[LeftAtUtc] IS NULL")
            .HasDatabaseName("UQ_PartyMember_OneLiveParty");

        builder.HasIndex(m => new { m.PartyId, m.LeftAtUtc }).HasDatabaseName("IX_PartyMember_Party");
    }
}

public class PartyInvitationConfiguration : IEntityTypeConfiguration<PartyInvitation>
{
    public void Configure(EntityTypeBuilder<PartyInvitation> builder)
    {
        builder.ToTable("PartyInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.State)
            .HasConversion(EnumWire.Converter<InvitationState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne(i => i.Party).WithMany().HasForeignKey(i => i.PartyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.SenderUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.RecipientUserId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(i => new { i.PartyId, i.RecipientUserId })
            .IsUnique()
            .HasFilter("[State] = 'PENDING'")
            .HasDatabaseName("UQ_PartyInvitation_Pending");

        builder.HasIndex(i => new { i.RecipientUserId, i.State }).HasDatabaseName("IX_PartyInvitation_Recipient");
    }
}

public class PlayerRatingConfiguration : IEntityTypeConfiguration<PlayerRating>
{
    public void Configure(EntityTypeBuilder<PlayerRating> builder)
    {
        builder.ToTable("PlayerRatings");
        builder.HasKey(r => new { r.UserId, r.ModeId });

        builder.Property(r => r.SeasonKey).IsRequired().HasMaxLength(16);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Share7.Domain.Play.GameMode>().WithMany().HasForeignKey(r => r.ModeId).OnDelete(DeleteBehavior.Cascade);

        // Ticket matchmaking reads a pool's ratings by mode.
        builder.HasIndex(r => r.ModeId).HasDatabaseName("IX_PlayerRating_Mode");
    }
}

public class PlayerRatingChangeConfiguration : IEntityTypeConfiguration<PlayerRatingChange>
{
    public void Configure(EntityTypeBuilder<PlayerRatingChange> builder)
    {
        builder.ToTable("PlayerRatingChanges");

        // One change per match and player: the guarantee a match is rated once.
        builder.HasKey(c => new { c.SessionId, c.UserId });

        builder.Property(c => c.SkippedReason).HasMaxLength(32);
        builder.Property(c => c.SeasonKey).IsRequired().HasMaxLength(16);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        // The anti-boosting look-back: this player's rated matches in the last day.
        builder.HasIndex(c => new { c.UserId, c.CreatedAtUtc }).HasDatabaseName("IX_PlayerRatingChange_UserTime");
    }
}

public class RankedSeasonStandingConfiguration : IEntityTypeConfiguration<RankedSeasonStanding>
{
    public void Configure(EntityTypeBuilder<RankedSeasonStanding> builder)
    {
        builder.ToTable("RankedSeasonStandings");
        builder.HasKey(s => new { s.UserId, s.ModeId, s.SeasonKey });

        builder.Property(s => s.SeasonKey).HasMaxLength(16);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Share7.Domain.Play.GameMode>().WithMany().HasForeignKey(s => s.ModeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MatchmakingTicketConfiguration : IEntityTypeConfiguration<MatchmakingTicket>
{
    public void Configure(EntityTypeBuilder<MatchmakingTicket> builder)
    {
        builder.ToTable("MatchmakingTickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.State)
            .HasConversion(EnumWire.Converter<TicketState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.Region).HasMaxLength(32);
        builder.Property(t => t.EndReason).HasMaxLength(32);
        builder.Property(t => t.RequestId).HasMaxLength(128);

        // The owner is a member too; the membership is what cascades from the account.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(t => t.Members).WithOne(m => m.Ticket).HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Lessons).WithOne(l => l.Ticket).HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);

        // The worker's read: searching tickets, oldest first.
        builder.HasIndex(t => new { t.State, t.EnqueuedAtUtc }).HasDatabaseName("IX_MatchmakingTicket_Searching");

        // "My ticket", and the requeue pass's join to sessions.
        builder.HasIndex(t => t.OwnerUserId).HasDatabaseName("IX_MatchmakingTicket_Owner");
        builder.HasIndex(t => t.SessionId).HasDatabaseName("IX_MatchmakingTicket_Session");
    }
}

public class MatchmakingTicketMemberConfiguration : IEntityTypeConfiguration<MatchmakingTicketMember>
{
    public void Configure(EntityTypeBuilder<MatchmakingTicketMember> builder)
    {
        builder.ToTable("MatchmakingTicketMembers");
        builder.HasKey(m => new { m.TicketId, m.UserId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);

        // One live ticket per account — the guarantee behind "you are already searching".
        builder.HasIndex(m => m.UserId)
            .IsUnique()
            .HasFilter("[IsLive] = 1")
            .HasDatabaseName("UQ_MatchmakingTicketMember_Live");
    }
}

public class MatchmakingTicketLessonConfiguration : IEntityTypeConfiguration<MatchmakingTicketLesson>
{
    public void Configure(EntityTypeBuilder<MatchmakingTicketLesson> builder)
    {
        builder.ToTable("MatchmakingTicketLessons");
        builder.HasKey(l => new { l.TicketId, l.LessonId });
    }
}
