using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Authorization;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Workspace;

namespace Share7.API.Controllers;

// ===========================================================================
// The Content Studio's workspace: /api/studio/{curriculum,drafts,reviews,releases,imports,…}
//
// Studio tokens only (the StudioMember policy authenticates with the Studio scheme and nothing
// else), and a member with the 2-step setup still to do is refused here until they have done it.
// The member's role and scope are read fresh on every request; the services check them against the
// exact target of every call. See ContentStudioPhase3.md for the whole flow.
// ===========================================================================

/// <summary>What every workspace controller shares: who is asking, as a member with role and scope.</summary>
[ApiController]
[Authorize(Policy = Policies.StudioMember)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class StudioWorkspaceControllerBase : StudioControllerBase
{
    private StudioMember? _member;

    protected async Task<StudioMember?> MemberAsync(CancellationToken cancellationToken) =>
        _member ??= await HttpContext.RequestServices.GetRequiredService<IStudioMemberResolver>()
            .ResolveAsync(CurrentUserId, cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> for the calling member. A token whose member was suspended a
    /// moment ago is already refused by the session check; this covers the gap before that lands.
    /// </summary>
    protected async Task<IActionResult> AsMember(Func<StudioMember, Task<IActionResult>> action, CancellationToken cancellationToken)
    {
        var member = await MemberAsync(cancellationToken);
        if (member is null)
        {
            return ServiceResult.Failure(WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "Not an active content-team member.",
                new Dictionary<string, object?> { ["reason"] = "member" }).ToApiErrorResult();
        }

        return await action(member);
    }

    protected static IActionResult Reply<T>(ServiceResult<T> result) =>
        result.Succeeded ? new OkObjectResult(result.Value) : result.ToApiErrorResult();

    protected static IActionResult Reply(ServiceResult result) =>
        result.Succeeded ? new NoContentResult() : result.ToApiErrorResult();

    /// <summary>An uploaded sheet: .xlsx only, 10 MB at most — the limits the handbook states.</summary>
    protected static IActionResult? RefuseSheet(IFormFile? file)
    {
        const long MaxBytes = 10 * 1024 * 1024;

        if (file is null || file.Length == 0)
            return Refused("file", "Choose a sheet to upload.");
        if (file.Length > MaxBytes)
            return Refused("fileTooLarge", "Sheets are at most 10 MB.");
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Refused("fileType", "Only .xlsx sheets can be read.");

        return null;

        static IActionResult Refused(string reason, string message) =>
            ServiceResult.Failure(WorkspaceErrors.ImportInvalid, ServiceErrorKind.Validation, message,
                new Dictionary<string, object?> { ["reason"] = reason }).ToApiErrorResult();
    }
}

/// <summary>The curriculum as the Studio browses it: <c>/api/studio/curriculum</c>.</summary>
[Route("api/studio/curriculum")]
public class StudioCurriculumController : StudioWorkspaceControllerBase
{
    private readonly IStudioCurriculumService _curriculum;

    public StudioCurriculumController(IStudioCurriculumService curriculum) => _curriculum = curriculum;

    /// <summary>The content languages, in editor column order, with direction and whether each is required.</summary>
    [HttpGet("languages")]
    public async Task<IActionResult> Languages(CancellationToken cancellationToken) =>
        Ok(await _curriculum.LanguagesAsync(cancellationToken));

    /// <summary>A node's children — or the grades, with no <c>parentId</c> — with drafts, gaps and counts.</summary>
    [HttpGet("nodes")]
    public Task<IActionResult> Children([FromQuery] Guid? parentId, [FromQuery] bool includeRetired, CancellationToken cancellationToken) =>
        AsMember(async member => Ok(await _curriculum.ChildrenAsync(member, parentId, includeRetired, cancellationToken)), cancellationToken);

    [HttpGet("nodes/{nodeId:guid}")]
    public Task<IActionResult> Node(Guid nodeId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _curriculum.NodeAsync(member, nodeId, cancellationToken)), cancellationToken);

    /// <summary>A lesson's workspace: what is live, side by side with the team's open draft.</summary>
    [HttpGet("lessons/{lessonId:guid}/workspace")]
    public Task<IActionResult> Lesson(Guid lessonId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _curriculum.LessonAsync(member, lessonId, cancellationToken)), cancellationToken);

    /// <summary>The question bank: live questions containing <c>q</c>, optionally under a node and in one language.</summary>
    [HttpGet("search")]
    public Task<IActionResult> Search(
        [FromQuery] string q, [FromQuery] Guid? under, [FromQuery] Guid? langId, [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) =>
        AsMember(async _ => Ok(await _curriculum.SearchAsync(q, under, langId, take, cancellationToken)), cancellationToken);
}

/// <summary>Drafts, and reviewing them: <c>/api/studio/drafts</c>.</summary>
[Route("api/studio/drafts")]
public class StudioDraftsController : StudioWorkspaceControllerBase
{
    private readonly IDraftService _drafts;
    private readonly IReviewService _reviews;
    private readonly IStudioImportService _imports;

    public StudioDraftsController(IDraftService drafts, IReviewService reviews, IStudioImportService imports)
    {
        _drafts = drafts;
        _reviews = reviews;
        _imports = imports;
    }

    [HttpGet]
    public Task<IActionResult> List([FromQuery] DraftQuery query, CancellationToken cancellationToken) =>
        AsMember(async member => Ok(await _drafts.ListAsync(member, query, cancellationToken)), cancellationToken);

    /// <summary>Starts a draft, or joins the open one for the same target.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Create(CreateDraftRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.CreateAsync(member, request, cancellationToken)), cancellationToken);

    [HttpGet("{draftId:guid}")]
    public Task<IActionResult> Get(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.GetAsync(member, draftId, cancellationToken)), cancellationToken);

    /// <summary>Autosave. Send the revision you loaded; a moved one is a 409 with the current revision.</summary>
    [HttpPut("{draftId:guid}")]
    public Task<IActionResult> Save(Guid draftId, SaveDraftRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.SaveAsync(member, draftId, request, cancellationToken)), cancellationToken);

    /// <summary>The checks, as they stand — what would stop it being submitted.</summary>
    [HttpPost("{draftId:guid}/check")]
    public Task<IActionResult> Check(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.CheckAsync(member, draftId, cancellationToken)), cancellationToken);

    [HttpGet("{draftId:guid}/diff")]
    public Task<IActionResult> Diff(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.DiffAsync(member, draftId, cancellationToken)), cancellationToken);

    [HttpPost("{draftId:guid}/submit")]
    public Task<IActionResult> Submit(Guid draftId, DraftActionRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.SubmitAsync(member, draftId, request, cancellationToken)), cancellationToken);

    [HttpPost("{draftId:guid}/withdraw")]
    public Task<IActionResult> Withdraw(Guid draftId, DraftActionRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.WithdrawAsync(member, draftId, request, cancellationToken)), cancellationToken);

    /// <summary>The one real delete in the Studio — of work that never reached students.</summary>
    [HttpPost("{draftId:guid}/discard")]
    public Task<IActionResult> Discard(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.DiscardAsync(member, draftId, cancellationToken)), cancellationToken);

    /// <summary>What is live now for the draft's target — what an out-of-date draft is merged against.</summary>
    [HttpGet("{draftId:guid}/live")]
    public Task<IActionResult> Live(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.LiveNowAsync(member, draftId, cancellationToken)), cancellationToken);

    /// <summary>Brings an out-of-date draft up to date with live. Sends it back through review.</summary>
    [HttpPost("{draftId:guid}/rebase")]
    public Task<IActionResult> Rebase(Guid draftId, RebaseDraftRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.RebaseAsync(member, draftId, request, cancellationToken)), cancellationToken);

    /// <summary>"I have this open" — a heartbeat every half minute or so. Answers with who else does.</summary>
    [HttpPost("{draftId:guid}/presence")]
    public Task<IActionResult> Presence(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _drafts.PresenceAsync(member, draftId, cancellationToken)), cancellationToken);

    [HttpPost("{draftId:guid}/approve")]
    public Task<IActionResult> Approve(Guid draftId, DraftActionRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _reviews.ApproveAsync(member, draftId, request, cancellationToken)), cancellationToken);

    /// <summary>Sends a draft back. The note is required: it says what to change.</summary>
    [HttpPost("{draftId:guid}/request-changes")]
    public Task<IActionResult> RequestChanges(Guid draftId, DraftActionRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _reviews.RequestChangesAsync(member, draftId, request, cancellationToken)), cancellationToken);

    [HttpGet("{draftId:guid}/comments")]
    public Task<IActionResult> Comments(Guid draftId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _reviews.CommentsAsync(member, draftId, cancellationToken)), cancellationToken);

    [HttpPost("{draftId:guid}/comments")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Comment(Guid draftId, CommentRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _reviews.AddCommentAsync(member, draftId, request, cancellationToken)), cancellationToken);

    /// <summary>A sheet into a lesson draft. Rows that land on questions already there keep them.</summary>
    [HttpPost("{draftId:guid}/import")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<IActionResult> Import(Guid draftId, [FromForm] int revision, IFormFile? file, CancellationToken cancellationToken) =>
        AsMember(async member =>
        {
            if (RefuseSheet(file) is { } refused) return refused;

            await using var stream = file!.OpenReadStream();
            return Reply(await _imports.ImportToDraftAsync(member, draftId, revision, stream, file.FileName, cancellationToken));
        }, cancellationToken);
}

