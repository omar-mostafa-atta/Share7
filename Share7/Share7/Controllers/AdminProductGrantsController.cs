using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// What a product hands over. **Admin only** — these rows are what a purchase reads to decide what
/// an account receives.
/// <para>
/// This is the join a purchase walks: resolve the offer to its <c>productId</c>, select every grant
/// row carrying that id, and hand the account all of them together. Nothing else decides what a
/// purchase delivers, and there is no partial ownership.
/// </para>
/// <para>
/// <c>reference</c> is **the client's own id** and is never resolved here — there is no backend
/// cosmetic catalogue by decision, so a typo produces a product granting something Unity cannot
/// find, and nothing on this side can detect it. Check it against the client's catalogue.
/// </para>
/// <para>
/// A grant carries no kind of its own: it inherits the product's, which every response repeats as
/// <c>kind</c> for convenience.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/product-grants")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminProductGrantsController : ControllerBase
{
    private readonly IProductGrantAdminService _grants;

    public AdminProductGrantsController(IProductGrantAdminService grants)
    {
        _grants = grants;
    }

    /// <summary>
    /// Every grant, or one product's with <c>?productId=…</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? productId, CancellationToken cancellationToken)
    {
        var grants = await _grants.GetAllAsync(productId, cancellationToken);
        return Ok(new { grants });
    }

    [HttpGet("{grantId:guid}")]
    public async Task<IActionResult> Get(Guid grantId, CancellationToken cancellationToken)
    {
        var result = await _grants.GetAsync(grantId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Adds one thing to what a product hands over:
    /// <code>
    /// { "productId": "…", "reference": "cosmetic_astronaut", "quantity": 1 }
    /// </code>
    /// <para>
    /// A product may not grant the same reference twice — combine the quantities instead. The
    /// database enforces it, so two simultaneous adds cannot both land.
    /// </para>
    /// </summary>
    /// <response code="400">Blank reference or a quantity below 1.</response>
    /// <response code="404">No such product.</response>
    /// <response code="409"><c>PRODUCT_GRANT_REFERENCE_TAKEN</c>, or <c>PRODUCT_GRANTS_LOCKED</c>
    /// when accounts already own the product.</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductGrantRequest request, CancellationToken cancellationToken)
    {
        var result = await _grants.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { grantId = result.Value!.GrantId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Changes a grant's reference or quantity.
    /// <para>
    /// <c>productId</c> is not accepted — a grant cannot be moved between products, because that
    /// would silently alter what both of them hand over. Delete it and add it to the other product.
    /// </para>
    /// </summary>
    /// <response code="409"><c>PRODUCT_GRANTS_LOCKED</c> when accounts already own the product.</response>
    [HttpPut("{grantId:guid}")]
    public async Task<IActionResult> Update(
        Guid grantId,
        UpdateProductGrantRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _grants.UpdateAsync(grantId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Removes one thing from what a product hands over. **Idempotent** — deleting a grant that is
    /// already gone succeeds.
    /// <para>
    /// **Refused with <c>PRODUCT_GRANTS_LOCKED</c> (409) once any account owns the product**,
    /// reporting <c>details.ownerCount</c>. An entitlement resolves what it owns by reading these
    /// rows fresh every time rather than snapshotting them at purchase, so removing one would take
    /// something away from accounts that already bought it. Author a replacement product instead.
    /// </para>
    /// </summary>
    [HttpDelete("{grantId:guid}")]
    public async Task<IActionResult> Delete(Guid grantId, CancellationToken cancellationToken)
    {
        var result = await _grants.DeleteAsync(grantId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
