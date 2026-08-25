namespace Share7.Application.Leaderboards.Interfaces;

/// <summary>
/// Trims the <c>GameResults</c> stream once every consumer has finished with a row.
/// <para>
/// **This table is the one that falls over first.** Every finished lesson and every settled run
/// writes several rows, they are never updated and never deleted, and every board and every quest is
/// a projection of them. At a million sessions a day that is tens of millions of rows a week, on a
/// table whose indexes are on the hot path of both projectors. Nothing about the design is wrong —
/// an append-only stream is what makes a rebuild possible — but "append-only" and "kept forever" are
/// different decisions, and only the second one is unaffordable.
/// </para>
/// <para>
/// **What retention costs, stated plainly.** A full rebuild can only reach back as far as the
/// retained window. That is the trade being made deliberately: entries are durable, cycles settle
/// long before the window closes, and the alternative is keeping every row a five-year-old platform
/// ever wrote so that a rebuild nobody will run could reach 2026.
/// </para>
/// </summary>
public interface IGameResultRetentionService
{
    /// <summary>
    /// Deletes one bounded batch of results that are past the retention window and that **every**
    /// consumer has already read.
    /// <para>
    /// Returns how many rows went, so a caller can loop until a pass comes back short. Bounded rather
    /// than "delete everything eligible" because an unbounded delete on a table this size takes a lock
    /// escalation and a production incident with it.
    /// </para>
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}
