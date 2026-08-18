using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>A lesson's name in one language. Composite key (LessonId, LangId).</summary>
public class LessonTranslation
{
    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Name { get; set; } = string.Empty;
}
