namespace Share7.Application.Runs.Interfaces;

/// <summary>
/// One game's procedural layout, at one version, re-derived from a seed.
/// <para>
/// <b>An implementation of this is a port, not a design.</b> It must reproduce the client's generator
/// exactly — same seed, same RNG, same traversal order, same placement rules — because the two are two
/// implementations of one algorithm and every disagreement between them rejects a real run from a real
/// child. Write it against the Unity source and hold both to the same fixture vectors; do not
/// reimplement it from a description of what the track looks like.
/// </para>
/// <para>
/// <b>Registering one is what turns exact verification on for a game.</b> Until then the game settles
/// on plausibility bounds alone, which is the correct default: no verification is safe, and wrong
/// verification is not.
/// </para>
/// <para>
/// Never mutate a published version. A generator whose output changes retroactively invalidates every
/// run stamped with it, including the ones sitting in an offline queue. Ship a new
/// <see cref="Version"/> and leave the old one registered until nothing can still be carrying it.
/// </para>
/// </summary>
public interface IRunLayoutGenerator
{
    /// <summary>Which game this generates for — <c>Game.GameKey</c>, not the id.</summary>
    string GameKey { get; }

    /// <summary>
    /// This generator's version. Stamped onto a run at start, so a run is always checked against the
    /// generator the client actually used. Must be greater than zero — <c>0</c> means "unverified".
    /// </summary>
    int Version { get; }

    /// <summary>Everything the seed placed. Must be deterministic: one seed, one layout, forever.</summary>
    RunLayout Generate(long seed);
}
