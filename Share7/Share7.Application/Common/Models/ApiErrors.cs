using System.Reflection;

namespace Share7.Application.Common.Models;

/// <summary>
/// Every machine code the commerce and account endpoints can return.
/// <para>
/// These are contract. Renaming one is a breaking change for the Unity client — add a new code
/// rather than repurposing an existing one.
/// </para>
/// </summary>
public static class ApiErrors
{
    private static IReadOnlyList<ApiErrorCode>? _all;

    /// <summary>
    /// Every code declared here, for looking one up by its stored <c>messageKey</c> — a replayed
    /// purchase has to report the reason it was refused with the first time, and only the key was
    /// written down.
    /// <para>
    /// Reflected over the fields rather than hand-listed, so a new code cannot be forgotten here.
    /// **Built on first use, not in a field initializer**: static initializers run in declaration
    /// order, so reflecting eagerly from up here would read every field below while it was still
    /// null and hand back a list of nulls.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ApiErrorCode> All => _all ??= typeof(ApiErrors)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(ApiErrorCode))
        .Select(field => (ApiErrorCode)field.GetValue(null)!)
        .ToList();

    // ---- account -------------------------------------------------------------------------

    public static readonly ApiErrorCode AccountDeletionRefused =
        new("ACCOUNT_DELETION_REFUSED", "account.deletion.blocked");

    public static readonly ApiErrorCode ProfileNotFound =
        new("PROFILE_NOT_FOUND", "account.profile.not_found");

    // ---- currency ------------------------------------------------------------------------

    public static readonly ApiErrorCode CurrencyNotFound =
        new("CURRENCY_NOT_FOUND", "commerce.currency.not_found");

    public static readonly ApiErrorCode CurrencyDisabled =
        new("CURRENCY_DISABLED", "commerce.currency.disabled");

    public static readonly ApiErrorCode CurrencyKeyTaken =
        new("CURRENCY_KEY_TAKEN", "commerce.currency.key_taken");

    public static readonly ApiErrorCode InvalidAmount =
        new("INVALID_AMOUNT", "commerce.currency.invalid_amount");

    public static readonly ApiErrorCode InsufficientBalance =
        new("INSUFFICIENT_BALANCE", "commerce.insufficient_balance");

    // ---- rewards -------------------------------------------------------------------------

    public static readonly ApiErrorCode RewardRuleNotFound =
        new("REWARD_RULE_NOT_FOUND", "rewards.rule.not_found");

    /// <summary>
    /// The rule as authored could never pay correctly — an unknown event type, a limit that its
    /// repeat policy ignores, a currency listed twice. Rejected at authoring time because a rule
    /// that silently never fires is far harder to notice than a refused request.
    /// </summary>
    public static readonly ApiErrorCode RewardRuleInvalid =
        new("REWARD_RULE_INVALID", "rewards.rule.invalid");

    // ---- product kinds ---------------------------------------------------------------------

    public static readonly ApiErrorCode ProductKindNotFound =
        new("PRODUCT_KIND_NOT_FOUND", "commerce.product_kind.not_found");

    public static readonly ApiErrorCode ProductKindNameTaken =
        new("PRODUCT_KIND_NAME_TAKEN", "commerce.product_kind.name_taken");

    public static readonly ApiErrorCode ProductKindInvalid =
        new("PRODUCT_KIND_INVALID", "commerce.product_kind.invalid");

    /// <summary>
    /// Products still use this kind, so deleting it would leave them with nothing telling the
    /// client how to read their grants. Re-categorise them first.
    /// </summary>
    public static readonly ApiErrorCode ProductKindInUse =
        new("PRODUCT_KIND_IN_USE", "commerce.product_kind.in_use");

    // ---- products / entitlements -----------------------------------------------------------

    public static readonly ApiErrorCode ProductNotFound =
        new("PRODUCT_NOT_FOUND", "commerce.product.not_found");

    public static readonly ApiErrorCode ProductInactive =
        new("PRODUCT_INACTIVE", "commerce.product.inactive");

    public static readonly ApiErrorCode ProductKeyTaken =
        new("PRODUCT_KEY_TAKEN", "commerce.product.key_taken");

    public static readonly ApiErrorCode ProductInvalid =
        new("PRODUCT_INVALID", "commerce.product.invalid");

