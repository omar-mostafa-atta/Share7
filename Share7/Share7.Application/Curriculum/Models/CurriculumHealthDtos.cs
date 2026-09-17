namespace Share7.Application.Curriculum.Models;

/// <summary>
/// What is wrong with one node, and how badly.
/// <para>
/// <b>Severity is about the client, not about tidiness.</b> An <see cref="CurriculumIssueSeverity.Error"/>
/// means a child can reach this node and find nothing usable there — an empty chapter they can open,
/// a lesson that starts and has no questions, a lesson that cannot recover a wrong answer. A
/// <see cref="CurriculumIssueSeverity.Warning"/> means the content is playable but incomplete, most
/// often in one language only. Sorting the two together would bury the handful that break the app
/// under the hundreds that merely need finishing.
/// </para>
/// </summary>
public enum CurriculumIssueSeverity
{
    Warning = 0,
    Error = 1
}

/// <summary>The kinds of problem the health pass can find. Stable names — the console maps them to copy.</summary>
public enum CurriculumIssueKind
{
    /// <summary>A grade with no terms under it.</summary>
    GradeWithoutTerms = 0,

    /// <summary>A term with no subjects under it.</summary>
    TermWithoutSubjects,

    /// <summary>A subject with no chapters under it.</summary>
    SubjectWithoutChapters,

    /// <summary>A chapter with no lessons under it.</summary>
    ChapterWithoutLessons,

    /// <summary>A lesson nobody has published questions for, in any language.</summary>
    LessonWithoutQuestions,

    /// <summary>
    /// A lesson with a main pool and no recovery pool. An error: the recovery pool is what a wrong
    /// answer is met with, so without one the lesson has nothing to offer the child it just failed.
    /// </summary>
    LessonWithoutRecovery,

    /// <summary>Questions published in one content language but not the other.</summary>
    LessonLanguageGap,

    /// <summary>
    /// The two languages of a pool sit at different version numbers — the signature of the
    /// per-language importer. Harmless to a player, and the thing to fix before trusting a diff.
    /// </summary>
    LessonVersionDrift,

    /// <summary>A node with no name authored in one of the content languages.</summary>
    MissingTranslation
}

/// <summary>One finding, with enough trail on it to be navigated to.</summary>
public class CurriculumIssueDto
{
    public CurriculumIssueKind Kind { get; set; }
    public CurriculumIssueSeverity Severity { get; set; }

    public Guid NodeId { get; set; }

    /// <summary>grade | term | subject | chapter | lesson.</summary>
    public string NodeLevel { get; set; } = string.Empty;

    /// <summary>Grade → node, already resolved into the caller's language, for display.</summary>
    public IReadOnlyList<string> Path { get; set; } = [];

    /// <summary>What specifically is wrong, when the kind alone does not say it.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>Coverage counters for the whole tree.</summary>
public class CurriculumStatsDto
{
    public int Grades { get; set; }
    public int Terms { get; set; }
    public int Subjects { get; set; }
    public int Chapters { get; set; }
    public int Lessons { get; set; }

    /// <summary>Lessons with at least one active main question in at least one language.</summary>
    public int LessonsWithQuestions { get; set; }

    /// <summary>Lessons with at least one active recovery question.</summary>
    public int LessonsWithRecovery { get; set; }

    /// <summary>Lessons whose main pool is published in <b>both</b> content languages.</summary>
    public int LessonsFullyBilingual { get; set; }

    public int QuestionsEn { get; set; }
    public int QuestionsAr { get; set; }
    public int RecoveryQuestionsEn { get; set; }
    public int RecoveryQuestionsAr { get; set; }

    /// <summary>Whole-tree readiness: lessons that are published, bilingual and have recovery.</summary>
    public int LessonsReady { get; set; }
}

/// <summary>The dashboard payload: the counters, and everything wrong.</summary>
public class CurriculumHealthDto
{
    public CurriculumStatsDto Stats { get; set; } = new();

    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }

    /// <summary>
    /// Errors first, then warnings, capped. A tree with a thousand unfinished lessons produces a
    /// thousand findings, and a page that renders all of them is a page nobody scrolls — the counts
    /// above stay exact regardless of what the list was truncated to.
    /// </summary>
    public IReadOnlyList<CurriculumIssueDto> Issues { get; set; } = [];

    public bool Truncated { get; set; }
}
