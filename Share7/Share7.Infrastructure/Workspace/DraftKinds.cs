using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Recovery.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Content;
using Share7.Domain.Recovery;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Workspace;

/// <summary>The live state a reorder draft started from.</summary>
public sealed record ReorderBase(Guid ParentId, int Revision, IReadOnlyList<Guid> Children);

/// <summary>
/// Everything that differs between the seven kinds of draft: what the live state is, what "has not
/// moved" means, what the default proposal is, what makes a proposal publishable, which languages a
/// change touches and how to show it as a diff. The services above this are the same for every kind.
/// </summary>
public sealed class DraftKinds
{
    private readonly ApplicationDbContext _db;
    private readonly ILessonContentReader _reader;
    private readonly ILessonContentPublisher _publisher;
    private readonly IContentLanguages _languages;
    private readonly ICurriculumStructureService _structure;
    private readonly IRecoveryRuleReader _recovery;

    public DraftKinds(
        ApplicationDbContext db,
        ILessonContentReader reader,
        ILessonContentPublisher publisher,
        IContentLanguages languages,
        ICurriculumStructureService structure,
        IRecoveryRuleReader recovery)
    {
        _db = db;
        _reader = reader;
        _publisher = publisher;
        _languages = languages;
        _structure = structure;
        _recovery = recovery;
    }

    // =====================================================================================
    // The live state
    // =====================================================================================

    /// <summary>What is live for a draft's target now, as the draft's base would record it.</summary>
    public async Task<(string BaseJson, string Fingerprint)> LiveAsync(Draft draft, CancellationToken cancellationToken)
    {
        if (draft.IsPractice)
            return ("{}", string.Empty);

        switch (draft.Kind)
        {
            case DraftKind.LessonContent:
            {
                var content = await _reader.ReadAsync(draft.NodeId!.Value, cancellationToken);
                return content is null
                    ? ("{}", "gone")
                    : (WorkspaceJson.Write(content), ContentFingerprint(content.Sets.Select(s => (s.Role, s.LangId, s.Version))));
            }

            case DraftKind.NewNode:
                return ("{}", string.Empty);

            case DraftKind.Reorder:
            {
                var parent = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                if (parent is null) return ("{}", "gone");

                var children = await LiveChildrenAsync(parent.Id, cancellationToken);
                return (WorkspaceJson.Write(new ReorderBase(parent.Id, parent.Revision, children)), $"rev:{parent.Revision}");
            }

            case DraftKind.Restore:
            {
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                return node is null
                    ? ("{}", "gone")
                    : (WorkspaceJson.Write(node), node.RetiredAtUtc is { } at ? $"retired:{at.Ticks}" : "live");
            }

            case DraftKind.RecoveryRule:
            {
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                if (node is null) return ("{}", "gone");

                // The base is what is in force NOW, inherited or written here — not only what is
                // written at this node. A draft proposing "after 2 wrong answers" against an
                // inherited 2 proposes nothing, and the board has to be able to say so.
                var rule = await _recovery.InForceAsync(node.Id, cancellationToken);
                var from = rule.FromNodeId is { } fromId && fromId != node.Id
                    ? await _structure.GetAsync(fromId, cancellationToken)
                    : null;

                var state = new RecoveryRuleBase(
                    rule.AfterWrongAnswers,
                    rule.QuestionsToServe,
                    rule.AllowRepeats,
                    rule.IsOwn,
                    rule.FromNodeId,
                    from?.Titles.FirstOrDefault()?.Title,
                    node.Revision);

                return (WorkspaceJson.Write(state), rule.Fingerprint);
            }

            default:
            {
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                return node is null ? ("{}", "gone") : (WorkspaceJson.Write(node), $"rev:{node.Revision}");
            }
        }
    }

    public async Task<bool> IsOutOfDateAsync(Draft draft, CancellationToken cancellationToken)
    {
        if (draft.IsPractice || draft.Kind == DraftKind.NewNode || !draft.IsOpen)
            return false;

        var (_, fingerprint) = await LiveAsync(draft, cancellationToken);
        return fingerprint != draft.BaseFingerprint;
    }

