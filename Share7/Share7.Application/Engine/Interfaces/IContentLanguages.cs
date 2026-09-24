namespace Share7.Application.Engine.Interfaces;

/// <summary>
/// The languages content is written in, read from the <c>Languages</c> table rather than from
/// constants — so a third language is a row, not a release.
/// </summary>
public interface IContentLanguages
{
    /// <summary>Every known language, in editor column order (English 1, Arabic 2).</summary>
    Task<IReadOnlyList<ContentLanguage>> GetAsync(CancellationToken cancellationToken = default);
}

/// <param name="RequiredToPublish">A Studio release refuses a lesson missing this language.</param>
/// <param name="Direction"><c>ltr</c> or <c>rtl</c>.</param>
public sealed record ContentLanguage(
    Guid Id,
    string Code,
    string Name,
    bool IsContentLanguage,
    bool RequiredToPublish,
    string Direction,
    int SortOrder);
