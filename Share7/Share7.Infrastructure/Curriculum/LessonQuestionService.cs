using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Content;
using Share7.Infrastructure.Engine.Reads;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Read side of the question cache protocol. Every lookup is scoped to the caller's content
/// language: a lesson is one shared row, but its questions and their version are per language.
/// <para>
/// A lesson with nothing published in a language answers version 0 and an empty list — the lesson
/// exists and is named, it just is not playable in that language yet. Unknown lesson ids are left
/// out of batch answers and are a 404 on their own.
/// </para>
/// </summary>
public class LessonQuestionService : ILessonQuestionService
{
    private readonly ICurriculumReads _reads;
    private readonly ILanguageService _languageService;

    public LessonQuestionService(ICurriculumReads reads, ILanguageService languageService)
    {
        _reads = reads;
        _languageService = languageService;
    }

    public async Task<LessonVersionDto?> GetVersionAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var versions = await _reads.SetVersionsAsync([lessonId], NodeItemRole.Core, langId, cancellationToken);
        return versions.FirstOrDefault();
    }

    public async Task<IReadOnlyList<LessonVersionDto>> GetVersionsAsync(
        IEnumerable<Guid> lessonIds,
        CancellationToken cancellationToken = default)
    {
        var ids = lessonIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        return await _reads.SetVersionsAsync(ids, NodeItemRole.Core, langId, cancellationToken);
    }

    public async Task<LessonQuestionsDto?> GetQuestionsAsync(Guid lessonId, CancellationToken cancellationToken = default) =>
        await GetQuestionsAsync(lessonId, await _languageService.ResolveCurrentAsync(cancellationToken), cancellationToken);

    public Task<LessonQuestionsDto?> GetQuestionsAsync(
        Guid lessonId,
        Guid langId,
        CancellationToken cancellationToken = default) =>
        _reads.QuestionsAsync(lessonId, NodeItemRole.Core, langId, cancellationToken);
}