    /// <summary>
    /// The grant set cannot be edited because accounts already own the product — changing it would
    /// silently change what they own. Author a replacement product instead.
    /// </summary>
    public static readonly ApiErrorCode ProductGrantsLocked =
        new("PRODUCT_GRANTS_LOCKED", "commerce.product.grants_locked");

    /// <summary>
    /// The product cannot be deleted because accounts own it. Their entitlements resolve what they
    /// own by reading through to it, so deleting it would strand them. Retire it instead.
    /// </summary>
    public static readonly ApiErrorCode ProductOwned =
        new("PRODUCT_OWNED", "commerce.product.owned");

    public static readonly ApiErrorCode ProductGrantNotFound =
        new("PRODUCT_GRANT_NOT_FOUND", "commerce.product_grant.not_found");

    public static readonly ApiErrorCode ProductGrantInvalid =
        new("PRODUCT_GRANT_INVALID", "commerce.product_grant.invalid");

    /// <summary>The product already grants that reference. Change the quantity instead.</summary>
    public static readonly ApiErrorCode ProductGrantReferenceTaken =
        new("PRODUCT_GRANT_REFERENCE_TAKEN", "commerce.product_grant.reference_taken");

    // ---- offers / purchase ---------------------------------------------------------------

    public static readonly ApiErrorCode OfferNotFound =
        new("OFFER_NOT_FOUND", "commerce.offer.not_found");

    public static readonly ApiErrorCode OfferUnavailable =
        new("OFFER_UNAVAILABLE", "commerce.offer.unavailable");

    public static readonly ApiErrorCode OfferExpired =
        new("OFFER_EXPIRED", "commerce.offer.expired");

    public static readonly ApiErrorCode OfferSoldOut =
        new("OFFER_SOLD_OUT", "commerce.offer.sold_out");

    public static readonly ApiErrorCode PurchaseLimitReached =
        new("PURCHASE_LIMIT_REACHED", "commerce.offer.purchase_limit_reached");

    public static readonly ApiErrorCode NotEligible =
        new("NOT_ELIGIBLE", "commerce.offer.not_eligible");

    public static readonly ApiErrorCode GradeRestricted =
        new("GRADE_RESTRICTED", "commerce.offer.grade_restricted");

    public static readonly ApiErrorCode AlreadyOwned =
        new("ALREADY_OWNED", "commerce.offer.already_owned");

    public static readonly ApiErrorCode RequestIdRequired =
        new("REQUEST_ID_REQUIRED", "commerce.purchase.request_id_required");

    /// <summary>The offer as authored could never be sold — no products, a negative price, an
    /// original price below the sale price.</summary>
    public static readonly ApiErrorCode OfferInvalid =
        new("OFFER_INVALID", "commerce.offer.invalid");

    /// <summary>
    /// The offer has completed purchases, so deleting it would strand the transactions that point
    /// at it. Switch it to <c>UNAVAILABLE</c> instead.
    /// </summary>
    public static readonly ApiErrorCode OfferPurchased =
        new("OFFER_PURCHASED", "commerce.offer.purchased");

    // ---- equipment -------------------------------------------------------------------------

    /// <summary>
    /// The outfit breaks a structural limit — too many entries, an over-long or badly-formed key,
    /// or the same slot named twice. <c>details</c> names the offending field and value.
    /// <para>
    /// Never returned for an *unknown* key. There is no backend cosmetic catalogue by decision, so
    /// keys the server has never seen are stored verbatim; rejecting them would stop content
    /// shipping ahead of a backend deploy.
    /// </para>
    /// </summary>
    public static readonly ApiErrorCode EquipmentInvalid =
        new("EQUIPMENT_INVALID", "equipment.invalid");

    /// <summary>
    /// The account does not own a cosmetic it tried to equip. Only raised while ownership
    /// enforcement is switched on — see <c>EquipmentOptions.EnforceOwnership</c>.
    /// </summary>
    public static readonly ApiErrorCode EquipmentNotOwned =
        new("EQUIPMENT_NOT_OWNED", "equipment.not_owned");

    // ---- generic -------------------------------------------------------------------------

    public static readonly ApiErrorCode NotFound =
        new("NOT_FOUND", "common.not_found");

    public static readonly ApiErrorCode ValidationFailed =
        new("VALIDATION_FAILED", "common.validation_failed");

    public static readonly ApiErrorCode Forbidden =
        new("FORBIDDEN", "common.forbidden");
}