/// <summary>The review queue and comment threads: <c>/api/studio/reviews</c>.</summary>
[Route("api/studio/reviews")]
public class StudioReviewsController : StudioWorkspaceControllerBase
{
    private readonly IReviewService _reviews;

    public StudioReviewsController(IReviewService reviews) => _reviews = reviews;

    /// <summary>Everything waiting for review, oldest first, and whether the caller may review each.</summary>
    [HttpGet("queue")]
    public Task<IActionResult> Queue(CancellationToken cancellationToken) =>
        AsMember(async member => Ok(await _reviews.QueueAsync(member, cancellationToken)), cancellationToken);

    [HttpPut("comments/{commentId:guid}")]
    public Task<IActionResult> EditComment(Guid commentId, StudioCommentEditRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _reviews.EditCommentAsync(member, commentId, request.Body, cancellationToken)), cancellationToken);

    [HttpPost("comments/{commentId:guid}/resolve")]
    public Task<IActionResult> ResolveComment(Guid commentId, StudioCommentResolveRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _reviews.ResolveCommentAsync(member, commentId, request.Resolved, cancellationToken)), cancellationToken);
}

public sealed record StudioCommentEditRequest(string Body);

public sealed record StudioCommentResolveRequest(bool Resolved = true);

/// <summary>Releases — Leads only: <c>/api/studio/releases</c>.</summary>
[Route("api/studio/releases")]
public class StudioReleasesController : StudioWorkspaceControllerBase
{
    private readonly IReleaseService _releases;

