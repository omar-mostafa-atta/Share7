using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;

namespace Share7.Infrastructure.Workspace;

/// <inheritdoc cref="IReviewService"/>
public sealed class ReviewService : IReviewService
{
    private const int MaxCommentLength = 2000;
    private const int MaxAnchorLength = 1000;

    private readonly ApplicationDbContext _db;
    private readonly DraftService _drafts;
    private readonly DraftKinds _kinds;
    private readonly PeopleDirectory _people;
    private readonly StudioNotifier _notifier;
    private readonly IAuditLog _audit;

    public ReviewService(
        ApplicationDbContext db, DraftService drafts, DraftKinds kinds, PeopleDirectory people, StudioNotifier notifier, IAuditLog audit)
    {
        _db = db;
        _drafts = drafts;
        _kinds = kinds;
        _people = people;
        _notifier = notifier;
        _audit = audit;
    }

    // =====================================================================================
    // The queue
    // =====================================================================================

    public async Task<IReadOnlyList<ReviewQueueItemDto>> QueueAsync(StudioMember member, CancellationToken cancellationToken = default)
    {
        // Oldest first: the one that has waited longest is the one to pick up next.
        var waiting = await _db.Drafts.AsNoTracking()
            .Where(d => d.Status == DraftStatus.InReview && d.IsOpen)
            .OrderBy(d => d.SubmittedAtUtc)
            .ThenBy(d => d.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var summaries = await _drafts.SummariesAsync(waiting, cancellationToken);
        var ids = waiting.Select(d => d.Id).ToList();

        var contributed = (await _db.DraftContributors.AsNoTracking()
                .Where(c => ids.Contains(c.DraftId) && c.UserId == member.UserId)
                .Select(c => c.DraftId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return waiting.Select((draft, i) =>
        {
            var why = WhyNot(member, draft, contributed.Contains(draft.Id));
            return new ReviewQueueItemDto(summaries[i], draft.SubmittedAtUtc ?? draft.UpdatedAtUtc, why is null, why);
        }).ToList();
    }

    /// <summary>Why this member may not review this draft, or null when they may.</summary>
    private static string? WhyNot(StudioMember member, Draft draft, bool isContributor)
    {
        if (!member.IsAtLeast(StudioRole.Reviewer)) return "role";
        if (isContributor) return "ownWork";
        if (!draft.IsPractice && !member.CoversPath(draft.ScopePath)) return "node";
        if (member.LanguagesOutside(DraftService.ParseLanguages(draft.LanguagesTouched)).Count > 0) return "languages";
        return null;
    }

    // =====================================================================================
    // Verdicts
    // =====================================================================================

    public Task<ServiceResult<DraftDto>> ApproveAsync(
        StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default) =>
        DecideAsync(member, draftId, request, ReviewVerdict.Approved, cancellationToken);

    public Task<ServiceResult<DraftDto>> RequestChangesAsync(
        StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default) =>
        DecideAsync(member, draftId, request, ReviewVerdict.ChangesRequested, cancellationToken);

    private async Task<ServiceResult<DraftDto>> DecideAsync(
        StudioMember member, Guid draftId, DraftActionRequest request, ReviewVerdict verdict, CancellationToken cancellationToken)
    {
        var draft = await _db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null) return DraftService.DraftMissing<DraftDto>();

        if (!draft.IsOpen) return DraftService.Closed<DraftDto>();
        if (draft.Status != DraftStatus.InReview) return DraftService.WrongStatus<DraftDto>(draft);

        // The verdict is on exactly what the reviewer read. If it changed while they were reading,
        // they have to read it again.
        if (request.Revision != draft.Revision)
        {
            return ServiceResult<DraftDto>.Failure(
                WorkspaceErrors.DraftRevisionMoved, ServiceErrorKind.Conflict,
                "The draft changed while you were reviewing it.",
                new Dictionary<string, object?> { ["revision"] = draft.Revision });
        }

        var isContributor = await _db.DraftContributors.AnyAsync(c => c.DraftId == draftId && c.UserId == member.UserId, cancellationToken);

        switch (WhyNot(member, draft, isContributor))
        {
            case "ownWork":
                return ServiceResult<DraftDto>.Failure(WorkspaceErrors.OwnWork, ServiceErrorKind.Forbidden,
                    "You wrote part of this draft, so someone else has to review it.");
            case "role":
                return DraftService.OutOfScope<DraftDto>("role");
            case "node":
                return DraftService.OutOfScope<DraftDto>("node");
            case "languages":
                return DraftService.OutOfScope<DraftDto>("languages",
                    member.LanguagesOutside(DraftService.ParseLanguages(draft.LanguagesTouched)));
        }

        var note = request.Note?.Trim();

        if (verdict == ReviewVerdict.ChangesRequested && string.IsNullOrEmpty(note))
        {
            return ServiceResult<DraftDto>.Failure(WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation,
                "Say what needs changing.", new Dictionary<string, object?> { ["reason"] = "note" });
        }

        if (verdict == ReviewVerdict.Approved)
        {
            if (await _kinds.IsOutOfDateAsync(draft, cancellationToken))
                return DraftService.OutOfDate<DraftDto>();

            // Checked again at approval, not only at submission: a rule can have started to apply in
            // between (a sibling took the name, a language became required).
            var problems = await _kinds.CheckAsync(draft, cancellationToken);
            if (problems.Count > 0)
            {
                return ServiceResult<DraftDto>.Failure(WorkspaceErrors.DraftHasProblems, ServiceErrorKind.Validation,
                    "This draft has problems to fix before it can be approved.",
                    new Dictionary<string, object?> { ["problems"] = problems });
            }
        }

        if (note is { Length: > MaxCommentLength })
            note = note[..MaxCommentLength];

        var now = DateTime.UtcNow;
        _db.ReviewDecisions.Add(new ReviewDecision
        {
            Id = Guid.NewGuid(),
            DraftId = draft.Id,
            ReviewerUserId = member.UserId,
            Verdict = verdict,
            Note = note,
            DraftRevision = draft.Revision,
            CreatedAtUtc = now
        });

        draft.Status = verdict == ReviewVerdict.Approved ? DraftStatus.Approved : DraftStatus.ChangesRequested;

        var contributors = await _db.DraftContributors.AsNoTracking().Where(c => c.DraftId == draftId).Select(c => c.UserId).ToListAsync(cancellationToken);
        _notifier.Notify(
            contributors.Append(draft.CreatedByUserId),
            verdict == ReviewVerdict.Approved ? "draft.approved" : "draft.changes_requested",
            member.UserId, draft.Id, nodeId: draft.NodeId);

        _audit.Record(new AuditEntry(
            verdict == ReviewVerdict.Approved ? AuditActions.DraftApproved : AuditActions.DraftChangesRequested,
            AuditAreas.Workspace,
            verdict == ReviewVerdict.Approved ? "Approved a draft." : "Sent a draft back with changes to make.",
            "draft",
            draft.Id.ToString(),
            new { kind = draft.Kind.ToString(), nodeId = draft.NodeId, revision = draft.Revision }));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<DraftDto>.Failure(WorkspaceErrors.DraftRevisionMoved, ServiceErrorKind.Conflict,
                "The draft changed while you were reviewing it.");
        }

        return ServiceResult<DraftDto>.Success(await _drafts.ToDtoAsync(member, draft, cancellationToken));
    }

    // =====================================================================================
    // Comments
    // =====================================================================================

    public async Task<ServiceResult<IReadOnlyList<DraftCommentDto>>> CommentsAsync(
        StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        if (!await _db.Drafts.AnyAsync(d => d.Id == draftId, cancellationToken))
            return DraftService.DraftMissing<IReadOnlyList<DraftCommentDto>>();

        var comments = await _db.DraftComments.AsNoTracking()
            .Where(c => c.DraftId == draftId)
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var names = await _people.NamesAsync(comments.SelectMany(c => new[] { c.AuthorUserId, c.ResolvedByUserId }), cancellationToken);
        return ServiceResult<IReadOnlyList<DraftCommentDto>>.Success(comments.Select(c => ToDto(c, names)).ToList());
    }

    public async Task<ServiceResult<DraftCommentDto>> AddCommentAsync(
        StudioMember member, Guid draftId, CommentRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await _db.Drafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null) return DraftService.DraftMissing<DraftCommentDto>();

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length is 0 or > MaxCommentLength)
            return CommentInvalid("body");