    /// <summary>
    /// Which open drafts in a batch are out of date, in two queries rather than one per draft —
    /// what lists and queues use.
    /// </summary>
    public async Task<HashSet<Guid>> OutOfDateAsync(IReadOnlyList<Draft> drafts, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        var open = drafts.Where(d => d.IsOpen && !d.IsPractice && d.Kind != DraftKind.NewNode && d.NodeId is not null).ToList();
        if (open.Count == 0) return result;

        var content = open.Where(d => d.Kind == DraftKind.LessonContent).ToList();
        var lessonIds = content.Select(d => d.NodeId!.Value).Distinct().ToList();

        var sets = lessonIds.Count == 0
            ? []
            : await _db.PublishedItemSets.AsNoTracking()
                .Where(s => lessonIds.Contains(s.NodeId))
                .Select(s => new { s.NodeId, s.Role, s.LangId, s.Version })
                .ToListAsync(cancellationToken);

        foreach (var draft in content)
        {
            var fingerprint = ContentFingerprint(sets.Where(s => s.NodeId == draft.NodeId).Select(s => (s.Role, s.LangId, s.Version)));
            if (fingerprint != draft.BaseFingerprint) result.Add(draft.Id);
        }

        // A recovery rule does not go stale when its node's revision moves — a rename does not
        // change what happens to a child getting things wrong. It goes stale when the RULE it was
        // written against changes, which is what its own fingerprint records. Comparing it against
        // a node revision, as every other structural kind is compared, marks every one of them out
        // of date the moment it is written and a release will not carry it.
        var rules = open.Where(d => d.Kind == DraftKind.RecoveryRule).ToList();

        if (rules.Count > 0)
        {
            var inForce = await _recovery.InForceAsync(rules.Select(d => d.NodeId!.Value).ToList(), cancellationToken);

            foreach (var draft in rules)
            {
                var fingerprint = inForce.TryGetValue(draft.NodeId!.Value, out var rule) ? rule.Fingerprint : "gone";
                if (fingerprint != draft.BaseFingerprint) result.Add(draft.Id);
            }
        }

        var structural = open
            .Where(d => d.Kind != DraftKind.LessonContent && d.Kind != DraftKind.RecoveryRule)
            .ToList();

        var nodeIds = structural.Select(d => d.NodeId!.Value).Distinct().ToList();

        var nodes = nodeIds.Count == 0
            ? []
            : await _db.CurriculumNodes.AsNoTracking()
                .Where(n => nodeIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Revision, n.RetiredAtUtc })
                .ToListAsync(cancellationToken);

        foreach (var draft in structural)
        {
            var node = nodes.FirstOrDefault(n => n.Id == draft.NodeId);
            var fingerprint = node is null ? "gone"
                : draft.Kind == DraftKind.Restore ? (node.RetiredAtUtc is { } at ? $"retired:{at.Ticks}" : "live")
                : $"rev:{node.Revision}";

            if (fingerprint != draft.BaseFingerprint) result.Add(draft.Id);
        }

