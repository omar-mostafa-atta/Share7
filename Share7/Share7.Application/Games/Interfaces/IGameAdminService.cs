using Share7.Application.Common.Models;
using Share7.Application.Games.Models;

namespace Share7.Application.Games.Interfaces;

public interface IGameAdminService
{
    /// <summary>
    /// One game with names in <b>every</b> language — what an edit form has to be filled from.
    /// <para>
    /// <c>IGameService.GetByIdAsync</c> deliberately cannot serve this: it resolves a single
    /// translation from the caller's content language, and <c>UpdateAsync</c> is a full replace, so
    /// a form filled from that read would send one language back on its own and delete the rest.
    /// </para>
    /// </summary>
    /// <returns>Null when no game has that id.</returns>
    Task<GameAdminDto?> GetForAuthoringAsync(Guid gameId, CancellationToken cancellationToken = default);

    Task<ServiceResult<GameDto>> CreateAsync(SaveGameRequest request, CancellationToken cancellationToken = default);

    /// <summary>Full replace, translations included.</summary>
    Task<ServiceResult<GameDto>> UpdateAsync(Guid gameId, SaveGameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a game and everything recorded about it. Progress is per game, so this destroys
    /// every student's attempts and unlocks for it — refused with a count unless
    /// <paramref name="force"/> is set. Deactivating (<c>isActive: false</c>) is the reversible
    /// alternative and is almost always what is wanted instead.
    /// </summary>
    Task<ServiceResult<GameDeletionImpact>> DeleteAsync(Guid gameId, bool force, CancellationToken cancellationToken = default);
}
