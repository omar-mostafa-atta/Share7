using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;

namespace Share7.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LanguagesController : ControllerBase
{
    private readonly ILanguageService _languageService;

    public LanguagesController(ILanguageService languageService)
    {
        _languageService = languageService;
    }

    /// <summary>
    /// Content languages and their ids. The client shows these in the language picker and
    /// posts the chosen id back to /api/users/me/preferred-language.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var languages = await _languageService.GetAllAsync(cancellationToken);
        return Ok(languages);
    }
}
