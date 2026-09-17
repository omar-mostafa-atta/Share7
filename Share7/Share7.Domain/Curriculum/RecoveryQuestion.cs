using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// One row per question row of the uploaded recovery-question Excel sheet — the secondary
/// per-lesson pool, uploaded and versioned entirely separately from <see cref="Question"/>.
/// <para>
/// Structurally this is a clone of <see cref="Question"/>: same columns, same relationships,
/// same soft-delete-and-reversion lifecycle. It is a separate table rather than a flag on
/// <c>Questions</c> so that the two pools have independent versions and one can be re-uploaded
/// without disturbing the other.
/// </para>
/// <para>
/// Recovery questions are never edited in place. Re-uploading a sheet for a lesson soft-deletes
/// the previous set (<see cref="IsActive"/> = false) and inserts a fresh set stamped with the
/// new <see cref="Version"/>, so historical rows survive.
/// </para>
/// </summary>
public class RecoveryQuestion
{
    public Guid Id { get; set; }

    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>Which language's recovery pool this row belongs to — the pool is language-partitioned.</summary>
    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>Maps to the "Question" column. Excel column 1.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Points at the <see cref="RecoveryQuestionChoice"/> built from Excel column 2. Deliberately
    /// not a database FK, for the same reason as <see cref="Question.CorrectChoiceId"/>: the two
    /// tables reference each other and a real constraint in both directions creates a cycle SQL
    /// Server rejects. Both sides are written in a single transaction by the importer, which is
    /// the only writer.
    /// </summary>
    public Guid CorrectChoiceId { get; set; }

    /// <summary>The upload version this row was created by.</summary>
    public int Version { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Row number in the source sheet — used to report import errors back to the admin.</summary>
    public int RowNumber { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    public ICollection<RecoveryQuestionChoice> Choices { get; set; } = new List<RecoveryQuestionChoice>();
}
