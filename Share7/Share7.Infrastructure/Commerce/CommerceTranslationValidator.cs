using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

/// <summary>One language's validated, trimmed text.</summary>
internal record CommerceName(Guid LangId, string Name, string? Description);

/// <summary>
/// Checks a <c>translations[]</c> array the way the curriculum admin service does: a name is
/// required for **every** configured language, no language twice, no unknown language.
/// <para>
/// The rule is strict on purpose. A product missing its Arabic name is not a cosmetic gap — it is a
/// shop entry an Arabic student cannot read, and the backend has no fallback rule to hide it behind.
/// Refusing at authoring time is the only place it is cheap to fix.
/// </para>
/// </summary>
internal static class CommerceTranslationValidator
{
    public static async Task<ServiceResult<List<CommerceName>>> ValidateAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<CommerceTranslationRequest>? supplied,
        ApiErrorCode error,
        int nameMaxLength,
        CancellationToken cancellationToken)
    {
        var entries = supplied ?? [];
        var errors = new List<string>();
        var names = new List<CommerceName>();

        foreach (var entry in entries)
        {
            var name = (entry.Name ?? string.Empty).Trim();
            var description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim();

            if (name.Length == 0)
                errors.Add($"A name is required for language {entry.LangId}.");
            else if (name.Length > nameMaxLength)
                errors.Add($"The name for language {entry.LangId} is {name.Length} characters, above the {nameMaxLength} limit.");
            else
                names.Add(new CommerceName(entry.LangId, name, description));
        }

        if (entries.Select(t => t.LangId).Distinct().Count() != entries.Count)
            errors.Add("The same language appears more than once.");

        var languages = await dbContext.Languages
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
            ? ServiceResult<List<CommerceName>>.Failure(
                error,
                ServiceErrorKind.Validation,
                string.Join(" ", errors),
                new Dictionary<string, object?> { ["missingLanguages"] = missing })
            : ServiceResult<List<CommerceName>>.Success(names);
    }

    /// <summary>
    /// Picks the row to show this caller: **their language, then English, then whatever exists.**
    /// <para>
    /// The English step is what the fallback rule actually asks for. The last step only fires for
    /// rows written before every language was mandatory — a shop entry showing *something* beats one
    /// showing a blank name, and blanking it would hide the offer rather than flag the gap.
    /// </para>
    /// </summary>
    public static (string Name, string? Description) Resolve<T>(IEnumerable<T> translations, Guid langId)
        where T : ILocalizedText
    {
        var rows = translations as IReadOnlyCollection<T> ?? translations.ToList();

        var row = rows.FirstOrDefault(t => t.LangId == langId)
                  ?? rows.FirstOrDefault(t => t.LangId == LanguageIds.English)
                  ?? rows.FirstOrDefault();

        return (row?.Name ?? string.Empty, row?.Description);
    }

    /// <summary>Ordered by language code so repeated reads are byte-identical.</summary>
    public static IReadOnlyList<CommerceTranslationDto> ToDtos<T>(
        IEnumerable<T> translations,
        Func<T, Guid> langId,
        Func<T, string> name,
        Func<T, string?> description,
        IReadOnlyDictionary<Guid, string> languageCodes) =>
        translations
            .Select(t => new CommerceTranslationDto
            {
                LangId = langId(t),
                LangCode = languageCodes.GetValueOrDefault(langId(t), string.Empty),
                Name = name(t),
                Description = description(t)
            })
            .OrderBy(t => t.LangCode, StringComparer.Ordinal)
            .ToList();
}
