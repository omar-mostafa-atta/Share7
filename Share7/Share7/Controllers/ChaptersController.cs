using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChaptersController : ControllerBase
{
    private readonly ICurriculumService _curriculumService;

    public ChaptersController(ICurriculumService curriculumService)
    {
        _curriculumService = curriculumService;
    }

    /// <summary>
    /// Chapters under a subject, with names in the caller's content language. The tree itself
    /// is language-independent, so a chapter has the same id whichever language asks for it.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBySubject([FromQuery] Guid subjectId, CancellationToken cancellationToken)
    {
        if (subjectId == Guid.Empty)
            return BadRequest(new { errors = new[] { "subjectId is required." } });

        var chapters = await _curriculumService.GetChaptersAsync(subjectId, cancellationToken);
        return Ok(chapters);
    }
}
