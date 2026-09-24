using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Content;
using Share7.Domain.Progress;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine.Reads;

/// <summary>
/// The game's curriculum reads from the legacy typed tables — **the queries the services ran
/// before the rebuild, moved here unchanged** apart from a final order-by-id wherever the original
/// left the order to the database. This is the reference the node reads are compared against.
/// <para>
/// Retired rows are invisible here without any code saying so: the typed tables carry a global
/// query filter on their compatibility copy of the retirement. The main pool is <c>Questions</c>
/// (filtered to it); the recovery pool is its old table, <c>RecoveryQuestions</c>.
/// </para>
/// </summary>
public sealed class TypedCurriculumReads : ICurriculumReads
{
    private readonly ApplicationDbContext _db;

    public TypedCurriculumReads(ApplicationDbContext db) => _db = db;

    // ── browsing ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GradeDto>> GradesAsync(Guid langId, CancellationToken cancellationToken) =>
        await _db.Grades
            .AsNoTracking()
            .OrderBy(g => g.Order)
            .ThenBy(g => g.Id)
            .Select(g => new GradeDto
            {
                Id = g.Id,
                Name = g.Translations.Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                Order = g.Order
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TermDto>> TermsAsync(Guid? gradeId, Guid langId, CancellationToken cancellationToken)
    {
        var query = _db.Terms.AsNoTracking();
        if (gradeId is not null)
            query = query.Where(t => t.GradeId == gradeId.Value);

        return await query
            .OrderBy(t => t.Grade!.Order)
            .ThenBy(t => t.Order)
            .ThenBy(t => t.Id)
            .Select(t => new TermDto
            {
                Id = t.Id,
                Name = t.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                GradeId = t.GradeId,
                Order = t.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectDto>> SubjectsAsync(Guid? termId, Guid langId, CancellationToken cancellationToken)
    {
        var query = _db.Subjects.AsNoTracking();
        if (termId is not null)
            query = query.Where(s => s.TermId == termId.Value);

        return await query
            .OrderBy(s => s.Term!.Order)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.Id)
            .Select(s => new SubjectDto
            {
                Id = s.Id,
                Name = s.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                TermId = s.TermId,
                Order = s.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterDto>> ChaptersAsync(Guid subjectId, Guid langId, CancellationToken cancellationToken) =>
        await _db.Chapters
            .AsNoTracking()
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .Select(c => new ChapterDto
            {
                Id = c.Id,
                Name = c.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                SubjectId = c.SubjectId,
                Order = c.Order
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LessonDto>> LessonsAsync(Guid chapterId, Guid langId, CancellationToken cancellationToken) =>
        await _db.Lessons
            .AsNoTracking()
            .Where(l => l.ChapterId == chapterId)
            .OrderBy(l => l.Order)
            .ThenBy(l => l.Id)
            .Select(l => new LessonDto
            {
                Id = l.Id,
                Name = l.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                ChapterId = l.ChapterId,
                Order = l.Order,
                QuestionsVersion = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                HasQuestions = l.QuestionSets.Any(s => s.LangId == langId && s.Version > 0)
            })
            .ToListAsync(cancellationToken);

    // ── questions and their versions ────────────────────────────────────────────

    public async Task<IReadOnlyList<LessonVersionDto>> SetVersionsAsync(
        IReadOnlyList<Guid> lessonIds, NodeItemRole role, Guid langId, CancellationToken cancellationToken)
    {
        var ids = lessonIds.Distinct().ToList();
        var lessons = _db.Lessons.AsNoTracking().Where(l => ids.Contains(l.Id)).OrderBy(l => l.Id);

        if (role == NodeItemRole.Recovery)
        {
            return await lessons
                .Select(l => new LessonVersionDto
                {
                    LessonId = l.Id,
                    LangId = langId,
                    Version = l.RecoveryQuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                    QuestionCount = l.RecoveryQuestions.Count(q => q.IsActive && q.LangId == langId)
                })
                .ToListAsync(cancellationToken);
        }

        return await lessons
            .Select(l => new LessonVersionDto
            {
                LessonId = l.Id,
                LangId = langId,
                Version = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                QuestionCount = l.Questions.Count(q => q.IsActive && q.LangId == langId)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LessonQuestionsDto?> QuestionsAsync(
        Guid lessonId, NodeItemRole role, Guid langId, CancellationToken cancellationToken)
    {
        var recovery = role == NodeItemRole.Recovery;

        var lesson = await _db.Lessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new
            {
                l.Id,
                Version = recovery
                    ? l.RecoveryQuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()
                    : l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return null;

        var questions = recovery
            ? await _db.RecoveryQuestions
                .AsNoTracking()
                .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
                .OrderBy(q => q.RowNumber)
                .ThenBy(q => q.Id)
                .Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    CorrectAnswerId = q.CorrectChoiceId,
                    Answers = q.Choices.OrderBy(c => c.OrderIndex).Select(c => new AnswerDto { Id = c.Id, Text = c.Text }).ToList()
                })
                .ToListAsync(cancellationToken)
            : await _db.Questions
                .AsNoTracking()
                .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
                .OrderBy(q => q.RowNumber)
                .ThenBy(q => q.Id)
                .Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    CorrectAnswerId = q.CorrectChoiceId,
                    Answers = q.Choices.OrderBy(c => c.OrderIndex).Select(c => new AnswerDto { Id = c.Id, Text = c.Text }).ToList()
                })
                .ToListAsync(cancellationToken);

        return new LessonQuestionsDto { LessonId = lesson.Id, LangId = langId, Version = lesson.Version, Questions = questions };
    }

    // ── progress ────────────────────────────────────────────────────────────────

    public Task<bool> LessonExistsAsync(Guid lessonId, CancellationToken cancellationToken) =>
        _db.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken);

    public Task<LessonHeader?> LessonHeaderAsync(Guid lessonId, Guid langId, CancellationToken cancellationToken) =>
        _db.Lessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new LessonHeader(
                l.Id,
                l.Chapter!.Subject!.Term!.GradeId,
                l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LessonTotal>> LessonTotalsAsync(
        TreeScope scope, Guid nodeId, Guid langId, CancellationToken cancellationToken)
    {
        var lessons = _db.Lessons.AsNoTracking();

        lessons = scope switch
        {
            TreeScope.Chapter => lessons.Where(l => l.ChapterId == nodeId),
            TreeScope.Subject => lessons.Where(l => l.Chapter!.SubjectId == nodeId),
            TreeScope.Term => lessons.Where(l => l.Chapter!.Subject!.TermId == nodeId),
            TreeScope.Grade => lessons.Where(l => l.Chapter!.Subject!.Term!.GradeId == nodeId),
            _ => lessons.Where(l => l.Id == nodeId)
        };

        return await lessons
            .OrderBy(l => l.Id)
            .Select(l => new LessonTotal(l.Id, l.Questions.Count(q => q.IsActive && q.LangId == langId)))
            .ToListAsync(cancellationToken);
    }

    public Task<NamedNode?> GradeAsync(Guid gradeId, Guid langId, CancellationToken cancellationToken) =>
        _db.Grades
            .AsNoTracking()
            .Where(g => g.Id == gradeId)
            .Select(g => new NamedNode(
                g.Id,
                g.Translations.Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault() ?? string.Empty))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<GradeTree> GradeTreeAsync(Guid gradeId, Guid langId, CancellationToken cancellationToken)
    {
        var terms = await _db.Terms
            .AsNoTracking()
            .Where(t => t.GradeId == gradeId)
            .OrderBy(t => t.Order)
            .ThenBy(t => t.Id)
            .Select(t => new TreeNodeRow(
                t.Id, t.GradeId, t.Order,
                t.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);

        var subjects = await _db.Subjects
            .AsNoTracking()
            .Where(s => s.Term!.GradeId == gradeId)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Id)
            .Select(s => new TreeNodeRow(
                s.Id, s.TermId, s.Order,
                s.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);

        var chapters = await _db.Chapters
            .AsNoTracking()
            .Where(c => c.Subject!.Term!.GradeId == gradeId)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .Select(c => new TreeNodeRow(
                c.Id, c.SubjectId, c.Order,
                c.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);

        var lessons = await _db.Lessons
            .AsNoTracking()
            .Where(l => l.Chapter!.Subject!.Term!.GradeId == gradeId)
            .OrderBy(l => l.Order)
            .ThenBy(l => l.Id)
            .Select(l => new TreeLessonRow(
                l.Id, l.ChapterId, l.Order,
                l.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                l.Questions.Count(q => q.IsActive && q.LangId == langId),
                l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new GradeTree(terms, subjects, chapters, lessons);
    }

    // ── unlocks ─────────────────────────────────────────────────────────────────

    public async Task<Guid?> FirstTermAsync(Guid gradeId, CancellationToken cancellationToken) =>
        await _db.Terms
            .AsNoTracking()
            .Where(t => t.GradeId == gradeId)
            .OrderBy(t => t.Order)
            .ThenBy(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<LessonLocation?> LessonLocationAsync(Guid lessonId, CancellationToken cancellationToken) =>
        _db.Lessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new LessonLocation(
                l.Order,
                l.ChapterId,
                l.Chapter!.Order,
                l.Chapter!.SubjectId,
                l.Chapter!.Subject!.TermId,
                l.Chapter!.Subject!.Term!.Order,
                l.Chapter!.Subject!.Term!.GradeId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TermLesson>> LessonsInTermAsync(Guid termId, Guid langId, CancellationToken cancellationToken) =>
        await _db.Lessons
            .AsNoTracking()
            .Where(l => l.Chapter!.Subject!.TermId == termId)
            .OrderBy(l => l.Id)
            .Select(l => new TermLesson(
                l.Id, l.Order, l.ChapterId,
                l.QuestionSets.Any(s => s.LangId == langId && s.Version > 0)))
            .ToListAsync(cancellationToken);

    public async Task<Guid?> NextChapterAsync(Guid subjectId, int afterOrder, CancellationToken cancellationToken) =>
        await _db.Chapters
            .AsNoTracking()
            .Where(c => c.SubjectId == subjectId && c.Order > afterOrder)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> NextTermAsync(Guid gradeId, int afterOrder, CancellationToken cancellationToken) =>
        await _db.Terms
            .AsNoTracking()
            .Where(t => t.GradeId == gradeId && t.Order > afterOrder)
            .OrderBy(t => t.Order)
            .ThenBy(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> SubjectsOfTermsAsync(IReadOnlyList<Guid> termIds, CancellationToken cancellationToken)
    {
        var ids = termIds.ToList();
        return await _db.Subjects
            .AsNoTracking()
            .Where(s => ids.Contains(s.TermId))
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChildRow>> ChaptersOfSubjectsAsync(IReadOnlyList<Guid> subjectIds, CancellationToken cancellationToken)
    {
        var ids = subjectIds.ToList();
        return await _db.Chapters
            .AsNoTracking()
            .Where(c => ids.Contains(c.SubjectId))
            .OrderBy(c => c.Id)
            .Select(c => new ChildRow(c.Id, c.SubjectId, c.Order))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChildRow>> LessonsOfChaptersAsync(IReadOnlyList<Guid> chapterIds, CancellationToken cancellationToken)
    {
        var ids = chapterIds.ToList();
        return await _db.Lessons
            .AsNoTracking()
            .Where(l => ids.Contains(l.ChapterId))
            .OrderBy(l => l.Id)
            .Select(l => new ChildRow(l.Id, l.ChapterId, l.Order))
            .ToListAsync(cancellationToken);
    }

    // ── subject matchmaking ─────────────────────────────────────────────────────

    public async Task<Guid?> GradeOfSubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        await _db.Subjects
            .AsNoTracking()
            .Where(s => s.Id == subjectId)
            .Select(s => (Guid?)s.Term!.GradeId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EligibleLesson>> EligibleLessonsAsync(
        Guid userId, Guid gameId, Guid subjectId, Guid langId, CancellationToken cancellationToken) =>
        await _db.Lessons
            .AsNoTracking()
            .Where(l => l.Chapter!.SubjectId == subjectId)
            .Where(l => _db.UserNodeUnlocks.Any(u =>
                u.UserId == userId
                && u.GameId == gameId
                && u.NodeType == CurriculumNodeType.Lesson
                && u.NodeId == l.Id))
            .Where(l => _db.Questions.Any(q => q.LessonId == l.Id && q.LangId == langId && q.IsActive))
            .OrderBy(l => l.Id)
            .Select(l => new EligibleLesson(
                l.Id,
                _db.UserLessonProgress
                    .Where(p => p.UserId == userId && p.GameId == gameId && p.LessonId == l.Id)
                    .Select(p => p.BestPercent)
                    .FirstOrDefault(),
                l.Chapter!.Order,
                l.Order))
            .ToListAsync(cancellationToken);
}
