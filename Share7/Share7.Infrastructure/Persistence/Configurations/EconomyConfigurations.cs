using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Share7.Application.Common.Models;
using Share7.Domain.Economy;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Key).IsRequired().HasMaxLength(32);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Description).HasMaxLength(512);
        builder.Property(c => c.Enabled).HasDefaultValue(true);

        builder.HasIndex(c => c.Key).IsUnique();
    }
}

public class UserCurrencyBalanceConfiguration : IEntityTypeConfiguration<UserCurrencyBalance>
{
    public void Configure(EntityTypeBuilder<UserCurrencyBalance> builder)
    {
        builder.ToTable("UserCurrencyBalances");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Amount).HasDefaultValue(0L);

        // One balance per user per currency.
        builder.HasIndex(b => new { b.UserId, b.CurrencyId }).IsUnique();

        builder.HasOne(b => b.Currency)
            .WithMany(c => c.Balances)
            .HasForeignKey(b => b.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Declared here because Domain cannot see ApplicationUser. Cascade means balances go with
        // the account and need no entry in UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CurrencyLedgerEntryConfiguration : IEntityTypeConfiguration<CurrencyLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CurrencyLedgerEntry> builder)
    {
        builder.ToTable("CurrencyLedgerEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.TransactionType)
            .HasConversion(EnumWire.Converter<CurrencyTransactionType>())
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(e => e.SourceType)
            .HasConversion(EnumWire.Converter<LedgerSourceType>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.SourceId).HasMaxLength(128);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(128);

        // The obvious read is "this account's history, newest first".
        builder.HasIndex(e => new { e.UserId, e.CurrencyId, e.Id });

        // Tracing an entry back to the operation that produced it during an audit.
        builder.HasIndex(e => e.IdempotencyKey);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Stores enums as <c>SCREAMING_SNAKE_CASE</c> text.
/// <para>
/// Two reasons over the default integer: the ledger is an audit trail, and
/// <c>'LESSON_REWARD'</c> is legible in a SQL window where <c>3</c> is not; and reordering or
/// inserting an enum member can silently re-map every historical integer, which for an
/// append-only financial record is unrecoverable.
/// </para>
/// <para>
/// The mapping itself lives in <see cref="WireEnum"/>, shared with the DTOs, so the text in the
/// column and the text on the wire cannot drift apart.
/// </para>
/// </summary>
internal static class EnumWire
{
    public static ValueConverter<TEnum, string> Converter<TEnum>() where TEnum : struct, Enum =>
        new(value => WireEnum.ToWire(value), text => WireEnum.FromWire<TEnum>(text));
}
