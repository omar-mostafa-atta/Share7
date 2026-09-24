using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <inheritdoc cref="ILessonSheetService"/>
/// <remarks>
/// **An adapter over the engine since the rebuild.** The sheet's shape — one row, English and
/// Arabic side by side, a recovery flag — and its rules and messages are unchanged; what a publish
/// does underneath is the content publisher's (<see cref="ILessonContentPublisher"/>), which keeps
/// unchanged questions under their ids and moves a set's version only when what the game receives
/// for it changed. The two languages are found by code, not by constant.
/// </remarks>
public class LessonSheetService : ILessonSheetService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILessonContentReader _reader;
    private readonly ILessonContentPublisher _publisher;
    private readonly IContentLanguages _languages;

    public LessonSheetService(
        ApplicationDbContext dbContext,
        ILessonContentReader reader,
        ILessonContentPublisher publisher,
        IContentLanguages languages)
    {
        _dbContext = dbContext;
        _reader = reader;
        _publisher = publisher;
        _languages = languages;
    }

    // ── read ──────────────────────────────────────────────────────────────────

    public async Task<LessonSheetDto?> GetAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var content = await _reader.ReadAsync(lessonId, cancellationToken);
        if (content is null)
            return null;

        var (en, ar) = await LegacyContentAdapter.SheetLanguagesAsync(_languages, cancellationToken);

        var rows = new List<LessonSheetRow>();
        var unpaired = new List<int>();

        foreach (var item in content.Items.Where(i => i.Role is NodeItemRole.Core or NodeItemRole.Recovery))
        {
            var english = item.In(en);
            var arabic = item.In(ar);

            // A row present in only one language is still returned, with the missing side blank, and
            // its number is reported. Dropping it would hide content that is live in the client;
            // blanking it silently would let an admin save the blank back over a translation that
            // does exist. Naming it is the only option that does neither.
            if (english is null || arabic is null)
                unpaired.Add(item.Order);

            rows.Add(new LessonSheetRow
            {
                RowNumber = item.Order,
                IsRecovery = item.Role == NodeItemRole.Recovery,

                QuestionEn = english?.Text ?? string.Empty,
                CorrectEn = Correct(english),
                WrongEn1 = Wrong(english, 0),
                WrongEn2 = Wrong(english, 1),

                QuestionAr = arabic?.Text ?? string.Empty,
                CorrectAr = Correct(arabic),
                WrongAr1 = Wrong(arabic, 0),
                WrongAr2 = Wrong(arabic, 1)
            });
        }

        return new LessonSheetDto
        {
            LessonId = lessonId,
            MainVersionEn = content.VersionOf(NodeItemRole.Core, en),
            MainVersionAr = content.VersionOf(NodeItemRole.Core, ar),
            RecoveryVersionEn = content.VersionOf(NodeItemRole.Recovery, en),
            RecoveryVersionAr = content.VersionOf(NodeItemRole.Recovery, ar),
            Rows = [.. rows.OrderBy(r => r.IsRecovery).ThenBy(r => r.RowNumber)],
            UnpairedRowNumbers = [.. unpaired.Distinct().Order()]
        };
    }

    private static string Correct(ContentRenderingDto? rendering) =>
        rendering is not null && rendering.CorrectIndex >= 0 && rendering.CorrectIndex < rendering.Choices.Count
            ? rendering.Choices[rendering.CorrectIndex].Text
            : string.Empty;

    private static string Wrong(ContentRenderingDto? rendering, int index)
    {
        if (rendering is null) return string.Empty;

        var wrong = rendering.Choices.Where((_, i) => i != rendering.CorrectIndex).ToList();
        return index < wrong.Count ? wrong[index].Text : string.Empty;
    }

    // ── template ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Questions");

        string[] headers =
        [
            "Question (EN)", "Correct answer (EN)", "Wrong answer (EN)", "Wrong answer (EN)",
            "Question (AR)", "Correct answer (AR)", "Wrong answer (AR)", "Wrong answer (AR)",
            "Recovery? (yes/no)"
        ];

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }

        // One filled row, and it is a recovery row on purpose: the first thing an author does is
        // overwrite it, and a template whose only example is a main question is a template that
        // teaches the upload to fail the recovery check.
        sheet.Cell(2, 1).Value = "What is 2 + 3?";
        sheet.Cell(2, 2).Value = "5";
        sheet.Cell(2, 3).Value = "4";
        sheet.Cell(2, 4).Value = "6";
        sheet.Cell(2, 5).Value = "ما ناتج 2 + 3؟";
        sheet.Cell(2, 6).Value = "5";
        sheet.Cell(2, 7).Value = "4";
        sheet.Cell(2, 8).Value = "6";
        sheet.Cell(2, 9).Value = "yes";

        sheet.Columns(1, headers.Length).AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ── write ─────────────────────────────────────────────────────────────────

    public async Task<LessonSheetResult> ImportAsync(
        Guid lessonId,
        Stream excelStream,
        string fileName,
        bool hasHeaderRow = true,
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.CurriculumNodes.AnyAsync(n => n.Id == lessonId && n.KindKey == NodeKinds.Lesson && n.RetiredAtUtc == null, cancellationToken))
            return LessonSheetResult.Failed(lessonId, "Lesson not found.");

        var rows = LessonSheetParser.Parse(excelStream, hasHeaderRow, out var errors);

        if (errors.Count > 0)
            return new LessonSheetResult { Succeeded = false, LessonId = lessonId, Errors = errors };

        return await PublishAsync(
            lessonId, rows, QuestionSetSource.ExcelUpload, fileName, uploadedByUserId, cancellationToken);
    }

    public async Task<LessonSheetResult> SaveAsync(
        Guid lessonId,
        SaveLessonSheetRequest request,
        Guid? savedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.CurriculumNodes.AnyAsync(n => n.Id == lessonId && n.KindKey == NodeKinds.Lesson && n.RetiredAtUtc == null, cancellationToken))
            return LessonSheetResult.Failed(lessonId, "Lesson not found.");

        var rows = Normalise(request.Rows);
        var errors = ValidateRows(rows);

        if (errors.Count > 0)
            return new LessonSheetResult { Succeeded = false, LessonId = lessonId, Errors = errors };

        return await PublishAsync(
            lessonId, rows, QuestionSetSource.ManualEntry, string.Empty, savedByUserId, cancellationToken);
    }

    public async Task<LessonSheetResult> DeleteRowAsync(
        Guid lessonId,
        int rowNumber,
        Guid? deletedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var sheet = await GetAsync(lessonId, cancellationToken);
        if (sheet is null)
            return LessonSheetResult.Failed(lessonId, "Lesson not found.");

        var remaining = sheet.Rows.Where(r => r.RowNumber != rowNumber).ToList();

        if (remaining.Count == sheet.Rows.Count)
            return LessonSheetResult.Failed(lessonId, $"No question at row {rowNumber} in this lesson.");

        return await PublishAsync(
            lessonId, remaining, QuestionSetSource.ManualEntry, string.Empty, deletedByUserId, cancellationToken);
    }

    /// <summary>
    /// Trims what came off the wire and renumbers anything that arrived without a row number, so a
    /// row typed into the console gets a key that does not collide with an existing one.
    /// </summary>
    private static List<LessonSheetRow> Normalise(IReadOnlyList<LessonSheetRow> incoming)
    {
        var rows = new List<LessonSheetRow>(incoming.Count);
        var used = incoming.Where(r => r.RowNumber > 0).Select(r => r.RowNumber).ToHashSet();
        var next = used.Count == 0 ? 1 : used.Max() + 1;

        foreach (var row in incoming)
        {
            var number = row.RowNumber;
            if (number <= 0)
            {
                number = next++;
                used.Add(number);
            }

            rows.Add(new LessonSheetRow
            {
                RowNumber = number,
                IsRecovery = row.IsRecovery,
                QuestionEn = (row.QuestionEn ?? string.Empty).Trim(),
                CorrectEn = (row.CorrectEn ?? string.Empty).Trim(),
                WrongEn1 = (row.WrongEn1 ?? string.Empty).Trim(),
                WrongEn2 = (row.WrongEn2 ?? string.Empty).Trim(),
                QuestionAr = (row.QuestionAr ?? string.Empty).Trim(),
                CorrectAr = (row.CorrectAr ?? string.Empty).Trim(),
                WrongAr1 = (row.WrongAr1 ?? string.Empty).Trim(),
                WrongAr2 = (row.WrongAr2 ?? string.Empty).Trim()
            });
        }

        return rows;
    }

    private static List<QuestionImportError> ValidateRows(List<LessonSheetRow> rows)
    {
        var errors = new List<QuestionImportError>();

        foreach (var duplicate in rows.GroupBy(r => r.RowNumber).Where(g => g.Count() > 1))
        {
            errors.Add(new QuestionImportError
            {
                Row = duplicate.Key,
                Message = $"Row number {duplicate.Key} appears {duplicate.Count()} times. "
                          + "Row numbers pair a question's languages, so they have to be unique within a lesson."
            });
        }

        foreach (var row in rows)
        {
            foreach (var message in QuestionContentRules.Validate(
                         row.QuestionEn, row.CorrectEn, row.WrongEn1, row.WrongEn2,
                         "English question", "English correct answer",
                         "English wrong answer 1", "English wrong answer 2"))
            {
                errors.Add(new QuestionImportError { Row = row.RowNumber, Message = message });
            }

            foreach (var message in QuestionContentRules.Validate(
                         row.QuestionAr, row.CorrectAr, row.WrongAr1, row.WrongAr2,
                         "Arabic question", "Arabic correct answer",
                         "Arabic wrong answer 1", "Arabic wrong answer 2"))
            {
                errors.Add(new QuestionImportError { Row = row.RowNumber, Message = message });
            }
        }

        return errors;
    }

    /// <summary>
    /// Publishes all four sets — main and recovery, English and Arabic — as one engine publish.
    /// <para>
    /// <b>The recovery requirement is checked here, in the sheet's own words</b>, so it holds for
    /// every way content arrives through the sheet: an upload, a save from the console, and a delete
    /// that would take the last recovery row with it. A lesson with a main pool and no recovery pool
    /// has nothing to offer a child who answered wrong, which is the whole point of the second pool.
    /// </para>
    /// <para>
    /// Rows are retired, never deleted: <c>UserQuestionProgress</c> references the row that graded an
    /// attempt, and a child's history has to stay explicable after the question is rewritten.
    /// </para>
    /// </summary>
    private async Task<LessonSheetResult> PublishAsync(
        Guid lessonId,
        IReadOnlyList<LessonSheetRow> rows,
        QuestionSetSource source,
        string fileName,
        Guid? publishedByUserId,
        CancellationToken cancellationToken)
    {
        var mainRows = rows.Where(r => !r.IsRecovery).OrderBy(r => r.RowNumber).ToList();
        var recoveryRows = rows.Where(r => r.IsRecovery).OrderBy(r => r.RowNumber).ToList();

        if (rows.Count > 0 && recoveryRows.Count == 0)
        {
            return LessonSheetResult.Failed(
                lessonId,
                "This lesson would have no recovery questions. Flag at least one row in column 9 "
                + "(or in the Recovery column of the editor) before publishing.");
        }

        var (en, ar) = await LegacyContentAdapter.SheetLanguagesAsync(_languages, cancellationToken);

        static NodeItemRole RoleOf(LessonSheetRow row) => row.IsRecovery ? NodeItemRole.Recovery : NodeItemRole.Core;

        var lineage = await LegacyContentAdapter.ResolveLineageAsync(
            _dbContext, lessonId, rows.Select(r => (RoleOf(r), r.RowNumber)), cancellationToken);

        var items = rows.Select(row =>
        {
            var (itemId, sourceKey) = lineage[(RoleOf(row), row.RowNumber)];

            return new ContentDraftItem
            {
                ItemId = itemId,
                SourceKeyHint = sourceKey,
                Role = RoleOf(row),
                Order = row.RowNumber,
                Renderings =
                [
                    LegacyContentAdapter.Rendering(en, row.QuestionEn, row.CorrectEn, row.WrongEn1, row.WrongEn2),
                    LegacyContentAdapter.Rendering(ar, row.QuestionAr, row.CorrectAr, row.WrongAr1, row.WrongAr2)
                ]
            };
        }).ToList();

        var published = await _publisher.PublishAsync(new ContentPublishRequest
        {
            LessonId = lessonId,
            Items = items,
            Covers =
            [
                new(NodeItemRole.Core, en), new(NodeItemRole.Core, ar),
                new(NodeItemRole.Recovery, en), new(NodeItemRole.Recovery, ar)
            ],
            Rules = ContentRuleSet.LessonSheet,
            Source = source,
            FileName = QuestionSheetParser.Truncate(fileName, 260),
            ActorUserId = publishedByUserId,
            AuditPath = "lesson-sheet"
        }, cancellationToken);

        if (!published.Succeeded)
            return new LessonSheetResult { Succeeded = false, LessonId = lessonId, Errors = LegacyContentAdapter.Errors(published) };

        var outcome = published.Value!;

        return new LessonSheetResult
        {
            Succeeded = true,
            LessonId = lessonId,
            MainCount = mainRows.Count,
            RecoveryCount = recoveryRows.Count,

            // The two languages version independently now — each moves only when it changed — so the
            // sheet reports the one further ahead.
            MainVersion = Math.Max(outcome.For(NodeItemRole.Core, en)?.Version ?? 0, outcome.For(NodeItemRole.Core, ar)?.Version ?? 0),
            RecoveryVersion = Math.Max(outcome.For(NodeItemRole.Recovery, en)?.Version ?? 0, outcome.For(NodeItemRole.Recovery, ar)?.Version ?? 0),
            ReplacedCount = outcome.RetiredRows
        };
    }
}
