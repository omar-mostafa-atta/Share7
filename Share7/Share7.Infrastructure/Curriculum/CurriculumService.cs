using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Engine.Reads;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Browse the curriculum tree. The tree itself is language-independent — these methods do not
/// filter rows by language, they resolve each node's <i>name</i> into the caller's language.
/// A node with no translation in that language comes back with an empty name rather than
/// disappearing, which makes a missing translation visible instead of silently truncating
/// the tree.
/// <para>
/// Which tables answer is <see cref="ICurriculumReads"/>'s business (Curriculum:ReadModel). The
/// shapes, orderings and rules here are the game's contract and do not change with it: unfiltered
/// terms come back grouped by their grade's ladder position; unfiltered subjects by their term's;
/// a lesson's <c>questionsVersion</c> and <c>hasQuestions</c> are per language, because a lesson can
/// be playable in English and not yet in Arabic.
/// </para>
/// </summary>
public class CurriculumService : ICurriculumService
{
    private readonly ICurriculumReads _reads;
    private readonly ILanguageService _languageService;

    public CurriculumService(ICurriculumReads reads, ILanguageService languageService)
    {
        _reads = reads;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<TermDto>> GetTermsAsync(Guid? gradeId = null, CancellationToken cancellationToken = default) =>
        await _reads.TermsAsync(gradeId, await _languageService.ResolveCurrentAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(Guid? termId = null, CancellationToken cancellationToken = default) =>
        await _reads.SubjectsAsync(termId, await _languageService.ResolveCurrentAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<ChapterDto>> GetChaptersAsync(Guid subjectId, CancellationToken cancellationToken = default) =>
        await _reads.ChaptersAsync(subjectId, await _languageService.ResolveCurrentAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<LessonDto>> GetLessonsAsync(Guid chapterId, CancellationToken cancellationToken = default) =>
        await _reads.LessonsAsync(chapterId, await _languageService.ResolveCurrentAsync(cancellationToken), cancellationToken);
}
