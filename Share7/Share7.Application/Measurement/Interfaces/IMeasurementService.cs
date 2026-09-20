using Share7.Application.Measurement.Models;

namespace Share7.Application.Measurement.Interfaces;

/// <summary>
/// Computes learner × target measurements from observations, and applies the published mastery rule
/// to them.
/// <para>
/// Everything it writes is derived and disposable. The estimate is a proportion with a Wilson
/// interval — classical test theory, chosen because it is honest at the sample sizes that actually
/// exist and needs no calibration data that does not (<c>Docs/EducationalArchitecture.md</c> §5.1).
/// A second method will write its own rows beside these rather than replacing them.
/// </para>
/// </summary>
public interface IMeasurementService
{
    /// <summary>
    /// Rebuilds every measurement and verdict for one learner from their observations. Idempotent:
    /// running it twice over unchanged observations produces identical rows.
    /// </summary>
    Task<int> RecomputeForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What is currently known about this learner, per target, in one language. Projects pending
    /// responses first, so the answer accounts for what they just did.
    /// </summary>
    Task<IReadOnlyList<TargetMeasurementDto>> GetForLearnerAsync(
        Guid learnerId, Guid langId, Guid? nodeId = null, CancellationToken cancellationToken = default);
}
