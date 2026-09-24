using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Curriculum;
using Share7.Domain.Progress;
using Share7.Domain.Recovery;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;

namespace Share7.Infrastructure.Workspace;

/// <summary>A release entry's target, captured before and after: what a rollback puts back and checks.</summary>
public sealed record EntryState(NodeStateDto? Node, LessonContentDto? Content, IReadOnlyList<Guid>? Children);

/// <inheritdoc cref="IReleaseService"/>
public sealed class ReleaseService : IReleaseService
{
    private readonly ApplicationDbContext _db;
    private readonly DraftKinds _kinds;
    private readonly DraftService _drafts;
    private readonly ILessonContentPublisher _publisher;
    private readonly ILessonContentReader _reader;
    private readonly ICurriculumStructureService _structure;
    private readonly IStudioMemberResolver _members;
    private readonly NodeTrails _trails;
    private readonly PeopleDirectory _people;
    private readonly StudioNotifier _notifier;
    private readonly UnlockRepairSignal _repairs;
    private readonly IAuditLog _audit;

    public ReleaseService(
        ApplicationDbContext db,
        DraftKinds kinds,
        DraftService drafts,
        ILessonContentPublisher publisher,
        ILessonContentReader reader,
        ICurriculumStructureService structure,
        IStudioMemberResolver members,
        NodeTrails trails,
        PeopleDirectory people,
        StudioNotifier notifier,
        UnlockRepairSignal repairs,
        IAuditLog audit)
    {
        _db = db;
        _kinds = kinds;
        _drafts = drafts;
        _publisher = publisher;
        _reader = reader;
        _structure = structure;
        _members = members;
        _trails = trails;
        _people = people;
        _notifier = notifier;
        _repairs = repairs;
        _audit = audit;
    }

    // =====================================================================================
    // Read
    // =====================================================================================

