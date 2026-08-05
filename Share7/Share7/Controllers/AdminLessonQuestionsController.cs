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
    /// Publishes an .xlsx question sheet as the lesson's next question version.
    /// Columns: 1 = question, 2 = correct answer, 3 = wrong answer, 4 = wrong answer.
    /// <para>
    /// Validation is all-or-nothing — a sheet with any bad row is rejected in full with the
    /// offending row numbers, and the lesson's current version is left untouched.
    /// </para>
    /// </summary>
    [HttpPost("{lessonId:guid}/questions/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> UploadQuestions(
        Guid lessonId,
        IFormFile file,
        [FromQuery] bool hasHeaderRow = true,
        CancellationToken cancellationToken = default)
    {
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
            buffer,
            file.FileName,
            hasHeaderRow,
            _currentUserService.UserId,
            cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }
}
