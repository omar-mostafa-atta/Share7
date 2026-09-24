using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Content;

namespace Share7.Infrastructure.Engine;

/// <summary>
/// What makes a lesson's content publishable, under each of the three rule sets
/// (<see cref="ContentRuleSet"/>).
/// <para>
/// The per-question rules are the ones the importers have always enforced — the same limits as
/// <c>QuestionContentRules</c>, deliberately — so nothing that publishes today is refused tomorrow
/// by the old paths. What differs between rule sets is only what a <i>lesson</i> must contain:
/// which languages, and whether a recovery question.
/// </para>
/// <para>
/// Every problem is reported, each on its own field, so an author fixes everything in one pass.
/// </para>
/// </summary>
internal static class LessonContentRules
{
    public const int MaxQuestionsPerSet = 5000;
    public const int MaxQuestionLength = 1000;
    public const int MaxChoiceLength = 500;
    public const int ChoicesPerQuestion = 3;

    public static List<ContentProblem> Check(
        IReadOnlyList<ContentDraftItem> items,
        IReadOnlyList<ContentSetKey> covers,
        ContentRuleSet rules,
        IReadOnlyList<ContentLanguage> languages,
        IReadOnlySet<Guid> knownItemIds)
    {
        var problems = new List<ContentProblem>();
        var coveredRoles = covers.Select(c => c.Role).ToHashSet();
        var coveredLangs = covers.Select(c => c.LangId).ToHashSet();
        var byId = languages.ToDictionary(l => l.Id);

        foreach (var langId in coveredLangs.Where(id => !byId.ContainsKey(id)))
            problems.Add(new("unknownLanguage", $"Unknown language {langId}.", LangId: langId));

        var inScope = items.Where(i => coveredRoles.Contains(i.Role)).ToList();

        // Languages every item must be written in, per rule set.
        var mustHave = rules switch
        {
            ContentRuleSet.Studio => languages
                .Where(l => l.IsContentLanguage && l.RequiredToPublish && coveredLangs.Contains(l.Id))
                .Select(l => l.Id)
                .ToList(),

            // Restoring what was live puts back exactly what was there, languages included.
            ContentRuleSet.Restore => [],

            // The sheet has a column group per language and a row is refused with any of them blank;
            // a single-language publish exists to render that one language.
            _ => coveredLangs.Where(byId.ContainsKey).ToList()
        };

        foreach (var item in inScope)
        {
            if (item.Order < 1)
                problems.Add(new("positionInvalid", "Positions start at 1.", item.Role, item.Order));

            if (item.ItemId is { } id && !knownItemIds.Contains(id))
                problems.Add(new("unknownItem", "This question does not belong to this lesson.", item.Role, item.Order));

            foreach (var rendering in item.Renderings.Where(r => coveredLangs.Contains(r.LangId)))
                CheckRendering(item, rendering, byId, problems);

            foreach (var langId in mustHave.Where(l => item.In(l) is null))
            {
                problems.Add(new(
                    "languageMissing",
                    $"{Describe(item)} has no {NameOf(byId, langId)} version.",
                    item.Role, item.Order, langId));
            }
        }

        foreach (var clash in inScope.GroupBy(i => (i.Role, i.Order)).Where(g => g.Count() > 1))
        {
            problems.Add(new(
                "duplicatePosition",
                $"{clash.Count()} questions are at position {clash.Key.Order} in the {PoolName(clash.Key.Role)} pool.",
                clash.Key.Role, clash.Key.Order));
        }

        foreach (var twice in inScope.Where(i => i.ItemId is not null).GroupBy(i => i.ItemId).Where(g => g.Count() > 1))
        {
            var first = twice.First();
            problems.Add(new("duplicateItem", "The same question appears more than once.", first.Role, first.Order));
        }

        foreach (var pool in inScope.GroupBy(i => i.Role).Where(g => g.Count() > MaxQuestionsPerSet))
        {
            problems.Add(new(
                "tooManyQuestions",
                $"The {PoolName(pool.Key)} pool has {pool.Count()} questions, above the {MaxQuestionsPerSet} limit.",
                pool.Key));
        }

        var main = inScope.Count(i => i.Role == NodeItemRole.Core);
        var recovery = inScope.Count(i => i.Role == NodeItemRole.Recovery);

        var needsRecovery = rules switch
        {
            // A lesson with main questions and nothing to offer a child who answered wrong.
            ContentRuleSet.Studio => main > 0 && recovery == 0,

            // The sheet's own rule, unchanged: any rows at all need a recovery row among them.
            ContentRuleSet.LessonSheet => inScope.Count > 0 && recovery == 0,
            _ => false
        };

        if (needsRecovery)
        {
            problems.Add(new(
                "recoveryMissing",
                "This lesson would have no recovery questions. Add at least one.",
                NodeItemRole.Recovery));
        }

        return problems;
    }

    private static void CheckRendering(
        ContentDraftItem item,
        ContentDraftRendering rendering,
        Dictionary<Guid, ContentLanguage> languages,
        List<ContentProblem> problems)
    {
        var where = $"{Describe(item)} ({NameOf(languages, rendering.LangId)})";
        var text = rendering.Text ?? string.Empty;

        if (text.Trim().Length == 0)
            problems.Add(new("questionEmpty", $"{where}: the question is empty.", item.Role, item.Order, rendering.LangId, "question"));
        else if (text.Length > MaxQuestionLength)
            problems.Add(new("questionTooLong", $"{where}: the question is {text.Length} characters, above the {MaxQuestionLength} limit.", item.Role, item.Order, rendering.LangId, "question"));

        var choices = rendering.Choices ?? [];

        if (choices.Count != ChoicesPerQuestion)
        {
            problems.Add(new("choiceCount", $"{where}: a question has exactly {ChoicesPerQuestion} answers.", item.Role, item.Order, rendering.LangId));
            return;
        }

        if (rendering.CorrectIndex < 0 || rendering.CorrectIndex >= choices.Count)
            problems.Add(new("correctIndex", $"{where}: no answer is marked correct.", item.Role, item.Order, rendering.LangId));

        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i] ?? string.Empty;
            var field = $"choice{i + 1}";

            if (choice.Trim().Length == 0)
                problems.Add(new("choiceEmpty", $"{where}: answer {i + 1} is empty.", item.Role, item.Order, rendering.LangId, field));
            else if (choice.Length > MaxChoiceLength)
                problems.Add(new("choiceTooLong", $"{where}: answer {i + 1} is {choice.Length} characters, above the {MaxChoiceLength} limit.", item.Role, item.Order, rendering.LangId, field));
        }

        // Case-sensitive on purpose, as the importers have always been: "Fe" and "fe" can be the
        // whole point of a question.
        var present = choices.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (present.Distinct(StringComparer.Ordinal).Count() != present.Count)
            problems.Add(new("choicesNotDifferent", $"{where}: the three answers must be different from each other.", item.Role, item.Order, rendering.LangId));
    }

    private static string Describe(ContentDraftItem item) =>
        $"{(item.Role == NodeItemRole.Recovery ? "Recovery question" : "Question")} {item.Order}";

    private static string PoolName(NodeItemRole role) => role == NodeItemRole.Recovery ? "recovery" : "main";

    private static string NameOf(Dictionary<Guid, ContentLanguage> languages, Guid langId) =>
        languages.TryGetValue(langId, out var language) ? language.Code.ToUpperInvariant() : langId.ToString();
}
