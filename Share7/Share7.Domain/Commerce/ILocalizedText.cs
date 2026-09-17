namespace Share7.Domain.Commerce;

/// <summary>
/// A row of shop text in one language. Implemented by every commerce <c>*Translation</c> so one
/// resolver can pick the right row for a caller instead of each service repeating the fallback
/// chain slightly differently.
/// </summary>
public interface ILocalizedText
{
    Guid LangId { get; }
    string Name { get; }
    string? Description { get; }
}
