using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Audit;

namespace Share7.Infrastructure.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    /// <summary>
    /// The trigger that makes the table append-only, created by the <c>AuditEvents</c> migration.
    /// <para>
    /// Declared to EF as well as created in SQL, and that is load-bearing: EF's default INSERT uses
    /// <c>OUTPUT INSERTED</c> to read back the identity, which SQL Server refuses on a table with a
    /// trigger. Declaring it makes EF read the identity back a trigger-safe way instead.
    /// </para>
    /// </summary>
    public const string AppendOnlyTrigger = "TR_AuditEvents_AppendOnly";

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents", table => table.HasTrigger(AppendOnlyTrigger));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Sequence).UseIdentityColumn();
        builder.HasIndex(e => e.Sequence).IsUnique();

        builder.Property(e => e.ActorRoles).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Area).IsRequired().HasMaxLength(50);
        builder.Property(e => e.TargetType).HasMaxLength(50);
        builder.Property(e => e.TargetId).HasMaxLength(100);
        builder.Property(e => e.Summary).IsRequired().HasMaxLength(500);
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(256);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        // The three questions the audit viewer asks: what happened lately, what did this person
        // do, and what happened to this thing.
        builder.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("IX_AuditEvent_Time");
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredAtUtc }).HasDatabaseName("IX_AuditEvent_Actor");
        builder.HasIndex(e => new { e.TargetType, e.TargetId, e.OccurredAtUtc }).HasDatabaseName("IX_AuditEvent_Target");
        builder.HasIndex(e => new { e.Area, e.OccurredAtUtc }).HasDatabaseName("IX_AuditEvent_Area");

        // Deliberately no foreign key to AspNetUsers. A row outlives the account it names: the
        // audit trail is the platform's accounting, not a record about the person
        // (UserOwnedData.RetainedOnDeletion), and an FK would either block the erasure or cascade
        // the history away with it.
    }
}
