using Microsoft.EntityFrameworkCore;
using Share7.Application.Recovery.Interfaces;
using Share7.Domain.Recovery;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Recovery;

/// <summary>
/// Most specific wins, resolved off the materialised path.
/// <para>
/// A node's <c>Path</c> is its ancestry as slash-separated ids, so every rule that could govern it
/// is a rule whose own path is a prefix of it — including its own. The longest such path is the
/// most specific rule, and it is used whole. Nothing is merged: a rule inherited from a grade for
/// one number and a subject for another is a rule nobody can predict from looking at either.
/// </para>
/// </summary>
public sealed class RecoveryRuleReader : IRecoveryRuleReader
{
    private readonly ApplicationDbContext _db;

    public RecoveryRuleReader(ApplicationDbContext db) => _db = db;

    public async Task<RecoveryRuleInForce> InForceAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var all = await InForceAsync([nodeId], cancellationToken);
        return all.TryGetValue(nodeId, out var one) ? one : Default(nodeId: null);
    }

    public async Task<IReadOnlyDictionary<Guid, RecoveryRuleInForce>> InForceAsync(
        IReadOnlyList<Guid> nodeIds, CancellationToken cancellationToken = default)
    {
        var wanted = nodeIds.Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, RecoveryRuleInForce>();

        var paths = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => wanted.Contains(n.Id))
            .Select(n => new { n.Id, n.Path })
            .ToListAsync(cancellationToken);

        // Every active rule, ordered longest path first. There is one of these per authored node,
        // not per lesson: a curriculum with a rule on every subject is a few hundred rows, and
        // reading them whole is cheaper than a prefix query per node asked about.
        var rules = await _db.RecoveryRules.AsNoTracking()
            .Where(r => r.IsActive && r.TargetId == null)
            .Select(r => new
            {
                r.Id,
                r.NodeId,
                r.ScopePath,
                r.NodeKind,
                r.AfterWrongAnswers,
                r.QuestionsToServe,
                r.AllowRepeats
            })
            .ToListAsync(cancellationToken);

        var byLength = rules.OrderByDescending(r => r.ScopePath.Length).ToList();
        var answer = new Dictionary<Guid, RecoveryRuleInForce>(paths.Count);

        foreach (var node in paths)
        {
            var winner = byLength.FirstOrDefault(r => Covers(r.ScopePath, node.Path, r.NodeId, node.Id));

            answer[node.Id] = winner is null
                ? Default(node.Id)
                : new RecoveryRuleInForce
                {
                    AfterWrongAnswers = winner.AfterWrongAnswers,
                    QuestionsToServe = winner.QuestionsToServe,
                    AllowRepeats = winner.AllowRepeats,
                    RuleId = winner.Id,
                    FromNodeId = winner.NodeId,
                    FromNodeKind = winner.NodeKind,
                    IsOwn = winner.NodeId == node.Id
                };
        }

        // A node the tree has never heard of still gets an answer, because the caller asked and
        // "I have no rule for that" and "there is no such node" are the same thing to the game.
        foreach (var id in wanted.Where(id => !answer.ContainsKey(id)))
            answer[id] = Default(id);

        return answer;
    }

    /// <summary>
    /// Whether a rule written at one place governs a node. Segment-aware: a path is compared on
    /// whole ids, so a rule at <c>/a/</c> covers <c>/a/b/</c> and a rule at a sibling whose id
    /// merely starts with the same characters covers nothing.
    /// </summary>
    private static bool Covers(string rulePath, string nodePath, Guid ruleNodeId, Guid nodeId)
    {
        if (ruleNodeId == nodeId) return true;
        if (string.IsNullOrEmpty(rulePath) || string.IsNullOrEmpty(nodePath)) return false;

        var prefix = rulePath.EndsWith('/') ? rulePath : rulePath + '/';
        var path = nodePath.EndsWith('/') ? nodePath : nodePath + '/';
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static RecoveryRuleInForce Default(Guid? nodeId) => new()
    {
        AfterWrongAnswers = RecoveryDefaults.AfterWrongAnswers,
        QuestionsToServe = RecoveryDefaults.QuestionsToServe,
        AllowRepeats = RecoveryDefaults.AllowRepeats,
        RuleId = null,
        FromNodeId = null,
        FromNodeKind = null,
        IsOwn = false
    };
}
