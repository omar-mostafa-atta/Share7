using Share7.Application.Common.Models;
using Share7.Application.Equipment.Models;

namespace Share7.Application.Equipment.Interfaces;

public interface IEquipmentService
{
    /// <summary>
    /// The caller's saved outfit. **Never fails and never 404s** — a player with no stored outfit
    /// gets defaults with a null <c>updatedAtUtc</c>, which is how the client tells "never dressed"
    /// from "wearing nothing on purpose".
    /// </summary>
    Task<EquipmentDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the caller's whole outfit and returns what was stored.
    /// <para>
    /// Upsert on the user: the first save inserts that user's single row, every later one updates
    /// it. A second row for the same user is impossible, so the same user can never hold the same
    /// slot twice.
    /// </para>
    /// <para>
    /// Fails <see cref="ServiceErrorKind.Unprocessable"/> when the payload breaks a limit — too
    /// many entries, an over-long or badly-formed key, a slot named twice, or (when ownership
    /// enforcement is switched on) a cosmetic the account does not own.
    /// </para>
    /// </summary>
    Task<ServiceResult<EquipmentDto>> ReplaceAsync(
        Guid userId,
        UpdateEquipmentRequest request,
        CancellationToken cancellationToken = default);
}
