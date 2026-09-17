using ClosedXML.Excel;
using Share7.Application.Curriculum.Models;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Parses and validates the admin's 4-column question sheet. Shared by both importers: the main
/// pool (<see cref="QuestionImportService"/>) and the recovery pool
/// (<see cref="RecoveryQuestionImportService"/>) accept the same file format, so the rules live
/// in one place rather than in two copies that can drift apart.
/// <para>
/// Pure parsing only — nothing here touches the database. Validation is reported, not thrown:
/// the caller decides what to do with the errors, and both callers reject the whole sheet.
/// </para>
/// </summary>
internal static class QuestionSheetParser
{
    /// <summary>Guard against a runaway sheet being imported by accident.</summary>
    private const int MaxQuestionsPerSheet = QuestionContentRules.MaxQuestionsPerSet;

    private const int QuestionColumn = 1;
    private const int CorrectAnswerColumn = 2;
    private const int WrongAnswer1Column = 3;
    private const int WrongAnswer2Column = 4;

    public static List<PublishableQuestion> Parse(Stream excelStream, bool hasHeaderRow, out List<QuestionImportError> errors)
    {
        errors = [];
        var parsed = new List<PublishableQuestion>();

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

                parsed.Add(new PublishableQuestion(rowNumber, questionText, correct, wrong1, wrong2));
            }
        }

        return parsed;
    }

    /// <summary>
    /// Applies <see cref="QuestionContentRules"/> with spreadsheet-shaped labels, and tags each
    /// message with the row it came from so the admin can find it in the file.
    /// </summary>
    private static List<QuestionImportError> ValidateRow(
        int rowNumber, string questionText, string correct, string wrong1, string wrong2) =>
        QuestionContentRules
            .Validate(
                questionText, correct, wrong1, wrong2,
                "Column 1 (question)",
                "Column 2 (correct answer)",
                "Column 3 (wrong answer)",
                "Column 4 (wrong answer)")
            .Select(message => new QuestionImportError { Row = rowNumber, Message = message })
            .ToList();

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
