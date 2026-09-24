using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Recovery;

namespace Share7.Infrastructure.Persistence.Configurations;

public class RecoveryRuleConfiguration : IEntityTypeConfiguration<RecoveryRule>
{
    public void Configure(EntityTypeBuilder<RecoveryRule> builder)
    {
        builder.ToTable("RecoveryRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ScopePath).HasMaxLength(512).IsRequired();
        builder.Property(r => r.NodeKind).HasMaxLength(32).IsRequired();

        // **One live rule per node.** Superseded rows stay for ever, so the filter is what makes
        // "the rule in force here" a single row rather than a date comparison at serve time.
        builder.HasIndex(r => r.NodeId)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [TargetId] IS NULL");

        // What the game reads: every active rule whose path is a prefix of the lesson's, longest
        // first. One indexed scan, no tree walk.
        builder.HasIndex(r => new { r.IsActive, r.ScopePath });

        builder.HasIndex(r => r.ReleaseId).HasFilter("[ReleaseId] IS NOT NULL");

        // Per-skill rules are the next step and not the pilot's; the index is here so that adding
        // them later is a new row rather than a migration on a table the game is reading.
        builder.HasIndex(r => r.TargetId).HasFilter("[TargetId] IS NOT NULL");
    }
}
