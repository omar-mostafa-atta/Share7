using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Authorization;
using Share7.API.Extensions;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Builds out the curriculum tree. A node is one language-independent row with a name per
/// language, so every request carries a <c>translations</c> array — one entry for each
/// configured language — plus an optional <c>order</c> that defaults to appending last.
/// <para>
/// Open to the content team as well as the admins, except for <c>?force=true</c> on a delete —
/// see <see cref="Policies.ContentCascadeDelete"/>.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.ContentAuthoring)]
[ClosedAtCutover("Building the curriculum")]
public class AdminCurriculumController : ControllerBase
{
    private readonly ICurriculumAdminService _curriculumAdminService;
    private readonly IAuthorizationService _authorizationService;

    public AdminCurriculumController(
        ICurriculumAdminService curriculumAdminService,
        IAuthorizationService authorizationService)
    {
        _curriculumAdminService = curriculumAdminService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// A 403 when the caller asked to <c>force</c> a delete they may not force, otherwise null.
    /// <para>
    /// Checked before the service runs, so a refused cascade touches nothing. A non-forced delete
    /// is never refused here: it only succeeds on an empty node, and the 409 it answers otherwise
    /// is what tells a content-team member the node has to be emptied first.
    /// </para>
    /// </summary>
    private async Task<IActionResult?> RefuseCascadeAsync(bool force)
    {
        if (!force)
            return null;

        var allowed = await _authorizationService.AuthorizeAsync(User, Policies.ContentCascadeDelete);

        return allowed.Succeeded
            ? null
            : ServiceResult.Forbidden(
                    "Only an admin can delete something that still has content under it. " +
                    "Delete what is inside it first, or ask an admin.")
                .ToErrorResult();
    }

    /// <summary>Adds a term to a grade (e.g. "First Term").</summary>
    [HttpPost("grades/{gradeId:guid}/terms")]
    public async Task<IActionResult> AddTerm(
        Guid gradeId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _curriculumAdminService.AddTermToGradeAsync(gradeId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>Adds a subject to a term.</summary>
    [HttpPost("terms/{termId:guid}/subjects")]
    public async Task<IActionResult> AddSubject(
        Guid termId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _curriculumAdminService.AddSubjectToTermAsync(termId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>Adds a chapter to a subject.</summary>
    [HttpPost("subjects/{subjectId:guid}/chapters")]
    public async Task<IActionResult> AddChapter(
        Guid subjectId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _curriculumAdminService.AddChapterToSubjectAsync(subjectId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>
    /// Adds a lesson to a chapter. The new lesson has no question set in any language —
    /// upload a sheet per language to publish version 1 of each.
    /// </summary>
    [HttpPost("chapters/{chapterId:guid}/lessons")]
    public async Task<IActionResult> AddLesson(
        Guid chapterId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _curriculumAdminService.AddLessonToChapterAsync(chapterId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>
    /// Deletes a term and everything under it. Refused with 409 while it still has children,
    /// unless <paramref name="force"/> is set — the 409 body reports what would be removed.
    /// <paramref name="force"/> is admin-only; the content team gets a 403 for it.
    /// </summary>
    [HttpDelete("terms/{termId:guid}")]
    public async Task<IActionResult> DeleteTerm(Guid termId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        if (await RefuseCascadeAsync(force) is { } refusal)
            return refusal;

        var result = await _curriculumAdminService.DeleteTermAsync(termId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }

    /// <summary>Deletes a subject and everything under it. See DeleteTerm for the force rule.</summary>
    [HttpDelete("subjects/{subjectId:guid}")]
    public async Task<IActionResult> DeleteSubject(Guid subjectId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        if (await RefuseCascadeAsync(force) is { } refusal)
            return refusal;

        var result = await _curriculumAdminService.DeleteSubjectAsync(subjectId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }

    /// <summary>Deletes a chapter and everything under it. See DeleteTerm for the force rule.</summary>
    [HttpDelete("chapters/{chapterId:guid}")]
    public async Task<IActionResult> DeleteChapter(Guid chapterId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        if (await RefuseCascadeAsync(force) is { } refusal)
            return refusal;

        var result = await _curriculumAdminService.DeleteChapterAsync(chapterId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }

    /// <summary>
    /// Deletes a lesson along with its questions, answer choices and upload history.
    /// See DeleteTerm for the force rule.
    /// </summary>
    [HttpDelete("lessons/{lessonId:guid}")]
    public async Task<IActionResult> DeleteLesson(Guid lessonId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        if (await RefuseCascadeAsync(force) is { } refusal)
            return refusal;

        var result = await _curriculumAdminService.DeleteLessonAsync(lessonId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }
}
