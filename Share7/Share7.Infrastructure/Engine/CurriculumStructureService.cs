using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Engine;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Audit;
using Share7.Domain.Curriculum;
using Share7.Domain.Progress;
using Share7.Domain.Structure;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Persistence.Configurations;

namespace Share7.Infrastructure.Engine;

/// <inheritdoc cref="ICurriculumStructureService"/>
public sealed class CurriculumStructureService : ICurriculumStructureService
{
    public const int TermTitleMaxLength = 100;
    public const int TitleMaxLength = 200;

    private readonly ApplicationDbContext _db;
    private readonly IContentLanguages _languages;
    private readonly IAuditLog _audit;
    private readonly UnlockRepairSignal _repairs;

    public CurriculumStructureService(
        ApplicationDbContext db, IContentLanguages languages, IAuditLog audit, UnlockRepairSignal repairs)
    {
        _db = db;
        _languages = languages;
        _audit = audit;
        _repairs = repairs;
    }

    public async Task<NodeStateDto?> GetAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes
            .AsNoTracking()
            .Include(n => n.Translations)
            .FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);

        return node is null ? null : State(node);
    }

    // =====================================================================================
    // Create
    // =====================================================================================

    public async Task<ServiceResult<StructureChangeDto>> CreateAsync(
        CreateNodeCommand command, EngineActor actor, CancellationToken cancellationToken = default)
    {
        if (!NodeKinds.IsEditable(command.Kind))
            return Fail(EngineErrors.NodeNotEditable, ServiceErrorKind.Validation, $"A {command.Kind} cannot be added.");

        var parent = await _db.CurriculumNodes.FirstOrDefaultAsync(n => n.Id == command.ParentId, cancellationToken);
        if (parent is null)
            return Fail(EngineErrors.NodeNotFound, ServiceErrorKind.NotFound, "Parent not found.");

        if (parent.KindKey != NodeKinds.ParentOf(command.Kind))
            return Fail(EngineErrors.NodeWrongParent, ServiceErrorKind.Validation,
                $"A {command.Kind} goes under a {NodeKinds.ParentOf(command.Kind)}, not a {parent.KindKey}.");

        if (parent.RetiredAtUtc is not null)
            return Fail(EngineErrors.NodeParentRetired, ServiceErrorKind.Conflict, "The parent is retired. Restore it first.");

        if (command.Position is < 1)
            return Fail(EngineErrors.NodePositionTaken, ServiceErrorKind.Validation, "Positions start at 1.");

        var titles = await ValidateTitlesAsync(command.Kind, command.Titles, cancellationToken);
        if (!titles.Succeeded)
            return Propagate(titles);

        await using var own = await BeginAsync(cancellationToken);
        await LockChildrenAsync(parent.Id, cancellationToken);

        var siblings = await LiveChildrenAsync(parent.Id, cancellationToken);

        if (NameClash(siblings, null, titles.Value!) is { } clash)
            return clash;

        var last = siblings.Count == 0 ? 0 : siblings.Max(s => s.Order);
        var order = command.Position ?? last + 1;
        var shifted = new List<Guid>();

        if (siblings.Any(s => s.Order == order))
        {
            if (!command.ShiftSiblings)
                return Fail(EngineErrors.NodePositionTaken, ServiceErrorKind.Conflict,
                    $"Another node is already at position {order} under this parent.");

            // Make room: everything from the position down moves one place. Nodes first, then the
            // typed rows in two phases so the live-order unique index never sees two rows share a slot.
            var moving = siblings.Where(s => s.Order >= order).ToList();
            foreach (var sibling in moving)
            {
                sibling.Order += 1;
                Touch(sibling, actor);
                shifted.Add(sibling.Id);
            }

            await SetTypedOrdersAsync(command.Kind, moving.ToDictionary(s => s.Id, s => s.Order), cancellationToken);
        }

        var id = command.NodeId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;

        var node = new CurriculumNode
        {
            Id = id,
            CurriculumVersionId = parent.CurriculumVersionId,
            ParentNodeId = parent.Id,
            NodeKindId = CurriculumNodeKindConfiguration.SeededKindIds[command.Kind],
            KindKey = command.Kind,
            Order = order,
            Depth = parent.Depth + 1,
            Path = $"{parent.Path}/{id:D}",
            IsPlayable = command.Kind == NodeKinds.Lesson,
            LegacySource = LegacyTable(command.Kind),
            CreatedAtUtc = now,
            Revision = 1,
            UpdatedAtUtc = now,
            UpdatedByUserId = actor.UserId,
            Translations = titles.Value!.Select(t => new CurriculumNodeTranslation { NodeId = id, LangId = t.LangId, Title = t.Title }).ToList()
        };

        _db.CurriculumNodes.Add(node);
        AddTyped(command.Kind, id, parent.Id, order, titles.Value!);
        Touch(parent, actor);

        // A node placed before others: anyone already past its position would find it locked
        // behind them. Appended at the end, nobody is.
        var queued = order <= last;
        if (queued)
            QueueRepair(UnlockRepairKind.FillGaps, parent.Id, null, null, now);

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumNodeCreated,
            AuditAreas.Curriculum,
            $"Added a {command.Kind} to a {parent.KindKey}, at position {order}.",
            command.Kind,
            id.ToString(),
            new { parentType = parent.KindKey, parentId = parent.Id, order, shifted = shifted.Count, releaseId = actor.ReleaseId }));

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(own, queued, cancellationToken);

        return Done(node, shifted, queued);
    }

    // =====================================================================================
    // Rename
    // =====================================================================================

    public async Task<ServiceResult<StructureChangeDto>> RenameAsync(
        Guid nodeId, IReadOnlyList<NodeTitle> titles, int? expectedRevision, EngineActor actor,
        CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes
            .Include(n => n.Translations)
            .FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);

        if (Guard(node, expectedRevision, mustBeLive: true) is { } refused)
            return refused;

        var validated = await ValidateTitlesAsync(node!.KindKey, titles, cancellationToken);
        if (!validated.Succeeded)
            return Propagate(validated);

        await using var own = await BeginAsync(cancellationToken);

        if (node.ParentNodeId is { } parentId)
        {
            await LockChildrenAsync(parentId, cancellationToken);
            var siblings = await LiveChildrenAsync(parentId, cancellationToken);
            if (NameClash(siblings, node.Id, validated.Value!) is { } clash)
                return clash;
        }

        var before = node.Translations.ToDictionary(t => t.LangId, t => t.Title);

        foreach (var title in validated.Value!)
        {
            var existing = node.Translations.FirstOrDefault(t => t.LangId == title.LangId);
            if (existing is null)
                node.Translations.Add(new CurriculumNodeTranslation { NodeId = node.Id, LangId = title.LangId, Title = title.Title });
            else
                existing.Title = title.Title;
        }

        foreach (var dropped in node.Translations.Where(t => validated.Value!.All(v => v.LangId != t.LangId)).ToList())
            node.Translations.Remove(dropped);

        await SetTypedTitlesAsync(node.KindKey, node.Id, validated.Value!, cancellationToken);
        Touch(node, actor);

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumNodeRenamed,
            AuditAreas.Curriculum,
            $"Renamed a {node.KindKey}.",
            node.KindKey,
            node.Id.ToString(),
            new { before, after = validated.Value!.ToDictionary(t => t.LangId, t => t.Title), releaseId = actor.ReleaseId }));

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(own, false, cancellationToken);

        return Done(node, [], false);
    }

    // =====================================================================================
    // Move
    // =====================================================================================

    public async Task<ServiceResult<StructureChangeDto>> MoveAsync(
        Guid nodeId, Guid newParentId, int? position, int? expectedRevision, EngineActor actor,
        CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes
            .Include(n => n.Translations)
            .FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);

        if (Guard(node, expectedRevision, mustBeLive: true) is { } refused)
            return refused;

        if (node!.ParentNodeId == newParentId)
            return position is { } p ? await MoveWithinAsync(node, p, actor, cancellationToken) : Done(node, [], false);

        var target = await _db.CurriculumNodes.FirstOrDefaultAsync(n => n.Id == newParentId, cancellationToken);
        if (target is null)
            return Fail(EngineErrors.NodeNotFound, ServiceErrorKind.NotFound, "The new parent was not found.");

        if (target.KindKey != NodeKinds.ParentOf(node.KindKey))
            return Fail(EngineErrors.NodeWrongParent, ServiceErrorKind.Validation,
                $"A {node.KindKey} goes under a {NodeKinds.ParentOf(node.KindKey)}, not a {target.KindKey}.");

        if (target.RetiredAtUtc is not null)
            return Fail(EngineErrors.NodeParentRetired, ServiceErrorKind.Conflict, "The new parent is retired. Restore it first.");

        if (position is < 1)
            return Fail(EngineErrors.NodePositionTaken, ServiceErrorKind.Validation, "Positions start at 1.");

        await using var own = await BeginAsync(cancellationToken);
        await LockChildrenAsync(target.Id, cancellationToken);
        if (node.ParentNodeId is { } oldParentId)
            await LockChildrenAsync(oldParentId, cancellationToken);

        var siblings = await LiveChildrenAsync(target.Id, cancellationToken);
        var titles = node.Translations.Select(t => new NodeTitle(t.LangId, t.Title)).ToList();
        if (NameClash(siblings, node.Id, titles) is { } clash)
            return clash;

        var last = siblings.Count == 0 ? 0 : siblings.Max(s => s.Order);
        var order = position ?? last + 1;
        var changed = new List<Guid>();

        if (siblings.Any(s => s.Order == order))
        {
            var moving = siblings.Where(s => s.Order >= order).ToList();
            foreach (var sibling in moving)
            {
                sibling.Order += 1;
                Touch(sibling, actor);
                changed.Add(sibling.Id);
            }

            await SetTypedOrdersAsync(node.KindKey, moving.ToDictionary(s => s.Id, s => s.Order), cancellationToken);
        }

        var oldParent = await _db.CurriculumNodes.FirstAsync(n => n.Id == node.ParentNodeId, cancellationToken);
        var formerOrder = node.Order;
        var oldPath = node.Path;
        var newPath = $"{target.Path}/{node.Id:D}";

        // The whole subtree moves with it: one prefix rewrite, however deep it goes.
        await _db.CurriculumNodes
            .Where(n => n.Path.StartsWith(oldPath + "/"))
            .ExecuteUpdateAsync(s => s.SetProperty(
                n => n.Path, n => newPath + n.Path.Substring(oldPath.Length, 512)), cancellationToken);

        node.ParentNodeId = target.Id;
        node.Order = order;
        node.Path = newPath;
        Touch(node, actor);
        Touch(oldParent, actor);
        Touch(target, actor);

        await SetTypedParentAsync(node.KindKey, node.Id, target.Id, order, cancellationToken);

        var now = DateTime.UtcNow;

        // Students who had it in the old place get what finishing it would have given them there;
        // students already past its new place get it opened behind them.
        QueueRepair(UnlockRepairKind.PassForward, node.Id, oldParent.Id, formerOrder, now);
        if (order <= last)
            QueueRepair(UnlockRepairKind.FillGaps, target.Id, null, null, now);

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumNodeMoved,
            AuditAreas.Curriculum,
            $"Moved a {node.KindKey} to another {target.KindKey}, at position {order}.",
            node.KindKey,
            node.Id.ToString(),
            new { fromParentId = oldParent.Id, fromOrder = formerOrder, toParentId = target.Id, toOrder = order, releaseId = actor.ReleaseId }));

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(own, true, cancellationToken);

        return Done(node, changed, true);
    }

    /// <summary>A move to a new position under the same parent: a reorder of that parent's children.</summary>
    private async Task<ServiceResult<StructureChangeDto>> MoveWithinAsync(
        CurriculumNode node, int position, EngineActor actor, CancellationToken cancellationToken)
    {
        var siblings = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.ParentNodeId == node.ParentNodeId && n.RetiredAtUtc == null)
            .OrderBy(n => n.Order)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        siblings.Remove(node.Id);
        siblings.Insert(Math.Clamp(position - 1, 0, siblings.Count), node.Id);

        return await ReorderAsync(node.ParentNodeId!.Value, siblings, null, actor, cancellationToken);
    }

    // =====================================================================================
    // Reorder
    // =====================================================================================

    public async Task<ServiceResult<StructureChangeDto>> ReorderAsync(
        Guid parentId, IReadOnlyList<Guid> orderedChildIds, int? expectedRevision, EngineActor actor,
        CancellationToken cancellationToken = default)
    {
        var parent = await _db.CurriculumNodes
            .Include(n => n.Translations)
            .FirstOrDefaultAsync(n => n.Id == parentId, cancellationToken);

        if (Guard(parent, expectedRevision, mustBeLive: true, asParent: true) is { } refused)
            return refused;

        await using var own = await BeginAsync(cancellationToken);
        await LockChildrenAsync(parentId, cancellationToken);

        var children = await LiveChildrenAsync(parentId, cancellationToken);

        if (orderedChildIds.Count != children.Count
            || orderedChildIds.Distinct().Count() != orderedChildIds.Count
            || orderedChildIds.Any(id => children.All(c => c.Id != id)))
        {
            return Fail(EngineErrors.NodeOrderInvalid, ServiceErrorKind.Validation,
                "A new order must list every live child exactly once.");
        }

        var changed = new List<Guid>();
        var orders = new Dictionary<Guid, int>();

        for (var i = 0; i < orderedChildIds.Count; i++)
        {
            var child = children.First(c => c.Id == orderedChildIds[i]);
            var order = i + 1;
            orders[child.Id] = order;

            if (child.Order == order) continue;

            child.Order = order;
            Touch(child, actor);
            changed.Add(child.Id);
        }

        if (changed.Count == 0)
            return Done(parent!, [], false);

        var kind = children[0].KindKey;
        await SetTypedOrdersAsync(kind, orders, cancellationToken);
        Touch(parent!, actor);

        // Subjects are a parallel split of a term, not a sequence, so their order gates nothing.
        var queued = kind != NodeKinds.Subject;
        if (queued)
            QueueRepair(UnlockRepairKind.FillGaps, parentId, null, null, DateTime.UtcNow);

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumNodesReordered,
            AuditAreas.Curriculum,
            $"Put the {kind}s of a {parent!.KindKey} in a new order ({changed.Count} moved).",
            parent.KindKey,
            parent.Id.ToString(),
            new { order = orderedChildIds, moved = changed.Count, releaseId = actor.ReleaseId }));

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(own, queued, cancellationToken);

        return Done(parent, changed, queued);
    }

    // =====================================================================================
    // Retire and restore
    // =====================================================================================

    public async Task<ServiceResult<StructureChangeDto>> RetireAsync(
        Guid nodeId, int? expectedRevision, EngineActor actor, CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes
            .Include(n => n.Translations)
            .FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);

        if (Guard(node, expectedRevision, mustBeLive: false) is { } refused)
            return refused;

        if (node!.RetiredAtUtc is not null)
            return Fail(EngineErrors.NodeStateInvalid, ServiceErrorKind.Conflict, $"This {node.KindKey} is already retired.");

        await using var own = await BeginAsync(cancellationToken);
        if (node.ParentNodeId is { } parentId)
            await LockChildrenAsync(parentId, cancellationToken);

        var now = DateTime.UtcNow;
        var prefix = node.Path + "/";

        var subtree = await _db.CurriculumNodes
            .Where(n => n.RetiredAtUtc == null && n.Path.StartsWith(prefix))
            .Select(n => new { n.Id, n.KindKey })
            .ToListAsync(cancellationToken);

        // One instant for the node and everything under it: that instant is how a restore knows
        // which descendants were retired *with* it and which had been retired before.
        await _db.CurriculumNodes
            .Where(n => n.RetiredAtUtc == null && n.Path.StartsWith(prefix))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.RetiredAtUtc, now)
                .SetProperty(n => n.Revision, n => n.Revision + 1)
                .SetProperty(n => n.UpdatedAtUtc, now)
                .SetProperty(n => n.UpdatedByUserId, actor.UserId), cancellationToken);

        node.RetiredAtUtc = now;
        Touch(node, actor, now);

        if (node.ParentNodeId is { } parentNodeId)
            Touch(await _db.CurriculumNodes.FirstAsync(n => n.Id == parentNodeId, cancellationToken), actor, now);

        var byKind = subtree.Append(new { node.Id, node.KindKey }).GroupBy(n => n.KindKey);
        foreach (var group in byKind)
            await SetTypedRetiredAsync(group.Key, group.Select(n => n.Id).ToList(), now, cancellationToken);

        QueueRepair(UnlockRepairKind.PassForward, node.Id, node.ParentNodeId, node.Order, now);

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumNodeRetired,
            AuditAreas.Curriculum,
            subtree.Count == 0
                ? $"Retired a {node.KindKey}: hidden from students, history kept."
                : $"Retired a {node.KindKey} and the {subtree.Count} node(s) under it: hidden from students, history kept.",
            node.KindKey,
            node.Id.ToString(),
            new { parentId = node.ParentNodeId, order = node.Order, descendants = subtree.Count, releaseId = actor.ReleaseId }));

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(own, true, cancellationToken);

        return Done(node, subtree.Select(s => s.Id).ToList(), true);
    }

    public async Task<ServiceResult<StructureChangeDto>> RestoreAsync(
        Guid nodeId, EngineActor actor, CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes
            .Include(n => n.Translations)
            .FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);

        if (node is null)
            return Fail(EngineErrors.NodeNotFound, ServiceErrorKind.NotFound, "Not found.");

        if (node.RetiredAtUtc is not { } retiredAt)
            return Fail(EngineErrors.NodeStateInvalid, ServiceErrorKind.Conflict, $"This {node.KindKey} is not retired.");

        if (!NodeKinds.IsEditable(node.KindKey))
            return Fail(EngineErrors.NodeNotEditable, ServiceErrorKind.Validation, $"A {node.KindKey} cannot be restored.");

        var parent = await _db.CurriculumNodes.FirstAsync(n => n.Id == node.ParentNodeId, cancellationToken);
        if (parent.RetiredAtUtc is not null)
            return Fail(EngineErrors.NodeParentRetired, ServiceErrorKind.Conflict, "Its parent is retired. Restore that first.");

        await using var own = await BeginAsync(cancellationToken);
        await LockChildrenAsync(parent.Id, cancellationToken);

        var siblings = await LiveChildrenAsync(parent.Id, cancellationToken);
        var titles = node.Translations.Select(t => new NodeTitle(t.LangId, t.Title)).ToList();
        if (NameClash(siblings, node.Id, titles) is { } clash)
            return clash;

        // Back where it was when that position is still free; after the last live sibling if not.
        var last = siblings.Count == 0 ? 0 : siblings.Max(s => s.Order);
        if (siblings.Any(s => s.Order == node.Order))
            node.Order = last + 1;

        var now = DateTime.UtcNow;
        var prefix = node.Path + "/";

        // Exactly the nodes retired with it — the same instant. Anything retired earlier, on its own,
        // stays retired.
        var subtree = await _db.CurriculumNodes
            .Where(n => n.RetiredAtUtc == retiredAt && n.Path.StartsWith(prefix))
            .Select(n => new { n.Id, n.KindKey })
            .ToListAsync(cancellationToken);

        await _db.CurriculumNodes
            .Where(n => n.RetiredAtUtc == retiredAt && n.Path.StartsWith(prefix))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.RetiredAtUtc, (DateTime?)null)
                .SetProperty(n => n.Revision, n => n.Revision + 1)
                .SetProperty(n => n.UpdatedAtUtc, now)
                .SetProperty(n => n.UpdatedByUserId, actor.UserId), cancellationToken);

        node.RetiredAtUtc = null;
        Touch(node, actor, now);
        Touch(parent, actor, now);

        // Position first, while the typed row is still retired and outside the live-order index;
        // clearing the retirement then cannot collide with the sibling that took its old slot.
        await SetTypedOrdersAsync(node.KindKey, new Dictionary<Guid, int> { [node.Id] = node.Order }, cancellationToken);

        foreach (var group in subtree.Append(new { node.Id, node.KindKey }).GroupBy(n => n.KindKey))
            await SetTypedRetiredAsync(group.Key, group.Select(n => n.Id).ToList(), null, cancellationToken);

        // Back in the chain: anyone already past it would find it locked behind them.
        var queued = node.KindKey != NodeKinds.Subject;
        if (queued)
            QueueRepair(UnlockRepairKind.FillGaps, parent.Id, null, null, now);

        _audit.Record(new AuditEntry(
            AuditActions.CurriculumNodeRestored,
            AuditAreas.Curriculum,
            $"Restored a retired {node.KindKey}, at position {node.Order}.",
            node.KindKey,
            node.Id.ToString(),
            new { parentId = parent.Id, order = node.Order, descendants = subtree.Count, releaseId = actor.ReleaseId }));

        await _db.SaveChangesAsync(cancellationToken);
        await CommitAsync(own, queued, cancellationToken);

        return Done(node, subtree.Select(s => s.Id).ToList(), queued);
    }

    // =====================================================================================
    // The typed compatibility copy
    // =====================================================================================

    private static string LegacyTable(string kind) => kind switch
    {
        NodeKinds.Term => "Terms",
        NodeKinds.Subject => "Subjects",
        NodeKinds.Chapter => "Chapters",
        NodeKinds.Lesson => "Lessons",
        _ => "Grades"
    };

    private void AddTyped(string kind, Guid id, Guid parentId, int order, IReadOnlyList<NodeTitle> titles)
    {
        switch (kind)
        {
            case NodeKinds.Term:
                _db.Terms.Add(new Term
                {
                    Id = id, GradeId = parentId, Order = order,
                    Translations = titles.Select(t => new TermTranslation { TermId = id, LangId = t.LangId, Name = t.Title }).ToList()
                });
                break;
            case NodeKinds.Subject:
                _db.Subjects.Add(new Subject
                {
                    Id = id, TermId = parentId, Order = order,
                    Translations = titles.Select(t => new SubjectTranslation { SubjectId = id, LangId = t.LangId, Name = t.Title }).ToList()
                });
                break;
            case NodeKinds.Chapter:
                _db.Chapters.Add(new Chapter
                {
                    Id = id, SubjectId = parentId, Order = order,
                    Translations = titles.Select(t => new ChapterTranslation { ChapterId = id, LangId = t.LangId, Name = t.Title }).ToList()
                });
                break;
            case NodeKinds.Lesson:
                _db.Lessons.Add(new Lesson
                {
                    Id = id, ChapterId = parentId, Order = order,
                    Translations = titles.Select(t => new LessonTranslation { LessonId = id, LangId = t.LangId, Name = t.Title }).ToList()
                });
                break;
        }
    }

    /// <summary>
    /// New positions for typed rows, in two phases: everything touched steps far out of the way
    /// first, then lands on its new slot. The live-order unique index is checked per statement, and
    /// a permutation written row by row would otherwise collide with itself halfway through.
    /// </summary>
    private async Task SetTypedOrdersAsync(
        string kind, IReadOnlyDictionary<Guid, int> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0) return;

        const int Offset = 1_000_000;
        var ids = orders.Keys.ToList();

        switch (kind)
        {
            case NodeKinds.Term:
                await _db.Terms.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, x => x.Order + Offset), cancellationToken);
                foreach (var (id, order) in orders)
                    await _db.Terms.IgnoreQueryFilters().Where(x => x.Id == id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, order), cancellationToken);
                break;
            case NodeKinds.Subject:
                await _db.Subjects.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, x => x.Order + Offset), cancellationToken);
                foreach (var (id, order) in orders)
                    await _db.Subjects.IgnoreQueryFilters().Where(x => x.Id == id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, order), cancellationToken);
                break;
            case NodeKinds.Chapter:
                await _db.Chapters.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, x => x.Order + Offset), cancellationToken);
                foreach (var (id, order) in orders)
                    await _db.Chapters.IgnoreQueryFilters().Where(x => x.Id == id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, order), cancellationToken);
                break;
            case NodeKinds.Lesson:
                await _db.Lessons.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, x => x.Order + Offset), cancellationToken);
                foreach (var (id, order) in orders)
                    await _db.Lessons.IgnoreQueryFilters().Where(x => x.Id == id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order, order), cancellationToken);
                break;
        }
    }

    private async Task SetTypedParentAsync(string kind, Guid id, Guid parentId, int order, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case NodeKinds.Term:
                await _db.Terms.IgnoreQueryFilters().Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.GradeId, parentId).SetProperty(x => x.Order, order), cancellationToken);
                break;
            case NodeKinds.Subject:
                await _db.Subjects.IgnoreQueryFilters().Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.TermId, parentId).SetProperty(x => x.Order, order), cancellationToken);
                break;
            case NodeKinds.Chapter:
                await _db.Chapters.IgnoreQueryFilters().Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.SubjectId, parentId).SetProperty(x => x.Order, order), cancellationToken);
                break;
            case NodeKinds.Lesson:
                await _db.Lessons.IgnoreQueryFilters().Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ChapterId, parentId).SetProperty(x => x.Order, order), cancellationToken);
                break;
        }
    }

    private async Task SetTypedRetiredAsync(string kind, List<Guid> ids, DateTime? retiredAt, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case NodeKinds.Term:
                await _db.Terms.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetiredAtUtc, retiredAt), cancellationToken);
                break;
            case NodeKinds.Subject:
                await _db.Subjects.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetiredAtUtc, retiredAt), cancellationToken);
                break;
            case NodeKinds.Chapter:
                await _db.Chapters.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetiredAtUtc, retiredAt), cancellationToken);
                break;
            case NodeKinds.Lesson:
                await _db.Lessons.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetiredAtUtc, retiredAt), cancellationToken);
                break;
        }
    }

    /// <summary>
    /// The typed copy of the titles, through the change tracker: a node created and renamed in one
    /// unit of work (a release does both) already has its typed titles tracked, and a delete-and-add
    /// behind the tracker's back would collide with them.
    /// </summary>
    private async Task SetTypedTitlesAsync(string kind, Guid id, IReadOnlyList<NodeTitle> titles, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case NodeKinds.Term:
                Sync(await _db.TermTranslations.Where(t => t.TermId == id).ToListAsync(cancellationToken), titles,
                    t => t.LangId, (t, name) => t.Name = name,
                    title => _db.TermTranslations.Add(new TermTranslation { TermId = id, LangId = title.LangId, Name = title.Title }),
                    t => _db.TermTranslations.Remove(t));
                break;
            case NodeKinds.Subject:
                Sync(await _db.SubjectTranslations.Where(t => t.SubjectId == id).ToListAsync(cancellationToken), titles,
                    t => t.LangId, (t, name) => t.Name = name,
                    title => _db.SubjectTranslations.Add(new SubjectTranslation { SubjectId = id, LangId = title.LangId, Name = title.Title }),
                    t => _db.SubjectTranslations.Remove(t));
                break;
            case NodeKinds.Chapter:
                Sync(await _db.ChapterTranslations.Where(t => t.ChapterId == id).ToListAsync(cancellationToken), titles,
                    t => t.LangId, (t, name) => t.Name = name,
                    title => _db.ChapterTranslations.Add(new ChapterTranslation { ChapterId = id, LangId = title.LangId, Name = title.Title }),
                    t => _db.ChapterTranslations.Remove(t));
                break;
            case NodeKinds.Lesson:
                Sync(await _db.LessonTranslations.Where(t => t.LessonId == id).ToListAsync(cancellationToken), titles,
                    t => t.LangId, (t, name) => t.Name = name,
                    title => _db.LessonTranslations.Add(new LessonTranslation { LessonId = id, LangId = title.LangId, Name = title.Title }),
                    t => _db.LessonTranslations.Remove(t));
                break;
        }
    }

    private static void Sync<T>(
        List<T> existing,
        IReadOnlyList<NodeTitle> titles,
        Func<T, Guid> langOf,
        Action<T, string> rename,
        Action<NodeTitle> add,
        Action<T> remove)
    {
        foreach (var title in titles)
        {
            var row = existing.FirstOrDefault(t => langOf(t) == title.LangId);
            if (row is null) add(title);
            else rename(row, title.Title);
        }

        foreach (var row in existing.Where(t => titles.All(title => title.LangId != langOf(t))))
            remove(row);
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken cancellationToken) =>
        _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

    /// <summary>Commits a transaction this service opened, and wakes the unlock repairs it queued.</summary>
    private async Task CommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? own, bool queuedRepairs, CancellationToken cancellationToken)
    {
        if (own is null) return; // the caller's transaction; the caller nudges the repairs after it commits

        await own.CommitAsync(cancellationToken);
        if (queuedRepairs)
            _repairs.Nudge();
    }

    /// <summary>
    /// Serialises changes to one parent's children, so two edits that both read "the last position
    /// is 4" cannot both put something at 5.
    /// </summary>
    private async Task LockChildrenAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var resource = $"share7.node-children/{parentId:N}";

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                                         @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51001, 'Another change to this part of the curriculum did not finish in time.', 1;
            """, cancellationToken);
    }

    private Task<List<CurriculumNode>> LiveChildrenAsync(Guid parentId, CancellationToken cancellationToken) =>
        _db.CurriculumNodes
            .Include(n => n.Translations)
            .Where(n => n.ParentNodeId == parentId && n.RetiredAtUtc == null)
            .OrderBy(n => n.Order)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Two live siblings with the same name in one language would be indistinguishable to a student
    /// scanning the list. Compared case-insensitively, as the admin paths always have.
    /// </summary>
    private static ServiceResult<StructureChangeDto>? NameClash(
        List<CurriculumNode> siblings, Guid? self, IReadOnlyList<NodeTitle> titles)
    {
        foreach (var title in titles)
        {
            var clash = siblings
                .Where(s => s.Id != self)
                .Any(s => s.Translations.Any(t =>
                    t.LangId == title.LangId && string.Equals(t.Title, title.Title, StringComparison.OrdinalIgnoreCase)));

            if (clash)
            {
                return ServiceResult<StructureChangeDto>.Failure(
                    EngineErrors.NodeNameTaken,
                    ServiceErrorKind.Conflict,
                    $"Another item here is already called '{title.Title}'.",
                    new Dictionary<string, object?> { ["langId"] = title.LangId, ["title"] = title.Title });
            }
        }

        return null;
    }

    /// <summary>
    /// Trims each title and requires one for every language content must be published in; refuses
    /// blanks, over-long titles, unknown languages and a language given twice.
    /// </summary>
    private async Task<ServiceResult<List<NodeTitle>>> ValidateTitlesAsync(
        string kind, IReadOnlyList<NodeTitle> titles, CancellationToken cancellationToken)
    {
        var languages = await _languages.GetAsync(cancellationToken);
        var max = kind == NodeKinds.Term ? TermTitleMaxLength : TitleMaxLength;
        var problems = new List<object>();
        var clean = new List<NodeTitle>();

        foreach (var title in titles ?? [])
        {
            var text = (title.Title ?? string.Empty).Trim();

            if (languages.All(l => l.Id != title.LangId))
                problems.Add(new { code = "unknownLanguage", langId = title.LangId });
            else if (text.Length == 0)
                problems.Add(new { code = "titleMissing", langId = title.LangId });
            else if (text.Length > max)
                problems.Add(new { code = "titleTooLong", langId = title.LangId, max });
            else
                clean.Add(new NodeTitle(title.LangId, text));
        }

        foreach (var twice in (titles ?? []).GroupBy(t => t.LangId).Where(g => g.Count() > 1))
            problems.Add(new { code = "duplicateLanguage", langId = twice.Key });

        foreach (var required in languages.Where(l => l.IsContentLanguage && l.RequiredToPublish))
        {
            if ((titles ?? []).All(t => t.LangId != required.Id))
                problems.Add(new { code = "titleMissing", langId = required.Id });
        }

        if (problems.Count > 0)
        {
            return ServiceResult<List<NodeTitle>>.Failure(
                EngineErrors.NodeTitlesInvalid,
                ServiceErrorKind.Validation,
                "Every required language needs a name, and none may be blank or too long.",
                new Dictionary<string, object?> { ["problems"] = problems });
        }

        return ServiceResult<List<NodeTitle>>.Success(clean);
    }

    /// <param name="asParent">
    /// The node is the parent whose children are being reordered — the one change a grade allows.
    /// </param>
    private static ServiceResult<StructureChangeDto>? Guard(
        CurriculumNode? node, int? expectedRevision, bool mustBeLive, bool asParent = false)
    {
        if (node is null)
            return Fail(EngineErrors.NodeNotFound, ServiceErrorKind.NotFound, "Not found.");

        if (!NodeKinds.IsEditable(node.KindKey) && !(asParent && node.KindKey == NodeKinds.Grade))
            return Fail(EngineErrors.NodeNotEditable, ServiceErrorKind.Validation, $"A {node.KindKey} cannot be changed here.");

        if (mustBeLive && node.RetiredAtUtc is not null)
            return Fail(EngineErrors.NodeStateInvalid, ServiceErrorKind.Conflict, $"This {node.KindKey} is retired. Restore it first.");

        if (expectedRevision is { } expected && expected != node.Revision)
        {
            return ServiceResult<StructureChangeDto>.Failure(
                EngineErrors.NodeMoved,
                ServiceErrorKind.Conflict,
                $"This {node.KindKey} changed after you opened it.",
                new Dictionary<string, object?> { ["revision"] = node.Revision });
        }

        return null;
    }

    private void QueueRepair(UnlockRepairKind kind, Guid nodeId, Guid? parentId, int? formerOrder, DateTime now) =>
        _db.UnlockRepairJobs.Add(new UnlockRepairJob
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            NodeId = nodeId,
            ParentNodeId = parentId,
            FormerOrder = formerOrder,
            CreatedAtUtc = now
        });

    private static void Touch(CurriculumNode node, EngineActor actor, DateTime? at = null)
    {
        node.Revision += 1;
        node.UpdatedAtUtc = at ?? DateTime.UtcNow;
        node.UpdatedByUserId = actor.UserId;
    }

    private static NodeStateDto State(CurriculumNode node) => new()
    {
        Id = node.Id,
        Kind = node.KindKey,
        ParentId = node.ParentNodeId,
        Order = node.Order,
        Revision = node.Revision,
        Path = node.Path,
        RetiredAtUtc = node.RetiredAtUtc,
        Titles = node.Translations.Select(t => new NodeTitle(t.LangId, t.Title)).ToList()
    };

    private static ServiceResult<StructureChangeDto> Done(CurriculumNode node, List<Guid> alsoChanged, bool queued) =>
        ServiceResult<StructureChangeDto>.Success(new StructureChangeDto
        {
            Node = State(node),
            AlsoChanged = alsoChanged,
            UnlocksQueued = queued
        });

    private static ServiceResult<StructureChangeDto> Fail(ApiErrorCode code, ServiceErrorKind kind, string message) =>
        ServiceResult<StructureChangeDto>.Failure(code, kind, message);

    private static ServiceResult<StructureChangeDto> Propagate<T>(ServiceResult<T> source) =>
        new() { ErrorKind = source.ErrorKind, Error = source.Error, Errors = source.Errors, Details = source.Details };
}
