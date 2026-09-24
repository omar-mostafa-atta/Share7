namespace Share7.Domain.Measurement;

/// <summary>
/// The Wilson score interval for a proportion.
/// <para>
/// **Chosen deliberately over the normal approximation**, which is the one everybody reaches for
/// and the one that breaks exactly where this product lives. At 4 correct out of 4, the textbook
/// interval is 100% ± 0 — it reports certainty from four answers. Wilson gives roughly 51% to 100%,
/// which is the truth: a child who got four questions right might be excellent or might be lucky,
/// and four questions cannot tell the difference.
/// </para>
/// <para>
/// It also stays inside [0, 1] at every sample size, never produces a zero-width interval, and
/// needs no calibration, no item parameters and no training data. It is the method the evidence
/// currently supports, and saying so is more useful than an IRT model fitted to data that does not
/// exist yet — <c>Docs/EducationalArchitecture.md</c> §5.1.
/// </para>
/// </summary>
public static class WilsonInterval
{
    /// <summary>
    /// 95% two-sided. Not configurable per call: an interval whose confidence level varies by
    /// caller is an interval nobody can compare, and every report in this system is read side by
    /// side with another one.
    /// </summary>
    public const double Z95 = 1.959963984540054;

    /// <summary>
    /// The interval for <paramref name="successes"/> out of <paramref name="trials"/>.
    /// <para>
    /// Zero trials returns the whole range, which is the honest statement: with no evidence, every
    /// proficiency is equally consistent with what we know. Callers gate on N rather than reading
    /// this as "somewhere around 50%".
    /// </para>
    /// </summary>
    public static (double Low, double High) For(int successes, int trials, double z = Z95)
    {
        if (trials <= 0) return (0d, 1d);

        successes = Math.Clamp(successes, 0, trials);

        var n = (double)trials;
        var p = successes / n;
        var z2 = z * z;

        var denominator = 1d + z2 / n;
        var centre = p + z2 / (2d * n);
        var margin = z * Math.Sqrt(p * (1d - p) / n + z2 / (4d * n * n));

        var low = (centre - margin) / denominator;
        var high = (centre + margin) / denominator;

        return (Math.Clamp(low, 0d, 1d), Math.Clamp(high, 0d, 1d));
    }

    /// <summary>
    /// The same interval as decimals rounded to four places, which is the precision the database
    /// columns carry. Rounding here rather than at each call site keeps a stored interval and a
    /// freshly computed one comparable.
    /// </summary>
    public static (decimal Estimate, decimal Low, decimal High) ForStorage(int successes, int trials)
    {
        var (low, high) = For(successes, trials);
        var estimate = trials > 0 ? (double)successes / trials : 0d;

        return (Round(estimate), Round(low), Round(high));
    }

    private static decimal Round(double value) => Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);
}
