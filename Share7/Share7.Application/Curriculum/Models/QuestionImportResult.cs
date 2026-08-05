namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Outcome of an Excel upload. The import is all-or-nothing: if any row fails validation
/// nothing is written and <see cref="Errors"/> explains what to fix, so an admin can never
/// half-publish a sheet.
/// </summary>
public class QuestionImportResult
{
    public bool Succeeded { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>The version produced by this upload (1 for the first, then 2, 3, ...).</summary>
    public int Version { get; set; }

    public int ImportedCount { get; set; }

    /// <summary>Questions deactivated by this upload (the previous version's set).</summary>
    public int ReplacedCount { get; set; }

    public IReadOnlyList<QuestionImportError> Errors { get; set; } = [];

    public static QuestionImportResult Failed(Guid lessonId, params string[] messages) => new()
    {
        Succeeded = false,
        LessonId = lessonId,
        Errors = messages.Select(m => new QuestionImportError { Message = m }).ToList()
    };
}

public class QuestionImportError
{
    /// <summary>1-based row number in the source sheet; null for whole-file problems.</summary>
    public int? Row { get; set; }

    public string Message { get; set; } = string.Empty;
}
