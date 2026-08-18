using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Application.Equipment.Models;
using Share7.Domain.Equipment;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class UserEquipmentConfiguration : IEntityTypeConfiguration<UserEquipment>
{
    public void Configure(EntityTypeBuilder<UserEquipment> builder)
    {
        builder.ToTable("Equipments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BodyType)
            .HasConversion(EnumWire.Converter<BodyType>())
            .HasMaxLength(32)
            .IsRequired();

        // Nullable together, and only on the no-items row that records an intentionally empty
        // outfit. A real item always carries both.
        builder.Property(e => e.SlotKey).HasMaxLength(EquipmentLimits.MaxKeyLength);
        builder.Property(e => e.CosmeticKey).HasMaxLength(EquipmentLimits.MaxKeyLength);

        // Independently optional: a cosmetic may be worn with no colour chosen.
        builder.Property(e => e.ColorKey).HasMaxLength(EquipmentLimits.MaxKeyLength);

        // The rule, enforced by the database rather than by the service remembering to check: one
        // row per (user, slot).
        //
        // HasFilter(null) undoes EF's default for a unique index over a nullable column, which is
        // to add "WHERE [SlotKey] IS NOT NULL" so that duplicate nulls are permitted. That default
        // is wrong here: it would exclude the no-items rows from the index entirely and let one
        // user accumulate any number of them. Unfiltered, SQL Server treats nulls as equal, so a
        // user gets at most one no-items row — which is exactly the guarantee wanted.
        builder.HasIndex(e => new { e.UserId, e.SlotKey })
            .IsUnique()
            .HasFilter(null);

        // Cascade from the account. Deliberately a real FK with ON DELETE CASCADE rather than an
        // entry in UserOwnedData.ManuallyPurged — the database then enforces it, and
        // AccountDeletionCoverageTests accepts either but prefers this.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Computed from SlotKey for readability at call sites; nothing to store.
        builder.Ignore(e => e.IsNoItemsRow);
    }
}
