using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Admin side of the recovery-question pipeline: publish a lesson's recovery set, from a sheet or
/// typed by hand, and read back what is currently published in a given language.
/// The mirror of <see cref="AdminLessonQuestionsController"/> over the secondary pool.
/// </summary>
[ApiController]
[Route("api/admin/lessons")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminLessonRecoveryQuestionsController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IRecoveryQuestionImportService _recoveryQuestionImportService;
    private readonly ILessonRecoveryQuestionService _lessonRecoveryQuestionService;
    private readonly ICurrentUserService _currentUserService;

    public AdminLessonRecoveryQuestionsController(
        IRecoveryQuestionImportService recoveryQuestionImportService,
        ILessonRecoveryQuestionService lessonRecoveryQuestionService,
        ICurrentUserService currentUserService)
    {
        _recoveryQuestionImportService = recoveryQuestionImportService;
        _lessonRecoveryQuestionService = lessonRecoveryQuestionService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Publishes an .xlsx sheet as the next <em>recovery</em> question version for one lesson in
    /// one language. Columns are identical to the main question sheet: 1 = question,
    /// 2 = correct answer, 3 = wrong answer, 4 = wrong answer.
    /// <para>
    /// <paramref name="langId"/> is required: a lesson is one shared row across languages, so the
    /// sheet's language cannot be inferred from it. Each language has its own recovery set and its
    /// own version — uploading English leaves the Arabic recovery set untouched, and neither
    /// touches the lesson's main question set.
    /// </para>
    /// <para>
    /// Validation is all-or-nothing — a sheet with any bad row is rejected in full with the
    /// offending row numbers, and that language's current recovery version is left untouched.
    /// </para>
    /// </summary>
    [HttpPost("{lessonId:guid}/recovery-questions/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> UploadRecoveryQuestions(
        Guid lessonId,
        IFormFile file,
        [FromQuery] Guid langId,
        [FromQuery] bool hasHeaderRow = true,
        CancellationToken cancellationToken = default)
    {
        if (langId == Guid.Empty)
            return BadRequest(new { errors = new[] { "langId is required — it says which language's recovery question set this sheet publishes." } });

        if (file is null || file.Length == 0)
            return BadRequest(new { errors = new[] { "No file was uploaded." } });

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { errors = new[] { "Only .xlsx files are supported." } });

        // ClosedXML needs a seekable stream; the request body stream is not reliably seekable.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var result = await _recoveryQuestionImportService.ImportAsync(
            lessonId,
            langId,
            buffer,
            file.FileName,
            hasHeaderRow,
            _currentUserService.UserId,
            cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Publishes recovery questions <em>typed by hand</em> as the next recovery version for one
    /// lesson in one language. The mirror of
    /// <see cref="AdminLessonQuestionsController.PublishQuestionsManually"/> — identical request
    /// shape, identical rules — over the secondary pool and its own counter.
    /// <code>
    /// {
    ///   "mode": "APPEND",
    ///   "questions": [
    ///     { "text": "…", "correctChoice": "…", "wrongChoice1": "…", "wrongChoice2": "…" }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// Publishing here bumps the recovery version only and never touches the lesson's main
    /// question set.
    /// </para>
    /// </summary>
    [HttpPost("{lessonId:guid}/recovery-questions/manual")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> PublishRecoveryQuestionsManually(
        Guid lessonId,
        ManualQuestionSetRequest request,
        [FromQuery] Guid langId,
        CancellationToken cancellationToken)
    {
        if (langId == Guid.Empty)
            return BadRequest(new { errors = new[] { "langId is required — it says which language's recovery question set this publishes." } });

        var result = await _recoveryQuestionImportService.PublishManualAsync(
            lessonId, langId, request, _currentUserService.UserId, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// The lesson's active recovery question set in a <b>named</b> language, so the console can
    /// load what is published before editing it. The mirror of
    /// <see cref="AdminLessonQuestionsController.GetQuestions"/>.
    /// </summary>
    [HttpGet("{lessonId:guid}/recovery-questions")]
    public async Task<IActionResult> GetRecoveryQuestions(
        Guid lessonId,
        [FromQuery] Guid langId,
        CancellationToken cancellationToken)
    {
        if (langId == Guid.Empty)
            return BadRequest(new { errors = new[] { "langId is required — it says which language's recovery question set to read." } });

        var questions = await _lessonRecoveryQuestionService.GetQuestionsAsync(lessonId, langId, cancellationToken);
        return questions is null ? NotFound() : Ok(questions);
    }
}
