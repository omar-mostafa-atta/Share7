using Share7.Application.Assessment.Models;
using Share7.Domain.Assessment;

namespace Share7.Application.Assessment.Interfaces;

/// <summary>
/// Answers "if this learner walked into this examination today, what would we reasonably expect?"
/// — and answers it honestly, which mostly means answering a narrower question very well.
/// <para>
/// <b>Coverage is the day-one product.</b> It is arithmetic over the blueprint and the learner's
/// admitted evidence: no statistics, no calibration, nothing that has to age. "You have
/// exam-quality evidence on 62% of what this paper covers, and what is missing is geometry and
/// statistics, which are worth a third of the marks" is true on the first day, more useful to a
/// student two months out than any predicted score, and something no competitor says honestly
/// (§6.2, output A).
/// </para>
/// <para>
/// When the gates fail, the gaps <b>are</b> the response. There is no degraded number.
/// </para>
/// </summary>
public interface IExamCoverageService
{
    /// <summary>
    /// Computes — and stores — this learner's projection for one exam version. Projects pending
    /// responses and refreshes measurements first, so the answer accounts for what the learner
    /// just did.
    /// </summary>
    Task<ExamProjectionDto?> GetProjectionAsync(
        Guid learnerId, Guid examSpecificationVersionId, Guid langId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exams the caller can ask about. Published versions only — an unpublished specification is a
    /// draft somebody is still arguing about, and a coverage figure against a draft is a number
    /// about nothing.
    /// </summary>
    Task<IReadOnlyList<ExamSpecificationSummaryDto>> ListAsync(
        bool includeUnpublished = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Coverage against a blueprint directly, without an examination wrapped around it. What a
    /// teacher's "is my class ready for this unit test" read uses, and what the admin console shows
    /// while a blueprint is still being authored.
    /// </summary>
    Task<ExamProjectionDto?> GetBlueprintCoverageAsync(
        Guid learnerId, Guid blueprintId, Guid langId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Collects real examination results — the one input to §6 that cannot be engineered, only
/// gathered (ADR-E16).
/// </summary>
public interface IExamOutcomeService
{
    /// <summary>
    /// Records what a learner actually got, with their consent decision attached and a snapshot of
    /// what the platform believed about them at the time.
    /// <para>
    /// The snapshot matters: without it, a later recompute of the measurement layer would move the
    /// calibration sample underneath any model fitted on it, and nobody would be able to say what
    /// the fit had actually been fitted to.
    /// </para>
    /// </summary>
    Task<ReportedExamOutcome> ReportAsync(
        Guid learnerId, ReportOutcomeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws consent for a reported result. **The row stays and stops counting** — deleting
    /// would destroy the record that it was ever part of a sample.
    /// </summary>
    Task<bool> WithdrawConsentAsync(
        Guid learnerId, Guid outcomeId, CancellationToken cancellationToken = default);

    /// <summary>What the caller has already reported, so a client does not ask twice.</summary>
    Task<IReadOnlyList<ReportedOutcomeDto>> GetForLearnerAsync(
        Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How far each exam version is from a calibration that would let output C render. The honest
    /// answer today is "nowhere near", and saying it with a number is better than not saying it.
    /// </summary>
    Task<IReadOnlyList<CalibrationStatusDto>> GetCalibrationStatusAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>What a learner or guardian submits after a real examination.</summary>
public sealed record ReportOutcomeRequest
{
    public required Guid ExamSpecificationVersionId { get; init; }
    public decimal? ReportedScore { get; init; }
    public string? ReportedGrade { get; init; }
    public required DateOnly SittingDate { get; init; }

    /// <summary>
    /// **Must be explicit.** The result is being collected for a purpose the learner did not come
    /// here for, so silence is not consent and a default of true would not be one either.
    /// </summary>
    public required bool ConsentToCalibrationUse { get; init; }

    public string? Note { get; init; }
}

public sealed record ReportedOutcomeDto
{
    public required Guid OutcomeId { get; init; }
    public required Guid ExamSpecificationVersionId { get; init; }
    public required string ExamName { get; init; }
    public required string VersionLabel { get; init; }
    public decimal? ReportedScore { get; init; }
    public string? ReportedGrade { get; init; }
    public int? MaxScore { get; init; }
    public required DateOnly SittingDate { get; init; }
    public required OutcomeVerification Verification { get; init; }
    public required bool ConsentHeld { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
