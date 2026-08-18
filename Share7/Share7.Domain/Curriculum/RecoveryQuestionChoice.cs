namespace Share7.Domain.Curriculum;

/// <summary>
/// An answer option on a <see cref="RecoveryQuestion"/>. Exactly three per question, built from
/// Excel columns 2 (correct), 3 and 4 (wrong) — one per lane/door in the runner game.
/// <para>
/// Correctness is not stored here; it lives on <see cref="RecoveryQuestion.CorrectChoiceId"/>,
/// so there is a single source of truth rather than a per-choice boolean that could disagree
/// with itself.
/// </para>
/// </summary>
public class RecoveryQuestionChoice
{
    public Guid Id { get; set; }

    public Guid RecoveryQuestionId { get; set; }
    public RecoveryQuestion? RecoveryQuestion { get; set; }

    /// <summary>Maps to the "Choice" column.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Source column order: 0 = Excel col 2, 1 = col 3, 2 = col 4.</summary>
    public int OrderIndex { get; set; }
}
