namespace Share7.Domain.Multiplayer;

/// <summary>
/// One pairing a format produced. <see cref="B"/> null is a bye; both null is an empty branch.
/// <see cref="NotPlayed"/> marks a pair who may not be seated together — one has blocked the other —
/// and for whom no other pairing could be found.
/// </summary>
public sealed record TournamentPairing(Guid? A, Guid? B, bool NotPlayed = false);

/// <summary>An entrant as the Swiss pairer sees them.</summary>
public sealed record SwissPlayer(Guid UserId, int Points, int Seed, bool HadBye);

/// <summary>An entrant's final Swiss record, for placing.</summary>
public sealed record SwissRecord(Guid UserId, int Points, int Buchholz, int Wins, int Seed);

/// <summary>
/// The formats, as pure functions over who is left and what has been played. Nothing here reads a
/// database or a clock, so every pairing rule is a unit test.
/// <para>
/// <b>Strategies over one primitive.</b> A format only decides who meets whom and where they finish;
/// the match itself is always the same reserved room and the same derived result. A third format —
/// round robin, double elimination — is a third set of functions here, not a new kind of match.
/// </para>
/// </summary>
public static class TournamentBrackets
{
    /// <summary>
    /// Games a knockout pairing may take before the higher seed goes through. A level game is
    /// replayed — a fresh room, the same pair — because a bracket needs one winner and a coin toss is
    /// not one; three is enough for that and short enough that a round is not held up for long.
    /// </summary>
    public const int MaxGamesPerMatch = 3;

    /// <summary>The most rounds a Swiss may be asked for. Past this, a class has played everyone worth playing.</summary>
    public const int MaxSwissRounds = 12;

    /// <summary>Search budget for one Swiss round's pairing, so a pathological field degrades to a looser pairing rather than a slow request.</summary>
    private const int PairingBudget = 200_000;

    // ---- knockout -------------------------------------------------------------------------------

    /// <summary>Rounds a knockout of this many entrants takes: log₂ of the bracket.</summary>
    public static int EliminationRounds(int entrants)
    {
        if (entrants < 2)
            return 0;

        var rounds = 0;

        for (var size = 1; size < entrants; size <<= 1)
            rounds++;

        return rounds;
    }

    /// <summary>The bracket: the smallest power of two that holds every entrant. The gap is byes.</summary>
    public static int BracketSize(int entrants) => 1 << EliminationRounds(entrants);

    /// <summary>
    /// Seed numbers in bracket order — for 8: 1, 8, 4, 5, 2, 7, 3, 6. Built so that seeds 1 and 2 can
    /// only meet in the final, 1–4 not before the semi-finals, and so on: the standard draw, which is
    /// what makes a bracket fair to the players who earned a high seed.
    /// </summary>
    public static int[] SeedOrder(int bracketSize)
    {
        var order = new List<int> { 1 };

        for (var size = 2; size <= Math.Max(2, bracketSize); size <<= 1)
            order = order.SelectMany(seed => new[] { seed, size + 1 - seed }).ToList();

        return order.ToArray();
    }

    /// <summary>
    /// The first round of a knockout, for entrants listed best seed first. Byes fall to the top
    /// seeds, as they should: a bye is an advantage, and the draw gives it to whoever earned one.
    /// </summary>
    public static IReadOnlyList<TournamentPairing> EliminationFirstRound(IReadOnlyList<Guid> seeded)
    {
        var size = BracketSize(seeded.Count);
        var order = SeedOrder(size);
        var pairings = new List<TournamentPairing>(size / 2);

        Guid? At(int seed) => seed <= seeded.Count ? seeded[seed - 1] : null;

        for (var position = 0; position < size / 2; position++)
            pairings.Add(new TournamentPairing(At(order[2 * position]), At(order[2 * position + 1])));

        return pairings;
    }