    public StudioReleasesController(IReleaseService releases) => _releases = releases;

    [HttpGet]
    public Task<IActionResult> List([FromQuery] ReleaseStatus? status, CancellationToken cancellationToken) =>
        AsMember(async member => Ok(await _releases.ListAsync(member, status, cancellationToken)), cancellationToken);

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public Task<IActionResult> Create(CreateReleaseRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.CreateAsync(member, request, cancellationToken)), cancellationToken);

    /// <summary>A release with its changes, what would stop it going out, and who it reaches.</summary>
    [HttpGet("{releaseId:guid}")]
    public Task<IActionResult> Get(Guid releaseId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.GetAsync(member, releaseId, cancellationToken)), cancellationToken);

    [HttpPut("{releaseId:guid}/drafts")]
    public Task<IActionResult> UpdateDrafts(Guid releaseId, UpdateReleaseDraftsRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.UpdateDraftsAsync(member, releaseId, request, cancellationToken)), cancellationToken);

    /// <summary>Publishes now, as one transaction. Students see all of it or none of it.</summary>
    [HttpPost("{releaseId:guid}/publish")]
    public Task<IActionResult> Publish(Guid releaseId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.PublishAsync(member, releaseId, cancellationToken)), cancellationToken);

    [HttpPost("{releaseId:guid}/schedule")]
    public Task<IActionResult> Schedule(Guid releaseId, ScheduleReleaseRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.ScheduleAsync(member, releaseId, request, cancellationToken)), cancellationToken);

    [HttpPost("{releaseId:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid releaseId, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.CancelAsync(member, releaseId, cancellationToken)), cancellationToken);

    /// <summary>Puts back what a release changed, as a new release. A reason is required.</summary>
    [HttpPost("{releaseId:guid}/rollback")]
    public Task<IActionResult> Rollback(Guid releaseId, RollbackReleaseRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _releases.RollbackAsync(member, releaseId, request, cancellationToken)), cancellationToken);
}

