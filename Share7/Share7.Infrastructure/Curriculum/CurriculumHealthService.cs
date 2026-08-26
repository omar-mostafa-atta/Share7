using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Counts the tree and finds what is wrong with it.
/// <para>
/// <b>One flat pass, not a walk.</b> The tree is five levels and, once seeded, fifteen hundred
/// lessons — a recursive walk that asks the database a question per node is thousands of round
/// trips for a page that loads on every visit. Everything here is a handful of set-based reads
/// joined in memory, which is why it stays flat as the curriculum grows.
/// </para>
/// <para>
/// <b>It reports, it never repairs.</b> Every finding is something an author has to decide about —
/// an empty chapter might be next week's work or last week's mistake, and nothing here can tell
/// which. Auto-deleting a node because it is currently empty is how a term disappears the day
/// before someone fills it.
/// </para>
/// </summary>
public class CurriculumHealthService : ICurriculumHealthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public CurriculumHealthService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    /// <summary>
    /// How many findings the payload carries. High enough that a healthy tree's list is complete,
    /// low enough that an empty one does not ship a megabyte of JSON to say so.
    /// </summary>
    private const int MaxIssues = 250;

    public async Task<CurriculumHealthDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        // ── the tree, flat ───────────────────────────────────────────────────
        var grades = await _dbContext.Grades
            .AsNoTracking()
            .Select(g => new Node(
                g.Id,
                Guid.Empty,
                g.Order,
                g.Translations.Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault(),
                g.Translations.Count(t => t.LangId == En && t.Name != ""),
                g.Translations.Count(t => t.LangId == Ar && t.Name != "")))
            .ToListAsync(cancellationToken);

        var terms = await _dbContext.Terms
            .AsNoTracking()
            .Select(t => new Node(
                t.Id, t.GradeId, t.Order,
                t.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault(),
                t.Translations.Count(x => x.LangId == En && x.Name != ""),
                t.Translations.Count(x => x.LangId == Ar && x.Name != "")))
            .ToListAsync(cancellationToken);

        var subjects = await _dbContext.Subjects
            .AsNoTracking()
            .Select(s => new Node(
                s.Id, s.TermId, s.Order,
                s.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault(),
                s.Translations.Count(x => x.LangId == En && x.Name != ""),
                s.Translations.Count(x => x.LangId == Ar && x.Name != "")))
            .ToListAsync(cancellationToken);

        var chapters = await _dbContext.Chapters
            .AsNoTracking()
            .Select(c => new Node(
                c.Id, c.SubjectId, c.Order,
                c.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault(),
                c.Translations.Count(x => x.LangId == En && x.Name != ""),
                c.Translations.Count(x => x.LangId == Ar && x.Name != "")))
            .ToListAsync(cancellationToken);

        var lessons = await _dbContext.Lessons
            .AsNoTracking()
            .Select(l => new Node(
                l.Id, l.ChapterId, l.Order,
                l.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault(),
                l.Translations.Count(x => x.LangId == En && x.Name != ""),
                l.Translations.Count(x => x.LangId == Ar && x.Name != "")))
            .ToListAsync(cancellationToken);

        // ── what is published, per lesson per language ───────────────────────
        var mainCounts = await _dbContext.Questions
            .AsNoTracking()
            .Where(q => q.IsActive)
            .GroupBy(q => new { q.LessonId, q.LangId })
            .Select(g => new PoolCount(g.Key.LessonId, g.Key.LangId, g.Count()))
            .ToListAsync(cancellationToken);

        var recoveryCounts = await _dbContext.RecoveryQuestions
            .AsNoTracking()
            .Where(q => q.IsActive)
            .GroupBy(q => new { q.LessonId, q.LangId })
            .Select(g => new PoolCount(g.Key.LessonId, g.Key.LangId, g.Count()))
            .ToListAsync(cancellationToken);

        var mainVersions = await _dbContext.LessonQuestionSets
            .AsNoTracking()
            .Select(s => new PoolCount(s.LessonId, s.LangId, s.Version))
            .ToListAsync(cancellationToken);

        var recoveryVersions = await _dbContext.LessonRecoveryQuestionSets
            .AsNoTracking()
            .Select(s => new PoolCount(s.LessonId, s.LangId, s.Version))
            .ToListAsync(cancellationToken);

        var main = Index(mainCounts);
        var recovery = Index(recoveryCounts);
        var mainVersion = Index(mainVersions);
        var recoveryVersion = Index(recoveryVersions);

        // ── indexes for the trail ────────────────────────────────────────────
        var gradeById = grades.ToDictionary(n => n.Id);
        var termById = terms.ToDictionary(n => n.Id);
        var subjectById = subjects.ToDictionary(n => n.Id);
        var chapterById = chapters.ToDictionary(n => n.Id);

        var termsByGrade = terms.ToLookup(n => n.ParentId);
        var subjectsByTerm = subjects.ToLookup(n => n.ParentId);
        var chaptersBySubject = chapters.ToLookup(n => n.ParentId);
        var lessonsByChapter = lessons.ToLookup(n => n.ParentId);

        var issues = new List<CurriculumIssueDto>();

        // ── empty branches ───────────────────────────────────────────────────
        foreach (var grade in grades.Where(g => !termsByGrade[g.Id].Any()))
            issues.Add(Issue(CurriculumIssueKind.GradeWithoutTerms, CurriculumIssueSeverity.Error,
                grade, "grade", [Name(grade)], "No terms."));

        foreach (var term in terms.Where(t => !subjectsByTerm[t.Id].Any()))
            issues.Add(Issue(CurriculumIssueKind.TermWithoutSubjects, CurriculumIssueSeverity.Error,
                term, "term", TrailOfTerm(term), "No subjects."));

        foreach (var subject in subjects.Where(s => !chaptersBySubject[s.Id].Any()))
            issues.Add(Issue(CurriculumIssueKind.SubjectWithoutChapters, CurriculumIssueSeverity.Error,
                subject, "subject", TrailOfSubject(subject), "No chapters."));

        foreach (var chapter in chapters.Where(c => !lessonsByChapter[c.Id].Any()))
            issues.Add(Issue(CurriculumIssueKind.ChapterWithoutLessons, CurriculumIssueSeverity.Error,
                chapter, "chapter", TrailOfChapter(chapter), "No lessons."));

        // ── the lessons ──────────────────────────────────────────────────────
        var stats = new CurriculumStatsDto
        {
            Grades = grades.Count,
            Terms = terms.Count,
            Subjects = subjects.Count,
            Chapters = chapters.Count,
            Lessons = lessons.Count,
            QuestionsEn = mainCounts.Where(c => c.LangId == En).Sum(c => c.Value),
            QuestionsAr = mainCounts.Where(c => c.LangId == Ar).Sum(c => c.Value),
            RecoveryQuestionsEn = recoveryCounts.Where(c => c.LangId == En).Sum(c => c.Value),
            RecoveryQuestionsAr = recoveryCounts.Where(c => c.LangId == Ar).Sum(c => c.Value)
        };

        foreach (var lesson in lessons)
        {
            var mainEn = main.GetValueOrDefault((lesson.Id, En));
            var mainAr = main.GetValueOrDefault((lesson.Id, Ar));
            var recEn = recovery.GetValueOrDefault((lesson.Id, En));
            var recAr = recovery.GetValueOrDefault((lesson.Id, Ar));

            var published = mainEn > 0 || mainAr > 0;
            var bilingual = mainEn > 0 && mainAr > 0;
            var hasRecovery = recEn > 0 || recAr > 0;

            if (published) stats.LessonsWithQuestions++;
            if (bilingual) stats.LessonsFullyBilingual++;
            if (hasRecovery) stats.LessonsWithRecovery++;
            if (bilingual && recEn > 0 && recAr > 0) stats.LessonsReady++;

            var trail = TrailOfLesson(lesson);

            if (!published)
            {
                issues.Add(Issue(CurriculumIssueKind.LessonWithoutQuestions, CurriculumIssueSeverity.Error,
                    lesson, "lesson", trail, "No questions in any language."));
                continue;
            }

            // Only for a lesson that has a main pool. A lesson with nothing in it at all is already
            // reported above, and saying it twice would double the error count for one problem.
            if (!hasRecovery)
            {
                issues.Add(Issue(CurriculumIssueKind.LessonWithoutRecovery, CurriculumIssueSeverity.Error,
                    lesson, "lesson", trail,
                    "Has questions but no recovery pool — a wrong answer has nothing to fall back on."));
            }

            if (!bilingual)
            {
                var missing = mainEn > 0 ? "Arabic" : "English";
                issues.Add(Issue(CurriculumIssueKind.LessonLanguageGap, CurriculumIssueSeverity.Warning,
                    lesson, "lesson", trail, $"Main questions are missing in {missing}."));
            }
            else if (recEn > 0 != recAr > 0)
            {
                var missing = recEn > 0 ? "Arabic" : "English";
                issues.Add(Issue(CurriculumIssueKind.LessonLanguageGap, CurriculumIssueSeverity.Warning,
                    lesson, "lesson", trail, $"Recovery questions are missing in {missing}."));
            }

            var mainVersionEn = mainVersion.GetValueOrDefault((lesson.Id, En));
            var mainVersionAr = mainVersion.GetValueOrDefault((lesson.Id, Ar));
            var recVersionEn = recoveryVersion.GetValueOrDefault((lesson.Id, En));
            var recVersionAr = recoveryVersion.GetValueOrDefault((lesson.Id, Ar));

            if (bilingual && mainVersionEn != mainVersionAr)
            {
                issues.Add(Issue(CurriculumIssueKind.LessonVersionDrift, CurriculumIssueSeverity.Warning,
                    lesson, "lesson", trail,
                    $"Main pool is at v{mainVersionEn} in English and v{mainVersionAr} in Arabic."));
            }

            if (recEn > 0 && recAr > 0 && recVersionEn != recVersionAr)
            {
                issues.Add(Issue(CurriculumIssueKind.LessonVersionDrift, CurriculumIssueSeverity.Warning,
                    lesson, "lesson", trail,
                    $"Recovery pool is at v{recVersionEn} in English and v{recVersionAr} in Arabic."));
            }
        }

        // ── names ────────────────────────────────────────────────────────────
        AddTranslationGaps(grades, "grade", n => [Name(n)], issues);
        AddTranslationGaps(terms, "term", TrailOfTerm, issues);
        AddTranslationGaps(subjects, "subject", TrailOfSubject, issues);
        AddTranslationGaps(chapters, "chapter", TrailOfChapter, issues);
        AddTranslationGaps(lessons, "lesson", TrailOfLesson, issues);

        var errorCount = issues.Count(i => i.Severity == CurriculumIssueSeverity.Error);

        var ordered = issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Kind)
            .ThenBy(i => string.Join(" ", i.Path), StringComparer.Ordinal)
            .ToList();

        return new CurriculumHealthDto
        {
            Stats = stats,
            ErrorCount = errorCount,
            WarningCount = issues.Count - errorCount,
            Issues = [.. ordered.Take(MaxIssues)],
            Truncated = ordered.Count > MaxIssues
        };

        // ── local helpers ────────────────────────────────────────────────────

        List<string> TrailOfTerm(Node term) =>
            [Name(gradeById.GetValueOrDefault(term.ParentId)), Name(term)];

        List<string> TrailOfSubject(Node subject)
        {
            var term = subjectById.ContainsKey(subject.Id) ? termById.GetValueOrDefault(subject.ParentId) : null;
            return term is null ? [Name(subject)] : [.. TrailOfTerm(term), Name(subject)];
        }

        List<string> TrailOfChapter(Node chapter)
        {
            var subject = subjectById.GetValueOrDefault(chapter.ParentId);
            return subject is null ? [Name(chapter)] : [.. TrailOfSubject(subject), Name(chapter)];
        }

        List<string> TrailOfLesson(Node lesson)
        {
            var chapter = chapterById.GetValueOrDefault(lesson.ParentId);
            return chapter is null ? [Name(lesson)] : [.. TrailOfChapter(chapter), Name(lesson)];
        }
    }

    private static void AddTranslationGaps(
        List<Node> nodes, string level, Func<Node, List<string>> trail, List<CurriculumIssueDto> issues)
    {
        foreach (var node in nodes)
        {
            if (node.NamesEn > 0 && node.NamesAr > 0) continue;

            var missing = node.NamesEn == 0 && node.NamesAr == 0
                ? "English and Arabic"
                : node.NamesEn == 0 ? "English" : "Arabic";

            issues.Add(Issue(CurriculumIssueKind.MissingTranslation, CurriculumIssueSeverity.Warning,
                node, level, trail(node), $"No name in {missing}."));
        }
    }

    private static CurriculumIssueDto Issue(
        CurriculumIssueKind kind, CurriculumIssueSeverity severity,
        Node node, string level, IReadOnlyList<string> path, string detail) => new()
    {
        Kind = kind,
        Severity = severity,
        NodeId = node.Id,
        NodeLevel = level,
        Path = path,
        Detail = detail
    };

    /// <summary>
    /// The node's name in the caller's language, or a positional stand-in.
    /// <para>
    /// A trail is for finding the thing, so a missing name has to degrade to something locating —
    /// "#3" beats an empty segment that makes the breadcrumb read as though a level were skipped.
    /// </para>
    /// </summary>
    private static string Name(Node? node) =>
        node is null ? "?" : string.IsNullOrWhiteSpace(node.Name) ? $"#{node.Order}" : node.Name;

    private static Dictionary<(Guid, Guid), int> Index(List<PoolCount> counts) =>
        counts.ToDictionary(c => (c.LessonId, c.LangId), c => c.Value);

    private sealed record Node(
        Guid Id, Guid ParentId, int Order, string? Name, int NamesEn, int NamesAr);

    private sealed record PoolCount(Guid LessonId, Guid LangId, int Value);
}