    public async Task<IReadOnlyList<ReleaseSummaryDto>> ListAsync(
        StudioMember member, ReleaseStatus? status, CancellationToken cancellationToken = default)
    {
        var releases = _db.Releases.AsNoTracking().AsQueryable();
        if (status is { } wanted) releases = releases.Where(r => r.Status == wanted);

        var rows = await releases.OrderByDescending(r => r.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
        return await SummariesAsync(rows, cancellationToken);
    }

    public async Task<ServiceResult<ReleaseDto>> GetAsync(StudioMember member, Guid releaseId, CancellationToken cancellationToken = default)
    {
        var release = await _db.Releases.AsNoTracking().Include(r => r.Entries).FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        return release is null
            ? ReleaseMissing()
            : ServiceResult<ReleaseDto>.Success(await ToDtoAsync(member, release, cancellationToken));
    }

    // =====================================================================================
    // Build
    // =====================================================================================

    public async Task<ServiceResult<ReleaseDto>> CreateAsync(
        StudioMember member, CreateReleaseRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<ReleaseDto>("role");

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is 0 or > 200)
            return Invalid("title");

        var release = new Release
        {
            Id = Guid.NewGuid(),
            Title = title,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()[..Math.Min(request.Notes.Trim().Length, 2000)],
            CreatedByUserId = member.UserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var added = await AddDraftsAsync(member, release, request.DraftIds ?? [], cancellationToken);
        if (!added.Succeeded)
            return DraftService.Propagate<ReleaseDto>(added);

        _db.Releases.Add(release);
        Record(AuditActions.ReleaseCreated, release, $"Started a release with {release.Entries.Count} draft(s).");
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<ReleaseDto>.Success(await ToDtoAsync(member, release, cancellationToken));
    }

    public async Task<ServiceResult<ReleaseDto>> UpdateDraftsAsync(
        StudioMember member, Guid releaseId, UpdateReleaseDraftsRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<ReleaseDto>("role");

        var release = await _db.Releases.Include(r => r.Entries).FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (release is null) return ReleaseMissing();

        if (release.Status is not (ReleaseStatus.Building or ReleaseStatus.Failed))
            return WrongStatus(release);

        foreach (var entry in release.Entries.Where(e => e.DraftId is { } id && (request.Remove ?? []).Contains(id)).ToList())
        {
            release.Entries.Remove(entry);
            _db.ReleaseEntries.Remove(entry);
        }

        var added = await AddDraftsAsync(member, release, request.Add ?? [], cancellationToken);
        if (!added.Succeeded)
            return DraftService.Propagate<ReleaseDto>(added);

        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReleaseDto>.Success(await ToDtoAsync(member, release, cancellationToken));
    }

    /// <summary>
    /// Puts drafts in a release. Only approved, real (not practice) drafts in the Lead's scope, and
    /// none already waiting in another release.
    /// </summary>
    private async Task<ServiceResult> AddDraftsAsync(
        StudioMember member, Release release, IReadOnlyList<Guid> draftIds, CancellationToken cancellationToken)
    {
        var wanted = draftIds.Distinct().Where(id => release.Entries.All(e => e.DraftId != id)).ToList();
        if (wanted.Count == 0) return ServiceResult.Success();

        var drafts = await _db.Drafts.AsNoTracking().Where(d => wanted.Contains(d.Id)).ToListAsync(cancellationToken);
        if (drafts.Count != wanted.Count)
            return DraftService.DraftMissing<ReleaseDto>();

        foreach (var draft in drafts)
        {
            if (draft.IsPractice)
                return ServiceResult.Failure(WorkspaceErrors.PracticeNotReleasable, ServiceErrorKind.Validation,
                    "Practice drafts cannot be released.", new Dictionary<string, object?> { ["draftId"] = draft.Id });

            if (!draft.IsOpen)
                return DraftService.Closed<ReleaseDto>();

            if (draft.Status != DraftStatus.Approved)
                return DraftService.WrongStatus<ReleaseDto>(draft);

            if (!member.CoversPath(draft.ScopePath) || member.LanguagesOutside(DraftService.ParseLanguages(draft.LanguagesTouched)).Count > 0)
                return DraftService.OutOfScope<ReleaseDto>("node");
        }

        var busy = await (from e in _db.ReleaseEntries
                          join r in _db.Releases on e.ReleaseId equals r.Id
                          where e.DraftId != null && wanted.Contains(e.DraftId.Value) && r.Id != release.Id
                                && (r.Status == ReleaseStatus.Building || r.Status == ReleaseStatus.Scheduled
                                    || r.Status == ReleaseStatus.Publishing || r.Status == ReleaseStatus.Failed)
                          select e.DraftId).ToListAsync(cancellationToken);

        if (busy.Count > 0)
            return ServiceResult.Failure(WorkspaceErrors.ReleaseNotReady, ServiceErrorKind.Conflict,
                "A draft is already waiting in another release.",
                new Dictionary<string, object?> { ["drafts"] = busy.Select(id => new { draftId = id, reason = "inAnotherRelease" }) });

        foreach (var draft in drafts)
        {
            release.Entries.Add(new ReleaseEntry
            {
                Id = Guid.NewGuid(),
                ReleaseId = release.Id,
                DraftId = draft.Id,
                Kind = draft.Kind,
                NodeId = draft.NodeId!.Value
            });
        }

        return ServiceResult.Success();
    }

    // =====================================================================================
    // Schedule, cancel
    // =====================================================================================

    public async Task<ServiceResult<ReleaseDto>> ScheduleAsync(
        StudioMember member, Guid releaseId, ScheduleReleaseRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<ReleaseDto>("role");

        var release = await _db.Releases.Include(r => r.Entries).FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (release is null) return ReleaseMissing();

        if (release.Status is not (ReleaseStatus.Building or ReleaseStatus.Scheduled or ReleaseStatus.Failed))
            return WrongStatus(release);

        var at = DateTime.SpecifyKind(request.PublishAtUtc, DateTimeKind.Utc);
        if (at <= DateTime.UtcNow.AddMinutes(1))
            return Invalid("publishAtUtc");

        // Checked now, so the Lead hears about a problem while they can fix it, and again when it
        // goes out, because a lot can happen before the start of term.
        var blockers = await BlockersAsync(member, release, cancellationToken);
        if (blockers.Count > 0)
            return NotReady(blockers);

        release.Status = ReleaseStatus.Scheduled;
        release.ScheduledForUtc = at;
        release.ScheduledByUserId = member.UserId;
        release.FailureMessage = null;
        release.FailureDetailsJson = null;
        Record(AuditActions.ReleaseScheduled, release, $"Scheduled a release for {at:u}.");

        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReleaseDto>.Success(await ToDtoAsync(member, release, cancellationToken));
    }

    public async Task<ServiceResult<ReleaseDto>> CancelAsync(StudioMember member, Guid releaseId, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<ReleaseDto>("role");

        var release = await _db.Releases.Include(r => r.Entries).FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (release is null) return ReleaseMissing();

        if (release.Status is not (ReleaseStatus.Building or ReleaseStatus.Scheduled or ReleaseStatus.Failed))
            return WrongStatus(release);

        release.Status = ReleaseStatus.Cancelled;
        Record(AuditActions.ReleaseCancelled, release, "Cancelled a release. Its drafts are free for another.");

        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReleaseDto>.Success(await ToDtoAsync(member, release, cancellationToken));
    }

    // =====================================================================================
    // Publish
    // =====================================================================================

    public async Task<ServiceResult<ReleaseDto>> PublishAsync(StudioMember member, Guid releaseId, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<ReleaseDto>("role");

        var result = await ApplyAsync(member, releaseId, rollback: null, cancellationToken);
        if (!result.Succeeded)
            return DraftService.Propagate<ReleaseDto>(result);

        return await GetAsync(member, releaseId, cancellationToken);
    }

    public async Task<int> PublishDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var due = await _db.Releases.AsNoTracking()
            .Where(r => r.Status == ReleaseStatus.Scheduled && r.ScheduledForUtc <= now)
            .OrderBy(r => r.ScheduledForUtc)
            .Select(r => new { r.Id, r.ScheduledByUserId })
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var release in due)
        {
            // The Lead who scheduled it is who publishes it — with their scope as it is now, not as
            // it was when they scheduled. Somebody who has since lost the right cannot publish by clock.
            var member = release.ScheduledByUserId is { } leadId ? await _members.ResolveAsync(leadId, cancellationToken) : null;

            if (member is null || !member.IsAtLeast(StudioRole.Lead))
            {
                await FailAsync(release.Id, "The Lead who scheduled this release can no longer publish it.", null, cancellationToken);
                continue;
            }

            var result = await ApplyAsync(member, release.Id, rollback: null, cancellationToken);
            if (result.Succeeded) published++;

            _db.ChangeTracker.Clear();
        }

