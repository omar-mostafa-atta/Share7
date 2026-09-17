using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Commerce.Models;

// ---- entitlements (client-facing) -----------------------------------------------------------

/// <summary>
/// One product this account owns. Exactly the shape the Unity commerce contract specifies — the
/// same object appears here and in the purchase response, so the client parses one type.
/// </summary>
public class EntitlementDto
{
    public Guid EntitlementId { get; init; }
    public Guid ProductId { get; init; }

    /// <summary>
    /// UTC, serialized **with a trailing `Z`**. Unlike the older progress timestamps, the kind is
    /// set explicitly on read — SQL Server hands back a `DateTime` with no kind, which would
    /// otherwise serialize bare and leave the client to guess the zone.
    /// </summary>
    public DateTime GrantedAtUtc { get; init; }

    /// <summary>Stable token: <c>PURCHASE</c> or <c>ADMIN_GRANT</c>.</summary>
    public string Source { get; init; } = string.Empty;
}

public class EntitlementsResponse
{
    public IReadOnlyList<EntitlementDto> Entitlements { get; init; } = [];
}

/// <summary>
/// One thing a product hands over. <c>reference</c> is opaque to the backend — the client resolves
/// it against its own catalogue, which is why no backend cosmetic catalogue exists.
/// </summary>
public class ProductGrantDto
{
    /// <summary>
    /// The owning product's kind, normalised — <c>COSMETIC</c>, <c>CONTENT_PACK</c>. Repeated on
    /// every grant because that is the shape the commerce contract specifies; it is a property of
    /// the product, so it is identical across one product's grants.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    public string Reference { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

/// <summary>
/// A product as the client sees it: an id and what it grants, nothing else. Price, availability and
/// eligibility belong to the offer, not here.
/// <para>
/// This is the shape that will appear under <c>products[]</c> in the offers response, defined now
/// so both endpoints share one type rather than growing two that drift.
/// </para>
/// </summary>
public class ProductDto
{
    public Guid ProductId { get; init; }
    public IReadOnlyList<ProductGrantDto> Grants { get; init; } = [];
}

/// <summary>Outcome of granting a product, which is safe to call twice.</summary>
public class EntitlementGrantResult
{
    public required EntitlementDto Entitlement { get; init; }

    /// <summary>
    /// True when the account already owned it and nothing new was written. Callers that need to
    /// *refuse* a repeat — a purchase, which must not charge for something already owned — check
    /// eligibility before granting rather than relying on this.
    /// </summary>
    public required bool AlreadyOwned { get; init; }
}

// ---- translations ---------------------------------------------------------------------------

/// <summary>
/// One language's text for a product or a kind. A name is required for **every** configured
/// language — the same rule the curriculum tree follows, so nothing can end up half-translated.
/// </summary>
public class CommerceTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional, unlike the name.</summary>
    [MaxLength(1024)]
    public string? Description { get; set; }
}

public class CommerceTranslationDto
{
    public Guid LangId { get; init; }

    /// <summary><c>en</c>, <c>ar</c> — so a caller can render the right field without a second lookup.</summary>
    public string LangCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

// ---- product kinds (admin) ------------------------------------------------------------------

public class ProductKindDto
{
    public Guid ProductKindId { get; init; }

    /// <summary>
    /// The machine name, as authored, e.g. <c>Content Pack</c>. **Not translated** — see
    /// <see cref="Kind"/>. The human label per language is in <see cref="Translations"/>.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// What the client actually receives as a grant's <c>kind</c>: <see cref="Name"/> normalised to
    /// <c>SCREAMING_SNAKE</c>. Shown so an admin can see the token before authoring against it.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Admin-facing label and note per language. Never sent to the Unity client.</summary>
    public IReadOnlyList<CommerceTranslationDto> Translations { get; init; } = [];

