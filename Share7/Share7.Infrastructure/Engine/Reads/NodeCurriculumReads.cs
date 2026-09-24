using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Progress;
using Share7.Domain.Structure;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine.Reads;

/// <summary>
/// The game's curriculum reads from the source of truth: the node tree, its translations, the
/// published sets and the item bank (every pool in <c>Questions</c>).
/// <para>
/// **Only the Egyptian national tree is served to the game**, whatever else the node table comes to
/// hold: the current Unity build plays exactly one shape (grade → term → subject → chapter →
/// lesson), and other curricula are stored but "not playable yet".
/// </para>
/// </summary>
public sealed class NodeCurriculumReads : ICurriculumReads
{
    private static readonly Guid Served = EducationIds.EgyptianNationalAsMigrated;

    private readonly ApplicationDbContext _db;

    public NodeCurriculumReads(ApplicationDbContext db) => _db = db;

    private IQueryable<CurriculumNode> Live(string kind) =>
        _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.CurriculumVersionId == Served && n.KindKey == kind && n.RetiredAtUtc == null);

    // ── browsing ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GradeDto>> GradesAsync(Guid langId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Grade)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new GradeDto
            {
                Id = n.Id,
                Name = n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                Order = n.Order
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TermDto>> TermsAsync(Guid? gradeId, Guid langId, CancellationToken cancellationToken)
    {
        var query = Live(NodeKinds.Term);
        if (gradeId is not null)
            query = query.Where(n => n.ParentNodeId == gradeId.Value);

        return await query
            .OrderBy(n => n.Parent!.Order)
            .ThenBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new TermDto
            {
                Id = n.Id,
                Name = n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                GradeId = n.ParentNodeId!.Value,
                Order = n.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectDto>> SubjectsAsync(Guid? termId, Guid langId, CancellationToken cancellationToken)
    {
        var query = Live(NodeKinds.Subject);
        if (termId is not null)
            query = query.Where(n => n.ParentNodeId == termId.Value);

        return await query
            .OrderBy(n => n.Parent!.Order)
            .ThenBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new SubjectDto
            {
                Id = n.Id,
                Name = n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                TermId = n.ParentNodeId!.Value,
                Order = n.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterDto>> ChaptersAsync(Guid subjectId, Guid langId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Chapter)
            .Where(n => n.ParentNodeId == subjectId)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new ChapterDto
            {
                Id = n.Id,
                Name = n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                SubjectId = n.ParentNodeId!.Value,
                Order = n.Order
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LessonDto>> LessonsAsync(Guid chapterId, Guid langId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Lesson)
            .Where(n => n.ParentNodeId == chapterId)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new LessonDto
            {
                Id = n.Id,
                Name = n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                ChapterId = n.ParentNodeId!.Value,
                Order = n.Order,
                QuestionsVersion = _db.PublishedItemSets.Where(s => s.NodeId == n.Id && s.Role == NodeItemRole.Core && s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                HasQuestions = _db.PublishedItemSets.Any(s => s.NodeId == n.Id && s.Role == NodeItemRole.Core && s.LangId == langId && s.Version > 0)
            })
            .ToListAsync(cancellationToken);

    // ── questions and their versions ────────────────────────────────────────────

    public async Task<IReadOnlyList<LessonVersionDto>> SetVersionsAsync(
        IReadOnlyList<Guid> lessonIds, NodeItemRole role, Guid langId, CancellationToken cancellationToken)
    {
        var ids = lessonIds.Distinct().ToList();

        return await Live(NodeKinds.Lesson)
            .Where(n => ids.Contains(n.Id))
            .OrderBy(n => n.Id)
            .Select(n => new LessonVersionDto
            {
                LessonId = n.Id,
                LangId = langId,
                Version = _db.PublishedItemSets.Where(s => s.NodeId == n.Id && s.Role == role && s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                QuestionCount = _db.ItemLocalizations.Count(q =>
                    q.LessonId == n.Id && q.Role == role && q.LangId == langId && q.IsActive)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LessonQuestionsDto?> QuestionsAsync(
        Guid lessonId, NodeItemRole role, Guid langId, CancellationToken cancellationToken)
    {
        var lesson = await Live(NodeKinds.Lesson)
            .Where(n => n.Id == lessonId)
            .Select(n => new { n.Id, Version = _db.PublishedItemSets.Where(s => s.NodeId == n.Id && s.Role == role && s.LangId == langId).Select(s => s.Version).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return null;

        var questions = await _db.ItemLocalizations
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.Role == role && q.LangId == langId && q.IsActive)
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
        Live(NodeKinds.Lesson).AnyAsync(n => n.Id == lessonId, cancellationToken);

    public Task<LessonHeader?> LessonHeaderAsync(Guid lessonId, Guid langId, CancellationToken cancellationToken) =>
        Live(NodeKinds.Lesson)
            .Where(n => n.Id == lessonId)
            .Select(n => new LessonHeader(
                n.Id,
                n.Parent!.Parent!.Parent!.ParentNodeId!.Value,
                _db.PublishedItemSets.Where(s => s.NodeId == n.Id && s.Role == NodeItemRole.Core && s.LangId == langId).Select(s => s.Version).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LessonTotal>> LessonTotalsAsync(
        TreeScope scope, Guid nodeId, Guid langId, CancellationToken cancellationToken)
    {
        IQueryable<CurriculumNode> lessons;

        if (scope == TreeScope.Lesson)
        {
            lessons = Live(NodeKinds.Lesson).Where(n => n.Id == nodeId);
        }
        else
        {
            // The scope node must be of the scope's own kind — asking for a chapter's lessons with a
            // subject's id finds none, as it always did, rather than everything under the subject.
            var kind = scope switch
            {
                TreeScope.Chapter => NodeKinds.Chapter,
                TreeScope.Subject => NodeKinds.Subject,
                TreeScope.Term => NodeKinds.Term,
                _ => NodeKinds.Grade
            };

            var path = await _db.CurriculumNodes.AsNoTracking()
                .Where(n => n.Id == nodeId && n.KindKey == kind && n.CurriculumVersionId == Served)
                .Select(n => n.Path)
                .FirstOrDefaultAsync(cancellationToken);

            if (path is null)
                return [];

            var prefix = path + "/";
            lessons = Live(NodeKinds.Lesson).Where(n => n.Path.StartsWith(prefix));
        }

        return await lessons
            .OrderBy(n => n.Id)
            .Select(n => new LessonTotal(n.Id, _db.Questions.Count(q => q.LessonId == n.Id && q.IsActive && q.LangId == langId)))
            .ToListAsync(cancellationToken);
    }

    public Task<NamedNode?> GradeAsync(Guid gradeId, Guid langId, CancellationToken cancellationToken) =>
        Live(NodeKinds.Grade)
            .Where(n => n.Id == gradeId)
            .Select(n => new NamedNode(
                n.Id,
                n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<GradeTree> GradeTreeAsync(Guid gradeId, Guid langId, CancellationToken cancellationToken)
    {
        var gradePath = await Live(NodeKinds.Grade)
            .Where(n => n.Id == gradeId)
            .Select(n => n.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (gradePath is null)
            return new GradeTree([], [], [], []);

        var prefix = gradePath + "/";

        var terms = await Row(Live(NodeKinds.Term).Where(n => n.ParentNodeId == gradeId), langId, cancellationToken);
        var subjects = await Row(Live(NodeKinds.Subject).Where(n => n.Path.StartsWith(prefix)), langId, cancellationToken);
        var chapters = await Row(Live(NodeKinds.Chapter).Where(n => n.Path.StartsWith(prefix)), langId, cancellationToken);

        var lessons = await Live(NodeKinds.Lesson)
            .Where(n => n.Path.StartsWith(prefix))
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new TreeLessonRow(
                n.Id,
                n.ParentNodeId!.Value,
                n.Order,
                n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty,
                _db.Questions.Count(q => q.LessonId == n.Id && q.IsActive && q.LangId == langId),
                _db.PublishedItemSets.Where(s => s.NodeId == n.Id && s.Role == NodeItemRole.Core && s.LangId == langId).Select(s => s.Version).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new GradeTree(terms, subjects, chapters, lessons);
    }

    private static Task<List<TreeNodeRow>> Row(IQueryable<CurriculumNode> nodes, Guid langId, CancellationToken cancellationToken) =>
        nodes
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => new TreeNodeRow(
                n.Id,
                n.ParentNodeId!.Value,
                n.Order,
                n.Translations.Where(t => t.LangId == langId).Select(t => t.Title).FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);

    // ── unlocks ─────────────────────────────────────────────────────────────────

    public async Task<Guid?> FirstTermAsync(Guid gradeId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Term)
            .Where(n => n.ParentNodeId == gradeId)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<LessonLocation?> LessonLocationAsync(Guid lessonId, CancellationToken cancellationToken) =>
        Live(NodeKinds.Lesson)
            .Where(n => n.Id == lessonId)
            .Select(n => new LessonLocation(
                n.Order,
                n.ParentNodeId!.Value,
                n.Parent!.Order,
                n.Parent!.ParentNodeId!.Value,
                n.Parent!.Parent!.ParentNodeId!.Value,
                n.Parent!.Parent!.Parent!.Order,
                n.Parent!.Parent!.Parent!.ParentNodeId!.Value))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TermLesson>> LessonsInTermAsync(Guid termId, Guid langId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Lesson)
            .Where(n => n.Parent!.Parent!.ParentNodeId == termId && n.Parent!.RetiredAtUtc == null && n.Parent!.Parent!.RetiredAtUtc == null)
            .OrderBy(n => n.Id)
            .Select(n => new TermLesson(
                n.Id, n.Order, n.ParentNodeId!.Value,
                _db.PublishedItemSets.Any(s => s.NodeId == n.Id && s.Role == NodeItemRole.Core && s.LangId == langId && s.Version > 0)))
            .ToListAsync(cancellationToken);

    public async Task<Guid?> NextChapterAsync(Guid subjectId, int afterOrder, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Chapter)
            .Where(n => n.ParentNodeId == subjectId && n.Order > afterOrder)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> NextTermAsync(Guid gradeId, int afterOrder, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Term)
            .Where(n => n.ParentNodeId == gradeId && n.Order > afterOrder)
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> SubjectsOfTermsAsync(IReadOnlyList<Guid> termIds, CancellationToken cancellationToken)
    {
        var ids = termIds.ToList();
        return await Live(NodeKinds.Subject)
            .Where(n => ids.Contains(n.ParentNodeId!.Value))
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChildRow>> ChaptersOfSubjectsAsync(IReadOnlyList<Guid> subjectIds, CancellationToken cancellationToken)
    {
        var ids = subjectIds.ToList();
        return await Live(NodeKinds.Chapter)
            .Where(n => ids.Contains(n.ParentNodeId!.Value))
            .OrderBy(n => n.Id)
            .Select(n => new ChildRow(n.Id, n.ParentNodeId!.Value, n.Order))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChildRow>> LessonsOfChaptersAsync(IReadOnlyList<Guid> chapterIds, CancellationToken cancellationToken)
    {
        var ids = chapterIds.ToList();
        return await Live(NodeKinds.Lesson)
            .Where(n => ids.Contains(n.ParentNodeId!.Value))
            .OrderBy(n => n.Id)
            .Select(n => new ChildRow(n.Id, n.ParentNodeId!.Value, n.Order))
            .ToListAsync(cancellationToken);
    }

    // ── subject matchmaking ─────────────────────────────────────────────────────

    public async Task<Guid?> GradeOfSubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Subject)
            .Where(n => n.Id == subjectId)
            .Select(n => n.Parent!.ParentNodeId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EligibleLesson>> EligibleLessonsAsync(
        Guid userId, Guid gameId, Guid subjectId, Guid langId, CancellationToken cancellationToken) =>
        await Live(NodeKinds.Lesson)
            .Where(n => n.Parent!.ParentNodeId == subjectId && n.Parent!.RetiredAtUtc == null)
            .Where(n => _db.UserNodeUnlocks.Any(u =>
                u.UserId == userId
                && u.GameId == gameId
                && u.NodeType == CurriculumNodeType.Lesson
                && u.NodeId == n.Id))
            .Where(n => _db.Questions.Any(q => q.LessonId == n.Id && q.LangId == langId && q.IsActive))
            .OrderBy(n => n.Id)
            .Select(n => new EligibleLesson(
                n.Id,
                _db.UserLessonProgress
                    .Where(p => p.UserId == userId && p.GameId == gameId && p.LessonId == n.Id)
                    .Select(p => p.BestPercent)
                    .FirstOrDefault(),
                n.Parent!.Order,
                n.Order))
            .ToListAsync(cancellationToken);
}
