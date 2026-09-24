using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// What the caller's own evidence says about the examinations they care about.
/// <para>
/// **The honest day-one exam feature, and it is a coverage report rather than a prediction.**
/// "You have exam-quality evidence on 62% of what this paper covers, and what is missing is
/// geometry and statistics, which are worth a third of the marks" is true on the first day, needs
/// no calibration and nothing to age, and is more actionable to a student two months out than any
/// predicted score would be (<c>Docs/EducationalArchitecture.md</c> §6.2).
/// </para>
/// <para>
/// There is **no endpoint here that returns a predicted mark**, and no field on any response that
/// could carry one. That is structural rather than a matter of restraint: the mapping from
/// proficiency to score requires observed pairs of estimate and actual result, it cannot be
/// reasoned into existence, and telling a fifteen-year-old "you will get 61%" when nobody has
/// checked whether that is true is the specific harm this product must not cause (§6.4).
/// </para>
/// </summary>
[ApiController]
[Route("api/learning/exams")]
[Authorize]
public class ExamsController : ControllerBase
{
    private readonly IExamCoverageService _coverage;
    private readonly IExamOutcomeService _outcomes;
    private readonly ICurrentUserService _currentUser;
    private readonly ILanguageService _languages;

    public ExamsController(
        IExamCoverageService coverage,
        IExamOutcomeService outcomes,
        ICurrentUserService currentUser,
        ILanguageService languages)
    {
        _coverage = coverage;
        _outcomes = outcomes;
        _currentUser = currentUser;
        _languages = languages;
    }

    /// <summary>Published examinations the caller can ask about.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _coverage.ListAsync(includeUnpublished: false, cancellationToken));

    /// <summary>
    /// Coverage and, where the evidence supports it, a proficiency band for one examination.
    /// <code>
    /// {
    ///   "sufficiency": "InsufficientCoverage",
    ///   "coverageRatio": 0.24,
    ///   "weakestAreaCoverage": 0.0,
    ///   "areas": [ { "label": "Geometry", "weightInExam": 0.25, "coverage": 0.0, … } ],
    ///   "gaps": [
    ///     { "areaLabel": "Geometry", "kind": "NoEvidence", "weightInExam": 0.25,
    ///       "observationsNeeded": 8, "suggestedNodeId": "…", "rank": 1 }
    ///   ],
    ///   "proficiencyBandLow": null,
    ///   "outcomeBandLow": null
    /// }
    /// </code>
    /// <para>
    /// **An insufficient answer is a normal, well-formed, useful response** — not an error and not
    /// a zero. The gaps are the payload: ranked by what each is worth in the paper, so the list
    /// reads as "do this next" rather than as a catalogue of failures.
    /// </para>
    /// </summary>
    [HttpGet("{examSpecificationVersionId:guid}/projection")]
    public async Task<IActionResult> Projection(
        Guid examSpecificationVersionId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        var projection = await _coverage.GetProjectionAsync(
            userId, examSpecificationVersionId, langId, cancellationToken);

        return projection is null ? NotFound() : Ok(projection);
    }

    /// <summary>
    /// The caller's reported real-world results. Read so a client does not ask for the same
    /// sitting twice.
    /// </summary>
    [HttpGet("outcomes")]
    public async Task<IActionResult> Outcomes(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return Ok(await _outcomes.GetForLearnerAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Records what the caller actually got in a real examination.
    /// <para>
    /// **The one input to exam projection that cannot be engineered.** Everything else in §6 is
    /// reasoning about the model; this is a fact about the world, it is irreplaceable, and a
    /// platform that waits until it needs calibration data waits another two years after that for
    /// the data to accrue (ADR-E16).
    /// </para>
    /// <para>
    /// <c>consentToCalibrationUse</c> must be explicit. For a learner under 18 it is recorded as
    /// refused whatever the request says: a minor's own tick is not consent for a secondary use of
    /// their examination result, and Phase 4's guardian link is what will make that consent
    /// obtainable. The result itself is still stored — it is the child's own record of their own
    /// examination.
    /// </para>
    /// </summary>
    [HttpPost("outcomes")]
    public async Task<IActionResult> ReportOutcome(
        [FromBody] ReportOutcomeRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            var outcome = await _outcomes.ReportAsync(userId, request, cancellationToken);

            return Ok(new
            {
                outcomeId = outcome.Id,
                outcome.ExamSpecificationVersionId,
                outcome.ReportedScore,
                outcome.ReportedGrade,
                outcome.SittingDate,
                consentHeld = outcome.ConsentedToCalibrationUse,

                // Said plainly rather than silently dropped, so a learner who ticked the box and
                // was refused finds out why.
                consentNote = request.ConsentToCalibrationUse && !outcome.ConsentedToCalibrationUse
                    ? "Recorded without calibration consent: a guardian has to grant that for a "
                      + "learner under 18, and guardian links are not available yet."
                    : null
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Withdraws consent for a reported result to be used in calibration. The row stays and stops
    /// counting — deleting would destroy the record that it was ever part of a sample.
    /// </summary>
    [HttpDelete("outcomes/{outcomeId:guid}/consent")]
    public async Task<IActionResult> WithdrawConsent(Guid outcomeId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return await _outcomes.WithdrawConsentAsync(userId, outcomeId, cancellationToken)
            ? Ok(new { outcomeId, consentHeld = false })
            : NotFound();
    }
}
