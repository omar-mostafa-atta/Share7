using Share7.Application.Curriculum.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Content;

namespace Share7.Infrastructure.Engine.Reads;

// ===========================================================================
// Every read the game's routes make of the curriculum, behind one seam
//
// Two implementations answer them: TypedCurriculumReads, from the legacy Grade / Term / Subject /
// Chapter / Lesson tables (what the game has always been served), and NodeCurriculumReads, from the
// node tree and the item bank (the source of truth). Curriculum:ReadModel picks which one serves —
// or, in Shadow, serves the typed answer and compares the node answer against it, byte for byte, on
// real traffic. That comparison is the evidence the switch-over waits on (plan step A6).
//
// The services above this seam (grades, browse, question downloads, progress, unlocks, subject
// matchmaking) have one implementation each. Only these reads differ, so only these are compared.
//
// **Every list is ordered in SQL, down to a unique key.** Several of the original queries had no
// ORDER BY, and what the game receives today is whatever order SQL Server happened to return —
// by primary key, in practice. The batch version check is one, and its recorded baseline depends
// on that order. Ordering by id in the query keeps exactly that, and makes the two paths agree on
// ties they would otherwise break differently.
// ===========================================================================

public enum CurriculumReadModel
{
    /// <summary>The typed tables serve every game read — behaviour as before the rebuild.</summary>
    Legacy = 0,

    /// <summary>The typed tables serve; the node tables are read too, compared, and the result tallied.</summary>
    Shadow = 1,

    /// <summary>The node tables and the item bank serve every game read.</summary>
    Generic = 2
}

public sealed class CurriculumReadOptions
{
    public const string Section = "Curriculum";

    public CurriculumReadModel ReadModel { get; set; } = CurriculumReadModel.Legacy;

    /// <summary>In Shadow, the share of reads that are compared (0–1). Every read is still served.</summary>
    public double ShadowSampleRate { get; set; } = 1.0;
}

/// <summary>Which part of the tree a lesson list is drawn from.</summary>
public enum TreeScope
{
    Lesson,
    Chapter,
    Subject,
    Term,
    Grade
}

/// <param name="CurrentVersion">The caller's-language main-pool version (0 when nothing is published).</param>
public sealed record LessonHeader(Guid LessonId, Guid GradeId, int CurrentVersion);

/// <param name="LiveTotal">Active main-pool questions in the caller's language.</param>
public sealed record LessonTotal(Guid LessonId, int LiveTotal);

public sealed record NamedNode(Guid Id, string Name);

public sealed record TreeNodeRow(Guid Id, Guid ParentId, int Order, string Name);

public sealed record TreeLessonRow(Guid Id, Guid ChapterId, int Order, string Name, int LiveTotal, int CurrentVersion);

/// <summary>Everything under a grade, flat, for the progress snapshot to assemble.</summary>
public sealed record GradeTree(
    IReadOnlyList<TreeNodeRow> Terms,
    IReadOnlyList<TreeNodeRow> Subjects,
    IReadOnlyList<TreeNodeRow> Chapters,
    IReadOnlyList<TreeLessonRow> Lessons);

public sealed record LessonLocation(
    int LessonOrder, Guid ChapterId, int ChapterOrder, Guid SubjectId, Guid TermId, int TermOrder, Guid GradeId);

/// <param name="Playable">Has a published main pool in the language being evaluated.</param>
public sealed record TermLesson(Guid Id, int Order, Guid ChapterId, bool Playable);

public sealed record ChildRow(Guid Id, Guid ParentId, int Order);

public interface ICurriculumReads
{
    // ── browsing ────────────────────────────────────────────────────────────────
    Task<IReadOnlyList<GradeDto>> GradesAsync(Guid langId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TermDto>> TermsAsync(Guid? gradeId, Guid langId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubjectDto>> SubjectsAsync(Guid? termId, Guid langId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChapterDto>> ChaptersAsync(Guid subjectId, Guid langId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LessonDto>> LessonsAsync(Guid chapterId, Guid langId, CancellationToken cancellationToken);

    // ── questions and their versions ────────────────────────────────────────────
    /// <summary>One entry per requested lesson that exists; unknown ids are left out.</summary>
    Task<IReadOnlyList<LessonVersionDto>> SetVersionsAsync(
        IReadOnlyList<Guid> lessonIds, NodeItemRole role, Guid langId, CancellationToken cancellationToken);

    /// <summary>Null when the lesson does not exist.</summary>
    Task<LessonQuestionsDto?> QuestionsAsync(Guid lessonId, NodeItemRole role, Guid langId, CancellationToken cancellationToken);

    // ── progress ────────────────────────────────────────────────────────────────
    Task<bool> LessonExistsAsync(Guid lessonId, CancellationToken cancellationToken);

    Task<LessonHeader?> LessonHeaderAsync(Guid lessonId, Guid langId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LessonTotal>> LessonTotalsAsync(TreeScope scope, Guid nodeId, Guid langId, CancellationToken cancellationToken);

    Task<NamedNode?> GradeAsync(Guid gradeId, Guid langId, CancellationToken cancellationToken);

    Task<GradeTree> GradeTreeAsync(Guid gradeId, Guid langId, CancellationToken cancellationToken);

    // ── unlocks ─────────────────────────────────────────────────────────────────
    Task<Guid?> FirstTermAsync(Guid gradeId, CancellationToken cancellationToken);

    Task<LessonLocation?> LessonLocationAsync(Guid lessonId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TermLesson>> LessonsInTermAsync(Guid termId, Guid langId, CancellationToken cancellationToken);

    Task<Guid?> NextChapterAsync(Guid subjectId, int afterOrder, CancellationToken cancellationToken);

    Task<Guid?> NextTermAsync(Guid gradeId, int afterOrder, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> SubjectsOfTermsAsync(IReadOnlyList<Guid> termIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChildRow>> ChaptersOfSubjectsAsync(IReadOnlyList<Guid> subjectIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChildRow>> LessonsOfChaptersAsync(IReadOnlyList<Guid> chapterIds, CancellationToken cancellationToken);

    // ── subject matchmaking ─────────────────────────────────────────────────────
    Task<Guid?> GradeOfSubjectAsync(Guid subjectId, CancellationToken cancellationToken);

    /// <summary>Lessons in the subject that this player has unlocked in this game and can answer in this language.</summary>
    Task<IReadOnlyList<EligibleLesson>> EligibleLessonsAsync(
        Guid userId, Guid gameId, Guid subjectId, Guid langId, CancellationToken cancellationToken);
}
