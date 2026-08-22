namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Outcome of a publish — an Excel upload or a set typed by hand. All-or-nothing either way: if
/// any question fails validation nothing is written and <see cref="Errors"/> explains what to fix,
/// so an admin can never half-publish a set.
/// <para>
/// One shape for both paths deliberately. The admin console shows the same result panel whichever
/// button was pressed, and a caller scripting against the API does not have to branch on how the
/// questions were authored.
/// </para>
/// </summary>
public class QuestionImportResult
{
    public bool Succeeded { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>
    /// Which of the lesson's question sets this upload targeted. Versions are per language,
    /// so publishing English leaves the Arabic set and its version untouched.
    /// </summary>
    public Guid LangId { get; set; }

    /// <summary>The version produced by this publish (1 for the first, then 2, 3, ...).</summary>
    public int Version { get; set; }

    /// <summary>
    /// Questions in the new version. For an appending manual publish this counts the whole
    /// resulting set — the carried-forward questions as well as the newly typed ones — because
    /// that is what was written.
    /// </summary>
    public int ImportedCount { get; set; }

    /// <summary>Questions deactivated by this publish (the previous version's set).</summary>
    public int ReplacedCount { get; set; }

    public IReadOnlyList<QuestionImportError> Errors { get; set; } = [];

    public static QuestionImportResult Failed(Guid lessonId, Guid langId, params string[] messages) => new()
    {
        Succeeded = false,
        LessonId = lessonId,
        LangId = langId,
        Errors = messages.Select(m => new QuestionImportError { Message = m }).ToList()
    };
}

public class QuestionImportError
{
    /// <summary>
    /// Where in the submission the problem is, 1-based — the row for a sheet, the position in
    /// <c>questions</c> for a hand-typed set. Null for problems that belong to the submission as a
    /// whole rather than to one question.
    /// </summary>
    public int? Row { get; set; }

    public string Message { get; set; } = string.Empty;
}
