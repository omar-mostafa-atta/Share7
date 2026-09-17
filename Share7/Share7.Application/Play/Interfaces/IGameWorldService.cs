using Share7.Application.Common.Models;
using Share7.Application.Play.Models;

namespace Share7.Application.Play.Interfaces;

/// <summary>
/// Which worlds a game offers, and which of them this player owns.
/// <para>
/// Authenticated, unlike the mode read: ownership is the whole answer, and there is nothing useful
/// to say about a world without knowing who is asking.
/// </para>
/// </summary>
public interface IGameWorldService
{
    Task<GameWorldsResponse> GetForPlayerAsync(
        Guid userId, string gameKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether one world may be played by this account right now, for the run-start check. Refuses
    /// with <c>PC_WORLD_UNKNOWN</c> or <c>PC_WORLD_LOCKED</c> rather than returning a bare bool, so
    /// the caller can say which of the two happened.
    /// </summary>
    Task<ServiceResult<GameWorldDto>> AuthoriseAsync(
        Guid userId, Guid gameId, string worldKey, CancellationToken cancellationToken = default);
}

/// <summary>Authoring the world catalogue: what exists, and how each one is come by.</summary>
public interface IGameWorldAdminService
{
    Task<IReadOnlyList<GameWorldAdminDto>> ListForAuthoringAsync(
        Guid? gameId = null, CancellationToken cancellationToken = default);

    Task<GameWorldAdminDto?> GetForAuthoringAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<ServiceResult<GameWorldAdminDto>> CreateAsync(
        SaveGameWorldRequest request, CancellationToken cancellationToken = default);

    /// <summary>Full replace. The key and the owning game are immutable, as a mode's are.</summary>
    Task<ServiceResult<GameWorldAdminDto>> UpdateAsync(
        Guid worldId, SaveGameWorldRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a world policy row. Refused for a game's default and for a world an event is pinned
    /// to. **Deleting the row does not un-own the world** — the entitlements people bought stay,
    /// which is why deactivating is nearly always the right move instead.
    /// </summary>
    Task<ServiceResult> DeleteAsync(Guid worldId, CancellationToken cancellationToken = default);
}
