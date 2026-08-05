using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Parses the admin's question sheet and publishes it as the lesson's next question version.
/// </summary>
public class QuestionImportService : IQuestionImportService
{
    /// <summary>Guard against a runaway sheet being imported by accident.</summary>
    private const int MaxQuestionsPerSheet = 5000;

    private const int MaxQuestionLength = 1000;
    private const int MaxChoiceLength = 500;

    private const int QuestionColumn = 1;
    private const int CorrectAnswerColumn = 2;
    private const int WrongAnswer1Column = 3;
    private const int WrongAnswer2Column = 4;

    private readonly ApplicationDbContext _dbContext;

    public QuestionImportService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<QuestionImportResult> ImportAsync(
        Guid lessonId,
        Stream excelStream,
        string fileName,
        bool hasHeaderRow = true,
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var lesson = await _dbContext.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, cancellationToken);
        if (lesson is null)
            return QuestionImportResult.Failed(lessonId, "Lesson not found.");

        var parsed = ParseSheet(excelStream, hasHeaderRow, out var errors);
        if (errors.Count > 0)
            return new QuestionImportResult { Succeeded = false, LessonId = lessonId, Errors = errors };

        if (parsed.Count == 0)
            return QuestionImportResult.Failed(lessonId, "The sheet contains no question rows.");

        var now = DateTime.UtcNow;
        var newVersion = lesson.QuestionsVersion + 1;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Previous rows are kept, just retired — student progress may reference their ids.
        var replacedCount = await _dbContext.Questions
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        foreach (var row in parsed)
        {
            var question = new Question
            {
                Id = Guid.NewGuid(),
                LessonId = lessonId,
                LangId = lesson.LangId,
                Text = row.QuestionText,
                Version = newVersion,
                IsActive = true,
                RowNumber = row.RowNumber,
                CreatedAt = now
            };

            // Column 2 is the correct answer by contract; the other two are distractors.
            // Order is preserved as-is — the client shuffles before assigning doors to lanes.
            var choices = new[] { row.CorrectAnswer, row.WrongAnswer1, row.WrongAnswer2 }
                .Select((text, index) => new QuestionChoice
                {
                    Id = Guid.NewGuid(),
                    QuestionId = question.Id,
                    Text = text,
                    OrderIndex = index
                })
                .ToList();

            question.CorrectChoiceId = choices[0].Id;
            question.Choices = choices;

            _dbContext.Questions.Add(question);
        }

        lesson.QuestionsVersion = newVersion;

        _dbContext.LessonQuestionUploads.Add(new LessonQuestionUpload
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            Version = newVersion,
            FileName = Truncate(fileName, 260),
            QuestionCount = parsed.Count,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new QuestionImportResult
        {
            Succeeded = true,
            LessonId = lessonId,
            Version = newVersion,
            ImportedCount = parsed.Count,
            ReplacedCount = replacedCount
        };
    }

    private static List<ParsedRow> ParseSheet(Stream excelStream, bool hasHeaderRow, out List<QuestionImportError> errors)
    {
        errors = [];
        var parsed = new List<ParsedRow>();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(excelStream);
        }
        catch (Exception ex)
        {
            errors.Add(new QuestionImportError { Message = $"The file could not be read as an .xlsx workbook: {ex.Message}" });
            return parsed;
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null)
            {
                errors.Add(new QuestionImportError { Message = "The workbook contains no worksheets." });
                return parsed;
            }

            // RowsUsed() already skips entirely blank rows, so the first entry is the header.
            var rows = worksheet.RowsUsed().ToList();
            if (hasHeaderRow)
                rows = rows.Skip(1).ToList();

            if (rows.Count > MaxQuestionsPerSheet)
            {
                errors.Add(new QuestionImportError
                {
                    Message = $"The sheet has {rows.Count} rows, above the {MaxQuestionsPerSheet} limit for a single lesson."
                });
                return parsed;
            }

            foreach (var row in rows)
            {
                var rowNumber = row.RowNumber();
                var questionText = row.Cell(QuestionColumn).GetString().Trim();
                var correct = row.Cell(CorrectAnswerColumn).GetString().Trim();
                var wrong1 = row.Cell(WrongAnswer1Column).GetString().Trim();
                var wrong2 = row.Cell(WrongAnswer2Column).GetString().Trim();

                // A spacer row the admin left in the middle of the sheet.
                if (questionText.Length == 0 && correct.Length == 0 && wrong1.Length == 0 && wrong2.Length == 0)
                    continue;

                var rowErrors = ValidateRow(rowNumber, questionText, correct, wrong1, wrong2);
                if (rowErrors.Count > 0)
                {
                    errors.AddRange(rowErrors);
                    continue;
                }

                parsed.Add(new ParsedRow(rowNumber, questionText, correct, wrong1, wrong2));
            }
        }

        return parsed;
    }

    private static List<QuestionImportError> ValidateRow(
        int rowNumber, string questionText, string correct, string wrong1, string wrong2)
    {
        var rowErrors = new List<QuestionImportError>();

        void Fail(string message) => rowErrors.Add(new QuestionImportError { Row = rowNumber, Message = message });

        if (questionText.Length == 0)
            Fail("Column 1 (question) is empty.");
        else if (questionText.Length > MaxQuestionLength)
            Fail($"Column 1 (question) is {questionText.Length} characters, above the {MaxQuestionLength} limit.");

        var choiceLabels = new[]
        {
            ("Column 2 (correct answer)", correct),
            ("Column 3 (wrong answer)", wrong1),
            ("Column 4 (wrong answer)", wrong2)
        };

        foreach (var (label, value) in choiceLabels)
        {
            if (value.Length == 0)
                Fail($"{label} is empty.");
            else if (value.Length > MaxChoiceLength)
                Fail($"{label} is {value.Length} characters, above the {MaxChoiceLength} limit.");
        }

        // Two identical doors where one counts as wrong would be unanswerable.
        // Compared case-SENSITIVELY on purpose: capitalisation is often the thing being tested
        // ("Fe" vs "FE" vs "fe" for iron's chemical symbol is a real question), so folding case
        // here would reject valid content.
        var distinct = choiceLabels
            .Select(c => c.Item2)
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (distinct != choiceLabels.Count(c => c.Item2.Length > 0))
            Fail("The three answers must be different from each other.");

        return rowErrors;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ParsedRow(
        int RowNumber,
        string QuestionText,
        string CorrectAnswer,
        string WrongAnswer1,
        string WrongAnswer2);
}
