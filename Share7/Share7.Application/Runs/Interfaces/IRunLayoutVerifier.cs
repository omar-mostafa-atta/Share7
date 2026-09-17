namespace Share7.Application.Runs.Interfaces;

/// <summary>
/// What a seed actually generated, re-derived server-side.
/// </summary>
/// <param name="PickupCounts">How many of each kind the layout contains, keyed by pickup kind token.</param>
/// <param name="PickupIds">
/// Every pickup id the layout placed. A claim naming an id outside this set is collecting something
/// that was never spawned.
/// </param>
public sealed record RunLayout(
    IReadOnlyDictionary<string, int> PickupCounts,
    IReadOnlySet<int> PickupIds,
    IReadOnlySet<string>? SpawnedKinds = null)
{
    /// <summary>
    /// The kinds this layout is answerable for. Defaults to the kinds it placed.
    /// <para>
    /// **Not every reported signal is a spawned object.** A run reports what it collected *and* what
    /// it did — obstacles dodged, ground covered — and a layout has no opinion about those. Checking
    /// them against a spawn table they were never in reads every one as "claimed more than existed"
    /// and rejects an honest run, which is the one outcome exact verification must never produce.
    /// </para>
    /// <para>
    /// Name a kind here that this seed placed **zero** of to keep it checkable: without that, a claim
    /// for a kind the track happened not to spawn would go unverified rather than being refused.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> VerifiableKinds { get; } =
        SpawnedKinds ?? PickupCounts.Keys.ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Re-derives a run's layout from the seed the server issued, so a claim can be checked **exactly**
/// rather than merely bounded.
/// <para>
/// This is the difference between "500 coins in four seconds is improbable" and "this track had 180
/// coins on it". Everything else in run settlement caps and flags precisely because it can only ever
/// be probabilistic; a layout the server generated is the one thing it can be certain about, and it
/// is the only input allowed to <b>reject</b> a run outright.
/// </para>
/// <para>
/// <b>A generator is registered per game and per version, and both halves matter.</b> The client's
/// generation code and this one must agree bit for bit — they are two implementations of one
/// algorithm, and the moment they drift, legitimate runs start being rejected. The version is stamped
/// on the run at <i>start</i>, so a client mid-rollout is verified against the generator it actually
/// used rather than whichever one the server prefers today.
/// </para>
/// <para>
/// <b>Unregistered means unverified, never unpayable.</b> A game with no generator settles on the
/// plausibility bounds alone. That is the safe default and it is deliberate: a half-ported generator
/// that silently disagrees with the client would reject real runs from real children, which is far
/// worse than the farming it was meant to stop.
/// </para>
/// </summary>
public interface IRunLayoutVerifier
{
    /// <summary>
    /// The generator version to stamp on a new run, or <c>0</c> when this game has none registered.
    /// Read once at start and never re-read, so a deploy mid-run cannot change the rules under it.
    /// </summary>
    int VersionFor(string gameKey);

    /// <summary>
    /// The layout that seed produced, or null when nothing can verify it — an unregistered game, a
    /// run stamped <c>0</c>, or a version this deployment no longer carries.
    /// <para>
    /// Null is a decision, not a failure: a run whose generator has been retired still settles, on the
    /// bounds. Removing a version must not retroactively destroy runs queued offline against it.
    /// </para>
    /// </summary>
    RunLayout? Derive(string gameKey, int version, long seed);
}
