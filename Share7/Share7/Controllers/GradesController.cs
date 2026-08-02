using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class GradesController : ControllerBase
{
    private readonly IGradeService _gradeService;

    public GradesController(IGradeService gradeService)
    {
        _gradeService = gradeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var grades = await _gradeService.GetAllAsync(cancellationToken);
        return Ok(grades);
    }
}
