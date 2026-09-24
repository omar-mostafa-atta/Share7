using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Competency.Interfaces;
using Share7.Application.Measurement.Interfaces;
using Share7.Domain.Competency;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Competency;

/// <inheritdoc cref="ITargetAuthoringService"/>
public class TargetAuthoringService : ITargetAuthoringService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IObservationProjector _projector;
    private readonly ILogger<TargetAuthoringService> _logger;

    public TargetAuthoringService(
        ApplicationDbContext dbContext,
        IObservationProjector projector,
        ILogger<TargetAuthoringService> logger)
    {
        _dbContext = dbContext;
        _projector = projector;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlaceholderTargetDto>> GetPlaceholdersAsync(
        Guid nodeId, Guid langId, CancellationToken cancellationToken = default)
    {
        // Everything under this node, not just its direct children: an author works on a subject
        // or a chapter, and the placeholders live on the lessons beneath it. The materialised path
        // is what makes that one indexed prefix scan rather than a recursive walk.
        var node = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new { n.Path })
            .FirstOrDefaultAsync(cancellationToken);

        if (node is null) return [];

        var prefix = node.Path + "/";

        var descendantIds = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.RetiredAtUtc == null && (n.Id == nodeId || n.Path.StartsWith(prefix)))
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        var rows = await _dbContext.NodeTargetMappings
            .AsNoTracking()
            .Where(m => descendantIds.Contains(m.NodeId))
            .Where(m => m.Target!.IsPlaceholder)
            .Select(m => new
            {
                m.TargetId,
                m.NodeId,
                Statement = m.Target!.Translations
                    .Where(t => t.LangId == langId)
                    .Select(t => t.Statement)
                    .FirstOrDefault(),
                NodeTitle = _dbContext.CurriculumNodeTranslations
                    .Where(t => t.NodeId == m.NodeId && t.LangId == langId)
                    .Select(t => t.Title)
                    .FirstOrDefault(),
                ItemCount = _dbContext.ItemTargetMappings.Count(i => i.TargetId == m.TargetId),
                ObservationCount = _dbContext.Observations.Count(o => o.TargetId == m.TargetId),

                // A placeholder that something already supersedes is finished work, and hiding it
                // would make a half-authored subject look untouched.
                IsSuperseded = _dbContext.LearningTargetEdges.Any(
                    e => e.FromTargetId == m.TargetId
                         && e.EdgeKind == LearningTargetEdgeKind.SupersededBy)
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .Select(r => new PlaceholderTargetDto
                {
                    TargetId = r.TargetId,
                    Statement = r.Statement ?? string.Empty,
                    NodeId = r.NodeId,
                    NodeTitle = r.NodeTitle,
                    ItemCount = r.ItemCount,
                    ObservationCount = r.ObservationCount,
                    IsSuperseded = r.IsSuperseded
                })
                .OrderBy(r => r.IsSuperseded)
                .ThenByDescending(r => r.ObservationCount)
                .ThenBy(r => r.Statement)
        ];
    }

    public async Task<TargetPromotionReport> PromoteAsync(
        PromoteTargetRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ReplacesTargetIds.Count == 0)
            throw new InvalidOperationException("A promotion must name at least one placeholder to replace.");

        if (request.Statements.Count == 0 && request.UseExistingTargetId is null)
            throw new InvalidOperationException("A learning target with no statement is not a claim.");

        var placeholderIds = request.ReplacesTargetIds.Distinct().ToList();

        var placeholders = await _dbContext.LearningTargets
            .Where(t => placeholderIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        if (placeholders.Count != placeholderIds.Count)
            throw new InvalidOperationException("One or more of the named targets does not exist.");

        if (placeholders.Any(t => !t.IsPlaceholder))
            throw new InvalidOperationException(
                "Only lesson placeholders may be superseded this way. An authored target changes by "
                + "publishing a successor, because a published claim's meaning is immutable (ADR-E18).");

        var now = DateTime.UtcNow;
        var frameworkId = request.IntoFrameworkId ?? AssessmentIds.Share7CoreFramework;

        LearningTarget target;

        if (request.UseExistingTargetId is { } chosenId)
        {
            // The official outcomes are already imported and one of them IS the claim. Nothing is
            // minted and nothing is reworded: the stand-ins' questions simply move onto it.
            target = await _dbContext.LearningTargets.FirstOrDefaultAsync(t => t.Id == chosenId, cancellationToken)
                     ?? throw new InvalidOperationException("The skill to move onto does not exist.");

            if (target.IsPlaceholder)
                throw new InvalidOperationException("A stand-in cannot replace another stand-in.");
        }
        else
        {
            var existing = await _dbContext.LearningTargets
                .FirstOrDefaultAsync(
                    t => t.FrameworkId == frameworkId && t.TargetKey == request.TargetKey,
                    cancellationToken);

            target = existing ?? new LearningTarget
            {
                Id = Guid.NewGuid(),
                FrameworkId = frameworkId,
                TargetKey = request.TargetKey,
                CreatedAtUtc = now
            };

            target.TargetKindKey = request.TargetKindKey;
            target.IsPlaceholder = false;
            target.DifficultyBand = request.DifficultyBand;
            target.ReviewState = request.MarkReviewed ? TargetReviewState.Reviewed : TargetReviewState.Unreviewed;

            if (existing is null) _dbContext.LearningTargets.Add(target);

            await WriteStatementsAsync(target.Id, request.Statements, cancellationToken);
        }

        // The supersession edges, written before the mappings move so that the old claim stays
        // traceable whatever happens next. A measurement taken against a placeholder last month was
        // a real measurement; the row that records it still names that target and it still has to
        // resolve to something a reader can follow forward.
        var edges = await _dbContext.LearningTargetEdges
            .Where(e => placeholderIds.Contains(e.FromTargetId)
                        && e.EdgeKind == LearningTargetEdgeKind.SupersededBy)
            .ToListAsync(cancellationToken);

        foreach (var placeholder in placeholders)
        {
            if (!edges.Any(e => e.FromTargetId == placeholder.Id && e.ToTargetId == target.Id))
            {
                _dbContext.LearningTargetEdges.Add(new LearningTargetEdge
                {
                    Id = Guid.NewGuid(),
                    FromTargetId = placeholder.Id,
                    ToTargetId = target.Id,
                    EdgeKind = LearningTargetEdgeKind.SupersededBy,
                    CreatedAtUtc = now
                });
            }

            // Deprecated, never deleted, and never retired either: a retired target is filtered out
            // of the projector's mapping load, and history has to keep resolving.
            placeholder.ReviewState = TargetReviewState.Deprecated;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var (itemsMoved, affectedItemIds) = await MoveItemMappingsAsync(
            placeholderIds, target.Id, now, cancellationToken);

        var nodesMoved = await MoveNodeMappingsAsync(placeholderIds, target.Id, now, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // **The point of the whole exercise.** Every historical answer to every moved item is
        // re-interpreted against the real claim, from the immutable response log, without anybody
        // replaying a lesson. In a system that stored progress as current state, this step does not
        // exist because the evidence it needs was overwritten when it was recorded.
        var reprojection = await _projector.ReprojectItemsAsync(affectedItemIds, cancellationToken);

        _logger.LogInformation(
            "Promoted {Key}: {Placeholders} placeholders superseded, {Items} item mappings moved, "
            + "{Observations} observations rebuilt for {Learners} learners.",
            request.TargetKey, placeholders.Count, itemsMoved,
            reprojection.ObservationsWritten, reprojection.LearnersAffected);

        return new TargetPromotionReport
        {
            TargetId = target.Id,
            TargetKey = target.TargetKey,
            PlaceholdersSuperseded = placeholders.Count,
            ItemMappingsMoved = itemsMoved,
            NodeMappingsMoved = nodesMoved,
            ObservationsRebuilt = reprojection.ObservationsWritten,
            ExclusionsPreserved = reprojection.ExclusionsPreserved
        };
    }

    /// <summary>
    /// Repoints every item mapping from the placeholders onto the authored target, and returns the
    /// items whose evidence now has to be rebuilt.
    /// <para>
    /// Repointed in place rather than deleted and re-added, so an item's primary flag and emphasis
    /// survive. The one case that needs care is an item mapped to two placeholders that both
    /// promote to the same target: the second mapping would collide with the unique
    /// <c>(ItemId, TargetId)</c> index, so it is dropped rather than moved — the claim is already
    /// there and counting it twice would double that item's weight in every measurement.
    /// </para>
    /// </summary>
    private async Task<(int Moved, List<Guid> ItemIds)> MoveItemMappingsAsync(
        List<Guid> placeholderIds, Guid targetId, DateTime now, CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.ItemTargetMappings
            .Where(m => placeholderIds.Contains(m.TargetId))
            .ToListAsync(cancellationToken);

        if (mappings.Count == 0) return (0, []);

        var itemIds = mappings.Select(m => m.ItemId).Distinct().ToList();

        var alreadyThere = await _dbContext.ItemTargetMappings
            .Where(m => m.TargetId == targetId && itemIds.Contains(m.ItemId))
            .Select(m => m.ItemId)
            .ToListAsync(cancellationToken);

        var claimed = alreadyThere.ToHashSet();
        var moved = 0;

        foreach (var mapping in mappings)
        {
            if (!claimed.Add(mapping.ItemId))
            {
                _dbContext.ItemTargetMappings.Remove(mapping);
                continue;
            }

            mapping.TargetId = targetId;
            mapping.CreatedAtUtc = now;
            moved++;
        }

        return (moved, itemIds);
    }

    /// <summary>
    /// Moves "this lesson teaches this" onto the authored target. Same collision rule as the item
    /// mappings, for the same reason.
    /// </summary>
    private async Task<int> MoveNodeMappingsAsync(
        List<Guid> placeholderIds, Guid targetId, DateTime now, CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.NodeTargetMappings
            .Where(m => placeholderIds.Contains(m.TargetId))
            .ToListAsync(cancellationToken);

        if (mappings.Count == 0) return 0;

        var nodeIds = mappings.Select(m => m.NodeId).Distinct().ToList();

        var claimed = (await _dbContext.NodeTargetMappings
            .Where(m => m.TargetId == targetId && nodeIds.Contains(m.NodeId))
            .Select(m => m.NodeId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var moved = 0;

        foreach (var mapping in mappings)
        {
            if (!claimed.Add(mapping.NodeId))
            {
                _dbContext.NodeTargetMappings.Remove(mapping);
                continue;
            }

            mapping.TargetId = targetId;
            mapping.CreatedAtUtc = now;
            moved++;
        }

        return moved;
    }

    private async Task WriteStatementsAsync(
        Guid targetId, IReadOnlyDictionary<Guid, string> statements, CancellationToken cancellationToken)
    {
        var langIds = statements.Keys.ToList();

        var existing = await _dbContext.LearningTargetTranslations
            .Where(t => t.TargetId == targetId && langIds.Contains(t.LangId))
            .ToDictionaryAsync(t => t.LangId, cancellationToken);

        foreach (var (langId, statement) in statements)
        {
            if (existing.TryGetValue(langId, out var row))
            {
                row.Statement = statement;
                continue;
            }

            _dbContext.LearningTargetTranslations.Add(new LearningTargetTranslation
            {
                TargetId = targetId,
                LangId = langId,
                Statement = statement
            });
        }
    }

    public async Task<bool> SetReviewStateAsync(
        Guid targetId, TargetReviewState state, CancellationToken cancellationToken = default)
    {
        var target = await _dbContext.LearningTargets
            .FirstOrDefaultAsync(t => t.Id == targetId, cancellationToken);

        if (target is null) return false;

        target.ReviewState = state;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AuthoredTargetDto>> ListAuthoredAsync(
        Guid frameworkId, Guid langId, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.LearningTargets
            .AsNoTracking()
            .Where(t => t.FrameworkId == frameworkId && !t.IsPlaceholder && t.RetiredAtUtc == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t =>
                t.TargetKey.Contains(term)
                || t.Translations.Any(x => x.Statement.Contains(term)));
        }

        var rows = await query
            .Select(t => new AuthoredTargetDto
            {
                TargetId = t.Id,
                TargetKey = t.TargetKey,
                Statement = t.Translations
                    .Where(x => x.LangId == langId)
                    .Select(x => x.Statement)
                    .FirstOrDefault() ?? string.Empty,
                TargetKindKey = t.TargetKindKey,
                ReviewState = t.ReviewState,
                DifficultyBand = t.DifficultyBand,
                ItemCount = _dbContext.ItemTargetMappings.Count(m => m.TargetId == t.Id),
                SupersededPlaceholders = _dbContext.LearningTargetEdges.Count(
                    e => e.ToTargetId == t.Id && e.EdgeKind == LearningTargetEdgeKind.SupersededBy)
            })
            .OrderBy(t => t.TargetKey)
            .Take(500)
            .ToListAsync(cancellationToken);

        return rows;
    }
}
