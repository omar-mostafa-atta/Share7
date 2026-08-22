using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Everything to do with virtual currency: the catalogue, balances, and manual grants.
/// **Nothing here is real money.**
/// <para>
/// Authenticated by default. Everything that changes state — defining a currency, retiring one,
/// or moving a balance by hand — is Admin-only on the individual action. Reads are open to any
/// signed-in account.
/// </para>
/// </summary>
[ApiController]
[Route("api/currencies")]
[Authorize]
public class CurrenciesController : ControllerBase
{
    private readonly ICurrencyAdminService _currencyAdminService;
    private readonly IWalletService _wallet;
    private readonly ICurrentUserService _currentUserService;

    public CurrenciesController(
        ICurrencyAdminService currencyAdminService,
        IWalletService wallet,
        ICurrentUserService currentUserService)
    {
        _currencyAdminService = currencyAdminService;
        _wallet = wallet;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Every currency, retired ones included. This is where a <c>currencyId</c> comes from.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var currencies = await _currencyAdminService.GetAllAsync(cancellationToken);
        return Ok(new { currencies });
    }

    /// <summary>
    /// Defines a currency. **Admin only** — the catalogue is configuration, not something players
    /// extend.
    /// <para>
    /// The <c>key</c> is what the client speaks and is permanent: balances are cached against it,
    /// so it cannot be changed afterwards.
    /// </para>
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Create(CreateCurrencyRequest request, CancellationToken cancellationToken)
    {
        var result = await _currencyAdminService.CreateAsync(request, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAll), new { }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Renames a currency, or retires it with <c>enabled: false</c>. Retiring keeps balances and
    /// ledger history intact and refuses further credits and debits.
    /// <para>
    /// **Admin only**, same as creating: retiring the currency the whole economy runs on would
    /// otherwise be one request away for any signed-in account.
    /// </para>
    /// </summary>
    [HttpPut("{currencyId:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Update(
        Guid currencyId,
        UpdateCurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _currencyAdminService.UpdateAsync(currencyId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Credits or deducts the caller's own balance and returns the new absolute total. The account
    /// comes from the bearer token; there is no target-user field.
    /// <para>
    /// Recorded on the ledger as <c>ADMIN_GRANT</c>, or <c>ADMIN_ADJUSTMENT</c> when the amount is
    /// negative. A deduction that would overdraw is refused rather than clamped.
    /// </para>
    /// <para>
    /// **Admin only** — closed 2026-08-22. This is the only route in the economy where an amount
    /// travels client to server, so it is the only one where a caller could name their own balance.
    /// It was left open deliberately while currency had nothing to buy; reward rules and purchases
    /// have since landed, coins now have value, and the condition that comment set has been met.
    /// Gameplay currency comes from the server evaluating a validated progress attempt
    /// (<c>RewardService</c>), never from a figure the client supplied.
    /// </para>
    /// <para>
    /// It stays a **self**-grant rather than growing a target-user field. Topping up one's own test
    /// account is what the admin console uses it for, and a route that can credit an arbitrary user
    /// is a much larger thing to leave standing than one that can only credit the caller.
    /// </para>
    /// </summary>
    [HttpPost("grant")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Grant(AdminGrantCurrencyRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var result = await _currencyAdminService.GrantAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The caller's authoritative balances, one per currency they hold.
    /// <para>
    /// Absolute path rather than the controller's route: <c>/api/commerce/balances</c> is the path
    /// the Unity contract fixed, and moving it would break the client. The action lives here
    /// because it is currency, not because of its URL.
    /// </para>
    /// <para>
    /// <c>amount</c> is absolute, never a delta — assign it to the local wallet rather than adding
    /// to it. This is the reconciliation endpoint: call it on launch and after reconnecting, and
    /// take the server's answer over anything held locally. A currency the user has never held is
    /// absent rather than reported as zero.
    /// </para>
    /// </summary>
    [HttpGet("/api/commerce/balances")]
    public async Task<IActionResult> GetBalances(CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
            return Unauthorized();

        var balances = await _wallet.GetBalancesAsync(userId, cancellationToken);
        return Ok(new BalancesResponse { Balances = balances });
    }
}
