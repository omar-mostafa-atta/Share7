using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>A chapter's name in one language. Composite key (ChapterId, LangId).</summary>
public class ChapterTranslation
{
    public Guid ChapterId { get; set; }
    public Chapter? Chapter { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Name { get; set; } = string.Empty;
}
