using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Economy;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

public class RewardRuleConfiguration : IEntityTypeConfiguration<RewardRule>
{
    public void Configure(EntityTypeBuilder<RewardRule> builder)
    {
        builder.ToTable("RewardRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);

        builder.Property(r => r.EventType)
            .HasConversion(EnumWire.Converter<RewardEventType>())
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(r => r.RepeatPolicy)
            .HasConversion(EnumWire.Converter<RewardRepeatPolicy>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.TransactionType)
            .HasConversion(EnumWire.Converter<CurrencyTransactionType>())
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(r => r.ReferenceKey).HasMaxLength(128);
        builder.Property(r => r.Enabled).HasDefaultValue(true);

        // Every attempt asks the same question: which enabled rules watch these events? Ordering
        // by Id keeps evaluation deterministic when several match.
        builder.HasIndex(r => new { r.EventType, r.Enabled, r.ReferenceKey });
    }
}

public class RewardRuleGrantConfiguration : IEntityTypeConfiguration<RewardRuleGrant>
{
    public void Configure(EntityTypeBuilder<RewardRuleGrant> builder)
    {
        builder.ToTable("RewardRuleGrants");
        builder.HasKey(g => g.Id);

        // One line per currency per rule. Two rows for the same currency would be a config typo
        // that silently doubles a payout, so the database refuses it.
        builder.HasIndex(g => new { g.RewardRuleId, g.CurrencyId }).IsUnique();

        builder.HasOne(g => g.RewardRule)
            .WithMany(r => r.Grants)
            .HasForeignKey(g => g.RewardRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // A currency in use by a rule cannot be deleted out from under it. Retiring it with
        // Enabled = false is the supported move, and the reward path skips rules that reference
        // a retired currency rather than paying half of them.
        builder.HasOne(g => g.Currency)
            .WithMany()
            .HasForeignKey(g => g.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RewardTransactionConfiguration : IEntityTypeConfiguration<RewardTransaction>
{
    public void Configure(EntityTypeBuilder<RewardTransaction> builder)
    {
        builder.ToTable("RewardTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.EventType)
            .HasConversion(EnumWire.Converter<RewardEventType>())
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(t => t.SourceType)
            .HasConversion(EnumWire.Converter<LedgerSourceType>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.SourceId).HasMaxLength(128);
        builder.Property(t => t.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(t => t.SubmissionKey).IsRequired().HasMaxLength(256);

        // **The idempotency guarantee.** Two concurrent attempts can both read "not yet paid";
        // only one of them can insert. The loser catches the duplicate-key error and treats the
        // rule as already paid, which is exactly the right answer.
        builder.HasIndex(t => new { t.UserId, t.RewardRuleId, t.IdempotencyKey }).IsUnique();

        // Cooldown and daily-limit checks read "this user's payouts of this rule, most recent
        // first".
        builder.HasIndex(t => new { t.UserId, t.RewardRuleId, t.CreatedAtUtc });

        // Restrict, not cascade: a rule that has paid somebody cannot be deleted, because the
        // transaction it produced has to stay explicable. Retire it with Enabled = false instead.
        builder.HasOne(t => t.RewardRule)
            .WithMany()
            .HasForeignKey(t => t.RewardRuleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Declared here because Domain cannot see ApplicationUser. Cascade means reward history
        // goes with the account, so this needs no entry in UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RewardTransactionLineConfiguration : IEntityTypeConfiguration<RewardTransactionLine>
{
    public void Configure(EntityTypeBuilder<RewardTransactionLine> builder)
    {
        builder.ToTable("RewardTransactionLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).ValueGeneratedOnAdd();

        builder.HasOne(l => l.RewardTransaction)
            .WithMany(t => t.Lines)
            .HasForeignKey(l => l.RewardTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Currency)
            .WithMany()
            .HasForeignKey(l => l.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RewardRuleEntitlementGrantConfiguration
    : IEntityTypeConfiguration<RewardRuleEntitlementGrant>
{
    public void Configure(EntityTypeBuilder<RewardRuleEntitlementGrant> builder)
    {
        builder.ToTable("RewardRuleEntitlementGrants");
        builder.HasKey(g => g.Id);

        // One product per rule. Listing it twice would grant it twice, which is a no-op the second
        // time but reads as a bug in the admin UI.
        builder.HasIndex(g => new { g.RewardRuleId, g.ProductId }).IsUnique();

        builder.HasOne(g => g.RewardRule)
            .WithMany(r => r.EntitlementGrants)
            .HasForeignKey(g => g.RewardRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching how currency grants hold a currency: retiring a product must not
        // silently strip it out of the rules that hand it over.
        builder.HasOne(g => g.Product)
            .WithMany()
            .HasForeignKey(g => g.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
