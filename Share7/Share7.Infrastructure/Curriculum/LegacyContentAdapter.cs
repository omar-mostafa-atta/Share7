using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Content;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// What the three old authoring paths share now that they publish through the engine: finding
/// which item each of their rows is, and turning an engine refusal back into the errors their
/// endpoints have always returned.
/// </summary>
internal static class LegacyContentAdapter
{
    /// <summary>
    /// The item behind each (pool, row number) the old paths address rows by.
    /// <para>
    /// **Row number is the old paths' lineage**, and it stays theirs: the item currently served at
    /// that row, in any language — which is what makes an English re-upload land on the same items
    /// the Arabic rows are, as it always did — or else the item that row number minted before (a row
    /// removed and put back), or else a new one, keyed so the next publish finds it.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<(NodeItemRole Role, int Row), (Guid? ItemId, string SourceKey)>> ResolveLineageAsync(
        ApplicationDbContext db,
        Guid lessonId,
        IEnumerable<(NodeItemRole Role, int Row)> rows,
        CancellationToken cancellationToken)
    {
        var wanted = rows.Distinct().ToList();
        var current = await LessonContentReader.LoadActiveRowsAsync(db, lessonId, cancellationToken);

        var served = current
            .GroupBy(r => (r.Role, r.RowNumber))
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.LangId).First().ItemId);

        var keys = wanted.ToDictionary(w => w, w => ItemIdentityMinter.SourceKeyFor(lessonId, w.Role, w.Row));
        var lookup = keys.Where(k => !served.ContainsKey(k.Key)).Select(k => k.Value).ToList();

        var byKey = lookup.Count == 0
            ? new Dictionary<string, Guid>()
            : await db.Items
                .Where(i => lookup.Contains(i.SourceKey))
                .ToDictionaryAsync(i => i.SourceKey, i => i.Id, StringComparer.Ordinal, cancellationToken);

        return wanted.ToDictionary(
            w => w,
            w => (served.TryGetValue(w, out var itemId)
                    ? itemId
                    : byKey.TryGetValue(keys[w], out var minted) ? minted : (Guid?)null,
                keys[w]));
    }

    /// <summary>The correct answer first, by contract — the game shuffles before assigning lanes.</summary>
    public static ContentDraftRendering Rendering(Guid langId, string text, string correct, string wrong1, string wrong2) =>
        new(langId, text, [correct, wrong1, wrong2], 0);

    /// <summary>An engine refusal as the list of import errors the old endpoints return.</summary>
    public static IReadOnlyList<QuestionImportError> Errors(ServiceResult result)
    {
        if (result.Details?.GetValueOrDefault("problems") is IEnumerable<ContentProblem> problems)
        {
            return problems
                .Select(p => new QuestionImportError { Row = p.Order ?? 0, Message = p.Message })
                .ToList();
        }

        return result.Errors.Select(e => new QuestionImportError { Message = e }).ToList();
    }

    /// <summary>The paired sheet's two languages, found by code rather than by constant.</summary>
    public static async Task<(Guid English, Guid Arabic)> SheetLanguagesAsync(
        IContentLanguages languages, CancellationToken cancellationToken)
    {
        var all = await languages.GetAsync(cancellationToken);
        return (all.First(l => l.Code == "en").Id, all.First(l => l.Code == "ar").Id);
    }
}
