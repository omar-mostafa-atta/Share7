using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

public interface ILanguageService
{
    Task<IReadOnlyList<LanguageDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Content language for the current caller. Reads the access-token claim first (no
    /// database round trip); falls back to a lookup only when the token has no claim, and
    /// to English when there is no user at all.
    /// </summary>
    Task<Guid> ResolveCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the language for a specific user from the database, ignoring any claim.
    /// </summary>
    Task<Guid> ResolveForUserAsync(Guid? userId, CancellationToken cancellationToken = default);

    /// <summary>Stores the user's content language. Returns false if the language id is unknown.</summary>
    Task<bool> SetPreferredLanguageAsync(Guid userId, Guid langId, CancellationToken cancellationToken = default);
}
