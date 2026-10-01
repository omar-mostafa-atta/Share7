namespace Share7.Domain.Multiplayer;

/// <summary>
/// Every legal move between <see cref="MultiplayerSessionState"/>s, in one table.
/// <para>
/// **The state machine lives here and nowhere else.** Each service write is a conditional UPDATE
/// guarded on the states this table says the move may come from, so an illegal transition is not
/// something a service remembers to refuse — the row simply does not match. The sweeper, the host's
/// own moves and the admin close all read the same table.
/// </para>
/// <para>
/// The table is the contract's diagram plus the two moves the API collapses into one call:
/// <c>Created → Running</c> (start passes through <c>Starting</c> with nothing to wait for) and
/// <c>non-terminal → Closed</c> (close passes through <c>Closing</c> the same way). Both collapsed
/// intermediate states stay legal so a future client that drives them explicitly needs no server
/// change.
/// </para>
/// </summary>
public static class MultiplayerSessionTransitions
{
    private static readonly IReadOnlyDictionary<MultiplayerSessionState, IReadOnlySet<MultiplayerSessionState>> Legal =
        new Dictionary<MultiplayerSessionState, IReadOnlySet<MultiplayerSessionState>>
        {
            [MultiplayerSessionState.Creating] = Set(
                MultiplayerSessionState.Created,
                MultiplayerSessionState.Closed,
                MultiplayerSessionState.Failed,
                MultiplayerSessionState.Abandoned),

            [MultiplayerSessionState.Created] = Set(
                MultiplayerSessionState.Starting,
                MultiplayerSessionState.Running,
                MultiplayerSessionState.Closing,
                MultiplayerSessionState.Closed,
                MultiplayerSessionState.Abandoned),

            [MultiplayerSessionState.Starting] = Set(
                MultiplayerSessionState.Running,
                MultiplayerSessionState.Closing,
                MultiplayerSessionState.Closed,
                MultiplayerSessionState.Failed,
                MultiplayerSessionState.Abandoned),

            [MultiplayerSessionState.Running] = Set(
                MultiplayerSessionState.Ending,
                MultiplayerSessionState.Closing,
                MultiplayerSessionState.Closed,
                MultiplayerSessionState.Abandoned),

            [MultiplayerSessionState.Ending] = Set(
                MultiplayerSessionState.Closing,
                MultiplayerSessionState.Closed,
                MultiplayerSessionState.Abandoned),

            [MultiplayerSessionState.Closing] = Set(
                MultiplayerSessionState.Closed,
                MultiplayerSessionState.Failed,
                MultiplayerSessionState.Abandoned)

            // Terminal states have no entry: they are absorbing.
        };

    /// <summary>Whether a session may move from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool CanTransition(MultiplayerSessionState from, MultiplayerSessionState to) =>
        Legal.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>
    /// Every state <paramref name="to"/> may be reached from — exactly the set a conditional UPDATE
    /// moving a session into <paramref name="to"/> guards on.
    /// </summary>
    public static IReadOnlyList<MultiplayerSessionState> SourcesOf(MultiplayerSessionState to) =>
        Legal.Where(pair => pair.Value.Contains(to)).Select(pair => pair.Key).ToList();

    private static IReadOnlySet<MultiplayerSessionState> Set(params MultiplayerSessionState[] states) =>
        new HashSet<MultiplayerSessionState>(states);
}
