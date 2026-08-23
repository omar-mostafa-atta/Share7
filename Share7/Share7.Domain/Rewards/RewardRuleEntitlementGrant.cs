using Share7.Domain.Commerce;

namespace Share7.Domain.Rewards;

/// <summary>
/// A product a <see cref="RewardRule"/> hands over — a badge, a cosmetic, an unlock.
/// <para>
/// **This is what makes a badge not a system.** Owning a thing is already solved: <c>Product</c>,
/// <c>ProductKind</c> and <c>Entitlement</c> model it, the wardrobe already renders what a player
/// owns, and account deletion already reaches it. A badge is a product of kind <c>badge</c> granted
/// by the rule that pays for an achievement — so there is no badge table, no badge sync path and no
/// badge inventory, and a badge can later be seasonal or giftable for free.
/// </para>
/// <para>
/// Sits beside <see cref="RewardRuleGrant"/> rather than replacing it: a rule can pay coins, XP
/// *and* a badge, and all of it lands together or none of it does.
/// </para>
/// </summary>
public class RewardRuleEntitlementGrant
{
    public Guid Id { get; set; }

    public Guid RewardRuleId { get; set; }
    public RewardRule? RewardRule { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}
