using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubjectsController : ControllerBase
{
    private readonly ICurriculumService _curriculumService;

    public SubjectsController(ICurriculumService curriculumService)
    {
        _curriculumService = curriculumService;
    }

    /// <summary>
    /// Subjects in the caller's content language, taken from the access-token claim.
    /// Optionally narrowed to one term.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? termId, CancellationToken cancellationToken)
    {
        var subjects = await _curriculumService.GetSubjectsAsync(termId, cancellationToken);
        return Ok(subjects);
    }
}
