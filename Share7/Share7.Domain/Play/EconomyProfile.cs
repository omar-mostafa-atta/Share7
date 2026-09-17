namespace Share7.Domain.Play;

/// <summary>
/// How much of what a session earns is actually paid, as data an operator can tune without a deploy.
/// <para>
/// <b>It scales, it never prices.</b> What a coin is worth stays in <c>SignalValuation</c>, and what
/// finishing is worth stays in the reward rules — a profile only says how much of that result this
/// kind of session keeps. Two tunables rather than a per-mode copy of the valuation table, because
/// the moment a mode owns its own prices, the answer to "how much can a child earn in a day" stops
/// being answerable in one place.
/// </para>
/// <para>
/// Keyed rather than referenced by id in the client contract, so an operator can read a mode row and
/// see <c>event</c> rather than a GUID.
/// </para>
/// </summary>
public class EconomyProfile
{
    public Guid Id { get; set; }

    /// <summary>Stable key: <c>default</c>, <c>event</c>, <c>reduced</c>, <c>none</c>. Unique, lowercase.</summary>
    public string ProfileKey { get; set; } = string.Empty;

    /// <summary>
    /// Operator-facing label. Deliberately untranslated: no child ever sees it, and a profile is
    /// chosen in an admin console by the person who authored it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Percent of the settled payout that is granted — 100 pays exactly what the valuations say, 0
    /// pays nothing, 150 pays half again.
    /// <para>
    /// Applied <b>after</b> every cap, so it scales a bounded number. A multiplier applied before the
    /// caps would be a way to buy back the daily ceiling, which is the one bound a farming script
    /// actually runs into.
    /// </para>
    /// </summary>
    public int PayoutPercent { get; set; } = 100;

    /// <summary>
    /// Whether fixed reward rules fire for sessions under this profile. Separate from the percent
    /// because "half the coins" and "no completion bonus at all" are different decisions, and a
    /// percent of a fixed bonus is not what an operator means by either.
    /// </summary>
    public bool PaysRuleRewards { get; set; } = true;

    /// <summary>
    /// The platform's fallback, used by every mode that names no profile. Exactly one row carries
    /// it, enforced by a filtered unique index — a platform with two defaults has none.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Scales <paramref name="amount"/>, rounding down and never below zero.
    /// <para>
    /// Down rather than to nearest: rounding up hands out currency nobody authored, and at 1 coin a
    /// 50% profile paying 1 instead of 0 is a rounding rule a farming loop can repeat all day.
    /// </para>
    /// </summary>
    public long Scale(long amount)
    {
        if (amount <= 0 || PayoutPercent <= 0) return 0;
        if (PayoutPercent == 100) return amount;

        return (long)((decimal)amount * PayoutPercent / 100m);
    }
}
