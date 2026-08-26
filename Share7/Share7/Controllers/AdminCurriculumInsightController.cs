using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// The curriculum seen sideways: how complete it is, what is broken in it, and every question under
/// any branch of it.
/// <para>
/// Separate from <c>AdminCurriculumController</c>, which authors nodes. These are read-only views
/// over the same tree, and they answer questions the tree cannot: the tree says what is under a
/// node, this says what is <i>wrong</i> under it and what is <i>in</i> it.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/curriculum")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminCurriculumInsightController : ControllerBase
{
    private readonly ICurriculumHealthService _health;
    private readonly ICurriculumSearchService _search;

    public AdminCurriculumInsightController(
        ICurriculumHealthService health, ICurriculumSearchService search)
    {
        _health = health;
        _search = search;
    }

    /// <summary>
    /// Coverage counters for the whole tree, plus every finding — empty branches, unpublished
    /// lessons, lessons with no recovery pool, language gaps, version drift and missing names.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken) =>
        Ok(await _health.GetAsync(cancellationToken));

    /// <summary>
    /// Every question under a node, paired by language and paged.
    /// <para>
    /// <c>scopeLevel</c> is grade, term, subject, chapter or lesson; omitting both scope parameters
    /// searches the whole curriculum, which on a seeded database is tens of thousands of rows and is
    /// why paging is not optional.
    /// </para>
    /// </summary>
    [HttpGet("questions")]
    public async Task<IActionResult> Questions(
        [FromQuery] string? scopeLevel,
        [FromQuery] Guid? scopeId,
        [FromQuery] QuestionPoolFilter pool = QuestionPoolFilter.All,
        [FromQuery] string? search = null,
        [FromQuery] bool onlyUnpaired = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _search.SearchAsync(
            new QuestionSearchRequest
            {
                ScopeLevel = scopeLevel,
                ScopeId = scopeId,
                Pool = pool,
                Search = search,
                OnlyUnpaired = onlyUnpaired,
                Page = page,
                PageSize = pageSize
            },
            cancellationToken);

        return Ok(result);
    }
}
