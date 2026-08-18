using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>A subject's name in one language. Composite key (SubjectId, LangId).</summary>
public class SubjectTranslation
{
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Name { get; set; } = string.Empty;
}
