using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

/// <inheritdoc cref="IStudioInboxService"/>
public sealed class StudioInboxService : IStudioInboxService
{
    /// <summary>What the team feed shows: the curriculum, its questions, and the workspace.</summary>
    private static readonly string[] FeedAreas = [AuditAreas.Curriculum, AuditAreas.Questions, AuditAreas.Workspace];

    private readonly ApplicationDbContext _db;
    private readonly PeopleDirectory _people;
    private readonly NodeTrails _trails;
    private readonly StudioNotifier _notifier;
    private readonly IAuditLog _audit;

    public StudioInboxService(ApplicationDbContext db, PeopleDirectory people, NodeTrails trails, StudioNotifier notifier, IAuditLog audit)
    {
        _db = db;
        _people = people;
        _trails = trails;
        _notifier = notifier;
        _audit = audit;
    }

    // =====================================================================================
    // Notifications
    // =====================================================================================

    public async Task<IReadOnlyList<StudioNotificationDto>> NotificationsAsync(
        StudioMember member, bool unreadOnly, int take, CancellationToken cancellationToken = default)
    {
        var rows = _db.StudioNotifications.AsNoTracking().Where(n => n.UserId == member.UserId);
        if (unreadOnly) rows = rows.Where(n => n.ReadAtUtc == null);

        var list = await rows.OrderByDescending(n => n.CreatedAtUtc).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);

        var draftIds = list.Select(n => n.DraftId).OfType<Guid>().Distinct().ToList();
        var titles = await _db.Drafts.AsNoTracking().Where(d => draftIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Title, cancellationToken);

        var releaseIds = list.Select(n => n.ReleaseId).OfType<Guid>().Distinct().ToList();
        var releaseTitles = await _db.Releases.AsNoTracking().Where(r => releaseIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Title, cancellationToken);

        var names = await _people.NamesAsync(list.Select(n => n.ActorUserId), cancellationToken);

