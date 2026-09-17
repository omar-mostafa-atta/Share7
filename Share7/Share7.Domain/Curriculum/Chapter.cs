using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// One row per chapter, shared by every language — names live in <see cref="Translations"/>.
/// Table columns: Id, SubjectId, Order.
/// </summary>
public class Chapter
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }

    /// <summary>Position among the siblings under this subject, 1-based. Drives the unlock chain.</summary>
    public int Order { get; set; }

    public ICollection<ChapterTranslation> Translations { get; set; } = new List<ChapterTranslation>();

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}