/// <summary>Excel in and out: <c>/api/studio/imports</c>.</summary>
[Route("api/studio/imports")]
public class StudioImportsController : StudioWorkspaceControllerBase
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IStudioImportService _imports;

    public StudioImportsController(IStudioImportService imports) => _imports = imports;

    /// <summary>Reads a sheet and reports every row it would make and every problem — saves nothing.</summary>
    [HttpPost("trial")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<IActionResult> Trial(IFormFile? file, CancellationToken cancellationToken) =>
        AsMember(async _ =>
        {
            if (RefuseSheet(file) is { } refused) return refused;

            await using var stream = file!.OpenReadStream();
            return Reply(await _imports.TrialAsync(stream, cancellationToken));
        }, cancellationToken);

    /// <summary>A blank sheet for the given languages (<c>languages=en,ar</c>), or every content language.</summary>
    [HttpGet("template")]
    public Task<IActionResult> Template([FromQuery] string? languages, CancellationToken cancellationToken) =>
        AsMember(async _ =>
        {
            var codes = languages?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return File(await _imports.TemplateAsync(codes, cancellationToken), XlsxType, "share7-questions-template.xlsx");
        }, cancellationToken);

    /// <summary>A lesson back out as a sheet: what is live, or (<c>source=draft</c>) the team's open draft.</summary>
    [HttpGet("lessons/{lessonId:guid}/export")]
    public Task<IActionResult> Export(Guid lessonId, [FromQuery] string? source, CancellationToken cancellationToken) =>
        AsMember(async member =>
        {
            var fromDraft = string.Equals(source, "draft", StringComparison.OrdinalIgnoreCase);
            var result = await _imports.ExportAsync(member, lessonId, fromDraft, cancellationToken);
            return result.Succeeded
                ? File(result.Value!, XlsxType, $"lesson-{lessonId:N}{(fromDraft ? "-draft" : string.Empty)}.xlsx")
                : result.ToApiErrorResult();
        }, cancellationToken);
}

/// <summary>A member's inbox, assignments, the team and its activity: <c>/api/studio</c>.</summary>
[Route("api/studio")]
public class StudioInboxController : StudioWorkspaceControllerBase
{
    private readonly IStudioInboxService _inbox;

    public StudioInboxController(IStudioInboxService inbox) => _inbox = inbox;

    [HttpGet("notifications")]
    public Task<IActionResult> Notifications([FromQuery] bool unread = false, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        AsMember(async member => Ok(await _inbox.NotificationsAsync(member, unread, take, cancellationToken)), cancellationToken);

    /// <summary>Marks the listed notifications read — or every one, with no <c>ids</c>.</summary>
    [HttpPost("notifications/read")]
    public Task<IActionResult> MarkRead(StudioMarkReadRequest request, CancellationToken cancellationToken) =>
        AsMember(async member =>
        {
            await _inbox.MarkReadAsync(member, request.Ids, cancellationToken);
            return NoContent();
        }, cancellationToken);

    [HttpGet("assignments")]
    public Task<IActionResult> Assignments([FromQuery] bool mine = true, [FromQuery] bool includeClosed = false, CancellationToken cancellationToken = default) =>
        AsMember(async member => Ok(await _inbox.AssignmentsAsync(member, mine, includeClosed, cancellationToken)), cancellationToken);

    [HttpPost("assignments")]
    public Task<IActionResult> Assign(CreateAssignmentRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _inbox.AssignAsync(member, request, cancellationToken)), cancellationToken);

    [HttpPost("assignments/{assignmentId:guid}/close")]
    public Task<IActionResult> CloseAssignment(Guid assignmentId, StudioCloseAssignmentRequest request, CancellationToken cancellationToken) =>
        AsMember(async member => Reply(await _inbox.CloseAssignmentAsync(member, assignmentId, request.Status, cancellationToken)), cancellationToken);

    /// <summary>The team feed: who changed what, newest first. Page with <c>before</c> (a sequence).</summary>
    [HttpGet("activity")]
    public Task<IActionResult> Activity(
        [FromQuery] Guid? nodeId, [FromQuery] long? before, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        AsMember(async member => Ok(await _inbox.ActivityAsync(member, nodeId, before, take, cancellationToken)), cancellationToken);

    [HttpGet("team")]
    public Task<IActionResult> Team(CancellationToken cancellationToken) =>
        AsMember(async _ => Ok(await _inbox.TeamAsync(cancellationToken)), cancellationToken);
}

public sealed record StudioMarkReadRequest(IReadOnlyList<Guid>? Ids);

public sealed record StudioCloseAssignmentRequest(WorkAssignmentStatus Status);
