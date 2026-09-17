using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class CurriculumAdminService : ICurriculumAdminService
{
    private const int TermNameMaxLength = 100;
    private const int SubjectNameMaxLength = 200;
    private const int ChapterNameMaxLength = 200;
    private const int LessonNameMaxLength = 200;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public CurriculumAdminService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    // ---------------------------------------------------------------- adds

    public async Task<ServiceResult<TermDto>> AddTermToGradeAsync(
        Guid gradeId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateNamesAsync(request, TermNameMaxLength, cancellationToken);
        if (!validated.Succeeded)
            return Propagate<TermDto>(validated);

        var names = validated.Value!;

        if (!await _dbContext.Grades.AnyAsync(g => g.Id == gradeId, cancellationToken))
            return ServiceResult<TermDto>.NotFound("Grade not found.");

        foreach (var name in names)
        {
            if (await _dbContext.TermTranslations.AnyAsync(
                    t => t.Term!.GradeId == gradeId && t.LangId == name.LangId && t.Name.ToLower() == name.Comparable,
                    cancellationToken))
                return ServiceResult<TermDto>.Conflict($"This grade already has a term named '{name.Name}'.");
        }

        var order = await ResolveOrderAsync(
            _dbContext.Terms.Where(t => t.GradeId == gradeId).Select(t => t.Order), request.Order, cancellationToken);
        if (!order.Succeeded)
            return Propagate<TermDto>(order);

        var term = new Term
        {
            Id = Guid.NewGuid(),
            GradeId = gradeId,
            Order = order.Value,
            Translations = names.Select(n => new TermTranslation { LangId = n.LangId, Name = n.Name }).ToList()
        };

        _dbContext.Terms.Add(term);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var callerLangId = await _languageService.ResolveCurrentAsync(cancellationToken);
        return ServiceResult<TermDto>.Success(new TermDto
        {
            Id = term.Id,
            Name = NameFor(names, callerLangId),
            LangId = callerLangId,
            GradeId = term.GradeId,
            Order = term.Order
        });
    }

    public async Task<ServiceResult<SubjectDto>> AddSubjectToTermAsync(
        Guid termId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateNamesAsync(request, SubjectNameMaxLength, cancellationToken);
        if (!validated.Succeeded)
            return Propagate<SubjectDto>(validated);

        var names = validated.Value!;

        if (!await _dbContext.Terms.AnyAsync(t => t.Id == termId, cancellationToken))
            return ServiceResult<SubjectDto>.NotFound("Term not found.");

        foreach (var name in names)
        {
            if (await _dbContext.SubjectTranslations.AnyAsync(
                    t => t.Subject!.TermId == termId && t.LangId == name.LangId && t.Name.ToLower() == name.Comparable,
                    cancellationToken))
                return ServiceResult<SubjectDto>.Conflict($"This term already has a subject named '{name.Name}'.");
        }

        var order = await ResolveOrderAsync(
            _dbContext.Subjects.Where(s => s.TermId == termId).Select(s => s.Order), request.Order, cancellationToken);
        if (!order.Succeeded)
            return Propagate<SubjectDto>(order);

        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            TermId = termId,
            Order = order.Value,
            Translations = names.Select(n => new SubjectTranslation { LangId = n.LangId, Name = n.Name }).ToList()
        };

        _dbContext.Subjects.Add(subject);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var callerLangId = await _languageService.ResolveCurrentAsync(cancellationToken);
        return ServiceResult<SubjectDto>.Success(new SubjectDto
        {
            Id = subject.Id,
            Name = NameFor(names, callerLangId),
            LangId = callerLangId,
            TermId = subject.TermId,
            Order = subject.Order
        });
    }

    public async Task<ServiceResult<ChapterDto>> AddChapterToSubjectAsync(
        Guid subjectId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateNamesAsync(request, ChapterNameMaxLength, cancellationToken);
        if (!validated.Succeeded)
            return Propagate<ChapterDto>(validated);

        var names = validated.Value!;

        if (!await _dbContext.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
            return ServiceResult<ChapterDto>.NotFound("Subject not found.");

        foreach (var name in names)
        {
            if (await _dbContext.ChapterTranslations.AnyAsync(
                    t => t.Chapter!.SubjectId == subjectId && t.LangId == name.LangId && t.Name.ToLower() == name.Comparable,
                    cancellationToken))
                return ServiceResult<ChapterDto>.Conflict($"This subject already has a chapter named '{name.Name}'.");
        }

        var order = await ResolveOrderAsync(
            _dbContext.Chapters.Where(c => c.SubjectId == subjectId).Select(c => c.Order), request.Order, cancellationToken);
        if (!order.Succeeded)
            return Propagate<ChapterDto>(order);

        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Order = order.Value,
            Translations = names.Select(n => new ChapterTranslation { LangId = n.LangId, Name = n.Name }).ToList()
        };

        _dbContext.Chapters.Add(chapter);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var callerLangId = await _languageService.ResolveCurrentAsync(cancellationToken);
        return ServiceResult<ChapterDto>.Success(new ChapterDto
        {
            Id = chapter.Id,
            Name = NameFor(names, callerLangId),
            LangId = callerLangId,
            SubjectId = chapter.SubjectId,
            Order = chapter.Order
        });
    }

    public async Task<ServiceResult<LessonDto>> AddLessonToChapterAsync(
        Guid chapterId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateNamesAsync(request, LessonNameMaxLength, cancellationToken);
        if (!validated.Succeeded)
            return Propagate<LessonDto>(validated);

        var names = validated.Value!;

        if (!await _dbContext.Chapters.AnyAsync(c => c.Id == chapterId, cancellationToken))
            return ServiceResult<LessonDto>.NotFound("Chapter not found.");

        foreach (var name in names)
        {
            if (await _dbContext.LessonTranslations.AnyAsync(
                    t => t.Lesson!.ChapterId == chapterId && t.LangId == name.LangId && t.Name.ToLower() == name.Comparable,
                    cancellationToken))
                return ServiceResult<LessonDto>.Conflict($"This chapter already has a lesson named '{name.Name}'.");
        }

        var order = await ResolveOrderAsync(
            _dbContext.Lessons.Where(l => l.ChapterId == chapterId).Select(l => l.Order), request.Order, cancellationToken);
        if (!order.Succeeded)
            return Propagate<LessonDto>(order);

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            ChapterId = chapterId,
            Order = order.Value,
            Translations = names.Select(n => new LessonTranslation { LangId = n.LangId, Name = n.Name }).ToList()
        };

        _dbContext.Lessons.Add(lesson);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var callerLangId = await _languageService.ResolveCurrentAsync(cancellationToken);

        // No question set exists in any language until a sheet is uploaded, so the lesson is
        // created named but not yet playable.
        return ServiceResult<LessonDto>.Success(new LessonDto
        {
            Id = lesson.Id,
            Name = NameFor(names, callerLangId),
            LangId = callerLangId,
            ChapterId = lesson.ChapterId,
            Order = lesson.Order,
            QuestionsVersion = 0,
            HasQuestions = false
        });
    }

    // ------------------------------------------------------------- deletes

    public async Task<ServiceResult<CurriculumNodeChildCounts>> DeleteTermAsync(
        Guid termId, bool force, CancellationToken cancellationToken = default)
    {
        var term = await _dbContext.Terms.FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
            return ServiceResult<CurriculumNodeChildCounts>.NotFound("Term not found.");

        var subjectIds = await _dbContext.Subjects
            .Where(s => s.TermId == termId).Select(s => s.Id).ToListAsync(cancellationToken);

        var counts = await CountBelowSubjectsAsync(subjectIds, cancellationToken);
        counts.Subjects = subjectIds.Count;

        if (!force && counts.HasChildren)
            return Blocked(counts, "term");

        _dbContext.Terms.Remove(term);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<CurriculumNodeChildCounts>.Success(counts);
    }

    public async Task<ServiceResult<CurriculumNodeChildCounts>> DeleteSubjectAsync(
        Guid subjectId, bool force, CancellationToken cancellationToken = default)
    {
        var subject = await _dbContext.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken);
        if (subject is null)
            return ServiceResult<CurriculumNodeChildCounts>.NotFound("Subject not found.");

        var counts = await CountBelowSubjectsAsync([subjectId], cancellationToken);

        if (!force && counts.HasChildren)
            return Blocked(counts, "subject");

        _dbContext.Subjects.Remove(subject);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<CurriculumNodeChildCounts>.Success(counts);
    }

    public async Task<ServiceResult<CurriculumNodeChildCounts>> DeleteChapterAsync(
        Guid chapterId, bool force, CancellationToken cancellationToken = default)
    {
        var chapter = await _dbContext.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);
        if (chapter is null)
            return ServiceResult<CurriculumNodeChildCounts>.NotFound("Chapter not found.");

        var lessonIds = await _dbContext.Lessons
            .Where(l => l.ChapterId == chapterId).Select(l => l.Id).ToListAsync(cancellationToken);

        var counts = new CurriculumNodeChildCounts
        {
            Lessons = lessonIds.Count,
            Questions = await CountQuestionsAsync(lessonIds, cancellationToken)
        };

        if (!force && counts.HasChildren)
            return Blocked(counts, "chapter");

        _dbContext.Chapters.Remove(chapter);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<CurriculumNodeChildCounts>.Success(counts);
    }

    public async Task<ServiceResult<CurriculumNodeChildCounts>> DeleteLessonAsync(
        Guid lessonId, bool force, CancellationToken cancellationToken = default)
    {
        var lesson = await _dbContext.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, cancellationToken);
        if (lesson is null)
            return ServiceResult<CurriculumNodeChildCounts>.NotFound("Lesson not found.");

        var counts = new CurriculumNodeChildCounts
        {
            Questions = await CountQuestionsAsync([lessonId], cancellationToken)
        };

        if (!force && counts.HasChildren)
            return Blocked(counts, "lesson");

        // Translations, question sets, choices and the upload audit rows all cascade from the
        // lesson along with the questions.
        _dbContext.Lessons.Remove(lesson);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<CurriculumNodeChildCounts>.Success(counts);
    }

    // ------------------------------------------------------------- helpers

    private static ServiceResult<CurriculumNodeChildCounts> Blocked(CurriculumNodeChildCounts counts, string nodeType) =>
        ServiceResult<CurriculumNodeChildCounts>.Conflict(
            $"This {nodeType} still contains {counts.Describe()}. Deleting it removes all of that too — " +
            "resend with force=true to confirm.",
            counts);

    private async Task<CurriculumNodeChildCounts> CountBelowSubjectsAsync(
        List<Guid> subjectIds, CancellationToken cancellationToken)
    {
        if (subjectIds.Count == 0)
            return new CurriculumNodeChildCounts();

        var chapterIds = await _dbContext.Chapters
            .Where(c => subjectIds.Contains(c.SubjectId)).Select(c => c.Id).ToListAsync(cancellationToken);

        var lessonIds = chapterIds.Count == 0
            ? []
            : await _dbContext.Lessons
                .Where(l => chapterIds.Contains(l.ChapterId)).Select(l => l.Id).ToListAsync(cancellationToken);

        return new CurriculumNodeChildCounts
        {
            Chapters = chapterIds.Count,
            Lessons = lessonIds.Count,
            Questions = await CountQuestionsAsync(lessonIds, cancellationToken)
        };
    }

    private async Task<int> CountQuestionsAsync(List<Guid> lessonIds, CancellationToken cancellationToken) =>
        lessonIds.Count == 0
            ? 0
            : await _dbContext.Questions.CountAsync(q => lessonIds.Contains(q.LessonId), cancellationToken);

    /// <summary>
    /// Trims each name, rejects blanks and over-long values, and requires one name per
    /// configured language — a node missing a translation would be nameless for those
    /// students, and nothing else in the system would notice.
    /// <para>
    /// The lowercased form is what the duplicate check compares against <c>LOWER(name)</c> in
    /// SQL, so the rule is explicit rather than dependent on the database collation happening
    /// to be case-insensitive. The original casing is what gets stored.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<List<NodeName>>> ValidateNamesAsync(
        CreateCurriculumNodeRequest request, int maxLength, CancellationToken cancellationToken)
    {
        var supplied = request.Translations ?? [];
        if (supplied.Count == 0)
            return ServiceResult<List<NodeName>>.Invalid("At least one translation is required.");

        var errors = new List<string>();
        var names = new List<NodeName>();

        foreach (var translation in supplied)
        {
            var name = (translation.Name ?? string.Empty).Trim();

            if (name.Length == 0)
                errors.Add($"Name is required for language {translation.LangId}.");
            else if (name.Length > maxLength)
                errors.Add($"Name for language {translation.LangId} is {name.Length} characters, above the {maxLength} limit.");
            else
                names.Add(new NodeName(translation.LangId, name, name.ToLowerInvariant()));
        }

        if (supplied.Select(t => t.LangId).Distinct().Count() != supplied.Count)
            errors.Add("The same language appears more than once.");

        var languages = await _dbContext.Languages
            .Select(l => new { l.Id, l.Code })
            .ToListAsync(cancellationToken);

        foreach (var name in names.Where(n => languages.All(l => l.Id != n.LangId)))
            errors.Add($"Unknown language '{name.LangId}'.");

        var missing = languages
            .Where(l => names.All(n => n.LangId != l.Id))
            .Select(l => l.Code)
            .ToList();

        if (missing.Count > 0)
            errors.Add($"A name is required for every language. Missing: {string.Join(", ", missing)}.");

        return errors.Count > 0
            ? ServiceResult<List<NodeName>>.Invalid([.. errors])
            : ServiceResult<List<NodeName>>.Success(names);
    }

    /// <summary>
    /// Places the new node among its siblings. Omitting the position appends after the last
    /// one; supplying a taken position is refused rather than silently shuffling, because the
    /// unlock chain steps through this order and two siblings sharing a slot is not resolvable.
    /// </summary>
    private static async Task<ServiceResult<int>> ResolveOrderAsync(
        IQueryable<int> siblingOrders, int? requested, CancellationToken cancellationToken)
    {
        if (requested is { } explicitOrder)
        {
            if (explicitOrder < 1)
                return ServiceResult<int>.Invalid("Order must be 1 or greater.");

            if (await siblingOrders.AnyAsync(o => o == explicitOrder, cancellationToken))
                return ServiceResult<int>.Conflict($"Another node is already at position {explicitOrder} under this parent.");

            return ServiceResult<int>.Success(explicitOrder);
        }

        var highest = await siblingOrders.MaxAsync(o => (int?)o, cancellationToken) ?? 0;
        return ServiceResult<int>.Success(highest + 1);
    }

    /// <summary>Echoes the new node back in the caller's own language, falling back to whatever was supplied first.</summary>
    private static string NameFor(List<NodeName> names, Guid langId) =>
        names.FirstOrDefault(n => n.LangId == langId)?.Name ?? names[0].Name;

    private static ServiceResult<T> Propagate<T>(ServiceResult source) =>
        new() { ErrorKind = source.ErrorKind, Errors = source.Errors };

    private sealed record NodeName(Guid LangId, string Name, string Comparable);
}
