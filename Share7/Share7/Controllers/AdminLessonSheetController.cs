using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// A lesson's questions as one object: both languages, both pools, one sheet.
/// <para>
/// The per-language routes under <c>/api/admin/lessons/{id}/questions</c> and
/// <c>/recovery-questions</c> still exist and still work — they are the primitive, and a translator
/// finishing one language should not have to resend the other. This is the surface the console uses,
/// because the unit an author works in is a lesson, not a quarter of one.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/lessons/{lessonId:guid}/sheet")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminLessonSheetController : ControllerBase
{
    private readonly ILessonSheetService _sheets;
    private readonly ICurrentUserService _currentUser;

    /// <summary>
    /// Excel's own type, plus the generic fallback some browsers send for an .xlsx picked off a
    /// network share. Anything else is refused before the file is read.
    /// </summary>
    private static readonly string[] AcceptedContentTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/octet-stream"
    ];

    /// <summary>
    /// Sized for the 5,000-row ceiling the parser enforces, with room for formatting. A larger file
    /// is rejected on its length rather than after being buffered and parsed.
    /// </summary>
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public AdminLessonSheetController(ILessonSheetService sheets, ICurrentUserService currentUser)
    {
        _sheets = sheets;
        _currentUser = currentUser;
    }

    /// <summary>Everything published for this lesson, paired by row number.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid lessonId, CancellationToken cancellationToken)
    {
        var sheet = await _sheets.GetAsync(lessonId, cancellationToken);

        return sheet is null
            ? NotFound(new { errors = new[] { "Lesson not found." } })
            : Ok(sheet);
    }

    /// <summary>
    /// Publishes a nine-column workbook. Columns 1–4 are the English question and its three answers
    /// (correct first), 5–8 the same in Arabic, and 9 marks the row as a recovery question.
    /// <para>
    /// All or nothing: one bad row fails the upload with the row number, and a sheet with no recovery
    /// row is refused outright.
    /// </para>
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(
        Guid lessonId,
        IFormFile file,
        [FromQuery] bool hasHeaderRow = true,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { errors = new[] { "No file was uploaded." } });

        if (!AcceptedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { errors = new[] { "Upload an .xlsx workbook." } });

        await using var stream = file.OpenReadStream();

        var result = await _sheets.ImportAsync(
            lessonId, stream, file.FileName, hasHeaderRow, _currentUser.UserId, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Replaces the lesson's whole set with rows typed in the console. A full replace, like the
    /// upload — sending a subset publishes that subset and retires the rest.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Save(
        Guid lessonId, SaveLessonSheetRequest request, CancellationToken cancellationToken)
    {
        var result = await _sheets.SaveAsync(lessonId, request, _currentUser.UserId, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Removes one question in both languages and both pools, and republishes what remains.
    /// <para>
    /// Refused when it would leave the lesson with no recovery questions.
    /// </para>
    /// </summary>
    [HttpDelete("{rowNumber:int}")]
    public async Task<IActionResult> Delete(
        Guid lessonId, int rowNumber, CancellationToken cancellationToken)
    {
        var result = await _sheets.DeleteRowAsync(lessonId, rowNumber, _currentUser.UserId, cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// An empty workbook with the nine headers in place, so an author starts from the right shape
    /// rather than from a description of it.
    /// </summary>
    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = _sheets.BuildTemplate();

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "lesson-questions-template.xlsx");
    }
}
