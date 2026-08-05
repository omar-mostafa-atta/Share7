using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// Table columns: Id, Chapter, Lang_Id, SubjectId.
/// </summary>
public class Chapter
{
    public Guid Id { get; set; }

    /// <summary>Maps to the "Chapter" column.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}
