using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <inheritdoc cref="ICurriculumSearchService"/>
public class CurriculumSearchService : ICurriculumSearchService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public CurriculumSearchService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    private const int MaxPageSize = 200;

    public async Task<QuestionSearchResultDto> SearchAsync(
        QuestionSearchRequest request, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var lessonIds = await ResolveScopeAsync(request.ScopeLevel, request.ScopeId, cancellationToken);
        if (lessonIds is { Count: 0 })
            return Empty(request);

        // ── the rows ─────────────────────────────────────────────────────────
        var main = request.Pool == QuestionPoolFilter.Recovery
            ? []
            : await LoadMainAsync(lessonIds, cancellationToken);

        var recovery = request.Pool == QuestionPoolFilter.Main
            ? []
            : await LoadRecoveryAsync(lessonIds, cancellationToken);

        var items = new List<QuestionSearchItemDto>();
        Fold(main, isRecovery: false, items);
        Fold(recovery, isRecovery: true, items);

        if (request.OnlyUnpaired)
            items = [.. items.Where(i => i.IsUnpaired)];

        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // In memory rather than in SQL. The candidate set is already narrowed by scope, and the
            // alternative is four LIKE clauses over an nvarchar(max) column with no index behind it —
            // which is a table scan wearing a WHERE clause.
            items = [.. items.Where(i => Matches(i, term))];
        }

        var total = items.Count;

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var lessonNames = await TrailsAsync(
            items.Select(i => i.LessonId).Distinct().ToList(), langId, cancellationToken);

        var pageItems = items
            .OrderBy(i => string.Join(" ", lessonNames.GetValueOrDefault(i.LessonId) ?? []), StringComparer.Ordinal)
            .ThenBy(i => i.IsRecovery)
            .ThenBy(i => i.RowNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        foreach (var item in pageItems)
            item.Path = lessonNames.GetValueOrDefault(item.LessonId) ?? [];

        return new QuestionSearchResultDto
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            LessonCount = items.Select(i => i.LessonId).Distinct().Count(),
            Items = pageItems
        };
    }

    /// <summary>
    /// The lessons under a node.
    /// <para>
    /// Null means "the whole curriculum" and is distinct from an empty list, which means "that node
    /// exists and has no lessons". Collapsing the two would turn a scoped search of an empty chapter
    /// into an unscoped search of everything — the most surprising possible answer.
    /// </para>
    /// </summary>
    private async Task<List<Guid>?> ResolveScopeAsync(
        string? level, Guid? id, CancellationToken cancellationToken)
    {
        if (id is not { } scopeId || string.IsNullOrWhiteSpace(level))
            return null;

        var lessons = _dbContext.Lessons.AsNoTracking();

        var query = level.Trim().ToLowerInvariant() switch
        {
            "lesson" => lessons.Where(l => l.Id == scopeId),
            "chapter" => lessons.Where(l => l.ChapterId == scopeId),
            "subject" => lessons.Where(l => l.Chapter!.SubjectId == scopeId),
            "term" => lessons.Where(l => l.Chapter!.Subject!.TermId == scopeId),
            "grade" => lessons.Where(l => l.Chapter!.Subject!.Term!.GradeId == scopeId),
            _ => null
        };

        return query is null ? null : await query.Select(l => l.Id).ToListAsync(cancellationToken);
    }

    private async Task<List<Loaded>> LoadMainAsync(List<Guid>? lessonIds, CancellationToken cancellationToken)
    {
        var query = _dbContext.Questions.AsNoTracking().Where(q => q.IsActive);
        if (lessonIds is not null) query = query.Where(q => lessonIds.Contains(q.LessonId));

        return await query
            .Select(q => new Loaded(
                q.LessonId, q.RowNumber, q.LangId, q.Text,
                q.Choices.Where(c => c.Id == q.CorrectChoiceId).Select(c => c.Text).FirstOrDefault(),
                q.Choices.Select(c => c.Text).ToList()))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Loaded>> LoadRecoveryAsync(List<Guid>? lessonIds, CancellationToken cancellationToken)
    {
        var query = _dbContext.RecoveryQuestions.AsNoTracking().Where(q => q.IsActive);
        if (lessonIds is not null) query = query.Where(q => lessonIds.Contains(q.LessonId));

        return await query
            .Select(q => new Loaded(
                q.LessonId, q.RowNumber, q.LangId, q.Text,
                q.Choices.Where(c => c.Id == q.CorrectChoiceId).Select(c => c.Text).FirstOrDefault(),
                q.Choices.Select(c => c.Text).ToList()))
            .ToListAsync(cancellationToken);
    }

    private static void Fold(List<Loaded> loaded, bool isRecovery, List<QuestionSearchItemDto> into)
    {
        foreach (var group in loaded.GroupBy(q => (q.LessonId, q.RowNumber)))
        {
            var english = group.FirstOrDefault(q => q.LangId == En);
            var arabic = group.FirstOrDefault(q => q.LangId == Ar);

            into.Add(new QuestionSearchItemDto
            {
                LessonId = group.Key.LessonId,
                RowNumber = group.Key.RowNumber,
                IsRecovery = isRecovery,
                QuestionEn = english?.Text ?? string.Empty,
                CorrectEn = english?.Correct ?? string.Empty,
                QuestionAr = arabic?.Text ?? string.Empty,
                CorrectAr = arabic?.Correct ?? string.Empty,
                IsUnpaired = english is null || arabic is null,
                Choices = [.. group.SelectMany(q => q.Choices)]
            });
        }
    }

    private static bool Matches(QuestionSearchItemDto item, string term) =>
        item.QuestionEn.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.QuestionAr.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Choices.Any(c => c.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>Grade → lesson for every lesson that has a match, in one read per level.</summary>
    private async Task<Dictionary<Guid, List<string>>> TrailsAsync(
        List<Guid> lessonIds, Guid langId, CancellationToken cancellationToken)
    {
        if (lessonIds.Count == 0) return [];

        var rows = await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => lessonIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                Grade = l.Chapter!.Subject!.Term!.Grade!.Translations
                    .Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault(),
                Term = l.Chapter!.Subject!.Term!.Translations
                    .Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault(),
                Subject = l.Chapter!.Subject!.Translations
                    .Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault(),
                Chapter = l.Chapter!.Translations
                    .Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault(),
                Lesson = l.Translations
                    .Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => new[] { r.Grade, r.Term, r.Subject, r.Chapter, r.Lesson }
                .Select(name => string.IsNullOrWhiteSpace(name) ? "—" : name)
                .ToList());
    }

    private static QuestionSearchResultDto Empty(QuestionSearchRequest request) => new()
    {
        Total = 0,
        Page = Math.Max(1, request.Page),
        PageSize = Math.Clamp(request.PageSize, 1, MaxPageSize),
        LessonCount = 0,
        Items = []
    };

    private sealed record Loaded(
        Guid LessonId, int RowNumber, Guid LangId, string Text, string? Correct, List<string> Choices);
}
