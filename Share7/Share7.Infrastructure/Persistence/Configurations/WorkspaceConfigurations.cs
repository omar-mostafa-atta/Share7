using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    public void Configure(EntityTypeBuilder<Draft> builder)
    {
        builder.ToTable("Drafts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Kind).HasConversion<int>();
        builder.Property(d => d.Status).HasConversion<int>();
        builder.Property(d => d.NodeKind).HasMaxLength(32);
        builder.Property(d => d.ScopePath).HasMaxLength(512).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(300).IsRequired();
        builder.Property(d => d.ProposedJson).IsRequired();
        builder.Property(d => d.BaseJson).IsRequired();
        builder.Property(d => d.BaseFingerprint).HasMaxLength(4000).IsRequired();
        builder.Property(d => d.LanguagesTouched).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();

        // **One open draft per target and kind.** Two authors on one lesson share its draft instead of
        // racing two drafts to a merge; a second "rename this chapter" joins the first. Practice drafts
        // have no live target and are exempt.
        builder.HasIndex(d => new { d.NodeId, d.Kind })
            .IsUnique()
            .HasFilter("[IsOpen] = 1 AND [IsPractice] = 0 AND [NodeId] IS NOT NULL");

        builder.HasIndex(d => new { d.Status, d.SubmittedAtUtc });
        builder.HasIndex(d => d.ScopePath);
        builder.HasIndex(d => d.ReleaseId).HasFilter("[ReleaseId] IS NOT NULL");

        builder.HasMany(d => d.Contributors)
            .WithOne()
            .HasForeignKey(c => c.DraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DraftContributorConfiguration : IEntityTypeConfiguration<DraftContributor>
{
    public void Configure(EntityTypeBuilder<DraftContributor> builder)
    {
        builder.ToTable("DraftContributors");
        builder.HasKey(c => new { c.DraftId, c.UserId });
        builder.HasIndex(c => c.UserId);

        // Staff accounts are never deleted (they are deactivated), so this cascade is the account
        // deletion guard's requirement rather than something that runs: see UserOwnedData.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DraftCommentConfiguration : IEntityTypeConfiguration<DraftComment>
{
    public void Configure(EntityTypeBuilder<DraftComment> builder)
    {
        builder.ToTable("DraftComments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Body).HasMaxLength(2000).IsRequired();
        builder.Property(c => c.AnchorJson).HasMaxLength(1000);
        builder.HasIndex(c => new { c.DraftId, c.CreatedAtUtc });

        builder.HasOne<Draft>()
            .WithMany()
            .HasForeignKey(c => c.DraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReviewDecisionConfiguration : IEntityTypeConfiguration<ReviewDecision>
{
    public void Configure(EntityTypeBuilder<ReviewDecision> builder)
    {
        builder.ToTable("ReviewDecisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Verdict).HasConversion<int>();
        builder.Property(r => r.Note).HasMaxLength(2000);
        builder.HasIndex(r => new { r.DraftId, r.CreatedAtUtc });
        builder.HasIndex(r => r.ReviewerUserId);

        builder.HasOne<Draft>()
            .WithMany()
            .HasForeignKey(r => r.DraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DraftPresenceConfiguration : IEntityTypeConfiguration<DraftPresence>
{
    public void Configure(EntityTypeBuilder<DraftPresence> builder)
    {
        builder.ToTable("DraftPresence");
        builder.HasKey(p => new { p.DraftId, p.UserId });

        // Staff accounts are never deleted (they are deactivated), so this cascade is the account
        // deletion guard's requirement rather than something that runs: see UserOwnedData.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Draft>()
            .WithMany()
            .HasForeignKey(p => p.DraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("Releases");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.Reason).HasMaxLength(2000);
        builder.Property(r => r.FailureMessage).HasMaxLength(2000);
        builder.Property(r => r.RowVersion).IsRowVersion();

        // The scheduler's read: what is due.
        builder.HasIndex(r => new { r.Status, r.ScheduledForUtc });

        builder.HasMany(r => r.Entries)
            .WithOne()
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReleaseEntryConfiguration : IEntityTypeConfiguration<ReleaseEntry>
{
    public void Configure(EntityTypeBuilder<ReleaseEntry> builder)
    {
        builder.ToTable("ReleaseEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).HasConversion<int>();
        builder.HasIndex(e => new { e.ReleaseId, e.Sequence });
        builder.HasIndex(e => e.NodeId);
    }
}

public class WorkAssignmentConfiguration : IEntityTypeConfiguration<WorkAssignment>
{
    public void Configure(EntityTypeBuilder<WorkAssignment> builder)
    {
        builder.ToTable("StudioAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.Note).HasMaxLength(1000);
        builder.HasIndex(a => new { a.AssigneeUserId, a.Status });
        builder.HasIndex(a => a.NodeId);
    }
}

public class StudioNotificationConfiguration : IEntityTypeConfiguration<StudioNotification>
{
    public void Configure(EntityTypeBuilder<StudioNotification> builder)
    {
        builder.ToTable("StudioNotifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasMaxLength(64).IsRequired();
        builder.HasIndex(n => new { n.UserId, n.ReadAtUtc, n.CreatedAtUtc });

        // Staff accounts are never deleted (they are deactivated), so this cascade is the account
        // deletion guard's requirement rather than something that runs: see UserOwnedData.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
