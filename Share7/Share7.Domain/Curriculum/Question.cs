using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// One row per question row of the uploaded Excel sheet.
/// <para>
/// Questions are never edited in place. Re-uploading a sheet for a lesson soft-deletes the
/// previous set (<see cref="IsActive"/> = false) and inserts a fresh set stamped with the
/// new <see cref="Version"/>, so historical rows referenced by student progress survive.
/// </para>
/// </summary>
public class Question
{
    public Guid Id { get; set; }

    /// <summary>
    /// The immutable item revision this row is a rendering of.
    /// <para>
    /// **This row is an item localization, not an item version.** The distinction is the one that
    /// took a correction to get right: a republish creates a new version, but a *translation* does
    /// not — the English and Arabic rows share a key, a difficulty and a history, and differ only in
    /// text. Both rows therefore point at the same <see cref="Content.ItemVersion"/>, which is what
    /// makes a child's evidence continuous across a language switch (§10.3).
    /// </para>
    /// <para>
    /// Required, and minted by whoever writes the row. A question with no item identity cannot be
    /// measured, cannot accumulate statistics and cannot be mapped to a learning target — so it is
    /// a schema constraint rather than a convention somebody has to remember.
    /// </para>
    /// </summary>
    public Guid ItemVersionId { get; set; }
    public Content.ItemVersion? ItemVersion { get; set; }

    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>
    /// Which pool of the lesson this rendering belongs to: <see cref="Content.NodeItemRole.Core"/>
    /// (the main questions) or <see cref="Content.NodeItemRole.Recovery"/> (the second-chance pool).
    /// <para>
    /// **One table for every pool.** The recovery pool used to live in its own clone of this table
    /// with no item identity; its rows were merged in here with their ids kept, so a recovery
    /// question is an item like any other and one publisher writes both pools. A global query
    /// filter keeps <c>DbContext.Questions</c> to the main pool, so every reader written before the
    /// merge — grading, evidence, quality, search — goes on seeing exactly the rows it always saw.
    /// Readers that want another pool ask for it explicitly (<c>ItemLocalizations</c>).
    /// </para>
    /// </summary>
    public Content.NodeItemRole Role { get; set; } = Content.NodeItemRole.Core;

    /// <summary>Inherited from the parent lesson — the tree is language-scoped.</summary>
    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>Maps to the "Question" column. Excel column 1.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Points at the <see cref="QuestionChoice"/> built from Excel column 2. Deliberately not
    /// a database FK: Question and QuestionChoice reference each other, and a real constraint
    /// in both directions creates a cycle SQL Server rejects. Both sides are written in a
    /// single transaction by the importer, which is the only writer.
    /// </summary>
    public Guid CorrectChoiceId { get; set; }

    /// <summary>
    /// The set version this row was first published in. A row whose question did not change is
    /// carried into later versions as it is — same id, same version number — so this is "first
    /// served in", not "current".
    /// </summary>
    public int Version { get; set; }

    /// <summary>Whether the row is in the set currently served for its lesson, pool and language.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Row number in the source sheet — used to report import errors back to the admin.</summary>
    public int RowNumber { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    public ICollection<QuestionChoice> Choices { get; set; } = new List<QuestionChoice>();
}
