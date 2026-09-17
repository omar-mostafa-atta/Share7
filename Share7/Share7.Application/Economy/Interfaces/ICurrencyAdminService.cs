using Share7.Application.Common.Models;
using Share7.Application.Economy.Models;

namespace Share7.Application.Economy.Interfaces;

public interface ICurrencyAdminService
{
    Task<IReadOnlyList<CurrencyDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<CurrencyDto>> CreateAsync(
        CreateCurrencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the display fields and the enabled flag. The key is immutable.</summary>
    Task<ServiceResult<CurrencyDto>> UpdateAsync(
        Guid currencyId,
        UpdateCurrencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the caller's own balance by hand and records it as <c>ADMIN_GRANT</c> (or
    /// <c>ADMIN_ADJUSTMENT</c> when deducting).
    /// <para>
    /// <paramref name="userId"/> comes from the bearer token and is both the actor and the
    /// target — the request carries no user field, so this cannot credit anyone else. Gameplay
    /// currency is still earned through the reward rules and never declared by a client.
    /// </para>
    /// </summary>
    Task<ServiceResult<WalletMutationResult>> GrantAsync(
        Guid userId,
        AdminGrantCurrencyRequest request,
        CancellationToken cancellationToken = default);
}
