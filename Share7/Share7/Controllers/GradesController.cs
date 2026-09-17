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

    /// <summary>
    /// Grades in one language. With a bearer token the caller's preferred language is used;
    /// <paramref name="langId"/> overrides it and is the way anonymous callers (e.g. the
    /// admin page before sign-in) pick a language explicitly. Defaults to English.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? langId, CancellationToken cancellationToken)
    {
        var grades = await _gradeService.GetAllAsync(langId, cancellationToken);
        return Ok(grades);
    }
}
