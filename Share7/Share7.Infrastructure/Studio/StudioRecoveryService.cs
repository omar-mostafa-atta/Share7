using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Recovery.Interfaces;
using Share7.Application.Studio.Interfaces;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Models;
using Share7.Domain.Content;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Workspace;

namespace Share7.Infrastructure.Studio;

/// <inheritdoc cref="IStudioRecoveryService"/>
public sealed class StudioRecoveryService : IStudioRecoveryService
{
    private readonly ApplicationDbContext _db;
    private readonly IRecoveryRuleReader _recovery;
    private readonly NodeTrails _trails;

    public StudioRecoveryService(ApplicationDbContext db, IRecoveryRuleReader recovery, NodeTrails trails)
    {
        _db = db;
        _recovery = recovery;
        _trails = trails;
    }

    public async Task<ServiceResult<RecoveryAtNodeDto>> AtNodeAsync(
        StudioMember member, Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new { n.Id, n.Path, n.KindKey, n.IsPlayable })
            .FirstOrDefaultAsync(cancellationToken);

        if (node is null)
            return ServiceResult<RecoveryAtNodeDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no place with that id.");

        var rule = await _recovery.InForceAsync(nodeId, cancellationToken);

        // Everything playable at or under this node — what a rule written here would govern.
        var lessons = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => (n.Id == nodeId || n.Path.StartsWith(node.Path + "/")) && n.IsPlayable && n.RetiredAtUtc == null)
            .Select(n => new { n.Id, n.Path })
            .ToListAsync(cancellationToken);

        var lessonIds = lessons.Select(l => l.Id).ToList();

        // A lesson already governed by something more specific is not reached by a rule written
        // here, and a board that implies otherwise is the board that gets somebody's change quietly
        // ignored.
        //
        // Asked of the reader rather than counted off the rules table. Counting rows whose NodeId
        // is one of these lessons only finds rules written *at* a lesson — a rule on a chapter in
        // between overrides just as completely, and every lesson under it would have been reported
        // as reachable from here.
        // "Something else governs it" is exactly "the rule it resolves to is not the one that
        // resolves here" — and because these lessons are all under this node, anything else can
        // only be more specific, never less.
        var ownRules = lessonIds.Count == 0
            ? 0
            : (await _recovery.InForceAsync(lessonIds, cancellationToken))
                .Values.Count(r => r.RuleId != rule.RuleId);

        // A rule about second-chance questions means nothing where there are none to serve.
        var withRecovery = lessonIds.Count == 0
            ? []
            : await _db.NodeItemMappings.AsNoTracking()
                .Where(m => lessonIds.Contains(m.NodeId) && m.Role == NodeItemRole.Recovery && m.RemovedAtUtc == null)
                .Select(m => m.NodeId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var draftId = await _db.Drafts.AsNoTracking()
            .Where(d => d.NodeId == nodeId && d.Kind == DraftKind.RecoveryRule && d.IsOpen && !d.IsPractice)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var titles = await _trails.ForAsync(nodeId, cancellationToken);

        string? fromTitle = null;
        if (rule.FromNodeId is { } fromId && fromId != nodeId)
        {
            var from = await _trails.ForAsync(fromId, cancellationToken);
            fromTitle = from.LastOrDefault()?.Titles.FirstOrDefault()?.Title;
        }

        return ServiceResult<RecoveryAtNodeDto>.Success(new RecoveryAtNodeDto
        {
            NodeId = nodeId,
            NodeKind = node.KindKey,
            Title = titles.LastOrDefault()?.Titles.FirstOrDefault()?.Title ?? string.Empty,
            Trail = titles,
            AfterWrongAnswers = rule.AfterWrongAnswers,
            QuestionsToServe = rule.QuestionsToServe,
            AllowRepeats = rule.AllowRepeats,
            IsOwn = rule.IsOwn,
            FromNodeId = rule.IsOwn ? null : rule.FromNodeId,
            FromNodeTitle = fromTitle,
            IsDefault = rule.RuleId is null,
            Lessons = lessons.Count,
            LessonsWithOwnRule = ownRules,
            LessonsWithNoRecoveryQuestions = lessons.Count - withRecovery.Count,
            OpenDraftId = draftId,
            CanPropose = member.IsAtLeast(StudioRole.Author) && member.CoversPath(node.Path)
        });
    }

    public async Task<IReadOnlyList<RecoveryRuleRowDto>> WrittenAsync(
        StudioMember member, Guid langId, CancellationToken cancellationToken = default)
    {
        var rules = await _db.RecoveryRules.AsNoTracking()
            .Where(r => r.IsActive && r.TargetId == null)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.NodeId,
                r.ScopePath,
                r.NodeKind,
                r.AfterWrongAnswers,
                r.QuestionsToServe,
                r.AllowRepeats,
                r.CreatedAtUtc,
                r.ReleaseId
            })
            .ToListAsync(cancellationToken);

        var mine = rules.Where(r => member.CoversPath(r.ScopePath)).ToList();
        if (mine.Count == 0) return [];

        var titles = await _db.CurriculumNodeTranslations.AsNoTracking()
            .Where(t => mine.Select(r => r.NodeId).Contains(t.NodeId))
            .Select(t => new { t.NodeId, t.LangId, t.Title })
            .ToListAsync(cancellationToken);

        return mine.Select(r => new RecoveryRuleRowDto
        {
            RuleId = r.Id,
            NodeId = r.NodeId,
            NodeKind = r.NodeKind,
            Title = titles.FirstOrDefault(t => t.NodeId == r.NodeId && (langId == Guid.Empty || t.LangId == langId))?.Title
                    ?? titles.FirstOrDefault(t => t.NodeId == r.NodeId)?.Title
                    ?? string.Empty,
            AfterWrongAnswers = r.AfterWrongAnswers,
            QuestionsToServe = r.QuestionsToServe,
            AllowRepeats = r.AllowRepeats,
            WrittenAtUtc = r.CreatedAtUtc,
            ReleaseId = r.ReleaseId
        }).ToList();
    }
}
