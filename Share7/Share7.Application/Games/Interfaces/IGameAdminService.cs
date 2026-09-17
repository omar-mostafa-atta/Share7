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

    /// <summary>
    /// Every game in the authoring shape — translations included.
    /// <para>
    /// The same argument as <see cref="GetForAuthoringAsync"/>, applied to the listing.
    /// <c>IGameService.GetAllAsync</c> resolves one translation per row, which leaves a console
    /// unable to say how many languages a game has been authored in and — worse — makes a row an
    /// unsafe thing to open an editor from, because the editor would then save one language over
    /// all of them. Rows from here are the same shape the single read returns, so a console can
    /// edit straight off the list.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<GameAdminDto>> ListForAuthoringAsync(
        bool includeInactive = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a game and returns it in the authoring shape.
    /// <para>
    /// The authoring shape, not <see cref="GameDto"/>, so a caller can put the result straight into
    /// a list it already holds. Answering a write in the one-language read shape means whoever does
    /// that inserts a row carrying no translations, which then breaks the next thing to read them —
    /// a listing failure caused entirely by the write path replying in the wrong shape.
    /// </para>
    /// </summary>
    Task<ServiceResult<GameAdminDto>> CreateAsync(SaveGameRequest request, CancellationToken cancellationToken = default);

    /// <summary>Full replace, translations included. Returns the authoring shape, as <see cref="CreateAsync"/> does.</summary>
    Task<ServiceResult<GameAdminDto>> UpdateAsync(Guid gameId, SaveGameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a game and everything recorded about it. Progress is per game, so this destroys
    /// every student's attempts and unlocks for it — refused with a count unless
    /// <paramref name="force"/> is set. Deactivating (<c>isActive: false</c>) is the reversible
    /// alternative and is almost always what is wanted instead.
    /// </summary>
    Task<ServiceResult<GameDeletionImpact>> DeleteAsync(Guid gameId, bool force, CancellationToken cancellationToken = default);
}
