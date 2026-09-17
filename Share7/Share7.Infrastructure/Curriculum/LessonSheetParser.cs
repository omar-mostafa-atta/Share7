using ClosedXML.Excel;
using Share7.Application.Curriculum.Models;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Reads the nine-column lesson workbook: a question and its three answers in English, the same in
/// Arabic, and whether the pair belongs to the recovery pool.
/// <para>
/// <b>Column order is the contract and there is no header matching.</b> Headers are optional text an
/// admin may translate, reorder or delete, and a parser that trusts them fails in the one way a
/// spreadsheet import must not: silently, by reading the Arabic column as English. Position is the
/// only thing a sheet can be relied on for.
/// </para>
/// </summary>
internal static class LessonSheetParser
{
    private const int MaxRows = QuestionContentRules.MaxQuestionsPerSet;

    private const int QuestionEnColumn = 1;
    private const int CorrectEnColumn = 2;
    private const int WrongEn1Column = 3;
    private const int WrongEn2Column = 4;
    private const int QuestionArColumn = 5;
    private const int CorrectArColumn = 6;
    private const int WrongAr1Column = 7;
    private const int WrongAr2Column = 8;
    private const int RecoveryColumn = 9;

    public static List<LessonSheetRow> Parse(
        Stream excelStream, bool hasHeaderRow, out List<QuestionImportError> errors)
    {
        errors = [];
        var parsed = new List<LessonSheetRow>();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(excelStream);
        }
        catch (Exception ex)
        {
            errors.Add(new QuestionImportError
            {
                Message = $"The file could not be read as an .xlsx workbook: {ex.Message}"
            });
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
                rows = [.. rows.Skip(1)];

            if (rows.Count > MaxRows)
            {
                errors.Add(new QuestionImportError
                {
                    Message = $"The sheet has {rows.Count} rows, above the {MaxRows} limit for a single lesson."
                });
                return parsed;
            }

            foreach (var row in rows)
            {
                var rowNumber = row.RowNumber();

                var questionEn = Cell(row, QuestionEnColumn);
                var correctEn = Cell(row, CorrectEnColumn);
                var wrongEn1 = Cell(row, WrongEn1Column);
                var wrongEn2 = Cell(row, WrongEn2Column);
                var questionAr = Cell(row, QuestionArColumn);
                var correctAr = Cell(row, CorrectArColumn);
                var wrongAr1 = Cell(row, WrongAr1Column);
                var wrongAr2 = Cell(row, WrongAr2Column);

                // A spacer row the admin left in the middle of the sheet. The recovery flag alone
                // does not make a row: a stray "no" in column 9 is not a question.
                if (questionEn.Length == 0 && correctEn.Length == 0 && wrongEn1.Length == 0 && wrongEn2.Length == 0
                    && questionAr.Length == 0 && correctAr.Length == 0 && wrongAr1.Length == 0 && wrongAr2.Length == 0)
                {
                    continue;
                }

                var rowErrors = new List<string>();

                rowErrors.AddRange(QuestionContentRules.Validate(
                    questionEn, correctEn, wrongEn1, wrongEn2,
                    "English question (column 1)",
                    "English correct answer (column 2)",
                    "English wrong answer (column 3)",
                    "English wrong answer (column 4)"));

                rowErrors.AddRange(QuestionContentRules.Validate(
                    questionAr, correctAr, wrongAr1, wrongAr2,
                    "Arabic question (column 5)",
                    "Arabic correct answer (column 6)",
                    "Arabic wrong answer (column 7)",
                    "Arabic wrong answer (column 8)"));

                if (rowErrors.Count > 0)
                {
                    errors.AddRange(rowErrors.Select(m => new QuestionImportError { Row = rowNumber, Message = m }));
                    continue;
                }

                if (!TryReadFlag(Cell(row, RecoveryColumn), out var isRecovery))
                {
                    errors.Add(new QuestionImportError
                    {
                        Row = rowNumber,
                        Message = "Recovery flag (column 9) must be yes/no, true/false, 1/0, or left empty for no."
                    });
                    continue;
                }

                parsed.Add(new LessonSheetRow
                {
                    RowNumber = rowNumber,
                    QuestionEn = questionEn,
                    CorrectEn = correctEn,
                    WrongEn1 = wrongEn1,
                    WrongEn2 = wrongEn2,
                    QuestionAr = questionAr,
                    CorrectAr = correctAr,
                    WrongAr1 = wrongAr1,
                    WrongAr2 = wrongAr2,
                    IsRecovery = isRecovery
                });
            }
        }

        return parsed;
    }

    private static string Cell(IXLRow row, int column) => row.Cell(column).GetString().Trim();

    /// <summary>
    /// Reads column 9 permissively, because it is typed by hand in whichever spreadsheet the author
    /// happens to use, and because Excel silently converts a typed <c>TRUE</c> into a boolean cell.
    /// <para>
    /// Empty means false — a sheet of ordinary questions should not need a column filled in — but
    /// anything else unrecognised is an error rather than a shrug. Guessing at "y" or "نعم" would be
    /// fine; guessing at a typo like "recovry" and quietly filing it as a main question is the
    /// failure this refuses.
    /// </para>
    /// </summary>
    private static bool TryReadFlag(string raw, out bool value)
    {
        value = false;
        if (raw.Length == 0) return true;

        switch (raw.ToLowerInvariant())
        {
            case "1":
            case "y":
            case "yes":
            case "true":
            case "recovery":
            case "نعم":
                value = true;
                return true;

            case "0":
            case "n":
            case "no":
            case "false":
            case "main":
            case "لا":
                value = false;
                return true;

            default:
                return false;
        }
    }
}