        return list.Select(n => new StudioNotificationDto(
            n.Id,
            n.Kind,
            PeopleDirectory.Ref(names, n.ActorUserId),
            n.DraftId,
            n.ReleaseId,
            n.AssignmentId,
            n.NodeId,
            n.DraftId is { } d && titles.TryGetValue(d, out var title) ? title
                : n.ReleaseId is { } r && releaseTitles.TryGetValue(r, out var releaseTitle) ? releaseTitle : null,
            n.CreatedAtUtc,
            n.ReadAtUtc is not null)).ToList();
    }

    public async Task MarkReadAsync(StudioMember member, IReadOnlyList<Guid>? ids, CancellationToken cancellationToken = default)
    {
        var rows = _db.StudioNotifications.Where(n => n.UserId == member.UserId && n.ReadAtUtc == null);
        if (ids is not null)
        {
            var wanted = ids.ToList();
            rows = rows.Where(n => wanted.Contains(n.Id));
        }

        var now = DateTime.UtcNow;
        await rows.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), cancellationToken);
    }

    // =====================================================================================
    // Assignments
    // =====================================================================================

    public async Task<IReadOnlyList<AssignmentDto>> AssignmentsAsync(
        StudioMember member, bool mine, bool includeClosed, CancellationToken cancellationToken = default)
    {
        var rows = _db.StudioAssignments.AsNoTracking().AsQueryable();
        if (mine || !member.IsAtLeast(StudioRole.Lead)) rows = rows.Where(a => a.AssigneeUserId == member.UserId);
        if (!includeClosed) rows = rows.Where(a => a.Status == WorkAssignmentStatus.Open);

        var list = await rows.OrderBy(a => a.Status).ThenBy(a => a.DueOn == null).ThenBy(a => a.DueOn).ThenByDescending(a => a.CreatedAtUtc)
            .Take(500).ToListAsync(cancellationToken);

        return await ToDtosAsync(list, cancellationToken);
    }

    public async Task<ServiceResult<AssignmentDto>> AssignAsync(
        StudioMember member, CreateAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return DraftService.OutOfScope<AssignmentDto>("role");

        var path = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == request.NodeId && n.RetiredAtUtc == null).Select(n => n.Path).FirstOrDefaultAsync(cancellationToken);
        if (path is null)
            return DraftService.NodeMissing<AssignmentDto>();

        if (!member.CoversPath(path))
            return DraftService.OutOfScope<AssignmentDto>("node");

        var assigneeActive = await _db.StaffProfiles.AnyAsync(p => p.UserId == request.AssigneeUserId && p.Status == StaffStatus.Active, cancellationToken);
        if (!assigneeActive)
            return ServiceResult<AssignmentDto>.Failure(WorkspaceErrors.AssignmentNotFound, ServiceErrorKind.Validation,
                "That person is not an active member of the content team.");

        var note = request.Note?.Trim();
        var assignment = new WorkAssignment
        {
            Id = Guid.NewGuid(),
            NodeId = request.NodeId,
            AssigneeUserId = request.AssigneeUserId,
            AssignedByUserId = member.UserId,
            Note = string.IsNullOrEmpty(note) ? null : note[..Math.Min(note.Length, 1000)],
            DueOn = request.DueOn,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.StudioAssignments.Add(assignment);
        _notifier.Notify([request.AssigneeUserId], "assignment.created", member.UserId, assignmentId: assignment.Id, nodeId: request.NodeId);
        _audit.Record(new AuditEntry(AuditActions.AssignmentCreated, AuditAreas.Workspace, "Assigned a piece of work.",
            "assignment", assignment.Id.ToString(), new { nodeId = request.NodeId, assignee = request.AssigneeUserId, dueOn = request.DueOn }));

        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<AssignmentDto>.Success((await ToDtosAsync([assignment], cancellationToken))[0]);
    }

    public async Task<ServiceResult<AssignmentDto>> CloseAssignmentAsync(
        StudioMember member, Guid assignmentId, WorkAssignmentStatus status, CancellationToken cancellationToken = default)
    {
        var assignment = await _db.StudioAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
            return ServiceResult<AssignmentDto>.Failure(WorkspaceErrors.AssignmentNotFound, ServiceErrorKind.NotFound, "Assignment not found.");

        // The assignee marks their own work done; a Lead can close or cancel any.
        var mayClose = assignment.AssigneeUserId == member.UserId && status == WorkAssignmentStatus.Done
                       || member.IsAtLeast(StudioRole.Lead);
        if (!mayClose)
            return DraftService.OutOfScope<AssignmentDto>("role");

        assignment.Status = status;
        assignment.ClosedAtUtc = status == WorkAssignmentStatus.Open ? null : DateTime.UtcNow;

        if (status == WorkAssignmentStatus.Done)
            _notifier.Notify([assignment.AssignedByUserId], "assignment.done", member.UserId, assignmentId: assignment.Id, nodeId: assignment.NodeId);

        _audit.Record(new AuditEntry(AuditActions.AssignmentClosed, AuditAreas.Workspace, $"Marked an assignment {status}.",
            "assignment", assignment.Id.ToString(), new { status = status.ToString() }));

        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<AssignmentDto>.Success((await ToDtosAsync([assignment], cancellationToken))[0]);
    }

    private async Task<IReadOnlyList<AssignmentDto>> ToDtosAsync(IReadOnlyList<WorkAssignment> assignments, CancellationToken cancellationToken)
    {
        var names = await _people.NamesAsync(assignments.SelectMany(a => new Guid?[] { a.AssigneeUserId, a.AssignedByUserId }), cancellationToken);
        var trails = await _trails.ForAsync(assignments.Select(a => a.NodeId), cancellationToken);

        return assignments.Select(a => new AssignmentDto(
            a.Id, a.NodeId, trails.TryGetValue(a.NodeId, out var trail) ? trail : [],
            PersonRefs.Of(names, a.AssigneeUserId), PersonRefs.Of(names, a.AssignedByUserId),
            a.Note, a.DueOn, a.Status, a.CreatedAtUtc, a.ClosedAtUtc)).ToList();
    }

    // =====================================================================================
    // Activity and the team
    // =====================================================================================

    public async Task<IReadOnlyList<ActivityItemDto>> ActivityAsync(
        StudioMember member, Guid? nodeId, long? before, int take, CancellationToken cancellationToken = default)
    {
        var rows = _db.AuditEvents.AsNoTracking().Where(e => FeedAreas.Contains(e.Area));
        if (before is { } sequence) rows = rows.Where(e => e.Sequence < sequence);

        if (nodeId is { } node)
        {
            var id = node.ToString();
            var draftIds = await _db.Drafts.AsNoTracking().Where(d => d.NodeId == node).Select(d => d.Id.ToString()).ToListAsync(cancellationToken);
            rows = rows.Where(e => e.TargetId == id || draftIds.Contains(e.TargetId!));
        }

        var list = await rows.OrderByDescending(e => e.Sequence).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);
        var names = await _people.NamesAsync(list.Select(e => e.ActorUserId), cancellationToken);

        return list.Select(e => new ActivityItemDto(
            e.Sequence, e.OccurredAtUtc, PeopleDirectory.Ref(names, e.ActorUserId), e.Action, e.Summary, e.TargetType, e.TargetId)).ToList();
    }

    public async Task<IReadOnlyList<TeammateDto>> TeamAsync(CancellationToken cancellationToken = default) =>
        await _db.StaffProfiles.AsNoTracking()
            .Where(p => p.Status == StaffStatus.Active)
            .OrderBy(p => p.FullName)
            .Select(p => new TeammateDto(p.UserId, p.FullName, p.StudioRole))
            .ToListAsync(cancellationToken);
}

/// <summary>Publishes scheduled releases when their time comes. Checks every thirty seconds.</summary>
public sealed class ReleaseScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReleaseScheduler> _logger;

    public ReleaseScheduler(IServiceScopeFactory scopes, ILogger<ReleaseScheduler> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var published = await scope.ServiceProvider.GetRequiredService<IReleaseService>().PublishDueAsync(stoppingToken);
                if (published > 0)
                    _logger.LogInformation("Published {Count} scheduled release(s).", published);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "The release scheduler failed; it will look again shortly.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
