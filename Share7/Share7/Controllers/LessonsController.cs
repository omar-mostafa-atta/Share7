using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Read side of the question cache protocol used by the game client:
/// check the version first, download the set only when it moved.
/// <para>
/// Every route here exists twice — once for the main question pool and once for the secondary
/// <c>recovery-questions</c> pool, with identical shapes. The two pools are versioned separately,
/// so a client caching both compares two versions and re-downloads only the one that moved.
/// </para>
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LessonsController : ControllerBase
{
    private readonly ILessonQuestionService _lessonQuestionService;
    private readonly ILessonRecoveryQuestionService _lessonRecoveryQuestionService;
    private readonly ICurriculumService _curriculumService;

    public LessonsController(
        ILessonQuestionService lessonQuestionService,
        ILessonRecoveryQuestionService lessonRecoveryQuestionService,
        ICurriculumService curriculumService)
    {
        _lessonQuestionService = lessonQuestionService;
        _lessonRecoveryQuestionService = lessonRecoveryQuestionService;
        _curriculumService = curriculumService;
    }

    /// <summary>
    /// Lessons under a chapter, in the caller's content language. Each entry carries its
    /// current <c>questionsVersion</c>, so a client can validate its whole cache from this
    /// response without a separate version call.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetByChapter([FromQuery] Guid chapterId, CancellationToken cancellationToken)
    {
        if (chapterId == Guid.Empty)
            return BadRequest(new { errors = new[] { "chapterId is required." } });

        var lessons = await _curriculumService.GetLessonsAsync(chapterId, cancellationToken);
        return Ok(lessons);
    }

    /// <summary>
    /// Current question version for one lesson. Cheap — call this on game open and compare
    /// against the cached version before fetching anything.
    /// </summary>
    [HttpGet("{lessonId:guid}/questions/version")]
    public async Task<IActionResult> GetQuestionsVersion(Guid lessonId, CancellationToken cancellationToken)
    {
        var version = await _lessonQuestionService.GetVersionAsync(lessonId, cancellationToken);
        return version is null ? NotFound() : Ok(version);
    }

    /// <summary>
    /// Versions for many lessons in one round trip — the practical form for a client
    /// validating its whole cache at startup. Unknown ids are omitted from the response.
    /// </summary>
    [HttpPost("questions/versions")]
    public async Task<IActionResult> GetQuestionsVersions(
        LessonVersionsRequest request,
        CancellationToken cancellationToken)
    {
        var versions = await _lessonQuestionService.GetVersionsAsync(request.LessonIds, cancellationToken);
        return Ok(versions);
    }

    /// <summary>
    /// Full active question set plus its version. The response carries correctAnswerId so
    /// the client can grade offline against its cache.
    /// </summary>
    [HttpGet("{lessonId:guid}/questions")]
    public async Task<IActionResult> GetQuestions(Guid lessonId, CancellationToken cancellationToken)
    {
        var questions = await _lessonQuestionService.GetQuestionsAsync(lessonId, cancellationToken);
        return questions is null ? NotFound() : Ok(questions);
    }

    /// <summary>
    /// Current <em>recovery</em> question version for one lesson. Same contract as
    /// <c>questions/version</c>, over the secondary pool and its own counter.
    /// </summary>
    [HttpGet("{lessonId:guid}/recovery-questions/version")]
    public async Task<IActionResult> GetRecoveryQuestionsVersion(Guid lessonId, CancellationToken cancellationToken)
    {
        var version = await _lessonRecoveryQuestionService.GetVersionAsync(lessonId, cancellationToken);
        return version is null ? NotFound() : Ok(version);
    }

    /// <summary>
    /// Recovery versions for many lessons in one round trip. Unknown ids are omitted from the
    /// response, exactly as the main pool's batch call does.
    /// </summary>
    [HttpPost("recovery-questions/versions")]
    public async Task<IActionResult> GetRecoveryQuestionsVersions(
        LessonVersionsRequest request,
        CancellationToken cancellationToken)
    {
        var versions = await _lessonRecoveryQuestionService.GetVersionsAsync(request.LessonIds, cancellationToken);
        return Ok(versions);
    }

    /// <summary>
    /// Full active recovery question set plus its version. Identical response shape to
    /// <c>questions</c> — same fields, same <c>correctAnswerId</c> — so the client deserialises it
    /// with the model it already has.
    /// </summary>
    [HttpGet("{lessonId:guid}/recovery-questions")]
    public async Task<IActionResult> GetRecoveryQuestions(Guid lessonId, CancellationToken cancellationToken)
    {
        var questions = await _lessonRecoveryQuestionService.GetQuestionsAsync(lessonId, cancellationToken);
        return questions is null ? NotFound() : Ok(questions);
    }
}
