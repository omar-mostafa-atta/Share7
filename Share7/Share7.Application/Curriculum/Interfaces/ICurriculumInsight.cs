using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// Counts the curriculum and reports what is wrong with it.
/// <para>
/// Read-only by design. Everything it finds is a decision an author has to make — an empty chapter
/// is either next week's work or last week's mistake, and nothing here can tell which — so it never
/// repairs, only names.
/// </para>
/// </summary>
public interface ICurriculumHealthService
{
    Task<CurriculumHealthDto> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Questions across a whole branch of the tree rather than one lesson at a time.
/// <para>
/// <b>Why the tree needs a flat view at all.</b> Navigating to a question means five clicks, and the
/// questions an author actually wants together are rarely in one lesson: everything in a term,
/// everything untranslated in a subject, every recovery question in a grade. The tree answers "what
/// is under this node"; this answers "what is <i>in</i> everything under this node", which is a
/// different question and was previously unanswerable without opening every lesson in turn.
/// </para>
/// </summary>
public interface ICurriculumSearchService
{
    /// <summary>
    /// Every question under <paramref name="request"/>'s scope, paired by language and paged.
    /// </summary>
    Task<QuestionSearchResultDto> SearchAsync(
        QuestionSearchRequest request, CancellationToken cancellationToken = default);
}