        return published;
    }

    /// <summary>
    /// Publishes a release — or, with <paramref name="rollback"/>, the rollback it describes — as one
    /// transaction. On any refusal nothing it touched changes, and the release is marked failed with
    /// the reason.
    /// </summary>
    private async Task<ServiceResult> ApplyAsync(
        StudioMember member, Guid releaseId, Release? rollback, CancellationToken cancellationToken)
    {
        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            // One release at a time, platform-wide. Every draft is checked against live state up front
            // and then applied without re-checking between steps — which is only sound while no other
            // release can move live state in between.
            await _db.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = 'share7.release', @LockMode = 'Exclusive',
                                             @LockOwner = 'Transaction', @LockTimeout = 30000;
                IF @result < 0 THROW 51002, 'Another release is being published; try again in a moment.', 1;
                """, cancellationToken);

            var release = rollback ?? await _db.Releases.Include(r => r.Entries).FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
            if (release is null)
                return ReleaseMissing();

            if (rollback is null && release.Status is not (ReleaseStatus.Building or ReleaseStatus.Scheduled or ReleaseStatus.Failed))
                return WrongStatus(release);

            if (rollback is null && release.Entries.Count == 0)
                return Invalid("drafts");

            IReadOnlyList<ReleaseBlockerDto> blockers = rollback is null ? await BlockersAsync(member, release, cancellationToken) : [];
            if (blockers.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                await FailAsync(releaseId, "Some drafts in this release cannot go out as they are.", blockers, cancellationToken);
                return NotReady(blockers);
            }

            var now = DateTime.UtcNow;

            // What the release will reach, counted before it changes anything.
            var impact = rollback is null ? await ImpactAsync(release, cancellationToken) : new ReleaseImpactDto([]);

            try
            {
                if (rollback is null)
                    await ApplyDraftsAsync(member, release, now, cancellationToken);
                else
                    await ApplyRollbackAsync(member, release, cancellationToken);
            }
            catch (ReleaseStepRefused refused)
            {
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();

                if (rollback is null)
                    await FailAsync(releaseId, refused.Message, refused.Details, cancellationToken);

                return ServiceResult.Failure(refused.Code, ServiceErrorKind.Conflict, refused.Message,
                    new Dictionary<string, object?> { ["draftId"] = refused.DraftId, ["details"] = refused.Details });
            }

            // Every entry's "after" is taken once the whole release is in: the state a rollback
            // compares against is the one the release left, not a step half way through it.
            foreach (var entry in release.Entries)
                entry.AfterJson = WorkspaceJson.Write(await StateAsync(entry.Kind, entry.NodeId, cancellationToken));

            release.Status = ReleaseStatus.Published;
            release.PublishedAtUtc = now;
            release.PublishedByUserId = member.UserId;
            release.FailureMessage = null;
            release.FailureDetailsJson = null;
            release.ImpactJson = WorkspaceJson.Write(impact);

            Record(rollback is null ? AuditActions.ReleasePublished : AuditActions.ReleaseRolledBack, release,
                rollback is null
                    ? $"Published a release: {release.Entries.Count} change(s)."
                    : $"Rolled back a release: {release.Entries.Count} change(s) put back.");

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Retirements and new orders queue unlock repairs in the transaction; they run now it is in.
        _repairs.Nudge();
        return ServiceResult.Success();
    }

    /// <summary>The apply order: restores, new nodes (parents first), renames, moves, reorders, questions, retirements.</summary>
    private static int Stage(DraftKind kind) => kind switch
    {
        DraftKind.Restore => 0,
        DraftKind.NewNode => 1,
        DraftKind.Rename => 2,
        DraftKind.Move => 3,
        DraftKind.Reorder => 4,
        DraftKind.LessonContent => 5,
        _ => 6
    };

    private async Task ApplyDraftsAsync(StudioMember member, Release release, DateTime now, CancellationToken cancellationToken)
    {
        var draftIds = release.Entries.Select(e => e.DraftId!.Value).ToList();
        var drafts = await _db.Drafts.Where(d => draftIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var covers = await _kinds.CoversAsync(cancellationToken);
        var actor = new EngineActor(member.UserId, release.Id);

        var ordered = release.Entries
            .OrderBy(e => Stage(e.Kind))
            .ThenBy(e => drafts[e.DraftId!.Value].ScopePath.Length)
            .ThenBy(e => drafts[e.DraftId!.Value].CreatedAtUtc)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];
            var draft = drafts[entry.DraftId!.Value];

            entry.Sequence = i + 1;
            entry.BeforeJson = WorkspaceJson.Write(await StateAsync(draft.Kind, entry.NodeId, cancellationToken));

            var outcome = await ApplyDraftAsync(draft, covers, actor, cancellationToken);
            entry.OutcomeJson = outcome;

            draft.Status = DraftStatus.Released;
            draft.IsOpen = false;
            draft.ReleaseId = release.Id;
            draft.ReleasedAtUtc = now;
        }

        var people = await _db.DraftContributors.AsNoTracking().Where(c => draftIds.Contains(c.DraftId)).Select(c => c.UserId).ToListAsync(cancellationToken);
        _notifier.Notify(people.Concat(drafts.Values.Select(d => d.CreatedByUserId)), "draft.released", member.UserId, releaseId: release.Id);
    }

    /// <summary>One draft, through the engine. Nothing is re-checked here: the release checked every draft up front, under the release lock.</summary>
    private async Task<string?> ApplyDraftAsync(Draft draft, IReadOnlyList<ContentSetKey> covers, EngineActor actor, CancellationToken cancellationToken)
    {
        switch (draft.Kind)
        {
            case DraftKind.LessonContent:
            {
                var proposal = WorkspaceJson.Read<LessonContentProposal>(draft.ProposedJson)!;
                var baseVersions = DraftKinds.ExpectedVersions(draft.BaseFingerprint);

                var published = await _publisher.PublishAsync(new ContentPublishRequest
                {
                    LessonId = draft.NodeId!.Value,
                    Items = proposal.Items,
                    Covers = covers,
                    Rules = ContentRuleSet.Studio,
                    Source = QuestionSetSource.Release,
                    ActorUserId = actor.UserId,
                    ReleaseId = actor.ReleaseId,

                    // Every covered set, including ones that did not exist when the draft started:
                    // a set published since by an old admin path is a change underneath the draft too.
                    ExpectedVersions = covers.ToDictionary(c => c, c => baseVersions.GetValueOrDefault(c)),
                    AuditPath = "studio-release"
                }, cancellationToken);

                return Outcome(draft, published);
            }

            case DraftKind.NewNode:
            {
                var proposal = WorkspaceJson.Read<NewNodeProposal>(draft.ProposedJson)!;

                var created = await _structure.CreateAsync(new CreateNodeCommand
                {
                    NodeId = draft.NodeId,
                    ParentId = draft.ParentNodeId!.Value,
                    Kind = draft.NodeKind!,
                    Titles = proposal.Titles,
                    Position = proposal.Position,
                    ShiftSiblings = true
                }, actor, cancellationToken);

                Outcome(draft, created);

                if (draft.NodeKind == NodeKinds.Lesson && proposal.Items is { Count: > 0 } items)
                {
                    var published = await _publisher.PublishAsync(new ContentPublishRequest
                    {
                        LessonId = draft.NodeId!.Value,
                        Items = items,
                        Covers = covers,
                        Rules = ContentRuleSet.Studio,
                        Source = QuestionSetSource.Release,
                        ActorUserId = actor.UserId,
                        ReleaseId = actor.ReleaseId,
                        AuditPath = "studio-release"
                    }, cancellationToken);

                    return Outcome(draft, published);
                }

                return WorkspaceJson.Write(new { created = created.Value!.Node.Id });
            }

            case DraftKind.Rename:
                return Outcome(draft, await _structure.RenameAsync(
                    draft.NodeId!.Value, WorkspaceJson.Read<RenameProposal>(draft.ProposedJson)!.Titles, null, actor, cancellationToken));

            case DraftKind.Move:
            {
                var move = WorkspaceJson.Read<MoveProposal>(draft.ProposedJson)!;
                return Outcome(draft, await _structure.MoveAsync(draft.NodeId!.Value, move.NewParentId, move.Position, null, actor, cancellationToken));
            }

            case DraftKind.Reorder:
                return Outcome(draft, await _structure.ReorderAsync(
                    draft.NodeId!.Value, WorkspaceJson.Read<ReorderProposal>(draft.ProposedJson)!.OrderedChildIds, null, actor, cancellationToken));

            case DraftKind.Retire:
                return Outcome(draft, await _structure.RetireAsync(draft.NodeId!.Value, null, actor, cancellationToken));

            case DraftKind.Restore:
                return Outcome(draft, await _structure.RestoreAsync(draft.NodeId!.Value, actor, cancellationToken));

            case DraftKind.RecoveryRule:
                return await ApplyRecoveryRuleAsync(draft, actor, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Writes a recovery rule at a node. Rows are superseded, never updated and never deleted: a
    /// child's sitting last week was governed by one of these, and "why did it do that then" has to
    /// stay answerable after the rule has moved on.
    /// </summary>
    private async Task<string?> ApplyRecoveryRuleAsync(Draft draft, EngineActor actor, CancellationToken cancellationToken)
    {
        var nodeId = draft.NodeId!.Value;
        var proposal = WorkspaceJson.Read<RecoveryRuleProposal>(draft.ProposedJson)!;
        var now = DateTime.UtcNow;

        var node = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new { n.Path, n.KindKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (node is null)
            throw new ReleaseStepRefused(draft.Id, WorkspaceErrors.ReleaseNotReady, "The place this rule is written at is gone.", null);

        var standing = await _db.RecoveryRules
            .Where(x => x.NodeId == nodeId && x.IsActive && x.TargetId == null)
            .ToListAsync(cancellationToken);

        foreach (var old in standing)
        {
            old.IsActive = false;
            old.SupersededAtUtc = now;
        }

        if (proposal.Clear)
            return WorkspaceJson.Write(new { cleared = standing.Count });

        var rule = new RecoveryRule
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            ScopePath = node.Path,
            NodeKind = node.KindKey,
            AfterWrongAnswers = proposal.AfterWrongAnswers,
            QuestionsToServe = proposal.QuestionsToServe,
            AllowRepeats = proposal.AllowRepeats,
            TargetId = null,
            IsActive = true,
            ReleaseId = actor.ReleaseId,
            WrittenByUserId = actor.UserId,
            CreatedAtUtc = now
        };

        _db.RecoveryRules.Add(rule);

        return WorkspaceJson.Write(new
        {
            ruleId = rule.Id,
            afterWrongAnswers = rule.AfterWrongAnswers,
            questionsToServe = rule.QuestionsToServe,
            allowRepeats = rule.AllowRepeats,
            replaced = standing.Count
        });
    }

    private static string Outcome<T>(Draft draft, ServiceResult<T> result)
    {
        if (!result.Succeeded)
        {
            throw new ReleaseStepRefused(
                draft.Id,
                result.Error ?? WorkspaceErrors.ReleaseNotReady,
                result.Errors.FirstOrDefault() ?? "The change was refused.",
                result.Details);
        }

        return result.Value is ContentPublishOutcome content
            ? WorkspaceJson.Write(new { sets = content.Sets, content.NewRows, content.KeptRows, content.RetiredRows })
            : WorkspaceJson.Write(new { applied = true });
    }

    /// <summary>A step of a release the engine refused; the whole release is rolled back.</summary>
    private sealed class ReleaseStepRefused : Exception
    {
        public ReleaseStepRefused(Guid? draftId, ApiErrorCode code, string message, object? details) : base(message)
        {
            DraftId = draftId;
            Code = code;
            Details = details;
        }

        public Guid? DraftId { get; }
        public ApiErrorCode Code { get; }
        public object? Details { get; }
    }

    private async Task FailAsync(Guid releaseId, string message, object? details, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var release = await _db.Releases.FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (release is null) return;

        release.Status = ReleaseStatus.Failed;
        release.FailureMessage = message.Length > 2000 ? message[..2000] : message;
        release.FailureDetailsJson = details is null ? null : WorkspaceJson.Write(details);
        Record(AuditActions.ReleaseFailed, release, "A release was refused; nothing it touched changed.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    // =====================================================================================
    // Rollback
    // =====================================================================================

    public async Task<ServiceResult<ReleaseDto>> RollbackAsync(
        StudioMember member, Guid releaseId, RollbackReleaseRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<ReleaseDto>("role");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 2000)
            return Invalid("reason");

        var target = await _db.Releases.Include(r => r.Entries).FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (target is null) return ReleaseMissing();

        if (target.Status != ReleaseStatus.Published || target.RolledBackByReleaseId is not null)
            return WrongStatus(target);

        // Everything the release changed must be exactly as it left it. Anything changed again since
        // would be undone along with it — so the later change has to be rolled back first.
        var conflicts = new List<object>();
        foreach (var entry in target.Entries)
        {
            var now = WorkspaceJson.Write(await StateAsync(entry.Kind, entry.NodeId, cancellationToken));
            if (Fingerprint(entry.Kind, now) != Fingerprint(entry.Kind, entry.AfterJson))
                conflicts.Add(new { nodeId = entry.NodeId, kind = entry.Kind.ToString() });

            var scopePath = await _db.CurriculumNodes.AsNoTracking().Where(n => n.Id == entry.NodeId).Select(n => n.Path).FirstOrDefaultAsync(cancellationToken);
            if (scopePath is not null && !member.CoversPath(scopePath))
                return DraftService.OutOfScope<ReleaseDto>("node");
        }

        if (conflicts.Count > 0)
        {
            return ServiceResult<ReleaseDto>.Failure(WorkspaceErrors.RollbackBlocked, ServiceErrorKind.Conflict,
                "Some of what this release changed has been changed again since.",
                new Dictionary<string, object?> { ["conflicts"] = conflicts });
        }

        var rollback = new Release
        {
            Id = Guid.NewGuid(),
            Title = $"Rollback: {target.Title}",
            Reason = reason,
            RollbackOfReleaseId = target.Id,
            Status = ReleaseStatus.Publishing,
            CreatedByUserId = member.UserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Undone in reverse, so each change is taken back out of the state it was put into.
        var sequence = 1;
        foreach (var entry in target.Entries.OrderByDescending(e => e.Sequence))
        {
            rollback.Entries.Add(new ReleaseEntry
            {
                Id = Guid.NewGuid(),
                ReleaseId = rollback.Id,
                Kind = entry.Kind,
                NodeId = entry.NodeId,
                Sequence = sequence++,
                BeforeJson = entry.AfterJson,

                // What to put back travels on the entry itself.
                OutcomeJson = entry.BeforeJson
            });
        }

        _db.Releases.Add(rollback);
        target.RolledBackByReleaseId = rollback.Id;

        var applied = await ApplyAsync(member, rollback.Id, rollback, cancellationToken);
        if (!applied.Succeeded)
            return DraftService.Propagate<ReleaseDto>(applied);

        return await GetAsync(member, rollback.Id, cancellationToken);
    }

    private async Task ApplyRollbackAsync(StudioMember member, Release rollback, CancellationToken cancellationToken)
    {
        var actor = new EngineActor(member.UserId, rollback.Id);

        foreach (var entry in rollback.Entries.OrderBy(e => e.Sequence))
        {
            var putBack = WorkspaceJson.Read<EntryState>(entry.OutcomeJson ?? "{}");
            var leftBy = WorkspaceJson.Read<EntryState>(entry.BeforeJson ?? "{}");

            entry.OutcomeJson = entry.Kind switch
            {
                DraftKind.LessonContent => await RestoreContentAsync(entry, putBack?.Content, leftBy?.Content, actor, cancellationToken),
                DraftKind.NewNode => Undone(await _structure.RetireAsync(entry.NodeId, null, actor, cancellationToken)),
                DraftKind.Rename => Undone(await _structure.RenameAsync(entry.NodeId, putBack!.Node!.Titles, null, actor, cancellationToken)),
                DraftKind.Move => Undone(await _structure.MoveAsync(entry.NodeId, putBack!.Node!.ParentId!.Value, putBack.Node.Order, null, actor, cancellationToken)),
                DraftKind.Reorder => Undone(await _structure.ReorderAsync(entry.NodeId, putBack!.Children!, null, actor, cancellationToken)),
                DraftKind.Retire => Undone(await _structure.RestoreAsync(entry.NodeId, actor, cancellationToken)),
                DraftKind.Restore => Undone(await _structure.RetireAsync(entry.NodeId, null, actor, cancellationToken)),
                _ => null
            };
        }
    }

    /// <summary>
    /// Puts a lesson's questions back as they were before the release: every item, in every pool and
    /// language either state had, under the permissive restore rules.
    /// </summary>
    private async Task<string> RestoreContentAsync(
        ReleaseEntry entry, LessonContentDto? before, LessonContentDto? after, EngineActor actor, CancellationToken cancellationToken)
    {
        before ??= new LessonContentDto { LessonId = entry.NodeId, Sets = [], Items = [] };
        after ??= await _reader.ReadAsync(entry.NodeId, cancellationToken) ?? before;

        var covers = before.Sets.Select(s => new ContentSetKey(s.Role, s.LangId))
            .Union(after.Sets.Select(s => new ContentSetKey(s.Role, s.LangId)))
            .ToList();

        if (covers.Count == 0)
            return WorkspaceJson.Write(new { applied = true });

        var published = await _publisher.PublishAsync(new ContentPublishRequest
        {
            LessonId = entry.NodeId,
            Items = before.Items.Select(DraftKinds.ToDraftItem).ToList(),
            Covers = covers,
            Rules = ContentRuleSet.Restore,
            Source = QuestionSetSource.Release,
            ActorUserId = actor.UserId,
            ReleaseId = actor.ReleaseId,
            ExpectedVersions = covers.ToDictionary(c => c, c => after.VersionOf(c.Role, c.LangId)),
            AuditPath = "studio-rollback"
        }, cancellationToken);

        return Undone(published);
    }

    private static string Undone<T>(ServiceResult<T> result)
    {
        if (!result.Succeeded)
            throw new ReleaseStepRefused(null, result.Error ?? WorkspaceErrors.RollbackBlocked, result.Errors.FirstOrDefault() ?? "Refused.", result.Details);

        return result.Value is ContentPublishOutcome content
            ? WorkspaceJson.Write(new { sets = content.Sets, content.NewRows, content.KeptRows, content.RetiredRows })
            : WorkspaceJson.Write(new { applied = true });
    }

    // =====================================================================================
    // Target state, fingerprints
    // =====================================================================================

    private async Task<EntryState> StateAsync(DraftKind kind, Guid nodeId, CancellationToken cancellationToken)
    {
        if (kind == DraftKind.LessonContent)
            return new EntryState(null, await _reader.ReadAsync(nodeId, cancellationToken), null);

        var node = await _structure.GetAsync(nodeId, cancellationToken);
        var children = kind == DraftKind.Reorder ? await _kinds.LiveChildrenAsync(nodeId, cancellationToken) : null;
        return new EntryState(node, null, children);
    }

    /// <summary>What "has not moved since" is compared on, for a rollback.</summary>
    private static string Fingerprint(DraftKind kind, string? stateJson)
    {
        var state = stateJson is null ? null : WorkspaceJson.Read<EntryState>(stateJson);
        if (state is null) return "none";

        return kind == DraftKind.LessonContent
            ? DraftKinds.ContentFingerprint((state.Content?.Sets ?? []).Select(s => (s.Role, s.LangId, s.Version)))
            : $"{state.Node?.Revision}:{state.Node?.RetiredAtUtc?.Ticks}:{string.Join(",", state.Children ?? [])}";
    }

    // =====================================================================================
    // Blockers and impact
    // =====================================================================================

    private async Task<IReadOnlyList<ReleaseBlockerDto>> BlockersAsync(StudioMember member, Release release, CancellationToken cancellationToken)
    {
        var draftIds = release.Entries.Where(e => e.DraftId is not null).Select(e => e.DraftId!.Value).ToList();
        var drafts = await _db.Drafts.AsNoTracking().Where(d => draftIds.Contains(d.Id)).ToListAsync(cancellationToken);
        var outOfDate = await _kinds.OutOfDateAsync(drafts, cancellationToken);
        var blockers = new List<ReleaseBlockerDto>();

        foreach (var draft in drafts)
        {
            if (!draft.IsOpen) blockers.Add(new(draft.Id, "closed"));
            else if (draft.IsPractice) blockers.Add(new(draft.Id, "practice"));
            else if (draft.Status != DraftStatus.Approved) blockers.Add(new(draft.Id, "notApproved"));
            else if (outOfDate.Contains(draft.Id)) blockers.Add(new(draft.Id, "outOfDate"));
            else if (!member.CoversPath(draft.ScopePath) || member.LanguagesOutside(DraftService.ParseLanguages(draft.LanguagesTouched)).Count > 0)
                blockers.Add(new(draft.Id, "outOfScope"));
            else if ((await _kinds.CheckAsync(draft, cancellationToken)).Count > 0)
                blockers.Add(new(draft.Id, "hasProblems"));
        }

        // A new node whose parent is itself new must go out with, or after, the draft that makes it.
        foreach (var draft in drafts.Where(d => d.Kind == DraftKind.NewNode && d.ParentNodeId is not null))
        {
            var parentLive = await _db.CurriculumNodes.AnyAsync(n => n.Id == draft.ParentNodeId && n.RetiredAtUtc == null, cancellationToken);
            var parentHere = drafts.Any(d => d.Kind == DraftKind.NewNode && d.NodeId == draft.ParentNodeId);

            if (!parentLive && !parentHere)
            {
                var parentDraft = await _db.Drafts.AsNoTracking()
                    .Where(d => d.Kind == DraftKind.NewNode && d.NodeId == draft.ParentNodeId && d.IsOpen)
                    .Select(d => (Guid?)d.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                blockers.Add(new(draft.Id, "needsDraft", parentDraft));
            }
        }

        // Two changes to one thing in one release, applied back to back, would trip over each other.
        // A new order for a parent's children also cannot share a release with anything that adds to,
        // takes from or reshuffles those children.
        var structural = drafts.Where(d => d.Kind is DraftKind.Rename or DraftKind.Move or DraftKind.Retire or DraftKind.Restore or DraftKind.Reorder).ToList();
        foreach (var clash in structural.GroupBy(d => d.NodeId).Where(g => g.Count() > 1))
            foreach (var draft in clash.Skip(1))
                blockers.Add(new(draft.Id, "sameTarget", clash.First().Id));

        foreach (var reorder in drafts.Where(d => d.Kind == DraftKind.Reorder))
        {
            var parentId = reorder.NodeId!.Value;

            foreach (var other in drafts.Where(d => d.Id != reorder.Id))
            {
                var touchesChildren = other.Kind switch
                {
                    DraftKind.NewNode => other.ParentNodeId == parentId,
                    DraftKind.Move => other.ParentNodeId == parentId
                                      || WorkspaceJson.Read<MoveProposal>(other.ProposedJson)?.NewParentId == parentId,
                    DraftKind.Retire or DraftKind.Restore => other.ParentNodeId == parentId,
                    _ => false
                };

                if (touchesChildren)
                    blockers.Add(new(other.Id, "sameTarget", reorder.Id));
            }
        }

        return blockers;
    }

    private async Task<ReleaseImpactDto> ImpactAsync(Release release, CancellationToken cancellationToken)
    {
        var draftIds = release.Entries.Where(e => e.DraftId is not null).Select(e => e.DraftId!.Value).ToList();
        var drafts = await _db.Drafts.AsNoTracking().Where(d => draftIds.Contains(d.Id)).ToListAsync(cancellationToken);
        var lines = new List<(string Kind, Guid NodeId, int Students)>();

        foreach (var draft in drafts)
        {
            var nodeId = draft.NodeId!.Value;

            switch (draft.Kind)
            {
                case DraftKind.LessonContent:
                    lines.Add(("questionsChanged", nodeId,
                        await _db.UserLessonProgress.Where(p => p.LessonId == nodeId).Select(p => p.UserId).Distinct().CountAsync(cancellationToken)));
                    break;

                case DraftKind.Retire:
                case DraftKind.Move:
                {
                    var type = await NodeTypeAsync(nodeId, cancellationToken);
                    lines.Add((draft.Kind == DraftKind.Retire ? "retired" : "moved", nodeId,
                        await _db.UserNodeUnlocks.Where(u => u.NodeType == type && u.NodeId == nodeId).Select(u => u.UserId).Distinct().CountAsync(cancellationToken)));
                    break;
                }

                case DraftKind.Reorder:
                {
                    // Part way through: holding some of the children, not all of them.
                    var children = await _kinds.LiveChildrenAsync(nodeId, cancellationToken);
                    if (children.Count == 0) break;

                    var partWay = await _db.UserNodeUnlocks
                        .Where(u => children.Contains(u.NodeId))
                        .GroupBy(u => new { u.UserId, u.GameId })
                        .Where(g => g.Count() < children.Count)
                        .Select(g => g.Key.UserId)
                        .Distinct()
                        .CountAsync(cancellationToken);

                    lines.Add(("orderChanged", nodeId, partWay));
                    break;
                }

                case DraftKind.RecoveryRule:
                {
                    // Everybody playing anything under this node, not only this lesson: a rule on a
                    // subject reaches every child in every lesson beneath it, and a release that
                    // understated that would be the one nobody checked.
                    var path = await _db.CurriculumNodes.AsNoTracking()
                        .Where(n => n.Id == nodeId).Select(n => n.Path).FirstOrDefaultAsync(cancellationToken);

                    if (path is null) break;

                    var under = await _db.CurriculumNodes.AsNoTracking()
                        .Where(n => n.Id == nodeId || n.Path.StartsWith(path + "/"))
                        .Where(n => n.IsPlayable)
                        .Select(n => n.Id)
                        .ToListAsync(cancellationToken);

                    lines.Add(("recoveryChanged", nodeId, under.Count == 0
                        ? 0
                        : await _db.UserLessonProgress
                            .Where(p => under.Contains(p.LessonId))
                            .Select(p => p.UserId).Distinct().CountAsync(cancellationToken)));
                    break;
                }
            }
        }

        var trails = await _trails.ForAsync(lines.Select(l => l.NodeId), cancellationToken);
        return new ReleaseImpactDto(lines
            .Select(l => new ImpactLineDto(l.Kind, l.NodeId, trails.TryGetValue(l.NodeId, out var t) ? t : [], l.Students))
            .ToList());
    }

    private async Task<CurriculumNodeType> NodeTypeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var kind = await _db.CurriculumNodes.AsNoTracking().Where(n => n.Id == nodeId).Select(n => n.KindKey).FirstOrDefaultAsync(cancellationToken);
        return kind switch
        {
            NodeKinds.Term => CurriculumNodeType.Term,
            NodeKinds.Subject => CurriculumNodeType.Subject,
            NodeKinds.Chapter => CurriculumNodeType.Chapter,
            _ => CurriculumNodeType.Lesson
        };
    }

    // =====================================================================================
    // Shaping for the wire
    // =====================================================================================

    private async Task<ReleaseDto> ToDtoAsync(StudioMember member, Release release, CancellationToken cancellationToken)
    {
        var summary = (await SummariesAsync([release], cancellationToken))[0];
        var draftIds = release.Entries.Where(e => e.DraftId is not null).Select(e => e.DraftId!.Value).ToList();
        var drafts = await _db.Drafts.AsNoTracking().Where(d => draftIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var trails = await _trails.ForAsync(release.Entries.Select(e => e.NodeId).Concat(drafts.Values.Select(d => d.ParentNodeId).OfType<Guid>()), cancellationToken);

        var items = release.Entries
            .OrderBy(e => e.Sequence == 0 ? int.MaxValue : e.Sequence)
            .ThenBy(e => Stage(e.Kind))
            .Select(e =>
            {
                var draft = e.DraftId is { } id && drafts.TryGetValue(id, out var d) ? d : null;
                var trailAt = draft?.Kind == DraftKind.NewNode && release.Status != ReleaseStatus.Published ? draft.ParentNodeId ?? e.NodeId : e.NodeId;

                return new ReleaseItemDto(
                    e.DraftId,
                    e.Kind,
                    e.NodeId,
                    draft?.Title ?? e.Kind.ToString(),
                    trails.TryGetValue(trailAt, out var trail) ? trail : [],
                    draft is null ? [] : DraftKinds.Diff(draft),
                    e.OutcomeJson is null ? null : WorkspaceJson.Element(e.OutcomeJson));
            })
            .ToList();

        var open = release.Status is ReleaseStatus.Building or ReleaseStatus.Scheduled or ReleaseStatus.Failed;

        return new ReleaseDto
        {
            Summary = summary,
            Items = items,
            Blockers = open ? await BlockersAsync(member, release, cancellationToken) : [],
            Impact = release.ImpactJson is { } stored
                ? WorkspaceJson.Read<ReleaseImpactDto>(stored) ?? new([])
                : open ? await ImpactAsync(release, cancellationToken) : new([])
        };
    }

    private async Task<IReadOnlyList<ReleaseSummaryDto>> SummariesAsync(IReadOnlyList<Release> releases, CancellationToken cancellationToken)
    {
        var names = await _people.NamesAsync(releases.SelectMany(r => new[] { (Guid?)r.CreatedByUserId, r.PublishedByUserId }), cancellationToken);
        var ids = releases.Select(r => r.Id).ToList();

        var counts = await _db.ReleaseEntries.AsNoTracking()
            .Where(e => ids.Contains(e.ReleaseId))
            .GroupBy(e => e.ReleaseId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return releases.Select(r => new ReleaseSummaryDto
        {
            Id = r.Id,
            Title = r.Title,
            Notes = r.Notes,
            Status = r.Status,
            DraftCount = counts.GetValueOrDefault(r.Id, r.Entries.Count),
            CreatedBy = PersonRefs.Of(names, r.CreatedByUserId),
            CreatedAtUtc = r.CreatedAtUtc,
            ScheduledForUtc = r.ScheduledForUtc,
            PublishedAtUtc = r.PublishedAtUtc,
            PublishedBy = PeopleDirectory.Ref(names, r.PublishedByUserId),
            RollbackOfReleaseId = r.RollbackOfReleaseId,
            RolledBackByReleaseId = r.RolledBackByReleaseId,
            Reason = r.Reason,
            FailureMessage = r.FailureMessage
        }).ToList();
    }

    private void Record(string action, Release release, string summary) =>
        _audit.Record(new AuditEntry(
            action,
            AuditAreas.Workspace,
            summary,
            "release",
            release.Id.ToString(),
            new
            {
                status = release.Status.ToString(),
                entries = release.Entries.Select(e => new { e.DraftId, kind = e.Kind.ToString(), e.NodeId, e.Sequence }),
                rollbackOf = release.RollbackOfReleaseId,
                scheduledFor = release.ScheduledForUtc
            }));

    private static ServiceResult<ReleaseDto> ReleaseMissing() =>
        ServiceResult<ReleaseDto>.Failure(WorkspaceErrors.ReleaseNotFound, ServiceErrorKind.NotFound, "Release not found.");

    private static ServiceResult<ReleaseDto> WrongStatus(Release release) =>
        ServiceResult<ReleaseDto>.Failure(WorkspaceErrors.ReleaseWrongStatus, ServiceErrorKind.Conflict,
            $"Not possible while the release is {release.Status}.", new Dictionary<string, object?> { ["status"] = release.Status.ToString() });

    private static ServiceResult<ReleaseDto> Invalid(string reason) =>
        ServiceResult<ReleaseDto>.Failure(WorkspaceErrors.ReleaseInvalid, ServiceErrorKind.Validation,
            $"The release's '{reason}' is missing or not allowed.", new Dictionary<string, object?> { ["reason"] = reason });

    private static ServiceResult<ReleaseDto> NotReady(IReadOnlyList<ReleaseBlockerDto> blockers) =>
        ServiceResult<ReleaseDto>.Failure(WorkspaceErrors.ReleaseNotReady, ServiceErrorKind.Conflict,
            "Some drafts in this release cannot go out as they are.", new Dictionary<string, object?> { ["drafts"] = blockers });
}