    /// <summary>
    /// The next knockout round from the winners of this one, in position order — the winners of 2p
    /// and 2p+1 meet at p. A missing winner (nobody showed) hands the other side a bye.
    /// </summary>
    public static IReadOnlyList<TournamentPairing> EliminationNextRound(
        IReadOnlyList<Guid?> winners, Func<Guid, int> seedOf)
    {
        var pairings = new List<TournamentPairing>((winners.Count + 1) / 2);

        for (var position = 0; position < (winners.Count + 1) / 2; position++)
        {
            var left = winners[2 * position];
            var right = 2 * position + 1 < winners.Count ? winners[2 * position + 1] : null;

            pairings.Add(Ordered(left, right, seedOf));
        }

        return pairings;
    }

    /// <summary>
    /// Separates pairs who may not meet, by swapping one of them with a player from the nearest
    /// pairing that clears every block. What cannot be separated — a final between the only two left —
    /// is marked <see cref="TournamentPairing.NotPlayed"/>, for the caller to settle by seed.
    /// </summary>
    public static IReadOnlyList<TournamentPairing> SeparateBlocked(
        IReadOnlyList<TournamentPairing> pairings, Func<Guid, Guid, bool> blocked, Func<Guid, int> seedOf)
    {
        var result = pairings.ToList();

        for (var i = 0; i < result.Count; i++)
        {
            if (result[i] is not { A: { } a, B: { } b } || !blocked(a, b))
                continue;

            var fixedIt = false;

            for (var distance = 1; distance < result.Count && !fixedIt; distance++)
            {
                foreach (var j in new[] { i + distance, i - distance })
                {
                    if (j < 0 || j >= result.Count || result[j] is not { A: { } c, B: { } d, NotPlayed: false })
                        continue;

                    // Swap b with d: (a, d) and (c, b). Or b with c: (a, c) and (b, d).
                    if (!blocked(a, d) && !blocked(c, b))
                    {
                        result[i] = Ordered(a, d, seedOf);
                        result[j] = Ordered(c, b, seedOf);
                        fixedIt = true;
                        break;
                    }

                    if (!blocked(a, c) && !blocked(b, d))
                    {
                        result[i] = Ordered(a, c, seedOf);
                        result[j] = Ordered(b, d, seedOf);
                        fixedIt = true;
                        break;
                    }
                }
            }

            if (!fixedIt)
                result[i] = result[i] with { NotPlayed = true };
        }

        return result;
    }

    /// <summary>
    /// Where a knockout entrant finishes: 1 for the champion, and for everyone else the place the
    /// round they went out in earns — shared, because two semi-final losers did exactly as well as
    /// each other. Out in the final is 2nd, in the semi-finals 3rd, in the quarter-finals 5th.
    /// </summary>
    public static int EliminationPlacement(int roundCount, int eliminatedInRound) =>
        (1 << Math.Max(0, roundCount - eliminatedInRound)) + 1;

    // ---- Swiss ----------------------------------------------------------------------------------

    /// <summary>Rounds a Swiss runs: as asked, or log₂ of the field — enough for one unbeaten player to emerge — within what the field can play.</summary>
    public static int SwissRoundsFor(int entrants, int requested)
    {
        if (entrants < 2)
            return 0;

        var rounds = requested > 0 ? requested : EliminationRounds(entrants);
        return Math.Clamp(rounds, 1, Math.Min(entrants - 1, MaxSwissRounds));
    }

