using Share7.Domain.Multiplayer;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The session state machine, as one table. The client mirrors the same machine
/// (<c>SessionStateMachine</c>), so every edge here is also a promise to it.
/// </summary>
public class MultiplayerSessionTransitionsTests
{
    private static readonly MultiplayerSessionState[] Live =
    [
        MultiplayerSessionState.Creating,
        MultiplayerSessionState.Created,
        MultiplayerSessionState.Starting,
        MultiplayerSessionState.Running,
        MultiplayerSessionState.Ending,
        MultiplayerSessionState.Closing
    ];

    [Theory]
    [InlineData(MultiplayerSessionState.Creating, MultiplayerSessionState.Created)]
    [InlineData(MultiplayerSessionState.Created, MultiplayerSessionState.Starting)]
    [InlineData(MultiplayerSessionState.Starting, MultiplayerSessionState.Running)]
    [InlineData(MultiplayerSessionState.Running, MultiplayerSessionState.Ending)]
    [InlineData(MultiplayerSessionState.Ending, MultiplayerSessionState.Closing)]
    [InlineData(MultiplayerSessionState.Closing, MultiplayerSessionState.Closed)]
    [InlineData(MultiplayerSessionState.Closing, MultiplayerSessionState.Failed)]
    [InlineData(MultiplayerSessionState.Creating, MultiplayerSessionState.Failed)]
    // The two moves the API collapses into one call.
    [InlineData(MultiplayerSessionState.Created, MultiplayerSessionState.Running)]
    [InlineData(MultiplayerSessionState.Running, MultiplayerSessionState.Closed)]
    public void The_contract_diagram_is_legal(MultiplayerSessionState from, MultiplayerSessionState to) =>
        Assert.True(MultiplayerSessionTransitions.CanTransition(from, to));

    [Theory]
    [InlineData(MultiplayerSessionState.Creating, MultiplayerSessionState.Running)]
    [InlineData(MultiplayerSessionState.Running, MultiplayerSessionState.Created)]
    [InlineData(MultiplayerSessionState.Running, MultiplayerSessionState.Starting)]
    [InlineData(MultiplayerSessionState.Ending, MultiplayerSessionState.Running)]
    [InlineData(MultiplayerSessionState.Created, MultiplayerSessionState.Creating)]
    public void Going_backwards_or_skipping_confirmation_is_not(MultiplayerSessionState from, MultiplayerSessionState to) =>
        Assert.False(MultiplayerSessionTransitions.CanTransition(from, to));

    [Fact]
    public void Terminal_states_are_absorbing()
    {
        foreach (var terminal in MultiplayerSessionStates.Terminal)
        foreach (var to in Enum.GetValues<MultiplayerSessionState>())
            Assert.False(MultiplayerSessionTransitions.CanTransition(terminal, to), $"{terminal} -> {to}");
    }

    [Fact]
    public void Every_live_state_can_be_abandoned_and_closed()
    {
        // The sweeper and a close both act on whatever state a session is in. If a live state could
        // not reach these, a session stuck in it could never be cleaned up.
        foreach (var state in Live)
        {
            Assert.True(MultiplayerSessionTransitions.CanTransition(state, MultiplayerSessionState.Abandoned), $"{state} -> Abandoned");
            Assert.True(MultiplayerSessionTransitions.CanTransition(state, MultiplayerSessionState.Closed), $"{state} -> Closed");
        }
    }

    [Fact]
    public void Closing_is_reachable_from_exactly_the_live_states()
    {
        // The close's WHERE clause excludes the terminal states; this is the table saying the same thing.
        Assert.Equal(
            Live.OrderBy(s => s),
            MultiplayerSessionTransitions.SourcesOf(MultiplayerSessionState.Closed).OrderBy(s => s));
    }

    [Fact]
    public void Unknown_goes_nowhere()
    {
        foreach (var to in Enum.GetValues<MultiplayerSessionState>())
            Assert.False(MultiplayerSessionTransitions.CanTransition(MultiplayerSessionState.Unknown, to));
    }
}
