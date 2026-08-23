namespace Share7.Domain.Progression;

/// <summary>
/// One rung of the level curve: the lifetime XP at which a level begins.
/// <para>
/// **The curve is data, and the level is derived from it — never stored on the player.** A player's
/// level is <c>max(Level) where CumulativeXp &lt;= balance</c>, computed on read. Storing it would
/// create a second copy of a number the wallet already holds, and the two would disagree the first
/// time a grant failed halfway.
/// </para>
/// <para>
/// **Cumulative, not per-level cost.** Both express the same curve, and only this one survives
/// being edited: level N always means "you have earned at least X", so changing what level 12 costs
/// cannot silently reshuffle who is level 30. Per-level costs re-derive every rung above the edit.
/// </para>
/// </summary>
public class LevelThreshold
{
    /// <summary>
    /// The level itself, and the key. Starts at 1 — a player with no XP is level 1, not level 0,
    /// because "level 0" reads as broken to a child.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Lifetime XP at which this level begins. Level 1 is always <c>0</c>.
    /// <para>
    /// Must increase strictly with <see cref="Level"/>. A flat or falling step would make two
    /// levels start at the same balance, and the derivation would pick whichever the sort happened
    /// to return; authoring refuses it rather than leaving that to chance.
    /// </para>
    /// </summary>
    public long CumulativeXp { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
