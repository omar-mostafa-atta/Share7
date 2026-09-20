using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Domain.Assessment;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Assessment;

/// <inheritdoc cref="IExamOutcomeService"/>
public class ExamOutcomeService : IExamOutcomeService
{
    /// <summary>
    /// Below this, a learner cannot grant consent for their result to be used in calibration.
    /// <para>
    /// **A minor's own tick is not consent** for a secondary use of the most sensitive academic
    /// fact the platform can hold about them. Phase 4's <c>GuardianLink</c> is what makes that
    /// consent obtainable; until it exists the result is still recorded — it is the child's own
    /// record of their own examination and they asked for it — and it simply does not enter the
    /// sample. Recording the result and refusing the secondary use are different decisions and the
    /// code keeps them different.
    /// </para>
    /// </summary>
    public const int SelfConsentAge = 18;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ExamOutcomeService> _logger;

    public ExamOutcomeService(ApplicationDbContext dbContext, ILogger<ExamOutcomeService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReportedExamOutcome> ReportAsync(
        Guid learnerId, ReportOutcomeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ReportedScore is null && string.IsNullOrWhiteSpace(request.ReportedGrade))
            throw new InvalidOperationException("A reported outcome needs either a score or a grade.");

        var version = await _dbContext.ExamSpecificationVersions
            .AsNoTracking()
            .Where(v => v.Id == request.ExamSpecificationVersionId)
            .Select(v => new { v.Id, v.MaxScore, v.BlueprintId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No such examination version.");

        if (request.ReportedScore is { } score
            && version.MaxScore is { } max
            && (score < 0 || score > max))
        {
            throw new InvalidOperationException(
                $"A score of {score} is outside this paper's range of 0 to {max}.");
        }

        var age = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => p.UserId == learnerId)
            .Select(p => (int?)p.Age)
            .FirstOrDefaultAsync(cancellationToken);

        // **Unknown age refuses.** On a platform whose audience is children, not knowing how old
        // somebody is is not a reason to treat them as an adult — it is the commonest way they get
        // treated as one. The result is still recorded either way; only the secondary use is
        // withheld, and it can be granted later once a guardian link or a profile exists.
        var mayConsent = age is >= SelfConsentAge;
        var consent = request.ConsentToCalibrationUse && mayConsent;

        var now = DateTime.UtcNow;

        var outcome = await _dbContext.ReportedExamOutcomes
            .FirstOrDefaultAsync(
                o => o.LearnerId == learnerId
                     && o.ExamSpecificationVersionId == request.ExamSpecificationVersionId,
                cancellationToken);

        var isNew = outcome is null;

        outcome ??= new ReportedExamOutcome
        {
            Id = Guid.NewGuid(),
            LearnerId = learnerId,
            ExamSpecificationVersionId = request.ExamSpecificationVersionId,
            CreatedAtUtc = now
        };

        outcome.ReportedScore = request.ReportedScore;
        outcome.ReportedGrade = request.ReportedGrade;

        // Denormalised now rather than joined later: an examining body that restates a paper's
        // maximum must not silently rescale a result somebody already reported against the old one.
        outcome.MaxScore = version.MaxScore;

        outcome.SittingDate = request.SittingDate;
        outcome.Verification = OutcomeVerification.SelfReported;
        outcome.ReportedByUserId = learnerId;
        outcome.Note = request.Note;

        outcome.ConsentedToCalibrationUse = consent;
        outcome.ConsentGrantedByUserId = consent ? learnerId : null;
        outcome.ConsentGrantedAtUtc = consent ? now : null;
        outcome.ConsentWithdrawnAtUtc = null;

        await SnapshotAsync(outcome, learnerId, version.BlueprintId, cancellationToken);

        if (isNew) _dbContext.ReportedExamOutcomes.Add(outcome);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (request.ConsentToCalibrationUse && !mayConsent)
        {
            _logger.LogInformation(
                "Outcome {Outcome} recorded without calibration consent: the learner is under {Age} "
                + "and no guardian link exists to grant it.", outcome.Id, SelfConsentAge);
        }

        return outcome;
    }

    /// <summary>
    /// Freezes what the platform believed about this learner at the moment they reported.
    /// <para>
    /// **Without this the calibration sample moves.** Everything below the recompute line is
    /// rebuilt when a method changes or an item is re-targeted, so a model fitted against live
    /// measurements would be fitted against numbers that no longer exist by the time anybody
    /// checked it. The snapshot is the row that makes a fit reproducible.
    /// </para>
    /// </summary>
    private async Task SnapshotAsync(
        ReportedExamOutcome outcome, Guid learnerId, Guid blueprintId, CancellationToken cancellationToken)
    {
        var projection = await _dbContext.ExamProjections
            .AsNoTracking()
            .Where(p => p.LearnerId == learnerId
                        && p.ExamSpecificationVersionId == outcome.ExamSpecificationVersionId)
            .Select(p => new
            {
                p.CoverageRatio,
                p.ProficiencyBandLow,
                p.ProficiencyBandHigh,
                p.BasisObservationCount
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (projection is null) return;

        // The midpoint of the band, and only for the snapshot. It never leaves this table and is
        // never rendered — a calibration needs one number per pair to regress on, and that is a
        // statistical input rather than a claim shown to anybody.
        outcome.BlueprintWeightedEstimateAtReport =
            projection.ProficiencyBandLow is { } low && projection.ProficiencyBandHigh is { } high
                ? (low + high) / 2m
                : null;

        outcome.CoverageRatioAtReport = projection.CoverageRatio;
        outcome.ObservationCountAtReport = projection.BasisObservationCount;
    }

    public async Task<bool> WithdrawConsentAsync(
        Guid learnerId, Guid outcomeId, CancellationToken cancellationToken = default)
    {
        var outcome = await _dbContext.ReportedExamOutcomes
            .FirstOrDefaultAsync(o => o.Id == outcomeId && o.LearnerId == learnerId, cancellationToken);

        if (outcome is null) return false;

        // The row stays. Deleting it would destroy the record that it was ever part of a sample,
        // and a calibration nobody can reconstruct is a calibration nobody should trust.
        outcome.ConsentedToCalibrationUse = false;
        outcome.ConsentWithdrawnAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReportedOutcomeDto>> GetForLearnerAsync(
        Guid learnerId, CancellationToken cancellationToken = default) =>
        await _dbContext.ReportedExamOutcomes
            .AsNoTracking()
            .Where(o => o.LearnerId == learnerId)
            .Select(o => new ReportedOutcomeDto
            {
                OutcomeId = o.Id,
                ExamSpecificationVersionId = o.ExamSpecificationVersionId,
                ExamName = o.ExamSpecificationVersion!.ExamSpecification!.Name,
                VersionLabel = o.ExamSpecificationVersion.VersionLabel,
                ReportedScore = o.ReportedScore,
                ReportedGrade = o.ReportedGrade,
                MaxScore = o.MaxScore,
                SittingDate = o.SittingDate,
                Verification = o.Verification,
                ConsentHeld = o.ConsentedToCalibrationUse && o.ConsentWithdrawnAtUtc == null,
                CreatedAtUtc = o.CreatedAtUtc
            })
            .OrderByDescending(o => o.SittingDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CalibrationStatusDto>> GetCalibrationStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.ExamSpecificationVersions
            .AsNoTracking()
            .Where(v => v.RetiredAtUtc == null)
            .Select(v => new
            {
                v.Id,
                Name = v.ExamSpecification!.Name,
                v.VersionLabel,
                Outcomes = v.ReportedOutcomes.Select(o => new
                {
                    o.Verification,
                    o.ConsentedToCalibrationUse,
                    o.ConsentWithdrawnAtUtc,
                    HasResult = o.ReportedScore != null || o.ReportedGrade != null
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(v =>
            {
                var usable = v.Outcomes.Count(o =>
                    o.ConsentedToCalibrationUse
                    && o.ConsentWithdrawnAtUtc == null
                    && o.Verification != OutcomeVerification.Disputed
                    && o.HasResult);

                return new CalibrationStatusDto
                {
                    ExamSpecificationVersionId = v.Id,
                    Name = v.Name,
                    VersionLabel = v.VersionLabel,
                    ReportedOutcomes = v.Outcomes.Count,
                    UsableOutcomes = usable,
                    WithdrawnConsent = v.Outcomes.Count(o => o.ConsentWithdrawnAtUtc != null),
                    Disputed = v.Outcomes.Count(o => o.Verification == OutcomeVerification.Disputed),
                    ByVerification = v.Outcomes
                        .GroupBy(o => o.Verification)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    RequiredForCalibration = ExamCoverageService.MinimumCalibrationSample,
                    IsCalibrated = usable >= ExamCoverageService.MinimumCalibrationSample
                };
            })
            .OrderByDescending(v => v.UsableOutcomes)
            .ThenBy(v => v.Name)
        ];
    }
}
