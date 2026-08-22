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
/// Admin side of the question pipeline: publish a lesson's question set, from a sheet or typed
/// by hand, and read back what is currently published in a given language.
/// </summary>
[ApiController]
[Route("api/admin/lessons")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminLessonQuestionsController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IQuestionImportService _questionImportService;
    private readonly ILessonQuestionService _lessonQuestionService;
    private readonly ICurrentUserService _currentUserService;

    public AdminLessonQuestionsController(
        IQuestionImportService questionImportService,
        ILessonQuestionService lessonQuestionService,
        ICurrentUserService currentUserService)
    {
        _questionImportService = questionImportService;
        _lessonQuestionService = lessonQuestionService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Publishes an .xlsx question sheet as the next question version for one lesson in one
    /// language. Columns: 1 = question, 2 = correct answer, 3 = wrong answer, 4 = wrong answer.
    /// <para>
    /// <paramref name="langId"/> is required: a lesson is one shared row across languages, so
    /// the sheet's language cannot be inferred from it. Each language has its own question set
    /// and its own version — uploading English leaves the Arabic set untouched.
    /// </para>
    /// <para>
    /// Validation is all-or-nothing — a sheet with any bad row is rejected in full with the
    /// offending row numbers, and that language's current version is left untouched.
    /// </para>
    /// </summary>
    [HttpPost("{lessonId:guid}/questions/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> UploadQuestions(
        Guid lessonId,
        IFormFile file,
        [FromQuery] Guid langId,
        [FromQuery] bool hasHeaderRow = true,
        CancellationToken cancellationToken = default)
    {
        if (langId == Guid.Empty)
            return BadRequest(new { errors = new[] { "langId is required — it says which language's question set this sheet publishes." } });

        if (file is null || file.Length == 0)
            return BadRequest(new { errors = new[] { "No file was uploaded." } });

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { errors = new[] { "Only .xlsx files are supported." } });

        // ClosedXML needs a seekable stream; the request body stream is not reliably seekable.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var result = await _questionImportService.ImportAsync(
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
    /// Publishes questions <em>typed by hand</em> as the next question version for one lesson in
    /// one language — the same act as uploading a sheet, from a form instead of a file.
    /// <code>
    /// {
    ///   "mode": "APPEND",
    ///   "questions": [
    ///     { "text": "…", "correctChoice": "…", "wrongChoice1": "…", "wrongChoice2": "…" }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// <c>mode</c> is required and has no default. <c>APPEND</c> keeps the questions already
    /// published and adds these after them; <c>REPLACE</c> publishes these instead of them, which
    /// is also how a question is edited or removed — read the set, change it, publish it back.
    /// </para>
    /// <para>
    /// **Either mode produces a new version.** A published set is immutable, so appending
    /// republishes the existing questions alongside the new ones rather than inserting into what is
    /// there. Client caches key on that version, so every publish costs those clients a
    /// re-download of this lesson.
    /// </para>
    /// <para>
    /// Correctness is positional — the first choice is the right one — matching the sheet, where
    /// column 2 is the correct answer. Validation is identical to the sheet's and equally
    /// all-or-nothing: one bad question rejects the request and leaves the current version
    /// untouched. Errors carry <c>row</c> as the 1-based position in <c>questions</c>.
    /// </para>
    /// </summary>
    [HttpPost("{lessonId:guid}/questions/manual")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> PublishQuestionsManually(
        Guid lessonId,
        ManualQuestionSetRequest request,
        [FromQuery] Guid langId,
        CancellationToken cancellationToken)
    {
        if (langId == Guid.Empty)
            return BadRequest(new { errors = new[] { "langId is required — it says which language's question set this publishes." } });

        var result = await _questionImportService.PublishManualAsync(
            lessonId, langId, request, _currentUserService.UserId, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// The lesson's active question set in a <b>named</b> language, so the console can load what is
    /// published before editing it.
    /// <para>
    /// Separate from <c>GET /api/lessons/{id}/questions</c>, which serves the *caller's* language.
    /// An admin editing Arabic while signed in with an English token would otherwise load the
    /// English set and republish it over the Arabic one. The upload endpoint has always taken an
    /// explicit <c>langId</c> for exactly this reason.
    /// </para>
    /// </summary>
    [HttpGet("{lessonId:guid}/questions")]
    public async Task<IActionResult> GetQuestions(
        Guid lessonId,
        [FromQuery] Guid langId,
        CancellationToken cancellationToken)
    {
        if (langId == Guid.Empty)
            return BadRequest(new { errors = new[] { "langId is required — it says which language's question set to read." } });

        var questions = await _lessonQuestionService.GetQuestionsAsync(lessonId, langId, cancellationToken);
        return questions is null ? NotFound() : Ok(questions);
    }
}
