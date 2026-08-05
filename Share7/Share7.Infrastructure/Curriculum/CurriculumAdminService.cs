using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class CurriculumAdminService : ICurriculumAdminService
{
    private readonly ApplicationDbContext _dbContext;

    public CurriculumAdminService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // ---------------------------------------------------------------- adds

    public async Task<ServiceResult<TermDto>> AddTermToGradeAsync(
        Guid gradeId, string name, CancellationToken cancellationToken = default)
    {
        if (Normalize(ref name, out var comparable) is { } error)
            return ServiceResult<TermDto>.Invalid(error);

        var grade = await _dbContext.Grades
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gradeId, cancellationToken);

        if (grade is null)
            return ServiceResult<TermDto>.NotFound("Grade not found.");

        if (await _dbContext.Terms.AnyAsync(
                t => t.GradeId == gradeId && t.Name.ToLower() == comparable, cancellationToken))
            return ServiceResult<TermDto>.Conflict($"This grade already has a term named '{name}'.");

        var term = new Term
        {
            Id = Guid.NewGuid(),
            Name = name,
            GradeId = gradeId,
            LangId = grade.LangId
        };

        _dbContext.Terms.Add(term);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<TermDto>.Success(new TermDto
        {
            Id = term.Id,
            Name = term.Name,
            LangId = term.LangId,
            GradeId = term.GradeId
        });
    }

    public async Task<ServiceResult<SubjectDto>> AddSubjectToTermAsync(
        Guid termId, string name, CancellationToken cancellationToken = default)
    {
        if (Normalize(ref name, out var comparable) is { } error)
            return ServiceResult<SubjectDto>.Invalid(error);

        var term = await _dbContext.Terms
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);

        if (term is null)
            return ServiceResult<SubjectDto>.NotFound("Term not found.");

        if (await _dbContext.Subjects.AnyAsync(
                s => s.TermId == termId && s.Name.ToLower() == comparable, cancellationToken))
            return ServiceResult<SubjectDto>.Conflict($"This term already has a subject named '{name}'.");

        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            Name = name,
            TermId = termId,
            LangId = term.LangId
        };

        _dbContext.Subjects.Add(subject);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<SubjectDto>.Success(new SubjectDto
        {
            Id = subject.Id,
            Name = subject.Name,
            LangId = subject.LangId,
            TermId = subject.TermId
        });
    }

    public async Task<ServiceResult<ChapterDto>> AddChapterToSubjectAsync(
        Guid subjectId, string name, CancellationToken cancellationToken = default)
    {
        if (Normalize(ref name, out var comparable) is { } error)
            return ServiceResult<ChapterDto>.Invalid(error);

        var subject = await _dbContext.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken);

        if (subject is null)
            return ServiceResult<ChapterDto>.NotFound("Subject not found.");

        if (await _dbContext.Chapters.AnyAsync(
                c => c.SubjectId == subjectId && c.Name.ToLower() == comparable, cancellationToken))
            return ServiceResult<ChapterDto>.Conflict($"This subject already has a chapter named '{name}'.");

        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            Name = name,
            SubjectId = subjectId,
            LangId = subject.LangId
        };

        _dbContext.Chapters.Add(chapter);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ChapterDto>.Success(new ChapterDto
        {
            Id = chapter.Id,
            Name = chapter.Name,
            LangId = chapter.LangId,
            SubjectId = chapter.SubjectId
        });
    }

    public async Task<ServiceResult<LessonDto>> AddLessonToChapterAsync(
        Guid chapterId, string name, CancellationToken cancellationToken = default)
    {
        if (Normalize(ref name, out var comparable) is { } error)
            return ServiceResult<LessonDto>.Invalid(error);

        var chapter = await _dbContext.Chapters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);

        if (chapter is null)
            return ServiceResult<LessonDto>.NotFound("Chapter not found.");

        if (await _dbContext.Lessons.AnyAsync(
                l => l.ChapterId == chapterId && l.Name.ToLower() == comparable, cancellationToken))
            return ServiceResult<LessonDto>.Conflict($"This chapter already has a lesson named '{name}'.");

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            Name = name,
            ChapterId = chapterId,
            LangId = chapter.LangId,
            QuestionsVersion = 0
        };

        _dbContext.Lessons.Add(lesson);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<LessonDto>.Success(new LessonDto
        {
            Id = lesson.Id,
            Name = lesson.Name,
            LangId = lesson.LangId,
            ChapterId = lesson.ChapterId,
            QuestionsVersion = lesson.QuestionsVersion
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

        // Choices and the upload audit rows cascade from the lesson along with the questions.
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
    /// Trims the name, rejects blanks, and produces the lowercased form used for the
    /// duplicate check. Comparing against <c>LOWER(name)</c> in SQL makes the check explicit
    /// rather than depending on the database collation happening to be case-insensitive.
    /// The original casing is what gets stored, so display names keep their capitals.
    /// </summary>
    private static string? Normalize(ref string name, out string comparable)
    {
        name = (name ?? string.Empty).Trim();
        comparable = name.ToLowerInvariant();
        return name.Length == 0 ? "Name is required." : null;
    }
}
