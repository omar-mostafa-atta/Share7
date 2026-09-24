using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Measurement.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// What is known about the caller's own learning, per learning target.
/// <para>
/// **This is deliberately not a score screen.** Every row carries its interval and its evidence
/// count, or it carries <c>insufficient</c> and no number at all. There is no endpoint here that
/// returns "Mathematics: 83%", because no such number has a defensible meaning yet: the targets are
/// still per-lesson placeholders, and rolling placeholders up would produce a completion percentage
/// wearing a proficiency's clothes.
/// </para>
/// <para>
/// Reading is a write: the observation projector and the measurement pass run first, so what a
/// learner sees accounts for the lesson they finished ten seconds ago rather than for whenever a
/// background job last ran.
/// </para>
/// </summary>
[ApiController]
[Route("api/learning")]
[Authorize]
public class LearningController : ControllerBase
{
    private readonly IMeasurementService _measurements;
    private readonly ICurrentUserService _currentUser;
    private readonly ILanguageService _languages;

    public LearningController(
        IMeasurementService measurements,
        ICurrentUserService currentUser,
        ILanguageService languages)
    {
        _measurements = measurements;
        _currentUser = currentUser;
        _languages = languages;
    }

    /// <summary>
    /// The caller's targets, strongest evidence first. Pass <c>nodeId</c> to scope to one lesson.
    /// <code>
    /// [
    ///   {
    ///     "targetId": "…",
    ///     "statement": "Adding fractions with unlike denominators",
    ///     "isPlaceholder": true,
    ///     "state": "Developing",
    ///     "estimate": 0.6667,
    ///     "intervalLow": 0.3542,
    ///     "intervalHigh": 0.8796,
    ///     "observationCount": 12,
    ///     "assessmentCount": 5,
    ///     "observationsUntilReportable": 0
    ///   }
    /// ]
    /// </code>
    /// <para>
    /// A target below the sufficiency gate comes back with <c>state: "Insufficient"</c> and
    /// <c>estimate: null</c>. **Null, not zero** — the difference between "we do not know yet" and
    /// "they scored nothing" is the difference between an honest report and a damaging one, and the
    /// client has no field in which to confuse them.
    /// </para>
    /// </summary>
    [HttpGet("targets")]
    public async Task<IActionResult> Targets(
        [FromQuery] Guid? nodeId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var langId = await _languages.ResolveCurrentAsync(cancellationToken);

        return Ok(await _measurements.GetForLearnerAsync(userId, langId, nodeId, cancellationToken));
    }
}
