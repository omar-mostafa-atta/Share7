using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// One row per subject, shared by every language — names live in <see cref="Translations"/>.
/// Secondary-stage specializations (علمي / أدبي) are modelled as subjects here rather than as
/// separate grades, which is what keeps the grade ladder linear.
/// Table columns: Id, TermId, Order.
/// </summary>
public class Subject
{
    public Guid Id { get; set; }

    public Guid TermId { get; set; }
    public Term? Term { get; set; }

    /// <summary>Position among the siblings under this term, 1-based.</summary>
    public int Order { get; set; }

    /// <summary>
    /// **Compatibility copy.** Set when the subject is retired: hidden from students, history kept.
    /// The curriculum node is the source of truth (<c>CurriculumNode.RetiredAtUtc</c>); this column
    /// exists so every reader still on the typed tables sees the same tree the node readers do. A
    /// global query filter hides retired rows, so to those readers a retired subject is simply gone.
    /// </summary>
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<SubjectTranslation> Translations { get; set; } = new List<SubjectTranslation>();

    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
}
