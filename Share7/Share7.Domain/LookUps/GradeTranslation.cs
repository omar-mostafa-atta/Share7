namespace Share7.Domain.LookUps;

/// <summary>
/// A grade's name in one language. Composite key (GradeId, LangId) — one row per language,
/// deleted with its grade.
/// </summary>
public class GradeTranslation
{
    public Guid GradeId { get; set; }
    public Grade? Grade { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Name { get; set; } = string.Empty;
}
