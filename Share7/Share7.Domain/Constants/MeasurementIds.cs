namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the measurement rows the code depends on existing.
/// <para>
/// Seeded for the same reason as <see cref="EvidenceContractIds"/>: a deployment with no published
/// mastery rule computes measurements happily and produces no verdicts at all, which looks like
/// "this learner has not studied anything" rather than like a missing row.
/// </para>
/// </summary>
public static class MeasurementIds
{
    /// <summary>The platform's default mastery rule, version 1.</summary>
    public static readonly Guid DefaultMasteryRuleV1 =
        Guid.Parse("c9f5a7b1-0e82-4d36-94c2-1a6e3d50a799");
}
