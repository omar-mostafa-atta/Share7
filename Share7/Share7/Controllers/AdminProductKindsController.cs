using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// Product categories — what kind of thing a product is, and therefore which of its own catalogues
/// the client resolves that product's grants against. **Admin only.**
/// <para>
/// This replaced the old <c>GrantKind</c> enum, and moved the concept up a level with it: kind
/// belongs to the product now, not to each grant, so every grant of one product is the same kind of
/// thing. A bundle mixing categories is authored as two products.
/// </para>
/// <para>
/// <c>name</c> is **contract with the client**: it is normalised to <c>SCREAMING_SNAKE</c> and sent
/// as each grant's <c>kind</c>. Because it is a table rather than an enum, nothing here can check
/// the vocabulary — a name Unity does not recognise is undetectable on this side. Every response
/// reports the normalised token as <c>kind</c> so it can be checked by eye before authoring
/// against it.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/product-kinds")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminProductKindsController : ControllerBase
{
    private readonly IProductKindAdminService _kinds;

    public AdminProductKindsController(IProductKindAdminService kinds)
    {
        _kinds = kinds;
    }

    /// <summary>
    /// Every kind. <c>productCount</c> is how many products use it — non-zero means a delete will
    /// be refused, so this is where to look before attempting one.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var productKinds = await _kinds.GetAllAsync(cancellationToken);
        return Ok(new { productKinds });
    }

    [HttpGet("{productKindId:guid}")]
    public async Task<IActionResult> Get(Guid productKindId, CancellationToken cancellationToken)
    {
        var result = await _kinds.GetAsync(productKindId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Defines a kind:
    /// <code>
    /// {
    ///   "name": "Cosmetic",
    ///   "translations": [
    ///     { "langId": "…en", "name": "Cosmetic",  "description": "Skins and trails." },
    ///     { "langId": "…ar", "name": "تجميلي",     "description": "أزياء ومؤثرات." }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// <c>name</c> is the **machine** name and is not translated — it becomes the <c>kind</c> token
    /// Unity matches on, which has to mean the same thing in every language. <c>translations</c> is
    /// the human label an admin reads and never reaches the client.
    /// </para>
    /// <para>
    /// Names collide on their **normalised** form, not their text: <c>Content Pack</c>,
    /// <c>content-pack</c> and <c>ContentPack</c> are all <c>CONTENT_PACK</c>, and a second row
    /// producing a token that already exists is refused — the client could not tell them apart.
    /// </para>
    /// </summary>
    /// <response code="400">Blank name, or a translation missing for any configured language.</response>
    /// <response code="409">Another kind already normalises to the same token.</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductKindRequest request, CancellationToken cancellationToken)
    {
        var result = await _kinds.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { productKindId = result.Value!.ProductKindId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Renames a kind or edits its labels. <c>translations</c> replaces the whole set, so every
    /// configured language must be present.
    /// <para>
    /// **Changing <c>name</c> is a contract change.** Every product of this kind immediately starts
    /// reporting the new token to the client, including products accounts already own — unless the
    /// new name normalises to the same thing, which is just a spelling change. Editing the
    /// translations is always safe: they never leave the admin surface.
    /// </para>
    /// </summary>
    [HttpPut("{productKindId:guid}")]
    public async Task<IActionResult> Update(
        Guid productKindId,
        UpdateProductKindRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _kinds.UpdateAsync(productKindId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Deletes a kind nothing uses. **Idempotent** — deleting one that is already gone succeeds.
    /// </summary>
    /// <response code="409"><c>PRODUCT_KIND_IN_USE</c> — products still reference it, with
    /// <c>details.productCount</c>. Re-categorise them first.</response>
    [HttpDelete("{productKindId:guid}")]
    public async Task<IActionResult> Delete(Guid productKindId, CancellationToken cancellationToken)
    {
        var result = await _kinds.DeleteAsync(productKindId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
