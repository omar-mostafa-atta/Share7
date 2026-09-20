using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Application.Common.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// Sittings: opening one, being served its paper, answering it and closing it.
/// <para>
/// **The path by which practice becomes exam-grade evidence.** Everything the platform collects
/// from gameplay is practice-class — retryable, hinted, replayed — and practice does not generalize
/// to an examination however much of it a child does. A sitting is the first thing in the product
/// whose conditions are fixed by the server before the first question is served and enforced for
/// its whole duration, which is what an exam-grade claim actually rests on
/// (<c>Docs/EducationalArchitecture.md</c> §4.3).
/// </para>
/// <para>
/// Answers taken here go through the **same** evidence recorder as a gameplay answer, into the same
/// append-only log, and are measured by the same code. There is no second pipeline — which is what
/// makes a sitting and a runner level comparable evidence about one child rather than two
/// histories that can never be reconciled.
/// </para>
/// </summary>
[ApiController]
[Route("api/assessments")]
[Authorize]
public class AssessmentsController : ControllerBase
{
    private readonly IAssessmentService _assessments;
    private readonly ICurrentUserService _currentUser;

    public AssessmentsController(IAssessmentService assessments, ICurrentUserService currentUser)
    {
        _assessments = assessments;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Opens a sitting. The conditions are fixed here and cannot be changed afterwards — the
    /// response carries <c>expectedStrength</c> so whoever set it up can see what the answers will
    /// be worth **before** a learner starts, rather than discovering it in a report months later.
    /// </summary>
    [HttpPost("administrations")]
    public async Task<IActionResult> Start(
        [FromBody] StartAdministrationRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _assessments.StartAsync(userId, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("administrations/{administrationId:guid}")]
    public async Task<IActionResult> Get(Guid administrationId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var administration = await _assessments.GetAsync(administrationId, userId, cancellationToken);
        return administration is null ? NotFound() : Ok(administration);
    }

    /// <summary>
    /// The paper, in order. **Carries no correctness field** — the client renders what the learner
    /// must choose between and nothing that would tell them which to pick.
    /// </summary>
    [HttpGet("administrations/{administrationId:guid}/items")]
    public async Task<IActionResult> Items(Guid administrationId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        return Ok(await _assessments.GetItemsAsync(administrationId, userId, cancellationToken));
    }

    /// <summary>
    /// Grades one answer and records the evidence.
    /// <para>
    /// A refusal comes back as <c>accepted: false</c> with a reason rather than as an error status:
    /// an expired sitting and an already-answered position are ordinary outcomes of a real client
    /// on a real network, and a client that has to parse a 400 to find that out will get it wrong.
    /// </para>
    /// </summary>
    [HttpPost("administrations/{administrationId:guid}/answers")]
    public async Task<IActionResult> Answer(
        Guid administrationId,
        [FromBody] AdministrationAnswerRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        try
        {
            return Ok(await _assessments.AnswerAsync(administrationId, userId, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Closes the sitting and scores it. Unreached positions are left unanswered rather than
    /// marked wrong — a learner who ran out of time did not get those questions wrong, and the
    /// measurement layer already treats an absent response as <c>NoResponse</c>.
    /// </summary>
    [HttpPost("administrations/{administrationId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid administrationId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var administration = await _assessments.CompleteAsync(administrationId, userId, cancellationToken);
        return administration is null ? NotFound() : Ok(administration);
    }
}
