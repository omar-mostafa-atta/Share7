using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <inheritdoc cref="ILessonSheetService"/>
public class LessonSheetService : ILessonSheetService
{
    private readonly ApplicationDbContext _dbContext;

    public LessonSheetService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    // ── read ──────────────────────────────────────────────────────────────────

    public async Task<LessonSheetDto?> GetAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken))
            return null;

        var main = await _dbContext.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .Select(q => new Loaded(
                q.RowNumber,
                q.LangId,
                q.Text,
                q.CorrectChoiceId,
                q.Choices.OrderBy(c => c.OrderIndex).Select(c => new LoadedChoice(c.Id, c.Text)).ToList()))
            .ToListAsync(cancellationToken);

        var recovery = await _dbContext.RecoveryQuestions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .Select(q => new Loaded(
                q.RowNumber,
                q.LangId,
                q.Text,
                q.CorrectChoiceId,
                q.Choices.OrderBy(c => c.OrderIndex).Select(c => new LoadedChoice(c.Id, c.Text)).ToList()))
            .ToListAsync(cancellationToken);

        var rows = new List<LessonSheetRow>();
        var unpaired = new List<int>();

        Pair(main, isRecovery: false, rows, unpaired);
        Pair(recovery, isRecovery: true, rows, unpaired);

        var mainSets = await _dbContext.LessonQuestionSets
            .AsNoTracking().Where(s => s.LessonId == lessonId)
            .ToDictionaryAsync(s => s.LangId, s => s.Version, cancellationToken);

        var recoverySets = await _dbContext.LessonRecoveryQuestionSets
            .AsNoTracking().Where(s => s.LessonId == lessonId)
            .ToDictionaryAsync(s => s.LangId, s => s.Version, cancellationToken);

        return new LessonSheetDto
        {
            LessonId = lessonId,
            MainVersionEn = mainSets.GetValueOrDefault(En),
            MainVersionAr = mainSets.GetValueOrDefault(Ar),
            RecoveryVersionEn = recoverySets.GetValueOrDefault(En),
            RecoveryVersionAr = recoverySets.GetValueOrDefault(Ar),
            Rows = [.. rows.OrderBy(r => r.IsRecovery).ThenBy(r => r.RowNumber)],
            UnpairedRowNumbers = [.. unpaired.Distinct().Order()]
        };
    }

    /// <summary>
    /// Joins the two languages of one pool on row number.
    /// <para>
    /// A row present in only one language is still returned, with the missing side blank, and its
    /// number is reported in <c>UnpairedRowNumbers</c>. Dropping it would hide content that is live
    /// in the client; blanking it silently would let an admin save the blank back over a translation
    /// that does exist. Naming it is the only option that does neither.
    /// </para>
    /// </summary>
    private static void Pair(
        List<Loaded> loaded, bool isRecovery, List<LessonSheetRow> rows, List<int> unpaired)
    {
        foreach (var group in loaded.GroupBy(q => q.RowNumber))
        {
            var english = group.FirstOrDefault(q => q.LangId == En);
            var arabic = group.FirstOrDefault(q => q.LangId == Ar);

            if (english is null || arabic is null)
                unpaired.Add(group.Key);

            rows.Add(new LessonSheetRow
            {
                RowNumber = group.Key,
                IsRecovery = isRecovery,

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
    }

    private static string Correct(Loaded? q) =>
        q?.Choices.FirstOrDefault(c => c.Id == q.CorrectChoiceId)?.Text ?? string.Empty;

    private static string Wrong(Loaded? q, int index)
    {
        if (q is null) return string.Empty;

        var wrong = q.Choices.Where(c => c.Id != q.CorrectChoiceId).ToList();
        return index < wrong.Count ? wrong[index].Text : string.Empty;
    }

    private sealed record Loaded(
        int RowNumber, Guid LangId, string Text, Guid CorrectChoiceId, List<LoadedChoice> Choices);

    private sealed record LoadedChoice(Guid Id, string Text);

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
        if (!await _dbContext.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken))
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
        if (!await _dbContext.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken))
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
    /// Writes all four sets in one transaction, retiring what each replaces.
    /// <para>
    /// <b>The recovery requirement is enforced here rather than at the parser</b>, so it holds for
    /// every way content arrives — an upload, a save from the console, and a delete that would take
    /// the last recovery row with it. A lesson with a main pool and no recovery pool has nothing to
    /// offer a child who answered wrong, which is the whole point of the second pool; letting one be
    /// published means finding out in the client.
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

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var replaced = await _dbContext.Questions
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        replaced += await _dbContext.RecoveryQuestions
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        var mainVersion = await BumpMainAsync(lessonId, cancellationToken);
        var recoveryVersion = await BumpRecoveryAsync(lessonId, cancellationToken);

        foreach (var row in mainRows)
        {
            AddMain(lessonId, En, mainVersion, row.RowNumber, now,
                row.QuestionEn, row.CorrectEn, row.WrongEn1, row.WrongEn2);
            AddMain(lessonId, Ar, mainVersion, row.RowNumber, now,
                row.QuestionAr, row.CorrectAr, row.WrongAr1, row.WrongAr2);
        }

        foreach (var row in recoveryRows)
        {
            AddRecovery(lessonId, En, recoveryVersion, row.RowNumber, now,
                row.QuestionEn, row.CorrectEn, row.WrongEn1, row.WrongEn2);
            AddRecovery(lessonId, Ar, recoveryVersion, row.RowNumber, now,
                row.QuestionAr, row.CorrectAr, row.WrongAr1, row.WrongAr2);
        }

        foreach (var langId in new[] { En, Ar })
        {
            _dbContext.LessonQuestionUploads.Add(new LessonQuestionUpload
            {
                Id = Guid.NewGuid(),
                LessonId = lessonId,
                LangId = langId,
                Version = mainVersion,
                FileName = QuestionSheetParser.Truncate(fileName, 260),
                Source = source,
                QuestionCount = mainRows.Count,
                UploadedByUserId = publishedByUserId,
                UploadedAt = now
            });

            _dbContext.LessonRecoveryQuestionUploads.Add(new LessonRecoveryQuestionUpload
            {
                Id = Guid.NewGuid(),
                LessonId = lessonId,
                LangId = langId,
                Version = recoveryVersion,
                FileName = QuestionSheetParser.Truncate(fileName, 260),
                Source = source,
                QuestionCount = recoveryRows.Count,
                UploadedByUserId = publishedByUserId,
                UploadedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LessonSheetResult
        {
            Succeeded = true,
            LessonId = lessonId,
            MainCount = mainRows.Count,
            RecoveryCount = recoveryRows.Count,
            MainVersion = mainVersion,
            RecoveryVersion = recoveryVersion,
            ReplacedCount = replaced
        };
    }

    /// <summary>
    /// One version number across both languages of the main pool.
    /// <para>
    /// The sets are stored per language and could carry different numbers — the per-language
    /// importer is why they can. A paired publish writes both, so letting them diverge would record
    /// a difference that did not happen. The new number is one past whichever set is further ahead,
    /// so a lesson that was previously published one language at a time still moves forward.
    /// </para>
    /// </summary>
    private async Task<int> BumpMainAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var sets = await _dbContext.LessonQuestionSets
            .Where(s => s.LessonId == lessonId)
            .ToListAsync(cancellationToken);

        var version = (sets.Count == 0 ? 0 : sets.Max(s => s.Version)) + 1;

        foreach (var langId in new[] { En, Ar })
        {
            var set = sets.FirstOrDefault(s => s.LangId == langId);

            if (set is null)
                _dbContext.LessonQuestionSets.Add(new LessonQuestionSet { LessonId = lessonId, LangId = langId, Version = version });
            else
                set.Version = version;
        }

        return version;
    }

    /// <inheritdoc cref="BumpMainAsync"/>
    private async Task<int> BumpRecoveryAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var sets = await _dbContext.LessonRecoveryQuestionSets
            .Where(s => s.LessonId == lessonId)
            .ToListAsync(cancellationToken);

        var version = (sets.Count == 0 ? 0 : sets.Max(s => s.Version)) + 1;

        foreach (var langId in new[] { En, Ar })
        {
            var set = sets.FirstOrDefault(s => s.LangId == langId);

            if (set is null)
                _dbContext.LessonRecoveryQuestionSets.Add(new LessonRecoveryQuestionSet { LessonId = lessonId, LangId = langId, Version = version });
            else
                set.Version = version;
        }

        return version;
    }

    private void AddMain(
        Guid lessonId, Guid langId, int version, int rowNumber, DateTime now,
        string text, string correct, string wrong1, string wrong2)
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            LangId = langId,
            Text = text,
            Version = version,
            IsActive = true,
            RowNumber = rowNumber,
            CreatedAt = now
        };

        // The correct answer is stored first by contract; the client shuffles before assigning lanes.
        var choices = new[] { correct, wrong1, wrong2 }
            .Select((choiceText, index) => new QuestionChoice
            {
                Id = Guid.NewGuid(),
                QuestionId = question.Id,
                Text = choiceText,
                OrderIndex = index
            })
            .ToList();

        question.CorrectChoiceId = choices[0].Id;
        question.Choices = choices;

        _dbContext.Questions.Add(question);
    }

    private void AddRecovery(
        Guid lessonId, Guid langId, int version, int rowNumber, DateTime now,
        string text, string correct, string wrong1, string wrong2)
    {
        var question = new RecoveryQuestion
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            LangId = langId,
            Text = text,
            Version = version,
            IsActive = true,
            RowNumber = rowNumber,
            CreatedAt = now
        };

        var choices = new[] { correct, wrong1, wrong2 }
            .Select((choiceText, index) => new RecoveryQuestionChoice
            {
                Id = Guid.NewGuid(),
                RecoveryQuestionId = question.Id,
                Text = choiceText,
                OrderIndex = index
            })
            .ToList();

        question.CorrectChoiceId = choices[0].Id;
        question.Choices = choices;

        _dbContext.RecoveryQuestions.Add(question);
    }
}
