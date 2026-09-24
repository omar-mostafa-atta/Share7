using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// A grade has two terms (first / second). One row per term, shared by every language —
/// names live in <see cref="Translations"/>.
/// Table columns: Id, GradeId, Order.
/// </summary>
public class Term
{
    public Guid Id { get; set; }

    public Guid GradeId { get; set; }
    public Grade? Grade { get; set; }

    /// <summary>Position among the siblings under this grade, 1-based.</summary>
    public int Order { get; set; }

    /// <summary>
    /// **Compatibility copy.** Set when the term is retired: hidden from students, history kept.
    /// The curriculum node is the source of truth (<c>CurriculumNode.RetiredAtUtc</c>); this column
    /// exists so every reader still on the typed tables sees the same tree the node readers do. A
    /// global query filter hides retired rows, so to those readers a retired term is simply gone.
    /// </summary>
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<TermTranslation> Translations { get; set; } = new List<TermTranslation>();

    public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
}
