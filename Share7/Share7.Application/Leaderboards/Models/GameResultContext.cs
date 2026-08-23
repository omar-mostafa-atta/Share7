namespace Share7.Application.Leaderboards.Models;

/// <summary>One measurement a completed piece of gameplay produced.</summary>
/// <param name="Metric">A value from <c>LeaderboardMetrics</c>. Anything else is refused.</param>
/// <param name="Value">The measurement, already in the metric's canonical unit.</param>
public sealed record GameResultDraft(string Metric, long Value);

/// <summary>
/// Everything the leaderboard needs to know about one authoritative piece of gameplay.
/// <para>
/// **This is the only seam between gameplay and ranking.** A mini-game does not know boards
/// exist; it reports what happened and the projector decides which ladders that lands on. Adding
/// a board must never require touching a game, and a game must never be able to name a board.
/// </para>
/// <para>
/// Nothing here is client-supplied. Every value is computed server-side by whoever graded the
/// result, which is what makes ranking un-inflatable without the client ever being trusted.
/// </para>
/// </summary>
public sealed class GameResultContext
{
    public required Guid UserId { get; init; }

    public required Guid GameId { get; init; }

    /// <summary>What produced this — the lesson for an attempt, the session for a match.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>
    /// **Server clock.** Decides which cycle the result lands in, so it can never come from the
    /// device: a tablet set to next year would otherwise post into a window that has not opened.
    /// </summary>
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// The player's grade at this moment, snapshotted so a child who moves up mid-cycle keeps the
    /// standing they earned in the grade they earned it in.
    /// </summary>
    public Guid? GradeId { get; init; }

    public Guid? LangId { get; init; }

    /// <summary>
    /// The submission's idempotency key, carried through so a duplicate emission collides at the
    /// index instead of quietly becoming a second scoring event.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// What was measured. Empty is normal and cheap — a run that improved nothing raises nothing,
    /// which is what keeps replaying a lesson from farming a counting metric.
    /// </summary>
    public required IReadOnlyList<GameResultDraft> Metrics { get; init; }
}
