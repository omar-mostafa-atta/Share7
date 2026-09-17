using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TermsController : ControllerBase
{
    private readonly ICurriculumService _curriculumService;

    public TermsController(ICurriculumService curriculumService)
    {
        _curriculumService = curriculumService;
    }

    /// <summary>
    /// Terms in the caller's content language, taken from the access-token claim.
    /// Optionally narrowed to one grade — without it every grade's terms come back, so
    /// "First Term" appears once per grade.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? gradeId, CancellationToken cancellationToken)
    {
        var terms = await _curriculumService.GetTermsAsync(gradeId, cancellationToken);
        return Ok(terms);
    }
}
