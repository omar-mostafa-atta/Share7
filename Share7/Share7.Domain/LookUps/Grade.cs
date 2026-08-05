namespace Share7.Domain.LookUps;

/// <summary>
/// Root of the curriculum tree. One row per grade *per language* — "Grade 5" (EN) and
/// "الصف الخامس" (AR) are two distinct rows with distinct Ids.
/// Table columns: Id, Grade, Lang_Id.
/// </summary>
public class Grade
{
    public Guid Id { get; set; }

    /// <summary>Maps to the "Grade" column.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public ICollection<Curriculum.Term> Terms { get; set; } = new List<Curriculum.Term>();
}
