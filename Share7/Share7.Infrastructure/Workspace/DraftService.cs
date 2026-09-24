using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Staff.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;

namespace Share7.Infrastructure.Workspace;

/// <inheritdoc cref="IDraftService"/>
public sealed class DraftService : IDraftService
{
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromSeconds(60);

    private readonly ApplicationDbContext _db;
    private readonly DraftKinds _kinds;
    private readonly IContentLanguages _languages;
    private readonly NodeTrails _trails;
    private readonly PeopleDirectory _people;
    private readonly StudioNotifier _notifier;
    private readonly IAuditLog _audit;

    public DraftService(
        ApplicationDbContext db,
        DraftKinds kinds,
        IContentLanguages languages,
        NodeTrails trails,
        PeopleDirectory people,
        StudioNotifier notifier,
        IAuditLog audit)
    {
        _db = db;
        _kinds = kinds;
        _languages = languages;
        _trails = trails;
        _people = people;
        _notifier = notifier;
        _audit = audit;
    }

    // =====================================================================================
    // Start
    // =====================================================================================

    public async Task<ServiceResult<DraftDto>> CreateAsync(
        StudioMember member, CreateDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (request.IsPractice)
            return await CreatePracticeAsync(member, request, cancellationToken);

        var target = await ResolveTargetAsync(member, request, cancellationToken);
        if (!target.Succeeded)
            return Propagate<DraftDto>(target);

        var (nodeId, parentId, scopePath, title) = target.Value!;

        // The team shares one open draft per target and kind: opening a lesson somebody is already
        // working on joins their draft rather than starting a rival one.
        if (request.Kind != DraftKind.NewNode)
        {
            var existing = await _db.Drafts.FirstOrDefaultAsync(
                d => d.NodeId == nodeId && d.Kind == request.Kind && d.IsOpen && !d.IsPractice, cancellationToken);

            if (existing is not null)
                return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, existing, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var draft = new Draft
        {
            Id = Guid.NewGuid(),
            Kind = request.Kind,
            NodeId = nodeId,
            ParentNodeId = parentId,
            NodeKind = request.Kind == DraftKind.NewNode ? request.NodeKind : null,
            ScopePath = scopePath,
            Title = title,
            CreatedByUserId = member.UserId,
            CreatedAtUtc = now,
            UpdatedByUserId = member.UserId,
            UpdatedAtUtc = now
        };

        var (baseJson, fingerprint) = await _kinds.LiveAsync(draft, cancellationToken);
        draft.BaseJson = baseJson;
        draft.BaseFingerprint = fingerprint;
        draft.ProposedJson = DraftKinds.DefaultProposal(draft.Kind, baseJson);

        if (request.Proposal is { } proposal)
        {
            var applied = ApplyProposal(member, draft, proposal, now);
            if (!applied.Succeeded)
                return Propagate<DraftDto>(applied);
        }

        _db.Drafts.Add(draft);
        Record(AuditActions.DraftCreated, draft, $"Started a draft: {InWords(draft.Kind)}.");

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.Kind != DraftKind.NewNode)
        {
            // Two people opened the same lesson at the same moment and both tried to start its
            // draft. The unique index let one through; the other joins it.
            _db.ChangeTracker.Clear();
            var winner = await _db.Drafts.FirstAsync(
                d => d.NodeId == nodeId && d.Kind == request.Kind && d.IsOpen && !d.IsPractice, cancellationToken);
            return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, winner, cancellationToken));
        }

        return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    private async Task<ServiceResult<DraftDto>> CreatePracticeAsync(
        StudioMember member, CreateDraftRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind != DraftKind.LessonContent)
            return Invalid<DraftDto>("practice");

        var now = DateTime.UtcNow;
        var draft = new Draft
        {
            Id = Guid.NewGuid(),
            Kind = DraftKind.LessonContent,
            IsPractice = true,
            ScopePath = string.Empty,
            Title = "Practice lesson",
            BaseJson = WorkspaceJson.Write(new LessonContentDto { LessonId = Guid.Empty, Sets = [], Items = [] }),
            ProposedJson = WorkspaceJson.Write(new LessonContentProposal([])),
            CreatedByUserId = member.UserId,
            CreatedAtUtc = now,
            UpdatedByUserId = member.UserId,
            UpdatedAtUtc = now
        };

        if (request.Proposal is { } proposal)
        {
            var applied = ApplyProposal(member, draft, proposal, now);
            if (!applied.Succeeded)
                return Propagate<DraftDto>(applied);
        }

        _db.Drafts.Add(draft);
        Record(AuditActions.DraftCreated, draft, "Started a practice draft.");
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    /// <summary>Where a new draft points, checked against the member's scope.</summary>
    private async Task<ServiceResult<(Guid NodeId, Guid? ParentId, string ScopePath, string Title)>> ResolveTargetAsync(
        StudioMember member, CreateDraftRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind == DraftKind.NewNode)
        {
            if (request.NodeKind is not { } kind || !NodeKinds.IsEditable(kind))
                return Invalid<(Guid, Guid?, string, string)>("nodeKind");

            if (request.ParentNodeId is not { } parentId)
                return Invalid<(Guid, Guid?, string, string)>("parentNodeId");

            // The parent may be live, or itself a new node in another open draft — a new chapter and
            // its lessons are drafted together and released together.
            var parent = await _db.CurriculumNodes.AsNoTracking()
                .Where(n => n.Id == parentId && n.RetiredAtUtc == null)
                .Select(n => new { n.KindKey, n.Path })
                .FirstOrDefaultAsync(cancellationToken);

            string parentKind, parentPath;
            if (parent is not null)
            {
                (parentKind, parentPath) = (parent.KindKey, parent.Path);
            }
            else
            {
                var proposed = await _db.Drafts.AsNoTracking()
                    .Where(d => d.Kind == DraftKind.NewNode && d.NodeId == parentId && d.IsOpen)
                    .Select(d => new { d.NodeKind, d.ScopePath })
                    .FirstOrDefaultAsync(cancellationToken);

                if (proposed is null)
                    return NodeMissing<(Guid, Guid?, string, string)>();

                (parentKind, parentPath) = (proposed.NodeKind!, $"{proposed.ScopePath}/{parentId:D}");
            }

            if (parentKind != NodeKinds.ParentOf(kind))
                return Invalid<(Guid, Guid?, string, string)>("parentKind");

            if (!member.CoversPath(parentPath))
                return OutOfScope<(Guid, Guid?, string, string)>("node");

            return ServiceResult<(Guid, Guid?, string, string)>.Success((Guid.NewGuid(), parentId, parentPath, $"New {kind}"));
        }

        if (request.NodeId is not { } nodeId)
            return Invalid<(Guid, Guid?, string, string)>("nodeId");

        var node = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new
            {
                n.KindKey,
                n.Path,
                n.RetiredAtUtc,
                n.ParentNodeId,
                Title = n.Translations.OrderBy(t => t.Language!.SortOrder).Select(t => t.Title).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (node is null)
            return NodeMissing<(Guid, Guid?, string, string)>();

        var fits = request.Kind switch
        {
            DraftKind.LessonContent => node.KindKey == NodeKinds.Lesson && node.RetiredAtUtc is null,
            DraftKind.Restore => NodeKinds.IsEditable(node.KindKey) && node.RetiredAtUtc is not null,
            DraftKind.Reorder => node.RetiredAtUtc is null && node.KindKey != NodeKinds.Lesson,

            // Any live node, grades included. `IsEditable` is about *structural* editing — the
            // fourteen grades are fixed because their ids are on student profiles — and writing a
            // recovery rule at a grade edits nothing about the grade. Holding this kind to that
            // predicate refused the first of the three placements the feature is described by
            // ("grade, subject or lesson"), while the Recovery board went on offering it: the board
            // showed the grade, said what it was inheriting, and its propose button answered 400.
            DraftKind.RecoveryRule => node.RetiredAtUtc is null,

            _ => NodeKinds.IsEditable(node.KindKey) && node.RetiredAtUtc is null
        };

        if (!fits)
            return Invalid<(Guid, Guid?, string, string)>("nodeKind");

        if (!member.CoversPath(node.Path))
            return OutOfScope<(Guid, Guid?, string, string)>("node");

        return ServiceResult<(Guid, Guid?, string, string)>.Success((nodeId, node.ParentNodeId, node.Path, node.Title ?? node.KindKey));
    }

    // =====================================================================================
    // Read
    // =====================================================================================

    public async Task<ServiceResult<DraftDto>> GetAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken);
        return draft is null
            ? DraftMissing<DraftDto>()
            : ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    public async Task<DraftPageDto> ListAsync(StudioMember member, DraftQuery query, CancellationToken cancellationToken = default)
    {
        var drafts = _db.Drafts.AsNoTracking().AsQueryable();

        if (!query.IncludeClosed) drafts = drafts.Where(d => d.IsOpen);
        if (!query.IncludePractice) drafts = drafts.Where(d => !d.IsPractice || d.CreatedByUserId == member.UserId);
        if (query.Status is { } status) drafts = drafts.Where(d => d.Status == status);
        if (query.NodeId is { } nodeId) drafts = drafts.Where(d => d.NodeId == nodeId);

        if (query.Mine)
            drafts = drafts.Where(d => d.CreatedByUserId == member.UserId
                                       || _db.DraftContributors.Any(c => c.DraftId == d.Id && c.UserId == member.UserId));

        if (query.UnderNodeId is { } under)
        {
            var path = await _db.CurriculumNodes.AsNoTracking().Where(n => n.Id == under).Select(n => n.Path).FirstOrDefaultAsync(cancellationToken);
            if (path is not null)
                drafts = drafts.Where(d => d.ScopePath == path || d.ScopePath.StartsWith(path + "/"));
        }

        var total = await drafts.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var page = Math.Max(1, query.Page);

        var rows = await drafts
            .OrderByDescending(d => d.UpdatedAtUtc)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new DraftPageDto(await SummariesAsync(rows, cancellationToken), total, page, pageSize);
    }

    public async Task<ServiceResult<IReadOnlyList<ContentProblem>>> CheckAsync(
        StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken);
        return draft is null
            ? DraftMissing<IReadOnlyList<ContentProblem>>()
            : ServiceResult<IReadOnlyList<ContentProblem>>.Success(await _kinds.CheckAsync(draft, cancellationToken));
    }

    public async Task<ServiceResult<DraftDiffDto>> DiffAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken);
        return draft is null
            ? DraftMissing<DraftDiffDto>()
            : ServiceResult<DraftDiffDto>.Success(new DraftDiffDto(DraftKinds.Diff(draft)));
    }

    public async Task<ServiceResult<JsonElement>> LiveNowAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken);
        if (draft is null)
            return DraftMissing<JsonElement>();

        var (live, _) = await _kinds.LiveAsync(draft, cancellationToken);
        return ServiceResult<JsonElement>.Success(WorkspaceJson.Element(live));
    }

    // =====================================================================================
    // Write
    // =====================================================================================

    public async Task<ServiceResult<DraftDto>> SaveAsync(
        StudioMember member, Guid draftId, SaveDraftRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken, tracked: true);
        if (draft is null) return DraftMissing<DraftDto>();

        if (Refuse(member, draft, request.Revision) is { } refused)
            return Propagate<DraftDto>(refused);

        var now = DateTime.UtcNow;
        var applied = ApplyProposal(member, draft, request.Proposal, now);
        if (!applied.Succeeded)
            return Propagate<DraftDto>(applied);

        if (!await TrySaveAsync(draft, cancellationToken))
            return Crossed<DraftDto>();
        return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    /// <summary>
    /// Replaces the proposal after checking its shape and the member's language scope, marks them a
    /// contributor, and — because what was reviewed is no longer what is there — takes a draft that
    /// was in review or approved back to editing.
    /// </summary>
    private ServiceResult ApplyProposal(StudioMember member, Draft draft, JsonElement proposal, DateTime now)
    {
        if (DraftKinds.Shape(draft.Kind, proposal) is { } reason)
            return Invalid(reason);

        var json = proposal.GetRawText();

        // What this save changes, language by language — a member writing only in Arabic can change
        // the Arabic of a question, and add Arabic questions, but not the English.
        var changed = DraftKinds.LanguagesTouched(draft.Kind, draft.ProposedJson, json, fromIsBase: false);
        var outside = member.LanguagesOutside(changed);
        if (outside.Count > 0)
            return OutOfScope("languages", outside);

        // A move needs scope at both ends: where it leaves and where it lands.
        if (draft.Kind == DraftKind.Move
            && WorkspaceJson.Parse<MoveProposal>(proposal) is { } move
            && _db.CurriculumNodes.AsNoTracking().Where(n => n.Id == move.NewParentId).Select(n => n.Path).FirstOrDefault() is { } targetPath
            && !member.CoversPath(targetPath))
        {
            return OutOfScope("node");
        }

        draft.ProposedJson = json;
        draft.LanguagesTouched = string.Join(' ', DraftKinds.LanguagesTouched(draft.Kind, draft.BaseJson, json, fromIsBase: true));

        if (draft.Kind == DraftKind.NewNode && WorkspaceJson.Parse<NewNodeProposal>(proposal) is { Titles.Count: > 0 } created)
            draft.Title = created.Titles.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Title))?.Title?.Trim() ?? draft.Title;

        Touch(member, draft, now);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult<DraftDto>> SubmitAsync(
        StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken, tracked: true);
        if (draft is null) return DraftMissing<DraftDto>();

        if (Refuse(member, draft, request.Revision) is { } refused)
            return Propagate<DraftDto>(refused);

        if (draft.Status is not (DraftStatus.Editing or DraftStatus.ChangesRequested))
            return WrongStatus<DraftDto>(draft);

        if (await _kinds.IsOutOfDateAsync(draft, cancellationToken))
            return OutOfDate<DraftDto>();

        var problems = await _kinds.CheckAsync(draft, cancellationToken);
        if (problems.Count > 0)
        {
            return ServiceResult<DraftDto>.Failure(
                WorkspaceErrors.DraftHasProblems, ServiceErrorKind.Validation,
                $"{problems.Count} thing(s) to fix before this can be reviewed.",
                new Dictionary<string, object?> { ["problems"] = problems });
        }

        var now = DateTime.UtcNow;
        draft.Status = DraftStatus.InReview;
        draft.SubmittedAtUtc = now;
        draft.SubmittedByUserId = member.UserId;

        // Everyone who could review it: reviewers and Leads whose scope covers it, apart from the
        // people who wrote it.
        var contributors = await ContributorIdsAsync(draft, cancellationToken);
        var reviewers = draft.IsPractice
            ? await _notifier.MembersCoveringAsync(string.Empty, StudioRole.Reviewer, cancellationToken)
            : await _notifier.MembersCoveringAsync(draft.ScopePath, StudioRole.Reviewer, cancellationToken);

        _notifier.Notify(reviewers.Except(contributors), "draft.submitted", member.UserId, draft.Id, nodeId: draft.NodeId);
        Record(AuditActions.DraftSubmitted, draft, "Sent a draft for review.");

        if (!await TrySaveAsync(draft, cancellationToken))
            return Crossed<DraftDto>();
        return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    public async Task<ServiceResult<DraftDto>> WithdrawAsync(
        StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken, tracked: true);
        if (draft is null) return DraftMissing<DraftDto>();

        if (Refuse(member, draft, request.Revision) is { } refused)
            return Propagate<DraftDto>(refused);

        if (draft.Status is not (DraftStatus.InReview or DraftStatus.Approved))
            return WrongStatus<DraftDto>(draft);

        draft.Status = DraftStatus.Editing;
        Record(AuditActions.DraftWithdrawn, draft, "Took a draft back out of review.");

        if (!await TrySaveAsync(draft, cancellationToken))
            return Crossed<DraftDto>();
        return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    public async Task<ServiceResult> DiscardAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken, tracked: true);
        if (draft is null) return DraftMissing<DraftDto>();

        if (!draft.IsOpen)
            return Closed<DraftDto>();

        // Discarding is the one real delete in the Studio, and only of work that never reached
        // students. The person who started it can always throw it away; otherwise it takes scope.
        if (draft.CreatedByUserId != member.UserId && !CanEdit(member, draft))
            return OutOfScope<DraftDto>("node");

        if (await InActiveReleaseAsync(draft.Id, cancellationToken))
            return WrongStatus<DraftDto>(draft);

        var now = DateTime.UtcNow;
        draft.Status = DraftStatus.Discarded;
        draft.IsOpen = false;
        draft.DiscardedAtUtc = now;
        draft.DiscardedByUserId = member.UserId;
        Record(AuditActions.DraftDiscarded, draft, "Discarded a draft that was never published.");

        if (!await TrySaveAsync(draft, cancellationToken))
            return Crossed<DraftDto>();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<DraftDto>> RebaseAsync(
        StudioMember member, Guid draftId, RebaseDraftRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await LoadAsync(draftId, cancellationToken, tracked: true);
        if (draft is null) return DraftMissing<DraftDto>();

        if (Refuse(member, draft, request.Revision) is { } refused)
            return Propagate<DraftDto>(refused);

        var now = DateTime.UtcNow;
        var (live, fingerprint) = await _kinds.LiveAsync(draft, cancellationToken);
        if (fingerprint == "gone")
            return NodeMissing<DraftDto>();

        draft.BaseJson = live;
        draft.BaseFingerprint = fingerprint;

        // A content draft is merged by its author against the new live state, and sends that merge;
        // a structural one keeps what it proposes and only takes the new base.
        if (request.Proposal is { } merged)
        {
            var applied = ApplyProposal(member, draft, merged, now);
            if (!applied.Succeeded)
                return Propagate<DraftDto>(applied);
        }
        else
        {
            draft.LanguagesTouched = string.Join(' ', DraftKinds.LanguagesTouched(draft.Kind, draft.BaseJson, draft.ProposedJson, fromIsBase: true));
            Touch(member, draft, now);
        }

        Record(AuditActions.DraftRebased, draft, "Brought a draft up to date with what is live.");

        if (!await TrySaveAsync(draft, cancellationToken))
            return Crossed<DraftDto>();
        return ServiceResult<DraftDto>.Success(await ToDtoAsync(member, draft, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<PersonRefDto>>> PresenceAsync(
        StudioMember member, Guid draftId, CancellationToken cancellationToken = default)
    {
        if (!await _db.Drafts.AnyAsync(d => d.Id == draftId, cancellationToken))
            return DraftMissing<IReadOnlyList<PersonRefDto>>();

        var now = DateTime.UtcNow;
        var row = await _db.DraftPresence.FirstOrDefaultAsync(p => p.DraftId == draftId && p.UserId == member.UserId, cancellationToken);
        if (row is null)
            _db.DraftPresence.Add(new DraftPresence { DraftId = draftId, UserId = member.UserId, LastSeenAtUtc = now });
        else
            row.LastSeenAtUtc = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two heartbeats from two tabs at once; one row is enough.
            _db.ChangeTracker.Clear();
        }

        return ServiceResult<IReadOnlyList<PersonRefDto>>.Success(await AlsoHereAsync(draftId, member.UserId, cancellationToken));
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    /// <summary>Everything that stops an edit: a closed draft, a stale revision, a draft outside scope.</summary>
    private ServiceResult? Refuse(StudioMember member, Draft draft, int revision)
    {
        if (!draft.IsOpen)
            return Closed<DraftDto>();

        if (revision != draft.Revision)
        {
            return ServiceResult.Failure(
                WorkspaceErrors.DraftRevisionMoved, ServiceErrorKind.Conflict,
                "Someone saved this draft since you loaded it.",
                new Dictionary<string, object?> { ["revision"] = draft.Revision });
        }

        if (!CanEdit(member, draft))
            return OutOfScope<DraftDto>("node");

        return null;
    }

    internal static bool CanEdit(StudioMember member, Draft draft) =>
        draft.IsOpen && (draft.IsPractice ? draft.CreatedByUserId == member.UserId : member.CoversPath(draft.ScopePath));

    private void Touch(StudioMember member, Draft draft, DateTime now)
    {
        draft.Revision += 1;
        draft.UpdatedAtUtc = now;
        draft.UpdatedByUserId = member.UserId;

        // What was reviewed is not what is there any more.
        if (draft.Status is DraftStatus.InReview or DraftStatus.Approved or DraftStatus.ChangesRequested)
            draft.Status = DraftStatus.Editing;

        var contributor = draft.Contributors.FirstOrDefault(c => c.UserId == member.UserId)
                          ?? _db.DraftContributors.Local.FirstOrDefault(c => c.DraftId == draft.Id && c.UserId == member.UserId);

        if (contributor is null)
            draft.Contributors.Add(new DraftContributor { DraftId = draft.Id, UserId = member.UserId, FirstEditAtUtc = now, LastEditAtUtc = now });
        else
            contributor.LastEditAtUtc = now;
    }

    /// <summary>
    /// Saves, unless the row moved between our read and our write — somebody else's save won the
    /// race. The revision check cannot see that window; the row version can.
    /// </summary>
    private async Task<bool> TrySaveAsync(Draft draft, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private static ServiceResult<T> Crossed<T>() =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftRevisionMoved, ServiceErrorKind.Conflict,
            "Someone saved this draft at the same moment. Reload it and try again.");

    private Task<Draft?> LoadAsync(Guid draftId, CancellationToken cancellationToken, bool tracked = false)
    {
        var query = _db.Drafts.Include(d => d.Contributors).Where(d => d.Id == draftId);
        return (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> InActiveReleaseAsync(Guid draftId, CancellationToken cancellationToken) =>
        await (from e in _db.ReleaseEntries
               join r in _db.Releases on e.ReleaseId equals r.Id
               where e.DraftId == draftId && (r.Status == ReleaseStatus.Building || r.Status == ReleaseStatus.Scheduled
                                              || r.Status == ReleaseStatus.Publishing || r.Status == ReleaseStatus.Failed)
               select e.Id).AnyAsync(cancellationToken);

    private async Task<IReadOnlyList<Guid>> ContributorIdsAsync(Draft draft, CancellationToken cancellationToken) =>
        await _db.DraftContributors.AsNoTracking().Where(c => c.DraftId == draft.Id).Select(c => c.UserId).ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<PersonRefDto>> AlsoHereAsync(Guid draftId, Guid self, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - PresenceWindow;
        var here = await _db.DraftPresence.AsNoTracking()
            .Where(p => p.DraftId == draftId && p.UserId != self && p.LastSeenAtUtc >= since)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        var names = await _people.NamesAsync(here.Cast<Guid?>(), cancellationToken);
        return here.Select(id => PersonRefs.Of(names, id)).ToList();
    }

    /// <summary>
    /// What a kind of draft is called in a sentence. The audit summary is read by
    /// people — in the Studio's activity feed and in the SuperAdmin's audit log —
    /// so it says "a lesson's questions", never <c>LessonContent</c>. The kind's
    /// own name is still in the detail object beside it for anything machine-read.
    /// </summary>
    private static string InWords(DraftKind kind) => kind switch
    {
        DraftKind.LessonContent => "a lesson's questions",
        DraftKind.NewNode => "something new",
        DraftKind.Rename => "a new name",
        DraftKind.Move => "a move",
        DraftKind.Reorder => "a new order",
        DraftKind.Retire => "taking something out",
        DraftKind.Restore => "putting something back",
        DraftKind.RecoveryRule => "when second chances are offered",
        _ => "a change",
    };

    private void Record(string action, Draft draft, string summary) =>
        _audit.Record(new AuditEntry(
            action,
            AuditAreas.Workspace,
            summary,
            "draft",
            draft.Id.ToString(),
            new { kind = draft.Kind.ToString(), nodeId = draft.NodeId, parentNodeId = draft.ParentNodeId, practice = draft.IsPractice, revision = draft.Revision }));

    // =====================================================================================
    // Shaping for the wire
    // =====================================================================================

    internal async Task<DraftDto> ToDtoAsync(StudioMember member, Draft draft, CancellationToken cancellationToken)
    {
        var summary = (await SummariesAsync([draft], cancellationToken))[0];
        var problems = draft.IsOpen ? await _kinds.CheckAsync(draft, cancellationToken) : [];

        var contributors = await _db.DraftContributors.AsNoTracking()
            .Where(c => c.DraftId == draft.Id)
            .OrderBy(c => c.FirstEditAtUtc)
            .Select(c => c.UserId)
            .ToListAsync(cancellationToken);

        var reviews = await _db.ReviewDecisions.AsNoTracking()
            .Where(r => r.DraftId == draft.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var names = await _people.NamesAsync(contributors.Concat(reviews.Select(r => r.ReviewerUserId)).Cast<Guid?>(), cancellationToken);
        var touched = ParseLanguages(draft.LanguagesTouched);

        var isContributor = contributors.Contains(member.UserId);
        var canEdit = CanEdit(member, draft);

        var canReview = draft.Status == DraftStatus.InReview
                        && member.IsAtLeast(StudioRole.Reviewer)
                        && !isContributor
                        && (draft.IsPractice || member.CoversPath(draft.ScopePath))
                        && member.LanguagesOutside(touched).Count == 0;

        var canRelease = draft.Status == DraftStatus.Approved && !draft.IsPractice
                         && member.IsAtLeast(StudioRole.Lead) && member.CoversPath(draft.ScopePath);

        return new DraftDto
        {
            Summary = summary,
            Proposal = WorkspaceJson.Element(draft.ProposedJson),
            Base = WorkspaceJson.Element(draft.BaseJson),
            Problems = problems,
            LanguagesTouched = touched,
            Contributors = contributors.Select(id => PersonRefs.Of(names, id)).ToList(),
            Reviews = reviews.Select(r => new ReviewDecisionDto(
                r.Id, PersonRefs.Of(names, r.ReviewerUserId), r.Verdict, r.Note, r.DraftRevision,
                r.DraftRevision == draft.Revision, r.CreatedAtUtc)).ToList(),
            AlsoHere = await AlsoHereAsync(draft.Id, member.UserId, cancellationToken),
            Can = new DraftPermissionsDto(
                Edit: canEdit,
                Submit: canEdit && draft.Status is DraftStatus.Editing or DraftStatus.ChangesRequested,
                Review: canReview,
                Discard: draft.IsOpen && (canEdit || draft.CreatedByUserId == member.UserId),
                Release: canRelease)
        };
    }

    internal async Task<IReadOnlyList<DraftSummaryDto>> SummariesAsync(IReadOnlyList<Draft> drafts, CancellationToken cancellationToken)
    {
        if (drafts.Count == 0) return [];

        var ids = drafts.Select(d => d.Id).ToList();

        var openComments = await _db.DraftComments.AsNoTracking()
            .Where(c => ids.Contains(c.DraftId) && c.ResolvedAtUtc == null && c.ParentCommentId == null)
            .GroupBy(c => c.DraftId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var trailNodes = drafts.Select(d => d.Kind == DraftKind.NewNode ? d.ParentNodeId : d.NodeId).OfType<Guid>();
        var trails = await _trails.ForAsync(trailNodes, cancellationToken);
        var outOfDate = await _kinds.OutOfDateAsync(drafts, cancellationToken);
        var names = await _people.NamesAsync(drafts.SelectMany(d => new Guid?[] { d.CreatedByUserId, d.UpdatedByUserId }), cancellationToken);

        return drafts.Select(d => new DraftSummaryDto
        {
            Id = d.Id,
            Kind = d.Kind,
            Status = d.Status,
            IsPractice = d.IsPractice,
            NodeId = d.NodeId,
            ParentNodeId = d.ParentNodeId,
            NodeKind = d.NodeKind,
            Title = d.Title,
            Trail = (d.Kind == DraftKind.NewNode ? d.ParentNodeId : d.NodeId) is { } at && trails.TryGetValue(at, out var trail) ? trail : [],
            CreatedBy = PersonRefs.Of(names, d.CreatedByUserId),
            CreatedAtUtc = d.CreatedAtUtc,
            UpdatedBy = PersonRefs.Of(names, d.UpdatedByUserId),
            UpdatedAtUtc = d.UpdatedAtUtc,
            SubmittedAtUtc = d.SubmittedAtUtc,
            Revision = d.Revision,
            IsOutOfDate = outOfDate.Contains(d.Id),
            OpenComments = openComments.GetValueOrDefault(d.Id),
            ReleaseId = d.ReleaseId
        }).ToList();
    }

    internal static IReadOnlyList<Guid> ParseLanguages(string languages) =>
        languages.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();

    // =====================================================================================
    // Refusals
    // =====================================================================================

    internal static ServiceResult<T> DraftMissing<T>() =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftNotFound, ServiceErrorKind.NotFound, "Draft not found.");

    internal static ServiceResult<T> NodeMissing<T>() =>
        ServiceResult<T>.Failure(WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "That part of the curriculum was not found.");

    internal static ServiceResult<T> Closed<T>() =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftClosed, ServiceErrorKind.Conflict, "This draft was released or discarded.");

    internal static ServiceResult<T> OutOfDate<T>() =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftOutOfDate, ServiceErrorKind.Conflict,
            "What is live changed after this draft was started. Bring it up to date first.");

    internal static ServiceResult<T> WrongStatus<T>(Draft draft) =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftWrongStatus, ServiceErrorKind.Conflict,
            $"Not possible while the draft is {draft.Status}.",
            new Dictionary<string, object?> { ["status"] = draft.Status.ToString() });

    internal static ServiceResult<T> OutOfScope<T>(string reason, IReadOnlyList<Guid>? languages = null) =>
        ServiceResult<T>.Failure(WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "That is outside your scope.",
            new Dictionary<string, object?> { ["reason"] = reason, ["languages"] = languages ?? [] });

    private static ServiceResult OutOfScope(string reason, IReadOnlyList<Guid>? languages = null) =>
        ServiceResult.Failure(WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "That is outside your scope.",
            new Dictionary<string, object?> { ["reason"] = reason, ["languages"] = languages ?? [] });

    private static ServiceResult<T> Invalid<T>(string reason) =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation, $"The draft is missing or has a malformed '{reason}'.",
            new Dictionary<string, object?> { ["reason"] = reason });

    private static ServiceResult Invalid(string reason) =>
        ServiceResult.Failure(WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation, $"The draft is missing or has a malformed '{reason}'.",
            new Dictionary<string, object?> { ["reason"] = reason });

    internal static ServiceResult<T> Propagate<T>(ServiceResult source) =>
        new() { ErrorKind = source.ErrorKind, Error = source.Error, Errors = source.Errors, Details = source.Details };
}
