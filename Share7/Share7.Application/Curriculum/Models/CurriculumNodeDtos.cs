namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Every node DTO carries its name already resolved into the caller's content language,
/// with <c>LangId</c> saying which language that was. The node ids themselves are
/// language-independent — the same lesson has the same id for an Arabic and an English
/// student, which is what lets progress survive a language switch.
/// </summary>
public class TermDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid GradeId { get; set; }
    public int Order { get; set; }
}

public class SubjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid TermId { get; set; }
    public int Order { get; set; }
}

public class ChapterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid SubjectId { get; set; }
    public int Order { get; set; }
}

public class LessonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid ChapterId { get; set; }
    public int Order { get; set; }

    /// <summary>
    /// Question version for this lesson <b>in the caller's language</b> — lets the client
    /// validate its whole cache from this list alone. 0 means nothing uploaded yet.
    /// </summary>
    public int QuestionsVersion { get; set; }

    /// <summary>
    /// False when no question sheet has been uploaded for this lesson in the caller's
    /// language. The lesson exists and is named, but there is nothing to play — the client
    /// should show it as unavailable rather than opening an empty session.
    /// </summary>
    public bool HasQuestions { get; set; }
}
