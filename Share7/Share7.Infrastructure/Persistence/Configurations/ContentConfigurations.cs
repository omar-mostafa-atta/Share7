using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Evidence;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ItemBankConfiguration : IEntityTypeConfiguration<ItemBank>
{
    public void Configure(EntityTypeBuilder<ItemBank> builder)
    {
        builder.ToTable("ItemBanks");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BankKey).HasMaxLength(128).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(b => b.BankKey).IsUnique();

        // Seeded for the same reason as the platform evidence contract: Item.ItemBankId is
        // non-nullable and the sheet importer mints items on every publish, so a deployment
        // without this row cannot accept content at all.
        builder.HasData(new ItemBank
        {
            Id = ContentIds.PlatformCurriculumBank,
            BankKey = "platform.curriculum",
            Name = "Share7 curriculum content",
            OwnerScope = ItemBankOwnerScope.Platform,
            OwnerId = null,
            ReviewPolicy = ItemBankReviewPolicy.Editorial,
            MaxEvidenceStrength = EvidenceStrength.Assessment,
            PoolsStatisticsGlobally = true,
            CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
        });
    }
}

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.SourceKey).HasMaxLength(200).IsRequired();

        // Unique because it is the key a republish looks the item up by. Two items sharing a
        // source key would make lineage linking pick one arbitrarily, silently splitting an
        // item's history at a republish — which is the exact fault this table exists to fix.
        builder.HasIndex(i => i.SourceKey).IsUnique();

        // The admin anchor surface, and the only query that scans by the flag.
        builder.HasIndex(i => i.IsAnchor).HasFilter("[IsAnchor] = 1");

        builder.HasOne(i => i.Bank)
            .WithMany(b => b.Items)
            .HasForeignKey(i => i.ItemBankId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ItemVersionConfiguration : IEntityTypeConfiguration<ItemVersion>
{
    public void Configure(EntityTypeBuilder<ItemVersion> builder)
    {
        builder.ToTable("ItemVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.ItemKindKey).HasMaxLength(32).IsRequired();

        builder.HasIndex(v => new { v.ItemId, v.VersionNumber }).IsUnique();

        // Restrict, not Cascade: an item version that has admitted responses must not be
        // removable by deleting the row above it. Retire it instead.
        builder.HasOne(v => v.Item)
            .WithMany(i => i.Versions)
            .HasForeignKey(v => v.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
