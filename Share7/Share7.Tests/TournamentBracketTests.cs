using Share7.Domain.Multiplayer;
using Xunit;

namespace Share7.Tests;

public class TournamentBracketTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(16)]
    public void Round_robin_schedules_every_pair_once_with_one_match_per_round(int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var pairs = new HashSet<(Guid, Guid)>();
        for (var round = 1; round <= TournamentBrackets.RoundRobinRounds(count); round++)
        {
            var scheduled = TournamentBrackets.RoundRobinRound(ids, round, _ => true, (_, _) => false);
            Assert.Equal(ids.Order(), scheduled.SelectMany(p => new[] { p.A, p.B }).OfType<Guid>().Order());
            foreach (var pair in scheduled.Where(p => p.A != null && p.B != null))
            {
                var a = pair.A!.Value; var b = pair.B!.Value;
                Assert.True(pairs.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a)));
            }
        }
        Assert.Equal(count * (count - 1) / 2, pairs.Count);
    }

    [Fact]
    public void Round_robin_withdrawal_and_blocks_preserve_the_original_schedule()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var blocked = TournamentBrackets.RoundRobinRound(ids, 1, _ => true, (_, _) => true);
        Assert.All(blocked, p => Assert.True(p.NotPlayed));
        var left = TournamentBrackets.RoundRobinRound(ids, 1, id => id != ids[0], (_, _) => false);
        Assert.DoesNotContain(left.SelectMany(p => new[] { p.A, p.B }), id => id == ids[0]);
    }
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(64)]
    [InlineData(256)]
    public void A_bracket_contains_each_entrant_once_and_gives_byes_to_the_top_seeds(int count)
    {
        var players = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var pairs = TournamentBrackets.EliminationFirstRound(players);
        var seated = pairs.SelectMany(p => new[] { p.A, p.B }).OfType<Guid>().ToList();
        Assert.Equal(count, seated.Count);
        Assert.Equal(players.Order(), seated.Order());
        var byes = TournamentBrackets.BracketSize(count) - count;
        Assert.Equal(players.Take(byes).Order(), pairs.Where(p => p.B is null).Select(p => p.A!.Value).Order());
    }

    [Fact]
    public void Top_two_seeds_can_only_meet_in_the_final()
    {
        var order = TournamentBrackets.SeedOrder(16);
        Assert.True(Array.IndexOf(order, 1) < 8);
        Assert.True(Array.IndexOf(order, 2) >= 8);
        Assert.Equal(Enumerable.Range(1, 16), order.Order());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    public void Knockout_placements_share_the_place_earned_by_the_round(int roundsBeforeFinal, int place) =>
        Assert.Equal(place, TournamentBrackets.EliminationPlacement(4, 5 - roundsBeforeFinal));

    [Fact]
    public void Swiss_avoids_previous_opponents_and_blocks_and_seats_everyone_once()
    {
        var players = Enumerable.Range(1, 12).Select(i => new SwissPlayer(Guid.NewGuid(), i % 3, i, false)).ToArray();
        var first = TournamentBrackets.SwissRound(players, 1, (_, _) => false, (_, _) => false);
        var met = first.SelectMany(p => new[] { (p.A!.Value, p.B!.Value), (p.B!.Value, p.A!.Value) }).ToHashSet();
        bool Blocked(Guid a, Guid b) => (a == players[0].UserId && b == players[2].UserId) || (b == players[0].UserId && a == players[2].UserId);
        var second = TournamentBrackets.SwissRound(players, 2, (a, b) => met.Contains((a, b)), Blocked);
        Assert.All(second, p => { Assert.False(p.NotPlayed); Assert.DoesNotContain((p.A!.Value, p.B!.Value), met); Assert.False(Blocked(p.A.Value, p.B.Value)); });
        Assert.Equal(players.Select(p => p.UserId).Order(), second.SelectMany(p => new[] { p.A, p.B }).OfType<Guid>().Order());
    }

    [Fact]
    public void Swiss_byes_rotate_and_an_unavoidable_block_is_never_a_playable_pair()
    {
        var players = Enumerable.Range(1, 5).Select(i => new SwissPlayer(Guid.NewGuid(), 0, i, i == 5)).ToArray();
        var round = TournamentBrackets.SwissRound(players, 2, (_, _) => false, (_, _) => true);
        Assert.Equal(players[3].UserId, Assert.Single(round, p => p.B is null).A);
        Assert.All(round.Where(p => p.B is not null), p => Assert.True(p.NotPlayed));
    }

    [Fact]
    public void Swiss_standings_use_points_then_opposition_then_wins_then_seed()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var placed = TournamentBrackets.SwissPlacements([
            new(a, 6, 2, 3, 1), new(b, 6, 5, 2, 3), new(c, 8, 0, 0, 4), new(d, 6, 5, 3, 2)]);
        Assert.Equal(new[] { c, d, b, a }, placed.Select(p => p.UserId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, placed.Select(p => p.Placement));
    }
}
