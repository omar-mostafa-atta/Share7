using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// Table columns: Id, Lesson, Lang_Id, ChapterId, QuestionsVersion.
/// <para>
/// <see cref="QuestionsVersion"/> is the cache key the game client holds on to. It is 0
/// until the first Excel upload, then increments by 1 on every subsequent upload. The
/// client compares its cached version against this value and only re-downloads when they
/// differ.
/// </para>
/// </summary>
public class Lesson
{
    public Guid Id { get; set; }

    /// <summary>Maps to the "Lesson" column.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public Guid ChapterId { get; set; }
    public Chapter? Chapter { get; set; }

    /// <summary>0 = no question sheet uploaded yet.</summary>
    public int QuestionsVersion { get; set; }

    public ICollection<Question> Questions { get; set; } = new List<Question>();
}
