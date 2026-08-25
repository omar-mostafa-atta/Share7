using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Economy;
using Share7.Domain.Games;
using Share7.Domain.Runs;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// The filtered-index predicates for the run tables.
/// <para>
/// **Raw SQL naming the <i>stored</i> form of the enum.** <c>EnumWire</c> writes
/// <c>SCREAMING_SNAKE</c>, so the token is <c>'OPEN'</c> and never <c>'Open'</c>. A predicate that
/// matches nothing produces an index that constrains nothing — every service test still passes and
/// the guarantee is simply gone. <c>RunSettlementTests</c> asserts against the live index for that
/// reason, the same way <c>MultiplayerIndexTests</c> does.
/// </para>
/// </summary>
internal static class RunFilters
{
    /// <summary>A run still awaiting its result. The only state a settlement is accepted from.</summary>
    public const string RunIsOpen = "[State] = 'OPEN'";
}

public class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("Runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.State)
            .HasConversion(EnumWire.Converter<RunState>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.Outcome)
            .HasConversion(EnumWire.Converter<RunOutcome>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.StartRequestId).HasMaxLength(128);
        builder.Property(r => r.ResultRequestId).HasMaxLength(128);
        builder.Property(r => r.FlagReason).HasMaxLength(256);
        builder.Property(r => r.CapMessage).HasMaxLength(64);
        builder.Property(r => r.ReviewNote).HasMaxLength(512);
        builder.Property(r => r.PickupsJson).IsRequired();

        // The per-kind daily allowance and the daily run count both ask "this account's settled runs
        // today". Filtered would be tempting, but the review queue below wants non-settled rows too.
        builder.HasIndex(r => new { r.UserId, r.State, r.EndedAtUtc })
            .HasDatabaseName("IX_Run_UserSettled");

        // The flagged-run review queue: everything still waiting on a human, newest first.
        builder.HasIndex(r => new { r.IsFlagged, r.ReviewedAtUtc, r.EndedAtUtc })
            .HasFilter("[IsFlagged] = 1")
            .HasDatabaseName("IX_Run_Flagged");

        // "What has this child been playing?" — the support and review query, and what a per-day run
        // count reads before the daily ledger is authoritative enough to trust on its own.
        builder.HasIndex(r => new { r.UserId, r.StartedAtUtc })
            .HasDatabaseName("IX_Run_User");

        // The expiry sweep's only query. Filtered, because the interesting rows are a vanishing
        // fraction of the table within a week of launch.
        builder.HasIndex(r => new { r.State, r.ExpiresAtUtc })
            .HasFilter(RunFilters.RunIsOpen)
            .HasDatabaseName("IX_Run_Open");

        // **Start idempotency.** A retried start returns the same run *and the same seed* — the
        // client generates its track from that seed, so two seeds for one run means the track on
        // screen is not the track the server can later check.
        builder.HasIndex(r => new { r.UserId, r.StartRequestId })
            .IsUnique()
            .HasFilter("[StartRequestId] IS NOT NULL")
            .HasDatabaseName("UX_Run_StartIdem");

        // **Result idempotency, and a second column rather than a shared one.** Start and result are
        // two requests carrying two keys; one column for both would have the start burn the key the
        // result needs. The index is belt-and-braces over the state check — a settled run replays its
        // settlement without reaching here — but it also catches one key spent across two *different*
        // runs, which is the shape a double-pay would actually take.
        builder.HasIndex(r => new { r.UserId, r.ResultRequestId })
            .IsUnique()
            .HasFilter("[ResultRequestId] IS NOT NULL")
            .HasDatabaseName("UX_Run_ResultIdem");

        // Restrict: a game with runs against it cannot be deleted, or their payouts stop resolving.
        // Deactivating the catalog entry is the supported move, exactly as multiplayer sessions and
        // offers do.
        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(r => r.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        // **SessionId is deliberately not a foreign key.** It is a corroboration pointer for phase 3,
        // and a run has to stay explicable after the session it belonged to has been swept.

        // Declared here because Domain cannot see ApplicationUser. Cascade means run history goes
        // with the account, and it takes the payout rows with it — so neither table needs an entry
        // in UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SignalValuationConfiguration : IEntityTypeConfiguration<SignalValuation>
{
    public void Configure(EntityTypeBuilder<SignalValuation> builder)
    {
        // Renamed from PickupValuations (2026-08-25) when the table stopped being about pickups.
        // The migration renames rather than recreates: these rows are the live economy, and dropping
        // them would silently zero every payout until an operator re-authored the lot.
        builder.ToTable("SignalValuations");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.SignalKind).IsRequired().HasMaxLength(SignalKinds.MaxLength);
        builder.Property(v => v.Enabled).HasDefaultValue(true);

        // One price per (game, kind, currency). Two rows for the same triple is a config typo that
        // silently doubles a payout, so the database refuses it.
        //
        // **HasFilter(null) is load-bearing, not noise.** EF Core's SQL Server provider adds
        // `WHERE [GameId] IS NOT NULL` to any unique index over a nullable column, which here would
        // leave the platform-default rows — the ones every unconfigured mini-game resolves through —
        // entirely unconstrained: duplicate defaults for one (kind, currency) would insert happily
        // and silently double a payout. SQL Server treats NULLs as equal in a unique index, so
        // clearing the filter is what makes "one default per kind per currency" a constraint rather
        // than a convention.
        builder.HasIndex(v => new { v.GameId, v.SignalKind, v.CurrencyId })
            .IsUnique()
            .HasFilter(null)
            .HasDatabaseName("UX_Valuation");

        // Settlement's read: every enabled price for this game plus the platform defaults, in one
        // query rather than one per kind.
        builder.HasIndex(v => new { v.SignalKind, v.Enabled })
            .HasDatabaseName("IX_Valuation_Kind");

        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(v => v.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        // A currency priced by a valuation cannot be deleted out from under it. Retiring it with
        // Enabled = false is the supported move; settlement skips a retired currency's rows rather
        // than paying into a currency the client can no longer hold.
        builder.HasOne(v => v.Currency)
            .WithMany()
            .HasForeignKey(v => v.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DailySignalLedgerConfiguration : IEntityTypeConfiguration<DailySignalLedger>
{
    public void Configure(EntityTypeBuilder<DailySignalLedger> builder)
    {
        builder.ToTable("DailySignalLedger");

        // Composite natural key, for the same reason DailyCurrencyLedger has one: every read is a
        // primary-key lookup and the row's existence *is* the counter. This is what replaced a
        // group-by over every RunPayout the platform had ever written.
        builder.HasKey(l => new { l.UserId, l.SignalKind, l.DayUtc });

        builder.Property(l => l.SignalKind).IsRequired().HasMaxLength(SignalKinds.MaxLength);

        // Date-only, so a stray time-of-day cannot split one day across two rows.
        builder.Property(l => l.DayUtc).HasColumnType("date");

        // Goes with the account. A deleted child's daily counters are not somebody else's business.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RunPayoutConfiguration : IEntityTypeConfiguration<RunPayout>
{
    public void Configure(EntityTypeBuilder<RunPayout> builder)
    {
        builder.ToTable("RunPayouts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Source).IsRequired().HasMaxLength(64);

        // One line per source per currency. A second row for the same pair would mean the same
        // pickup kind was valued twice in one settlement, which is a double-pay wearing an audit
        // row's clothes.
        builder.HasIndex(p => new { p.RunId, p.Source, p.CurrencyId })
            .IsUnique()
            .HasDatabaseName("UX_RunPayout_Line");

        builder.HasOne(p => p.Run)
            .WithMany(r => r.Payouts)
            .HasForeignKey(p => p.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Currency)
            .WithMany()
            .HasForeignKey(p => p.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DailyCurrencyLedgerConfiguration : IEntityTypeConfiguration<DailyCurrencyLedger>
{
    public void Configure(EntityTypeBuilder<DailyCurrencyLedger> builder)
    {
        builder.ToTable("DailyCurrencyLedger");

        // Composite natural key. The ceiling's read is a primary-key lookup, and the row's existence
        // *is* the counter — an identity column here would let two rows count the same day.
        builder.HasKey(l => new { l.UserId, l.CurrencyId, l.DayUtc });

        // Date-only. The time component is always midnight, and storing it as datetime2(0) with a
        // date type keeps a stray time-of-day from splitting one day across two rows.
        builder.Property(l => l.DayUtc).HasColumnType("date");

        builder.HasOne(l => l.Currency)
            .WithMany()
            .HasForeignKey(l => l.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade, so the counter goes with the account and needs no entry in
        // UserOwnedData.ManuallyPurged.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
