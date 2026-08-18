namespace Share7.Domain.LookUps;

/// <summary>
/// Root of the curriculum tree. One row per grade, shared by every language — the display
/// names live in <see cref="Translations"/>.
/// <para>
/// This replaces the old one-row-per-language model. A single set of node ids is what lets
/// progress and unlocks survive a student switching language: the ids do not change.
/// </para>
/// Table columns: Id, Order.
/// </summary>
public class Grade
{
    public Guid Id { get; set; }

    /// <summary>Position in the grade ladder, 1-based. Drives sort order and progression.</summary>
    public int Order { get; set; }

    public ICollection<GradeTranslation> Translations { get; set; } = new List<GradeTranslation>();

    public ICollection<Curriculum.Term> Terms { get; set; } = new List<Curriculum.Term>();
}
