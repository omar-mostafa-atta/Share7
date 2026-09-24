using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Content;
using Share7.Infrastructure.Engine.Reads;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Read side of the recovery-question cache protocol — the mirror of
/// <see cref="LessonQuestionService"/> over the secondary pool. Every lookup is scoped to the
/// caller's content language: a lesson is one shared row, but its recovery questions and their
/// version are per language, exactly as the main pool's are.
/// <para>
/// The pool now lives in the item bank (<c>Questions</c>, role Recovery) under the ids it always
/// had; <see cref="ICurriculumReads"/> decides whether that or the old table answers.
/// </para>
/// </summary>
public class LessonRecoveryQuestionService : ILessonRecoveryQuestionService
{
    private readonly ICurriculumReads _reads;
    private readonly ILanguageService _languageService;

    public LessonRecoveryQuestionService(ICurriculumReads reads, ILanguageService languageService)
    {
        _reads = reads;
        _languageService = languageService;
    }

    public async Task<LessonVersionDto?> GetVersionAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var versions = await _reads.SetVersionsAsync([lessonId], NodeItemRole.Recovery, langId, cancellationToken);
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
        return await _reads.SetVersionsAsync(ids, NodeItemRole.Recovery, langId, cancellationToken);
    }

    public async Task<LessonQuestionsDto?> GetQuestionsAsync(Guid lessonId, CancellationToken cancellationToken = default) =>
        await GetQuestionsAsync(lessonId, await _languageService.ResolveCurrentAsync(cancellationToken), cancellationToken);

    public Task<LessonQuestionsDto?> GetQuestionsAsync(
        Guid lessonId,
        Guid langId,
        CancellationToken cancellationToken = default) =>
        _reads.QuestionsAsync(lessonId, NodeItemRole.Recovery, langId, cancellationToken);
}
