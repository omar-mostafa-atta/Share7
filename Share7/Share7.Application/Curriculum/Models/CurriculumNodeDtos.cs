namespace Share7.Application.Curriculum.Models;

public class TermDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid GradeId { get; set; }
}

public class SubjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid TermId { get; set; }
}

public class ChapterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid SubjectId { get; set; }
}

public class LessonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LangId { get; set; }
    public Guid ChapterId { get; set; }

    /// <summary>Current question version — lets the client validate its cache from this list alone.</summary>
    public int QuestionsVersion { get; set; }
}
