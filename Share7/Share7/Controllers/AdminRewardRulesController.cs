using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// What gameplay outcomes are worth. **Admin only, without exception** — a player who could author
/// a rule could set their own payout, which is precisely what the server-authoritative economy
/// exists to prevent.
/// <para>
/// Rules are evaluated when <c>POST /api/progress/attempts</c> records a server-validated run.
/// Nothing here is reachable by the game client, and no endpoint anywhere accepts a reward amount
/// from it.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/reward-rules")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminRewardRulesController : ControllerBase
{
    private readonly IRewardAdminService _rewardAdminService;

    public AdminRewardRulesController(IRewardAdminService rewardAdminService)
    {
        _rewardAdminService = rewardAdminService;
    }

    /// <summary>
    /// Every rule, retired ones included. <c>grants[].currencyEnabled</c> is worth checking when a
    /// rule has stopped paying: a rule referencing a retired currency is skipped whole.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var rules = await _rewardAdminService.GetRulesAsync(cancellationToken);
        return Ok(new { rules });
    }

    /// <summary>
    /// Defines a rule:
    /// <code>
    /// {
    ///   "name": "Lesson passed",
    ///   "eventType": "LESSON_COMPLETED",
    ///   "repeatPolicy": "ONCE",
    ///   "grants": [ { "currencyId": "...", "amount": 10 }, { "currencyId": "...", "amount": 2 } ]
    /// }
    /// </code>
    /// <para>
    /// Several grants make one multi-currency reward: they are paid together in one transaction
    /// under one cooldown, or not at all. Two rules would instead be two independent payouts with
    /// two counters.
    /// </para>
    /// <para>
    /// Leave <c>referenceKey</c> null to cover every lesson, or set it to a lesson id for a bonus
    /// on one. Both fire when both match — there is no most-specific-wins precedence.
    /// </para>
    /// </summary>
    /// <response code="400">
    /// The rule could never pay as authored — unknown <c>eventType</c>, a limit that the repeat
    /// policy ignores, a duplicated currency, or a non-positive amount.
    /// </response>
    /// <response code="404">A granted currency does not exist.</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateRewardRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _rewardAdminService.CreateRuleAsync(request, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAll), new { }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Replaces a rule's policy and its whole grant set, or retires it with <c>enabled: false</c>.
    /// <para>
    /// <c>eventType</c> and <c>referenceKey</c> cannot be changed and are not accepted: reward
    /// transactions already recorded against this rule claim payment for the event it used to
    /// watch. Retire it and author a replacement instead.
    /// </para>
    /// <para>
    /// There is no delete, for the same reason — a rule that has paid somebody has to stay
    /// resolvable.
    /// </para>
    /// </summary>
    [HttpPut("{ruleId:guid}")]
    public async Task<IActionResult> Update(
        Guid ruleId,
        UpdateRewardRuleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _rewardAdminService.UpdateRuleAsync(ruleId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