        return result;
    }

    public static string ContentFingerprint(IEnumerable<(NodeItemRole Role, Guid LangId, int Version)> sets) =>
        string.Join("|", sets.OrderBy(s => s.Role).ThenBy(s => s.LangId).Select(s => $"{(int)s.Role}:{s.LangId:N}:{s.Version}"));

    /// <summary>The set versions a content draft started from, for the publisher's expected-versions check.</summary>
    public static IReadOnlyDictionary<ContentSetKey, int> ExpectedVersions(string fingerprint) =>
        fingerprint.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':'))
            .ToDictionary(
                part => new ContentSetKey((NodeItemRole)int.Parse(part[0]), Guid.Parse(part[1])),
                part => int.Parse(part[2]));

    public async Task<List<Guid>> LiveChildrenAsync(Guid parentId, CancellationToken cancellationToken) =>
        await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.ParentNodeId == parentId && n.RetiredAtUtc == null)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Every (pool, language) a Studio publish covers: both pools, every content language.</summary>
    public async Task<IReadOnlyList<ContentSetKey>> CoversAsync(CancellationToken cancellationToken) =>
        (await _languages.GetAsync(cancellationToken))
            .Where(l => l.IsContentLanguage)
            .SelectMany(l => new[] { new ContentSetKey(NodeItemRole.Core, l.Id), new ContentSetKey(NodeItemRole.Recovery, l.Id) })
            .ToList();

    // =====================================================================================
    // Default proposals
    // =====================================================================================

    /// <summary>What a new draft proposes before anyone has changed anything: the live state as it is.</summary>
    public static string DefaultProposal(DraftKind kind, string baseJson) => kind switch
    {
        DraftKind.LessonContent => WorkspaceJson.Write(new LessonContentProposal(
            (WorkspaceJson.Read<LessonContentDto>(baseJson)?.Items ?? []).Select(ToDraftItem).ToList())),
        DraftKind.NewNode => WorkspaceJson.Write(new NewNodeProposal([], null, null)),
        DraftKind.Rename => WorkspaceJson.Write(new RenameProposal(WorkspaceJson.Read<NodeStateDto>(baseJson)?.Titles ?? [])),
        DraftKind.Move => WorkspaceJson.Write(new MoveProposal(WorkspaceJson.Read<NodeStateDto>(baseJson)?.ParentId ?? Guid.Empty, null)),
        DraftKind.Reorder => WorkspaceJson.Write(new ReorderProposal(WorkspaceJson.Read<ReorderBase>(baseJson)?.Children ?? [])),

        // Opens on whatever is in force here, so the first thing a member sees is the truth rather
        // than the platform's defaults dressed up as somebody's decision.
        DraftKind.RecoveryRule => WorkspaceJson.Write(WorkspaceJson.Read<RecoveryRuleBase>(baseJson) is { } live
            ? new RecoveryRuleProposal(live.AfterWrongAnswers, live.QuestionsToServe, live.AllowRepeats, false)
            : new RecoveryRuleProposal(
                RecoveryDefaults.AfterWrongAnswers, RecoveryDefaults.QuestionsToServe, RecoveryDefaults.AllowRepeats, false)),

        _ => "{}"
    };

    public static ContentDraftItem ToDraftItem(ContentItemDto item) => new()
    {
        ItemId = item.ItemId,
        Role = item.Role,
        Order = item.Order,
        Renderings = item.Renderings
            .Select(r => new ContentDraftRendering(r.LangId, r.Text, r.Choices.Select(c => c.Text).ToList(), r.CorrectIndex))
            .ToList()
    };

    /// <summary>Checks a proposal is the shape its kind needs. Returns the reason when it is not.</summary>
    public static string? Shape(DraftKind kind, JsonElement proposal) => kind switch
    {
        DraftKind.LessonContent => WorkspaceJson.Parse<LessonContentProposal>(proposal) is { Items: not null } ? null : "items",
        DraftKind.NewNode => WorkspaceJson.Parse<NewNodeProposal>(proposal) is { Titles: not null } ? null : "titles",
        DraftKind.Rename => WorkspaceJson.Parse<RenameProposal>(proposal) is { Titles: not null } ? null : "titles",
        DraftKind.Move => WorkspaceJson.Parse<MoveProposal>(proposal) is { NewParentId: var p } && p != Guid.Empty ? null : "newParentId",
        DraftKind.Reorder => WorkspaceJson.Parse<ReorderProposal>(proposal) is { OrderedChildIds: not null } ? null : "orderedChildIds",
        DraftKind.RecoveryRule => WorkspaceJson.Parse<RecoveryRuleProposal>(proposal) is not null ? null : "recoveryRule",
        _ => null
    };

    // =====================================================================================
    // Checks — what would stop it being submitted or released
    // =====================================================================================

    public async Task<IReadOnlyList<ContentProblem>> CheckAsync(Draft draft, CancellationToken cancellationToken)
    {
        var covers = await CoversAsync(cancellationToken);

        switch (draft.Kind)
        {
            case DraftKind.LessonContent:
            {
                var proposal = WorkspaceJson.Read<LessonContentProposal>(draft.ProposedJson) ?? new([]);
                var target = draft.NodeId ?? Guid.Empty;
                return await _publisher.CheckAsync(target, proposal.Items, covers, ContentRuleSet.Studio, cancellationToken);
            }

            case DraftKind.NewNode:
            {
                var proposal = WorkspaceJson.Read<NewNodeProposal>(draft.ProposedJson) ?? new([], null, null);
                var problems = new List<ContentProblem>();

                problems.AddRange(await TitleProblemsAsync(draft.NodeKind ?? NodeKinds.Lesson, proposal.Titles, cancellationToken));
                if (draft.ParentNodeId is { } parentId)
                    problems.AddRange(await NameClashesAsync(parentId, null, proposal.Titles, cancellationToken));

                if (draft.NodeKind == NodeKinds.Lesson && proposal.Items is { Count: > 0 } items)
                    problems.AddRange(await _publisher.CheckAsync(draft.NodeId!.Value, items, covers, ContentRuleSet.Studio, cancellationToken));

                return problems;
            }

            case DraftKind.Rename:
            {
                var proposal = WorkspaceJson.Read<RenameProposal>(draft.ProposedJson) ?? new([]);
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                var problems = new List<ContentProblem>();

                problems.AddRange(await TitleProblemsAsync(node?.Kind ?? NodeKinds.Lesson, proposal.Titles, cancellationToken));
                if (node?.ParentId is { } parentId)
                    problems.AddRange(await NameClashesAsync(parentId, node.Id, proposal.Titles, cancellationToken));

                return problems;
            }

            case DraftKind.Move:
            {
                var proposal = WorkspaceJson.Read<MoveProposal>(draft.ProposedJson);
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                var target = proposal is null ? null : await _structure.GetAsync(proposal.NewParentId, cancellationToken);

                if (node is null || target is null)
                    return [new("parentMissing", "Choose where it moves to.", Field: "parent")];
                if (target.Kind != NodeKinds.ParentOf(node.Kind))
                    return [new("wrongParent", $"A {node.Kind} goes under a {NodeKinds.ParentOf(node.Kind)}.", Field: "parent")];
                if (target.RetiredAtUtc is not null)
                    return [new("parentRetired", "That place is retired.", Field: "parent")];
                if (target.Id == node.ParentId && proposal!.Position is null)
                    return [new("notMoved", "It is already there.", Field: "parent")];

                return await NameClashesAsync(target.Id, node.Id, node.Titles, cancellationToken);
            }

            case DraftKind.Reorder:
            {
                var proposal = WorkspaceJson.Read<ReorderProposal>(draft.ProposedJson) ?? new([]);
                var live = await LiveChildrenAsync(draft.NodeId!.Value, cancellationToken);

                return live.Count == proposal.OrderedChildIds.Count && !live.Except(proposal.OrderedChildIds).Any()
                    ? []
                    : [new ContentProblem("childrenChanged", "Something was added or removed here since; start the new order again.", Field: "order")];
            }

            case DraftKind.Retire:
            {
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                return node is { RetiredAtUtc: null } ? [] : [new ContentProblem("alreadyRetired", "It is already retired.")];
            }

            case DraftKind.Restore:
            {
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                if (node is not { RetiredAtUtc: not null })
                    return [new ContentProblem("notRetired", "It is not retired.")];

                var parent = node.ParentId is { } parentId ? await _structure.GetAsync(parentId, cancellationToken) : null;
                return parent is { RetiredAtUtc: null } ? [] : [new ContentProblem("parentRetired", "Its parent is retired; restore that first.")];
            }

            case DraftKind.RecoveryRule:
            {
                var node = await _structure.GetAsync(draft.NodeId!.Value, cancellationToken);
                if (node is not { RetiredAtUtc: null })
                    return [new ContentProblem("nodeRetired", "It has been taken out, so a rule on it would never be read.")];

                var proposal = WorkspaceJson.Read<RecoveryRuleProposal>(draft.ProposedJson);
                if (proposal is null)
                    return [new ContentProblem("noRule", "Nothing has been proposed yet.")];

                var problems = new List<ContentProblem>();

                // Taking a rule away is a complete proposal on its own: the numbers beside it are
                // whatever the form last held, and checking them would refuse a rule being removed.
                if (!proposal.Clear)
                {
                    if (proposal.AfterWrongAnswers < RecoveryDefaults.MinAfterWrongAnswers
                        || proposal.AfterWrongAnswers > RecoveryDefaults.MaxAfterWrongAnswers)
                        problems.Add(new ContentProblem(
                            "afterWrongAnswers",
                            $"The number of wrong answers has to be between {RecoveryDefaults.MinAfterWrongAnswers} and {RecoveryDefaults.MaxAfterWrongAnswers}.",
                            Field: "afterWrongAnswers"));

                    if (proposal.QuestionsToServe < RecoveryDefaults.MinQuestionsToServe
                        || proposal.QuestionsToServe > RecoveryDefaults.MaxQuestionsToServe)
                        problems.Add(new ContentProblem(
                            "questionsToServe",
                            $"The number of second-chance questions has to be between {RecoveryDefaults.MinQuestionsToServe} and {RecoveryDefaults.MaxQuestionsToServe}.",
                            Field: "questionsToServe"));
                }

                var live = WorkspaceJson.Read<RecoveryRuleBase>(draft.BaseJson);

                // A rule identical to what already happens here is not a change, and a release
                // entry that changes nothing is a lie in the permanent record.
                if (live is not null && problems.Count == 0)
                {
                    var same = proposal.Clear
                        ? !live.IsOwn
                        : live.IsOwn
                          && live.AfterWrongAnswers == proposal.AfterWrongAnswers
                          && live.QuestionsToServe == proposal.QuestionsToServe
                          && live.AllowRepeats == proposal.AllowRepeats;

                    if (same)
                        problems.Add(new ContentProblem(
                            "noChange",
                            proposal.Clear
                                ? "There is no rule written here to take away."
                                : "This is what already happens here. Change something, or throw the draft away."));
                }

                return problems;
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<ContentProblem>> TitleProblemsAsync(
        string kind, IReadOnlyList<NodeTitle> titles, CancellationToken cancellationToken)
    {
        var languages = await _languages.GetAsync(cancellationToken);
        var max = kind == NodeKinds.Term ? CurriculumStructureService.TermTitleMaxLength : CurriculumStructureService.TitleMaxLength;
        var problems = new List<ContentProblem>();

        foreach (var required in languages.Where(l => l.IsContentLanguage && l.RequiredToPublish))
        {
            var title = titles.FirstOrDefault(t => t.LangId == required.Id)?.Title?.Trim() ?? string.Empty;
            if (title.Length == 0)
                problems.Add(new("titleMissing", $"A name in {required.Code.ToUpperInvariant()} is needed.", LangId: required.Id, Field: "title"));
        }

        foreach (var title in titles.Where(t => (t.Title ?? string.Empty).Trim().Length > max))
            problems.Add(new("titleTooLong", $"Names are at most {max} characters.", LangId: title.LangId, Field: "title"));

        return problems;
    }

    private async Task<IReadOnlyList<ContentProblem>> NameClashesAsync(
        Guid parentId, Guid? self, IReadOnlyList<NodeTitle> titles, CancellationToken cancellationToken)
    {
        var siblings = await _db.CurriculumNodeTranslations.AsNoTracking()
            .Where(t => t.Node!.ParentNodeId == parentId && t.Node.RetiredAtUtc == null && t.NodeId != self)
            .Select(t => new { t.LangId, t.Title })
            .ToListAsync(cancellationToken);

        return titles
            .Where(t => siblings.Any(s => s.LangId == t.LangId && string.Equals(s.Title, t.Title?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(t => new ContentProblem("nameTaken", $"Another item here is already called '{t.Title}'.", LangId: t.LangId, Field: "title"))
            .ToList();
    }

    // =====================================================================================
    // Languages touched, and the diff
    // =====================================================================================

    /// <summary>
    /// The languages a change touches, comparing <paramref name="fromJson"/> to <paramref name="toJson"/>
    /// (both proposals of <paramref name="kind"/>, or a base and a proposal). A question added,
    /// removed or moved touches every language it is written in; a reworded one only its own.
    /// </summary>
    public static IReadOnlyList<Guid> LanguagesTouched(DraftKind kind, string fromJson, string toJson, bool fromIsBase)
    {
        switch (kind)
        {
            case DraftKind.LessonContent:
            {
                var before = fromIsBase
                    ? (WorkspaceJson.Read<LessonContentDto>(fromJson)?.Items ?? []).Select(ToDraftItem).ToList()
                    : (WorkspaceJson.Read<LessonContentProposal>(fromJson)?.Items ?? []).ToList();
                var after = (WorkspaceJson.Read<LessonContentProposal>(toJson)?.Items ?? []).ToList();
                return ContentLanguagesTouched(before, after);
            }

            case DraftKind.NewNode:
            {
                var before = fromIsBase ? new NewNodeProposal([], null, null) : WorkspaceJson.Read<NewNodeProposal>(fromJson) ?? new([], null, null);
                var after = WorkspaceJson.Read<NewNodeProposal>(toJson) ?? new([], null, null);

                return TitlesTouched(before.Titles, after.Titles)
                    .Concat(ContentLanguagesTouched(before.Items ?? [], after.Items ?? []))
                    .Distinct()
                    .ToList();
            }

            case DraftKind.Rename:
            {
                var before = fromIsBase
                    ? WorkspaceJson.Read<NodeStateDto>(fromJson)?.Titles ?? []
                    : WorkspaceJson.Read<RenameProposal>(fromJson)?.Titles ?? [];
                var after = WorkspaceJson.Read<RenameProposal>(toJson)?.Titles ?? [];
                return TitlesTouched(before, after);
            }

            // A recovery rule is numbers. It is written in no language and touches none, which is
            // why a reviewer scoped to one language may still review one.
            default:
                return [];
        }
    }

    private static IReadOnlyList<Guid> TitlesTouched(IReadOnlyList<NodeTitle> before, IReadOnlyList<NodeTitle> after) =>
        before.Select(t => t.LangId).Union(after.Select(t => t.LangId))
            .Where(lang => before.FirstOrDefault(t => t.LangId == lang)?.Title?.Trim() != after.FirstOrDefault(t => t.LangId == lang)?.Title?.Trim())
            .ToList();

    private static IReadOnlyList<Guid> ContentLanguagesTouched(IReadOnlyList<ContentDraftItem> before, IReadOnlyList<ContentDraftItem> after)
    {
        var touched = new HashSet<Guid>();
        var beforeById = before.Where(i => i.ItemId is not null).ToDictionary(i => i.ItemId!.Value);
        var matched = new HashSet<Guid>();

        foreach (var item in after)
        {
            if (item.ItemId is not { } id || !beforeById.TryGetValue(id, out var old))
            {
                touched.UnionWith(item.Renderings.Select(r => r.LangId));
                continue;
            }

            matched.Add(id);
            var langs = item.Renderings.Select(r => r.LangId).Union(old.Renderings.Select(r => r.LangId));

            if (old.Order != item.Order || old.Role != item.Role)
            {
                touched.UnionWith(langs);
                continue;
            }

            foreach (var lang in langs)
            {
                if (!SameRendering(old.In(lang), item.In(lang)))
                    touched.Add(lang);
            }
        }

        foreach (var removed in before.Where(i => i.ItemId is { } id && !matched.Contains(id)))
            touched.UnionWith(removed.Renderings.Select(r => r.LangId));

        // A question with no id on both sides (a draft's new question, edited again) is compared by
        // position, which is the only identity it has yet.
        foreach (var fresh in after.Where(i => i.ItemId is null))
        {
            var twin = before.FirstOrDefault(b => b.ItemId is null && b.Role == fresh.Role && b.Order == fresh.Order);
            if (twin is null) continue;

            foreach (var lang in fresh.Renderings.Select(r => r.LangId).Union(twin.Renderings.Select(r => r.LangId)))
                if (SameRendering(twin.In(lang), fresh.In(lang))) touched.Remove(lang);
        }

        return touched.ToList();
    }

    private static bool SameRendering(ContentDraftRendering? a, ContentDraftRendering? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Text?.Trim() == b.Text?.Trim()
               && a.CorrectIndex == b.CorrectIndex
               && a.Choices.Select(c => c?.Trim()).SequenceEqual(b.Choices.Select(c => c?.Trim()));
    }

    /// <summary>The draft's change against its base, line by line.</summary>
    public static IReadOnlyList<DiffLineDto> Diff(Draft draft)
    {
        switch (draft.Kind)
        {
            case DraftKind.LessonContent:
            {
                var before = (WorkspaceJson.Read<LessonContentDto>(draft.BaseJson)?.Items ?? []).Select(ToDraftItem).ToList();
                var after = (WorkspaceJson.Read<LessonContentProposal>(draft.ProposedJson)?.Items ?? []).ToList();
                return ContentDiff(before, after);
            }

            case DraftKind.NewNode:
            {
                var proposal = WorkspaceJson.Read<NewNodeProposal>(draft.ProposedJson) ?? new([], null, null);
                var lines = proposal.Titles.Select(t => new DiffLineDto("added", "title", null, null, null, t.LangId, null, t.Title)).ToList();
                lines.AddRange(ContentDiff([], proposal.Items ?? []));
                return lines;
            }

            case DraftKind.Rename:
            {
                var before = WorkspaceJson.Read<NodeStateDto>(draft.BaseJson)?.Titles ?? [];
                var after = WorkspaceJson.Read<RenameProposal>(draft.ProposedJson)?.Titles ?? [];

                return before.Select(t => t.LangId).Union(after.Select(t => t.LangId))
                    .Select(lang => (lang, was: before.FirstOrDefault(t => t.LangId == lang)?.Title, now: after.FirstOrDefault(t => t.LangId == lang)?.Title))
                    .Where(x => x.was != x.now)
                    .Select(x => new DiffLineDto(x.was is null ? "added" : x.now is null ? "removed" : "changed", "title", null, null, null, x.lang, x.was, x.now))
                    .ToList();
            }

            case DraftKind.Move:
            {
                var before = WorkspaceJson.Read<NodeStateDto>(draft.BaseJson);
                var after = WorkspaceJson.Read<MoveProposal>(draft.ProposedJson);
                return
                [
                    new("changed", "parent", null, null, null, null, before?.ParentId?.ToString(), after?.NewParentId.ToString()),
                    new("changed", "position", null, null, null, null, before?.Order.ToString(), after?.Position?.ToString() ?? "last")
                ];
            }

            case DraftKind.Reorder:
            {
                var before = WorkspaceJson.Read<ReorderBase>(draft.BaseJson)?.Children ?? [];
                var after = WorkspaceJson.Read<ReorderProposal>(draft.ProposedJson)?.OrderedChildIds ?? [];
                return [new("changed", "order", null, null, null, null, string.Join(",", before), string.Join(",", after))];
            }

            case DraftKind.RecoveryRule:
            {
                var was = WorkspaceJson.Read<RecoveryRuleBase>(draft.BaseJson);
                var now = WorkspaceJson.Read<RecoveryRuleProposal>(draft.ProposedJson);
                if (now is null) return [];

                if (now.Clear)
                    return [new("removed", "recoveryRule", null, null, null, null,
                        Rule(was?.AfterWrongAnswers, was?.QuestionsToServe, was?.AllowRepeats), null)];

                var lines = new List<DiffLineDto>();
                if (was?.AfterWrongAnswers != now.AfterWrongAnswers)
                    lines.Add(new("changed", "afterWrongAnswers", null, null, null, null, was?.AfterWrongAnswers.ToString(), now.AfterWrongAnswers.ToString()));
                if (was?.QuestionsToServe != now.QuestionsToServe)
                    lines.Add(new("changed", "questionsToServe", null, null, null, null, was?.QuestionsToServe.ToString(), now.QuestionsToServe.ToString()));
                if (was?.AllowRepeats != now.AllowRepeats)
                    lines.Add(new("changed", "allowRepeats", null, null, null, null, was?.AllowRepeats.ToString(), now.AllowRepeats.ToString()));

                // The numbers can match what is in force and the rule still be a change: it is
                // being written HERE rather than inherited, which is what stops a later change
                // above from moving it.
                if (lines.Count == 0 && was is { IsOwn: false })
                    lines.Add(new("added", "recoveryRule", null, null, null, null, null,
                        Rule(now.AfterWrongAnswers, now.QuestionsToServe, now.AllowRepeats)));

                return lines;
            }

            case DraftKind.Retire:
                return [new("changed", "state", null, null, null, null, "live", "retired")];

            case DraftKind.Restore:
                return [new("changed", "state", null, null, null, null, "retired", "live")];
        }

        return [];
    }

    /// <summary>A rule as one readable line, for a diff showing it added or taken away.</summary>
    private static string? Rule(int? afterWrong, int? toServe, bool? allowRepeats) =>
        afterWrong is null || toServe is null
            ? null
            : $"{afterWrong} wrong, {toServe} second-chance{(allowRepeats is true ? ", repeats allowed" : "")}";

    private static IReadOnlyList<DiffLineDto> ContentDiff(IReadOnlyList<ContentDraftItem> before, IReadOnlyList<ContentDraftItem> after)
    {
        var lines = new List<DiffLineDto>();
        var beforeById = before.Where(i => i.ItemId is not null).ToDictionary(i => i.ItemId!.Value);
        var seen = new HashSet<Guid>();

        foreach (var item in after.OrderBy(i => i.Role).ThenBy(i => i.Order))
        {
            ContentDraftItem? old = item.ItemId is { } id && beforeById.TryGetValue(id, out var found) ? found : null;
            if (old?.ItemId is { } oldId) seen.Add(oldId);

            if (old is not null && (old.Order != item.Order || old.Role != item.Role))
                lines.Add(new("moved", "question", item.ItemId, item.Role, item.Order, null, $"{old.Role}:{old.Order}", $"{item.Role}:{item.Order}"));

            foreach (var lang in item.Renderings.Select(r => r.LangId).Union(old?.Renderings.Select(r => r.LangId) ?? []))
            {
                var was = old?.In(lang);
                var now = item.In(lang);
                if (SameRendering(was, now)) continue;

                lines.Add(new(
                    was is null ? "added" : now is null ? "removed" : "changed",
                    "question", item.ItemId, item.Role, item.Order, lang, Describe(was), Describe(now)));
            }
        }

        foreach (var removed in before.Where(i => i.ItemId is { } id && !seen.Contains(id)))
            foreach (var rendering in removed.Renderings)
                lines.Add(new("removed", "question", removed.ItemId, removed.Role, removed.Order, rendering.LangId, Describe(rendering), null));

        return lines;
    }

    /// <summary>A rendering on one line: the question, then the right answer, then the others.</summary>
    private static string? Describe(ContentDraftRendering? rendering)
    {
        if (rendering is null) return null;

        var correct = rendering.CorrectIndex >= 0 && rendering.CorrectIndex < rendering.Choices.Count
            ? rendering.Choices[rendering.CorrectIndex]
            : "?";
        var others = rendering.Choices.Where((_, i) => i != rendering.CorrectIndex);
        return $"{rendering.Text} ✓ {correct} ✗ {string.Join(" ✗ ", others)}";
    }
}
