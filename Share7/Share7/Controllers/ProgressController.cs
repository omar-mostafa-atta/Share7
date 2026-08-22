using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Progress.Models;
using Share7.Domain.Progress;

namespace Share7.API.Controllers;

/// <summary>
/// A student's progress in one game. Everything here operates on the caller's own record —
/// there is no way to read another user's progress, and no teacher or parent view exists yet
/// (the schema has no class or enrollment relation to hang one off).
/// <para>
/// Progress is tracked independently per game, so every route carries a <c>gameId</c>: acing a
/// lesson in the runner game says nothing about the same lesson in another game.
/// </para>
/// </summary>
[ApiController]
[Route("api/progress")]
[Authorize]
public class ProgressController : ControllerBase
{
    private readonly IProgressService _progressService;
    private readonly ICurrentUserService _currentUser;

    public ProgressController(IProgressService progressService, ICurrentUserService currentUser)
    {
        _progressService = progressService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Records one run of a lesson. **Send what the student picked; the server decides what was
    /// right.**
    /// <code>
    /// {
    ///   "gameId": "…",
    ///   "lessonId": "…",
    ///   "requestId": "one-id-per-run",
    ///   "answers": [
    ///     { "questionId": "…", "choiceId": "…" },
    ///     { "questionId": "…", "choiceId": null }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// One entry per question, carrying the choice the student chose — right or wrong, with
    /// <c>null</c> for skipped. Grading happens server-side against each question's own correct
    /// answer, so **there is no field in which a client can assert a score.** Questions belonging to
    /// the lesson but missing from <c>answers</c> are recorded as wrong: a run shows every question,
    /// so not reaching one is not the same as getting it right.
    /// </para>
    /// <para>
    /// The response carries the recomputed score and a per-question <c>answers[]</c> breakdown —
    /// what was picked, what was right, whether it counted — so a review screen needs no second
    /// call and no client-side regrading. <c>unrecognisedAnswers</c> counts entries naming a
    /// question or choice this lesson does not have; non-zero almost always means a stale cached
    /// question set, so compare <c>questionsVersion</c> and re-fetch.
    /// </para>
    /// <para>
    /// Also returns anything the attempt unlocked, what it earned, and the authoritative balances.
    /// Single-player only for now.
    /// </para>
    /// </summary>
    /// <response code="400">The same question was answered twice.</response>
    /// <response code="403">The lesson is still locked in this game.</response>
    /// <response code="409">The game is disabled, or the lesson has no questions in the caller's language.</response>
    [HttpPost("attempts")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SubmitAttempt(SubmitAttemptRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _progressService.SubmitAttemptAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }

    /// <summary>
    /// One lesson's progress. <c>contentUpdated</c> is true when the question sheet has been
    /// re-uploaded since this score was earned — the score is carried forward rather than reset,
    /// so this is the cue to prompt for a replay.
    /// </summary>
    [HttpGet("games/{gameId:guid}/lessons/{lessonId:guid}")]
    public Task<IActionResult> GetLesson(Guid gameId, Guid lessonId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetLessonProgressAsync(userId, gameId, lessonId, cancellationToken));

    /// <summary>Chapter aggregate, computed from the lesson rows underneath it.</summary>
    [HttpGet("games/{gameId:guid}/chapters/{chapterId:guid}")]
    public Task<IActionResult> GetChapter(Guid gameId, Guid chapterId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetNodeProgressAsync(userId, gameId, CurriculumNodeType.Chapter, chapterId, cancellationToken));

    /// <summary>Subject aggregate.</summary>
    [HttpGet("games/{gameId:guid}/subjects/{subjectId:guid}")]
    public Task<IActionResult> GetSubject(Guid gameId, Guid subjectId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetNodeProgressAsync(userId, gameId, CurriculumNodeType.Subject, subjectId, cancellationToken));

    /// <summary>Term aggregate.</summary>
    [HttpGet("games/{gameId:guid}/terms/{termId:guid}")]
    public Task<IActionResult> GetTerm(Guid gameId, Guid termId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetNodeProgressAsync(userId, gameId, CurriculumNodeType.Term, termId, cancellationToken));

    /// <summary>Grade aggregate. Grades are never locked, so <c>isUnlocked</c> is always true here.</summary>
    [HttpGet("games/{gameId:guid}/grades/{gradeId:guid}")]
    public Task<IActionResult> GetGrade(Guid gameId, Guid gradeId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetGradeProgressAsync(userId, gameId, gradeId, cancellationToken));

    /// <summary>
    /// Questions the student did not get right on their last run of this lesson. Only questions
    /// that are still active are reported, so a lesson whose sheet was re-uploaded comes back
    /// empty until it is played again.
    /// </summary>
    [HttpGet("games/{gameId:guid}/lessons/{lessonId:guid}/wrong-questions")]
    public Task<IActionResult> GetWrongQuestions(Guid gameId, Guid lessonId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetWrongQuestionsAsync(userId, gameId, lessonId, cancellationToken));

    /// <summary>
    /// The whole grade in one call — every term, subject, chapter and lesson with its
    /// locked/unlocked state and completion. This is what the client wants on game open;
    /// the per-node endpoints above are for drilling in afterwards.
    /// <paramref name="gradeId"/> defaults to the student's own grade from their profile.
    /// </summary>
    [HttpGet("games/{gameId:guid}/snapshot")]
    public Task<IActionResult> GetSnapshot(Guid gameId, [FromQuery] Guid? gradeId, CancellationToken cancellationToken) =>
        Run(userId => _progressService.GetSnapshotAsync(userId, gameId, gradeId, cancellationToken));

    /// <summary>
    /// Resolves the caller and maps the result, so every read above stays a single expression
    /// instead of repeating the same null check and status mapping.
    /// </summary>
    private async Task<IActionResult> Run<T>(Func<Guid, Task<Application.Common.Models.ServiceResult<T>>> operation)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await operation(userId);
        return result.Succeeded ? Ok(result.Value) : result.ToErrorResult();
    }
}