    /// <summary>
    /// How many products use this kind. Non-zero means it cannot be deleted — surfaced so the
    /// refusal is visible before it is attempted.
    /// </summary>
    public int ProductCount { get; init; }
}

public class CreateProductKindRequest
{
    /// <summary>
    /// The machine name. Reaches the client normalised — <c>Content Pack</c>, <c>content-pack</c>
    /// and <c>ContentPack</c> are all <c>CONTENT_PACK</c> and collide with each other.
    /// <para>
    /// **Deliberately one value, not per language.** The token has to mean the same thing in every
    /// language; the translated label is what an admin reads.
    /// </para>
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>One entry per configured language. All of them required.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<CommerceTranslationRequest> Translations { get; set; } = [];
}

/// <summary>
/// Renames a kind or edits its labels. <strong>Changing <c>name</c> changes what every product of
/// this kind reports to the client</strong>, so it is a contract change, not a cosmetic one —
/// unless the new name normalises to the same token. Editing the translations is always safe.
/// </summary>
public class UpdateProductKindRequest
{
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<CommerceTranslationRequest> Translations { get; set; } = [];
}

// ---- product authoring (admin) --------------------------------------------------------------

/// <summary>One grant row, with the id its own endpoints address it by.</summary>
public class AdminProductGrantDto
{
    public Guid GrantId { get; init; }
    public Guid ProductId { get; init; }

    /// <summary>The owning product's kind, normalised. Not stored here — resolved on read.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Reference { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

public class CreateProductGrantRequest
{
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>
    /// The client's own stable id for the thing. Never resolved here — a typo becomes a cosmetic
    /// the client cannot find, and the backend has no catalogue to catch it against.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Reference { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Edits one grant. <c>productId</c> is absent and cannot change — moving a grant would silently
/// alter what two products hand over. Delete it and add it to the other product instead.
/// </summary>
public class UpdateProductGrantRequest
{
    [Required]
    [MaxLength(256)]
    public string Reference { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

public class CreateProductRequest
{
    /// <summary>Stable key for configuration and seed data. Permanent once created.</summary>
    [Required]
    [MaxLength(64)]
    [RegularExpression("^[a-z][a-z0-9_]*$", ErrorMessage = "Key must be lowercase letters, digits and underscores, starting with a letter.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Shop name and description, one entry per configured language. **All of them required** — a
    /// product with no text in a student's language would be unreadable to them.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<CommerceTranslationRequest> Translations { get; set; } = [];

    /// <summary>
    /// Shop art. Stored and handed back verbatim — the backend neither hosts nor fetches it, and
    /// does not check that it resolves. One image for every language.
    /// </summary>
    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    /// <summary>Required. Tells the client which of its catalogues to resolve the grants against.</summary>
    [Required]
    public Guid ProductKindId { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>
/// Retitles a product in every language, retires it, re-categorises it, or changes its art.
/// <para>
/// <c>key</c> is absent and cannot change — it is the stable handle configuration refers to. Grants
/// are not here either: they have their own endpoints.
/// </para>
/// </summary>
public class UpdateProductRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<CommerceTranslationRequest> Translations { get; set; } = [];

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    [Required]
    public Guid ProductKindId { get; set; }

    public bool Active { get; set; } = true;
}

public class AdminProductDto
{
    public Guid ProductId { get; init; }
    public string Key { get; init; } = string.Empty;

    /// <summary>Name and description per language, ordered by language code.</summary>
    public IReadOnlyList<CommerceTranslationDto> Translations { get; init; } = [];

    public string? ImageUrl { get; init; }
    public bool Active { get; init; }

    public Guid ProductKindId { get; init; }

    /// <summary>The kind as authored.</summary>
    public string KindName { get; init; } = string.Empty;

    /// <summary>The kind as the client receives it, normalised.</summary>
    public string Kind { get; init; } = string.Empty;

    public IReadOnlyList<AdminProductGrantDto> Grants { get; init; } = [];

    /// <summary>
    /// How many accounts own this product. Non-zero means the grant set is locked and the product
    /// cannot be deleted — surfaced so the admin can see *why* before attempting either.
    /// </summary>
    public int OwnerCount { get; init; }
}

public class GrantEntitlementRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid ProductId { get; set; }
}