        string? anchor = null;
        if (request.Anchor is { ValueKind: JsonValueKind.Object } element)
        {
            anchor = element.GetRawText();
            if (anchor.Length > MaxAnchorLength) return CommentInvalid("anchor");
        }

        if (request.ParentCommentId is { } parentId
            && !await _db.DraftComments.AnyAsync(c => c.Id == parentId && c.DraftId == draftId && c.ParentCommentId == null, cancellationToken))
        {
            return CommentInvalid("parentCommentId");
        }

        var comment = new DraftComment
        {
            Id = Guid.NewGuid(),
            DraftId = draftId,
            ParentCommentId = request.ParentCommentId,
            AuthorUserId = member.UserId,
            Body = body,
            AnchorJson = anchor,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.DraftComments.Add(comment);

        // Everyone already in the conversation hears about it: the draft's writers, and anyone who
        // has commented on it.
        var people = await _db.DraftContributors.AsNoTracking().Where(c => c.DraftId == draftId).Select(c => c.UserId)
            .Union(_db.DraftComments.AsNoTracking().Where(c => c.DraftId == draftId).Select(c => c.AuthorUserId))
            .ToListAsync(cancellationToken);
        _notifier.Notify(people.Append(draft.CreatedByUserId), "draft.commented", member.UserId, draftId, nodeId: draft.NodeId);

        await _db.SaveChangesAsync(cancellationToken);

        var names = await _people.NamesAsync([member.UserId], cancellationToken);
        return ServiceResult<DraftCommentDto>.Success(ToDto(comment, names));
    }

