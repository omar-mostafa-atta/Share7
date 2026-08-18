using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Admin side of the question pipeline: upload a lesson's question sheet.
/// </summary>
[ApiController]
[Route("api/admin/lessons")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminLessonQuestionsController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IQuestionImportService _questionImportService;
    private readonly ICurrentUserService _currentUserService;

    public AdminLessonQuestionsController(
        IQuestionImportService questionImportService,
        ICurrentUserService currentUserService)
    {
        _questionImportService = questionImportService;
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
}
