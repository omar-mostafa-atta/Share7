namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids and keys for the currencies the **code** depends on existing, seeded rather than
/// created through the admin API.
/// <para>
/// Most currencies are pure configuration — an operator creates one and nothing in the codebase
/// names it. <c>xp</c> is not one of those: the player level is derived from its balance, so a
/// deployment without it has a progression endpoint that cannot answer. Seeding it follows the
/// same reasoning — and the same pattern — as <see cref="LanguageIds"/> and <see cref="GradeIds"/>.
/// </para>
/// </summary>
public static class CurrencyIds
{
    /// <summary>
    /// Experience. **Non-spendable by design** — see <c>Currency.IsSpendable</c>. That is what
    /// makes lifetime-earned equal to the current balance, and therefore what makes the player
    /// level a pure function of one stored number.
    /// </summary>
    public static readonly Guid Xp = Guid.Parse("3f9c8b21-6d47-4e05-9a13-8c2e7f04b6d5");
}

/// <summary>
/// The stable wire keys for the same currencies. Separate from <see cref="CurrencyIds"/> because
/// the client speaks keys and the database speaks ids, and code that needs one rarely needs both.
/// </summary>
public static class CurrencyKeys
{
    public const string Xp = "xp";
}
