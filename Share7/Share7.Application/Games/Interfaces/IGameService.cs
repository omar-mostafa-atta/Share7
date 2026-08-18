using Share7.Application.Games.Models;

namespace Share7.Application.Games.Interfaces;

/// <summary>
/// Read side of the game catalog. Names are resolved into the caller's content language; the
/// game id is the same regardless of language.
/// </summary>
public interface IGameService
{
    /// <param name="includeInactive">Admin listings pass true; the client should not.</param>
    Task<IReadOnlyList<GameDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>Null when no game has that id.</summary>
    Task<GameDto?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken = default);
}
