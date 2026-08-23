using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Objectives.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Quests and achievements. **Admin only** — an objective decides what gets paid, so a player who
/// could author one could set their own reward.
/// <para>
/// One table is every daily quest, weekly quest and achievement the platform has. They differ by
/// <c>kind</c>, which is a statement about how often the counter resets and nothing else.
/// </para>
/// <para>
/// What an objective *pays* is not here. That is a reward rule on <c>OBJECTIVE_COMPLETED</c> with
/// the objective's key as its reference, authored in <c>/api/admin/reward-rules</c> like every other
/// payout — one place currency comes from, for the whole platform.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/objectives")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminObjectivesController : ControllerBase
{
    private readonly IObjectiveAdminService _objectives;

    public AdminObjectivesController(IObjectiveAdminService objectives) => _objectives = objectives;

    /// <summary>Every objective, active or retired, with its translations.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var objectives = await _objectives.GetAllAsync(cancellationToken);

        return Ok(new { objectives });
    }

    /// <summary>
    /// Authors one.
    /// <code>
    /// {
    ///   "key": "daily.lessons.complete.3",
    ///   "kind": "DAILY",
    ///   "metric": "LESSONS_COMPLETED",
    ///   "target": 3,
    ///   "aggregation": "SUM",
    ///   "iconKey": "quest_lessons",
    ///   "translations": [ { "langId": "…", "name": "Finish 3 lessons" } ]
    /// }
    /// </code>
    /// <para>
    /// The metric is validated against <c>LeaderboardMetrics</c>: an objective on something nothing
    /// raises is dead configuration that never errors and never completes. At least one translation
    /// is required, because an objective with no name has nothing a client could render.
    /// </para>
    /// <para>
    /// <c>scope</c> narrows the metric to one sub-dimension — a pickup kind, a currency key — so
    /// "collect 200 coins" and "collect 20 gems" are two rows over one metric.
    /// </para>
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateObjectiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _objectives.CreateAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>
    /// Retunes an objective's target, availability, art and ordering, and replaces its translations.
    /// <para>
    /// **Key, kind, metric and scope cannot be changed.** Every progress row already counting is
    /// counting under the old meaning, and the reward transactions already paid claim against the
    /// old key — moving either would strand both. Retire it and author a new one.
    /// </para>
    /// <para>
    /// Retiring (<c>isActive: false</c>) stops it being offered and stops it counting; it deletes
    /// nothing. A completed objective is somebody's record.
    /// </para>
    /// </summary>
    [HttpPut("{objectiveId:guid}")]
    public async Task<IActionResult> Update(
        Guid objectiveId,
        [FromBody] UpdateObjectiveRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _objectives.UpdateAsync(objectiveId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }
}
