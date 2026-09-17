namespace Share7.Domain.Equipment;

/// <summary>
/// Which avatar body the player's cosmetics are fitted to. Purely presentational — it selects a
/// mesh on the client and has no bearing on progress, entitlements or anything else.
/// <para>
/// <see cref="Male"/> is 0 so that it is the value a row gets when nothing was supplied, which is
/// the documented default for a player who has never chosen.
/// </para>
/// </summary>
public enum BodyType
{
    Male = 0,
    Female = 1
}