    /// <summary>
    /// One Swiss round. Players are ranked by points, then seed. An odd field gives the bye to the
    /// lowest-ranked player who has not had one. The first round folds the field — the top half meets
    /// the bottom half, 1 v n/2+1 — so the strongest do not meet at once; later rounds pair neighbours
    /// in the standings, so everyone meets someone on their own record.
    /// <para>
    /// <b>Never twice, never blocked</b> — tried in that order of strictness. First no rematches and no
    /// blocked pairs; if the field cannot be paired that way (late rounds of a small class), rematches
    /// are allowed; and if even that fails, the remaining blocked pairs are returned
    /// <see cref="TournamentPairing.NotPlayed"/> rather than seated together.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TournamentPairing> SwissRound(
        IReadOnlyList<SwissPlayer> players,
        int round,
        Func<Guid, Guid, bool> played,
        Func<Guid, Guid, bool> blocked)
    {
        var ordered = players
            .OrderByDescending(p => p.Points)
            .ThenBy(p => p.Seed)
            .ToList();

        var pairings = new List<TournamentPairing>(ordered.Count / 2 + 1);

        if (ordered.Count % 2 == 1)
        {
            var bye = ordered.LastOrDefault(p => !p.HadBye) ?? ordered[^1];
            ordered.Remove(bye);
            pairings.Add(new TournamentPairing(bye.UserId, null));
        }

        if (ordered.Count == 0)
            return pairings;

        // The preference order the search walks: neighbours, or for the first round the fold.
        var preference = round <= 1 ? Fold(ordered) : ordered;
        var ids = preference.Select(p => p.UserId).ToArray();

        var paired = Pair(ids, (x, y) => !played(x, y) && !blocked(x, y))
                     ?? Pair(ids, (x, y) => !blocked(x, y));

        if (paired is null)
        {
            // Nothing clean exists. Neighbours, and any pair that may not meet is not seated together.
            for (var i = 0; i + 1 < ids.Length; i += 2)
                pairings.Add(new TournamentPairing(ids[i], ids[i + 1], NotPlayed: blocked(ids[i], ids[i + 1])));

            return pairings;
        }

        var seedOf = players.ToDictionary(p => p.UserId, p => p.Seed);

        pairings.AddRange(paired.Select(pair => Ordered(pair.A, pair.B, id => seedOf[id])));
        return pairings;
    }

    /// <summary>
    /// Final Swiss placings, all distinct: points, then Buchholz (the points of everyone you played —
    /// a 3–1 against strong opposition beats a 3–1 against weak), then wins, then seed.
    /// </summary>
    public static IReadOnlyList<(Guid UserId, int Placement)> SwissPlacements(IEnumerable<SwissRecord> records) =>
        records
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.Buchholz)
            .ThenByDescending(r => r.Wins)
            .ThenBy(r => r.Seed)
            .Select((r, index) => (r.UserId, index + 1))
            .ToList();

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>Top half against bottom half, interleaved so neighbours in the result are the fold's pairs.</summary>
    private static List<SwissPlayer> Fold(List<SwissPlayer> ordered)
    {
        var half = ordered.Count / 2;
        var folded = new List<SwissPlayer>(ordered.Count);

        for (var i = 0; i < half; i++)
        {
            folded.Add(ordered[i]);
            folded.Add(ordered[i + half]);
        }

        return folded;
    }

    /// <summary>
    /// A perfect pairing of <paramref name="ids"/> under <paramref name="allowed"/>, preferring each
    /// player's earliest allowed partner in list order. Null when none exists within the budget.
    /// </summary>
    private static List<(Guid A, Guid B)>? Pair(Guid[] ids, Func<Guid, Guid, bool> allowed)
    {
        var partner = new int[ids.Length];
        Array.Fill(partner, -1);

        var budget = PairingBudget;

        bool Solve()
        {
            if (--budget < 0)
                return false;

            var i = Array.IndexOf(partner, -1);

            if (i < 0)
                return true;

            for (var j = i + 1; j < ids.Length; j++)
            {
                if (partner[j] != -1 || !allowed(ids[i], ids[j]))
                    continue;

                partner[i] = j;
                partner[j] = i;

                if (Solve())
                    return true;

                partner[i] = -1;
                partner[j] = -1;

                if (budget < 0)
                    return false;
            }

            return false;
        }

        if (!Solve())
            return null;

        var pairs = new List<(Guid, Guid)>(ids.Length / 2);

        for (var i = 0; i < ids.Length; i++)
        {
            if (partner[i] > i)
                pairs.Add((ids[i], ids[partner[i]]));
        }

        return pairs;
    }

    /// <summary>Puts the better seed in A, and a lone player in A with B empty.</summary>
    private static TournamentPairing Ordered(Guid? x, Guid? y, Func<Guid, int> seedOf)
    {
        if (x is null)
            return new TournamentPairing(y, null);

        if (y is null)
            return new TournamentPairing(x, null);

        return seedOf(x.Value) <= seedOf(y.Value)
            ? new TournamentPairing(x, y)
            : new TournamentPairing(y, x);
    }
}
