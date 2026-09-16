using Share7.Application.Common.Models;
using Share7.Application.Play.Models;

namespace Share7.Application.Play.Interfaces;

/// <summary>
/// Authoring the mode catalogue. Every write here is a policy change that reaches shipped clients
/// without a release, which is the entire reason modes are rows.
/// </summary>
public interface IGameModeAdminService
{
    /// <summary>Every mode of every game, or of one game, with all translations. Inactive included.</summary>
    Task<IReadOnlyList<GameModeAdminDto>> ListForAuthoringAsync(
        Guid? gameId = null, CancellationToken cancellationToken = default);

    /// <summary>Null when no mode has that id.</summary>
    Task<GameModeAdminDto?> GetForAuthoringAsync(Guid modeId, CancellationToken cancellationToken = default);

    Task<ServiceResult<GameModeAdminDto>> CreateAsync(
        SaveGameModeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full replace, translations included. The key and the owning game are immutable: an update
    /// that moves either is refused, because every run and result already written points at them.
    /// </summary>
    Task<ServiceResult<GameModeAdminDto>> UpdateAsync(
        Guid modeId, SaveGameModeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a mode. Refused while anything references it unless <paramref name="force"/> is set,
    /// and refused outright for a game's default mode — deleting that leaves every client that sends
    /// no mode key unable to start. Deactivating is the reversible alternative.
    /// </summary>
    Task<ServiceResult<GameModeDeletionImpact>> DeleteAsync(
        Guid modeId, bool force, CancellationToken cancellationToken = default);
}
