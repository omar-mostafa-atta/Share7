using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Interfaces;
using Share7.Domain.Commerce;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// The product catalogue — what a purchase hands over, defined independently of what it costs.
/// **Admin only**: a player able to author a product could grant themselves anything.
/// <para>
/// Price, availability and eligibility are not here. They belong to the Offer, which is not built
/// yet; a product exists whether or not anything currently sells it, and keeps existing after every
/// offer for it is gone.
/// </para>
/// <para>
/// **What a product hands over is not authored here either.** Grants are their own table with their
/// own endpoints at <c>/api/admin/product-grants</c>; a product carries identity, art and its kind.
/// A newly created product grants nothing until grants are added to it.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminProductsController : ControllerBase
{
    private readonly IProductAdminService _products;
    private readonly IEntitlementService _entitlements;
    private readonly ICurrentUserService _currentUser;

    public AdminProductsController(
        IProductAdminService products,
        IEntitlementService entitlements,
        ICurrentUserService currentUser)
    {
        _products = products;
        _entitlements = entitlements;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Every product, retired ones included, each with its grants. <c>ownerCount</c> is how many
    /// accounts own it — when it is non-zero the grant set is frozen and the product cannot be
    /// deleted, so this is where to look before attempting either.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var products = await _products.GetAllAsync(cancellationToken);
        return Ok(new { products });
    }

    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> Get(Guid productId, CancellationToken cancellationToken)
    {
        var result = await _products.GetAsync(productId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Defines a product:
    /// <code>
    /// {
    ///   "key": "skin_astronaut",
    ///   "productKindId": "…",
    ///   "imageUrl": "https://cdn.example.com/shop/astronaut.png",
    ///   "active": true,
    ///   "translations": [
    ///     { "langId": "…en", "name": "Astronaut skin", "description": "Gold visor." },
    ///     { "langId": "…ar", "name": "زي رائد الفضاء", "description": "بقناع ذهبي." }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// **A name is required for every configured language.** The product row carries no text of its
    /// own, exactly like a curriculum node — one product has one id in every language, which is what
    /// lets an entitlement survive a language switch. A missing translation is refused rather than
    /// left to a fallback, because a shop entry with no text in a student's language is unreadable
    /// to them and nothing downstream can repair it.
    /// </para>
    /// <para>
    /// <c>key</c> is permanent and machine-facing (<c>^[a-z][a-z0-9_]*$</c>). <c>productKindId</c> is
    /// required — it is what tells the client how to read the references this product grants.
    /// <c>imageUrl</c> is stored and handed back verbatim, one image for every language; the backend
    /// neither hosts nor fetches it.
    /// </para>
    /// <para>
    /// The product is created **with no grants**. Add them at <c>/api/admin/product-grants</c>.
    /// </para>
    /// </summary>
    /// <response code="400">A key that is not <c>^[a-z][a-z0-9_]*$</c>, or a translation missing for any configured language.</response>
    /// <response code="404">No such product kind.</response>
    /// <response code="409">The key is already taken.</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await _products.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { productId = result.Value!.ProductId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Retitles a product in every language, retires it with <c>active: false</c>, re-categorises
    /// it, or changes its art. <c>key</c> cannot change and is not accepted.
    /// <para>
    /// <c>translations</c> **replaces** the whole set, so every configured language must be present
    /// on every update — the same rule as create.
    /// </para>
    /// <para>
    /// All of this stays available on a product accounts already own, **including changing its
    /// kind**: kind describes how the client reads the grants, not which references it receives, so
    /// unlike editing the grant set it cannot hand an existing owner something different. Fixing a
    /// miscategorised product has to stay possible after it has sold.
    /// </para>
    /// <para>
    /// Retiring stops new grants and leaves every existing owner untouched.
    /// </para>
    /// </summary>
    [HttpPut("{productId:guid}")]
    public async Task<IActionResult> Update(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _products.UpdateAsync(productId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Deletes a product nobody owns, taking its grants with it. **Idempotent** — deleting one that
    /// is already gone succeeds.
    /// <para>
    /// **Refused with <c>PRODUCT_OWNED</c> (409) once any account owns it**, reporting
    /// <c>details.ownerCount</c>. An entitlement resolves what it owns by reading through to the
    /// product, so deleting it would strand every owner — the database enforces this too. Retire it
    /// with <c>active: false</c> instead, which removes it from sale and leaves ownership intact.
    /// </para>
    /// </summary>
    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> Delete(Guid productId, CancellationToken cancellationToken)
    {
        var result = await _products.DeleteAsync(productId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }

    /// <summary>
    /// Hands a product to an account without a purchase — support fixes, prizes, and testing the
    /// client's inventory before the shop exists.
    /// <para>
    /// Recorded with <c>source: "ADMIN_GRANT"</c> and the granting admin's id, so the entitlement
    /// explains itself later. **Idempotent**: granting something the account already owns returns
    /// the existing entitlement rather than failing or duplicating it.
    /// </para>
    /// </summary>
    /// <response code="400">The product is retired.</response>
    /// <response code="404">No such product.</response>
    [HttpPost("/api/admin/entitlements")]
    public async Task<IActionResult> GrantEntitlement(
        GrantEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _entitlements.GrantAsync(
            request.UserId,
            request.ProductId,
            EntitlementSource.AdminGrant,
            adminId.ToString(),
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
