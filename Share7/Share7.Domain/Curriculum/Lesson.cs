namespace Share7.Domain.Curriculum;

/// <summary>
/// One row per lesson, shared by every language — names live in <see cref="Translations"/>.
/// <para>
/// Questions, unlike the tree above them, stay language-partitioned: the admin uploads a
/// separate sheet per language and nothing pairs the rows across the two, so each language
/// gets its own question set and its own version. That version lives in
/// <see cref="QuestionSets"/> rather than on this row.
/// </para>
/// Table columns: Id, ChapterId, Order.
/// </summary>
public class Lesson
{
    public Guid Id { get; set; }

    public Guid ChapterId { get; set; }
    public Chapter? Chapter { get; set; }

    /// <summary>Position among the siblings under this chapter, 1-based. Drives the unlock chain.</summary>
    public int Order { get; set; }

    public ICollection<LessonTranslation> Translations { get; set; } = new List<LessonTranslation>();

    /// <summary>One entry per language that has had a question sheet uploaded.</summary>
    public ICollection<LessonQuestionSet> QuestionSets { get; set; } = new List<LessonQuestionSet>();

    public ICollection<Question> Questions { get; set; } = new List<Question>();

    /// <summary>
    /// The secondary pool, uploaded and versioned independently of <see cref="QuestionSets"/> —
    /// one entry per language that has had a recovery sheet uploaded.
    /// </summary>
    public ICollection<LessonRecoveryQuestionSet> RecoveryQuestionSets { get; set; } = new List<LessonRecoveryQuestionSet>();

    public ICollection<RecoveryQuestion> RecoveryQuestions { get; set; } = new List<RecoveryQuestion>();
}
