using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ProductKindConfiguration : IEntityTypeConfiguration<ProductKind>
{
    public void Configure(EntityTypeBuilder<ProductKind> builder)
    {
        builder.ToTable("ProductKinds");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name).IsRequired().HasMaxLength(64);

        // Case-insensitive by collation rather than by a lowercase shadow column: the name is
        // normalised to SCREAMING_SNAKE on the wire, so "Cosmetic" and "cosmetic" would reach the
        // client as one token and must not both exist. The service also folds separators, which
        // this index cannot see — it is the backstop, not the whole check.
        builder.HasIndex(k => k.Name).IsUnique();
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Key).IsRequired().HasMaxLength(64);
        builder.Property(p => p.ImageUrl).HasMaxLength(2048);
        builder.Property(p => p.Active).HasDefaultValue(true);

        builder.HasIndex(p => p.Key).IsUnique();

        // **Restrict.** A kind still in use cannot be deleted — every product of that kind would
        // lose the one thing that tells the client how to read its grants.
        builder.HasOne(p => p.Kind)
            .WithMany(k => k.Products)
            .HasForeignKey(p => p.ProductKindId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Both translation tables, configured identically to the curriculum's — composite key, the
/// <c>Lang_Id</c> column name the rest of the schema uses, cascade from the parent and
/// <see cref="DeleteBehavior.Restrict"/> to the language so retiring a language cannot silently
/// erase text.
/// </summary>
public class ProductTranslationConfiguration : IEntityTypeConfiguration<ProductTranslation>
{
    public void Configure(EntityTypeBuilder<ProductTranslation> builder)
    {
        builder.ToTable("ProductTranslations");
        builder.HasKey(t => new { t.ProductId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Description).HasMaxLength(1024);

        builder.HasOne(t => t.Product)
            .WithMany(p => p.Translations)
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}

public class ProductKindTranslationConfiguration : IEntityTypeConfiguration<ProductKindTranslation>
{
    public void Configure(EntityTypeBuilder<ProductKindTranslation> builder)
    {
        builder.ToTable("ProductKindTranslations");
        builder.HasKey(t => new { t.ProductKindId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Description).HasMaxLength(512);

        builder.HasOne(t => t.ProductKind)
            .WithMany(k => k.Translations)
            .HasForeignKey(t => t.ProductKindId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}

public class ProductGrantConfiguration : IEntityTypeConfiguration<ProductGrant>
{
    public void Configure(EntityTypeBuilder<ProductGrant> builder)
    {
        builder.ToTable("ProductGrants");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Reference).IsRequired().HasMaxLength(256);
        builder.Property(g => g.Quantity).HasDefaultValue(1);

        // The same reference twice in one product is a config typo that would hand the client a
        // duplicate. The database refuses it rather than the client having to de-duplicate.
        // Kind is no longer part of this: it lives on the product now, so it is constant across
        // every grant here and would add nothing to the key.
        builder.HasIndex(g => new { g.ProductId, g.Reference }).IsUnique();

        // Cascade: grants describe their product and mean nothing without it. Deleting a product is
        // itself refused while anyone owns it, so this can never orphan an entitlement.
        builder.HasOne(g => g.Product)
            .WithMany(p => p.Grants)
            .HasForeignKey(g => g.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("Offers");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Availability)
            .HasConversion(EnumWire.Converter<OfferAvailability>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.BadgeKey).HasMaxLength(64);

        // Cheapest-first within a sort bucket is how a shop list is read; the index matches the
        // order the offers endpoint returns so it never sorts in memory.
        builder.HasIndex(o => new { o.SortOrder, o.Id });

        // **Restrict.** A currency an offer prices in cannot be retired out from under it — the
        // price would become unpayable and the transaction history unreadable.
        builder.HasOne(o => o.Currency)
            .WithMany()
            .HasForeignKey(o => o.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class OfferTranslationConfiguration : IEntityTypeConfiguration<OfferTranslation>
{
    public void Configure(EntityTypeBuilder<OfferTranslation> builder)
    {
        builder.ToTable("OfferTranslations");
        builder.HasKey(t => new { t.OfferId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Description).HasMaxLength(1024);

        builder.HasOne(t => t.Offer)
            .WithMany(o => o.Translations)
            .HasForeignKey(t => t.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}

public class OfferProductConfiguration : IEntityTypeConfiguration<OfferProduct>
{
    public void Configure(EntityTypeBuilder<OfferProduct> builder)
    {
        builder.ToTable("OfferProducts");
        builder.HasKey(op => new { op.OfferId, op.ProductId });

        builder.HasOne(op => op.Offer)
            .WithMany(o => o.Products)
            .HasForeignKey(op => op.OfferId)
            .OnDelete(DeleteBehavior.Cascade);

        // **Restrict**, like Entitlement: a product currently on sale cannot be deleted. Delisting
        // is done by removing it from the offer, or by retiring the offer.
        builder.HasOne(op => op.Product)
            .WithMany()
            .HasForeignKey(op => op.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseTransactionConfiguration : IEntityTypeConfiguration<PurchaseTransaction>
{
    public void Configure(EntityTypeBuilder<PurchaseTransaction> builder)
    {
        builder.ToTable("PurchaseTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.State)
            .HasConversion(EnumWire.Converter<TransactionState>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.RequestId).IsRequired().HasMaxLength(128);
        builder.Property(t => t.FailureReasonKey).HasMaxLength(128);

        // **This index is the idempotency guarantee.** A retried purchase collides here instead of
        // charging twice, and because the database enforces it rather than a SELECT in the service,
        // it holds when two requests arrive at the same moment.
        //
        // **Filtered to completed rows**, because idempotency protects a charge, and a refusal never
        // made one. Without the filter a refused attempt would permanently burn its requestId: the
        // student tops up, retries with the same id, and collides with their own earlier "no".
        // Several refusals may therefore share a key; at most one completed purchase ever can.
        builder.HasIndex(t => new { t.UserId, t.RequestId })
            .IsUnique()
            .HasFilter("[State] = 'COMPLETED'");

        // Reading "how many times has this account bought this offer" is on the hot path of every
        // offers listing and every purchase.
        builder.HasIndex(t => new { t.UserId, t.OfferId, t.State });

        // Restrict: the offer has to stay resolvable for the history to mean anything.
        builder.HasOne(t => t.Offer)
            .WithMany()
            .HasForeignKey(t => t.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Currency)
            .WithMany()
            .HasForeignKey(t => t.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Declared here because Domain cannot see ApplicationUser. Cascade means a deleted account
        // takes its purchase history with it, so this needs no entry in UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.ToTable("Entitlements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Source)
            .HasConversion(EnumWire.Converter<EntitlementSource>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.SourceId).HasMaxLength(128);

        // Ownership is a boolean. This index is also what makes granting idempotent: a retried
        // purchase collides here instead of handing out a second copy.
        builder.HasIndex(e => new { e.UserId, e.ProductId }).IsUnique();

        // **Restrict, not cascade.** A product that anyone owns cannot be deleted — the entitlement
        // resolves what it owns by walking through to the product's grants, so removing the product
        // would strand it. Retiring with Active = false is the supported move.
        builder.HasOne(e => e.Product)
            .WithMany()
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Declared here because Domain cannot see ApplicationUser. Cascade means entitlements go
        // with the account, so this needs no entry in UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
