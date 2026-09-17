using Share7.Domain.Progression;

namespace Share7.Application.Progression.Interfaces;

/// <summary>
/// Holds the level curve for the life of the process, because it is small, authored, and read on
/// every path that touches XP.
/// <para>
/// **This is a scale fix, not a micro-optimisation.** The curve is at most a few dozen rows that
/// change when an operator retunes them — roughly never — and it was being fetched from SQL Server
/// once per <c>LevelService</c> instance, which is once per request. A single lesson attempt reads
/// the level three times (the baseline before granting, the level-up detection inside the reward
/// engine, the figure on the response), so at a million attempts a day that is three million queries
/// for an answer that has not changed since deployment.
/// </para>
/// <para>
/// **Correctness comes from the invalidation, and there is exactly one writer.** The curve is
/// replaced whole through <c>ILevelService.ReplaceCurveAsync</c>, which clears this. Nothing else
/// writes <c>LevelThresholds</c>, so there is no path by which a stale curve can outlive an edit —
/// on this instance. A multi-instance deployment keeps serving the old curve on other nodes until
/// their next restart, which is the deliberate trade: a curve edit is an operator action taken
/// minutes apart from a deploy, and the alternative is a distributed cache in front of forty rows.
/// </para>
/// </summary>
public interface ILevelCurveCache
{
    /// <summary>
    /// The authored curve, ascending by XP, or null when nothing has been loaded yet. The caller
    /// loads and calls <see cref="Set"/>; this type deliberately knows nothing about the database.
    /// </summary>
    IReadOnlyList<LevelThreshold>? Current { get; }

    /// <summary>Publishes a freshly read curve.</summary>
    void Set(IReadOnlyList<LevelThreshold> curve);

    /// <summary>
    /// Drops what is held, so the next read goes to the database. Called by the one path that edits
    /// the curve.
    /// </summary>
    void Invalidate();
}
