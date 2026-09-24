using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// The one-language paths — a sheet or hand entry for one pool in one language — as an engine
/// publish. Shared by the main-pool and recovery-pool services, which differ only in the pool.
/// <para>
/// **Their rules are theirs until cutover** (decided 22 Sep 2026): one language at a time, no
/// recovery requirement, the other language left exactly as it is. What changes underneath is only
/// what the publisher guarantees — a question whose text did not change keeps its id, and the
/// version moves only when what the game receives changed.
/// </para>
/// </summary>
internal static class SingleLanguagePublish
{
    public static async Task<QuestionImportResult?> ResolveTargetAsync(
        ApplicationDbContext db, Guid lessonId, Guid langId, CancellationToken cancellationToken)
    {
        if (!await db.CurriculumNodes.AnyAsync(
                n => n.Id == lessonId && n.KindKey == NodeKinds.Lesson && n.RetiredAtUtc == null, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Lesson not found.");

        // The lesson is language-independent, so the language cannot be inferred from it — it has
        // to be supplied, and it has to be a real one.
        if (!await db.Languages.AnyAsync(l => l.Id == langId, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Unknown language.");

        return null;
    }

    /// <summary>What one pool serves in one language now, in the shape a hand-entry append carries forward.</summary>
    public static async Task<IReadOnlyList<ExistingQuestion>> LoadActiveAsync(
        ApplicationDbContext db, Guid lessonId, Guid langId, NodeItemRole role, CancellationToken cancellationToken) =>
        await db.ItemLocalizations
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.LangId == langId && q.Role == role && q.IsActive)
            .OrderBy(q => q.RowNumber)
            .Select(q => new ExistingQuestion(
                q.Text,
                q.CorrectChoiceId,
                q.Choices
                    .OrderBy(c => c.OrderIndex)
                    .Select(c => new ExistingChoice(c.Id, c.Text))
                    .ToList()))
            .ToListAsync(cancellationToken);

    public static async Task<QuestionImportResult> PublishAsync(
        ApplicationDbContext db,
        ILessonContentPublisher publisher,
        Guid lessonId,
        Guid langId,
        NodeItemRole role,
        IReadOnlyList<PublishableQuestion> rows,
        QuestionSetSource source,
        string fileName,
        Guid? publishedByUserId,
        CancellationToken cancellationToken)
    {
        var lineage = await LegacyContentAdapter.ResolveLineageAsync(
            db, lessonId, rows.Select(r => (role, r.RowNumber)), cancellationToken);

        var items = rows.Select(row =>
        {
            var (itemId, sourceKey) = lineage[(role, row.RowNumber)];

            return new ContentDraftItem
            {
                ItemId = itemId,
                SourceKeyHint = sourceKey,
                Role = role,
                Order = row.RowNumber,
                Renderings =
                [
                    LegacyContentAdapter.Rendering(langId, row.QuestionText, row.CorrectAnswer, row.WrongAnswer1, row.WrongAnswer2)
                ]
            };
        }).ToList();

        var published = await publisher.PublishAsync(new ContentPublishRequest
        {
            LessonId = lessonId,
            Items = items,
            Covers = [new(role, langId)],
            Rules = ContentRuleSet.SingleLanguage,
            Source = source,
            FileName = QuestionSheetParser.Truncate(fileName, 260),
            ActorUserId = publishedByUserId,
            AuditPath = "single-language"
        }, cancellationToken);

        if (!published.Succeeded)
        {
            return new QuestionImportResult
            {
                Succeeded = false,
                LessonId = lessonId,
                LangId = langId,
                Errors = LegacyContentAdapter.Errors(published)
            };
        }

        var set = published.Value!.For(role, langId);

        return new QuestionImportResult
        {
            Succeeded = true,
            LessonId = lessonId,
            LangId = langId,
            Version = set?.Version ?? 0,
            ImportedCount = rows.Count,
            ReplacedCount = published.Value.RetiredRows
        };
    }
}
