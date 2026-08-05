using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// A grade has two terms (first / second). One row per term per language.
/// Table columns: Id, Term, Lang_Id, GradeId.
/// </summary>
public class Term
{
    public Guid Id { get; set; }

    /// <summary>Maps to the "Term" column.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public Guid GradeId { get; set; }
    public Grade? Grade { get; set; }

    public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
}
