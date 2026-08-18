using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Builds out the curriculum tree. A node is one language-independent row with a name per
/// language, so every request carries a <c>translations</c> array — one entry for each
/// configured language — plus an optional <c>order</c> that defaults to appending last.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminCurriculumController : ControllerBase
{
    private readonly ICurriculumAdminService _curriculumAdminService;

    public AdminCurriculumController(ICurriculumAdminService curriculumAdminService)
    {
        _curriculumAdminService = curriculumAdminService;
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
    /// </summary>
    [HttpDelete("terms/{termId:guid}")]
    public async Task<IActionResult> DeleteTerm(Guid termId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        var result = await _curriculumAdminService.DeleteTermAsync(termId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }

    /// <summary>Deletes a subject and everything under it. See DeleteTerm for the force rule.</summary>
    [HttpDelete("subjects/{subjectId:guid}")]
    public async Task<IActionResult> DeleteSubject(Guid subjectId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        var result = await _curriculumAdminService.DeleteSubjectAsync(subjectId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }

    /// <summary>Deletes a chapter and everything under it. See DeleteTerm for the force rule.</summary>
    [HttpDelete("chapters/{chapterId:guid}")]
    public async Task<IActionResult> DeleteChapter(Guid chapterId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
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
        var result = await _curriculumAdminService.DeleteLessonAsync(lessonId, force, cancellationToken);
        return result.Succeeded ? Ok(new { deleted = result.Value }) : result.ToErrorResult();
    }
}
