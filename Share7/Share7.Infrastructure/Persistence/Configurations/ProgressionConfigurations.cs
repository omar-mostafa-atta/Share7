using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Progression;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LevelThresholdConfiguration : IEntityTypeConfiguration<LevelThreshold>
{
    /// <summary>
    /// Fixed timestamp for the seeded curve. <c>DateTime.UtcNow</c> here would make the model
    /// differ from itself on every scaffold, and every <c>dotnet ef</c> run would produce a
    /// migration that changes nothing but the clock.
    /// </summary>
    private static readonly DateTime SeededAtUtc = new(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<LevelThreshold> builder)
    {
        builder.ToTable("LevelThresholds");

        // The level is the key. It is authored, small and never generated — an identity column
        // here would let two rows claim level 7.
        builder.HasKey(t => t.Level);
        builder.Property(t => t.Level).ValueGeneratedNever();

        builder.Property(t => t.CumulativeXp).IsRequired();

        // Every read is "the whole curve, ascending by XP", and the derivation binary-searches it.
        builder.HasIndex(t => t.CumulativeXp).IsUnique();

        builder.HasData(BuildStarterCurve());
    }

    /// <summary>
    /// A starter curve, seeded so the feature works on a fresh database instead of reporting
    /// level 1 forever until somebody remembers to author one.
    /// <para>
    /// <c>25 × (L−1) × L</c> — level 2 at 50 XP, level 10 at 2,250, level 50 at 61,250. Quadratic,
    /// so early levels come fast enough to teach a child what the bar means and later ones stay
    /// worth reaching. At the 20 XP a completed lesson is worth in the reference rules, level 2 is
    /// about three lessons.
    /// </para>
    /// <para>
    /// **Tuning this is an admin call, not a migration.** The curve is replaceable through
    /// <c>PUT /api/admin/progression/levels</c>; this is only what a database starts with.
    /// </para>
    /// </summary>
    private static LevelThreshold[] BuildStarterCurve() =>
        Enumerable.Range(1, 50)
            .Select(level => new LevelThreshold
            {
                Level = level,
                CumulativeXp = 25L * (level - 1) * level,
                CreatedAtUtc = SeededAtUtc,
                UpdatedAtUtc = SeededAtUtc
            })
            .ToArray();
}
