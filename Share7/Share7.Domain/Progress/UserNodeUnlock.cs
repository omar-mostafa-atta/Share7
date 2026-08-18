namespace Share7.Domain.Progress;

/// <summary>
/// An append-only ledger of what a student has opened up, per game. **Unlocks are permanent** —
/// nothing in the system ever deletes a row here, so a student who aces a lesson and later
/// replays it badly keeps everything that unlock earned them.
/// <para>
/// This cannot be derived from current state precisely because of that asymmetry: completion
/// follows the last attempt and can drop, while the unlock it granted does not.
/// </para>
/// <para>
/// <see cref="NodeId"/> carries no foreign key — it points at one of four different tables
/// depending on <see cref="NodeType"/>. Acceptable here because nothing aggregates off this
/// table; it is a pure ledger.
/// </para>
/// </summary>
public class UserNodeUnlock
{
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }

    public CurriculumNodeType NodeType { get; set; }
    public Guid NodeId { get; set; }

    public DateTime UnlockedAt { get; set; }
}
