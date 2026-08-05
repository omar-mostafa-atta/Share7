using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// Table columns: Id, Subject, Lang_Id, TermId.
/// </summary>
public class Subject
{
    public Guid Id { get; set; }

    /// <summary>Maps to the "Subject" column.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public Guid TermId { get; set; }
    public Term? Term { get; set; }

    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
}
