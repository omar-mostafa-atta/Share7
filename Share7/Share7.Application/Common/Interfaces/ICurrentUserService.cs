namespace Share7.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }

    /// <summary>
    /// Content language from the access token claim, or null when the token predates the
    /// claim. Callers should fall back to the database in that case rather than assuming.
    /// </summary>
    Guid? PreferredLanguageId { get; }
}