    public async Task<ServiceResult<DraftCommentDto>> EditCommentAsync(
        StudioMember member, Guid commentId, string body, CancellationToken cancellationToken = default)
    {
        var comment = await _db.DraftComments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);
        if (comment is null) return CommentMissing();

        if (comment.AuthorUserId != member.UserId)
            return DraftService.OutOfScope<DraftCommentDto>("author");

        var text = body?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxCommentLength)
            return CommentInvalid("body");

        comment.Body = text;
        comment.EditedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var names = await _people.NamesAsync([comment.AuthorUserId, comment.ResolvedByUserId], cancellationToken);
        return ServiceResult<DraftCommentDto>.Success(ToDto(comment, names));
    }

    public async Task<ServiceResult<DraftCommentDto>> ResolveCommentAsync(
        StudioMember member, Guid commentId, bool resolved, CancellationToken cancellationToken = default)
    {
        var comment = await _db.DraftComments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);
        if (comment is null) return CommentMissing();

        comment.ResolvedAtUtc = resolved ? DateTime.UtcNow : null;
        comment.ResolvedByUserId = resolved ? member.UserId : null;
        await _db.SaveChangesAsync(cancellationToken);

        var names = await _people.NamesAsync([comment.AuthorUserId, comment.ResolvedByUserId], cancellationToken);
        return ServiceResult<DraftCommentDto>.Success(ToDto(comment, names));
    }

    private static DraftCommentDto ToDto(DraftComment c, IReadOnlyDictionary<Guid, string> names) =>
        new(
            c.Id,
            c.ParentCommentId,
            PersonRefs.Of(names, c.AuthorUserId),
            c.Body,
            c.AnchorJson is null ? null : WorkspaceJson.Element(c.AnchorJson),
            c.CreatedAtUtc,
            c.EditedAtUtc,
            c.ResolvedAtUtc,
            PeopleDirectory.Ref(names, c.ResolvedByUserId));

    private static ServiceResult<DraftCommentDto> CommentMissing() =>
        ServiceResult<DraftCommentDto>.Failure(WorkspaceErrors.CommentNotFound, ServiceErrorKind.NotFound, "Comment not found.");

    private static ServiceResult<DraftCommentDto> CommentInvalid(string reason) =>
        ServiceResult<DraftCommentDto>.Failure(WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation,
            $"The comment's '{reason}' is missing or too long.", new Dictionary<string, object?> { ["reason"] = reason });
}
