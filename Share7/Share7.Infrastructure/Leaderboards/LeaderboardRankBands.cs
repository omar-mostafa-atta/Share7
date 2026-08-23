namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Turns a final rank into the reward rule scope that pays it.
/// <para>
/// **Bands exist so a prize table is data.** Without them an operator wanting to pay the top
/// hundred would author a hundred rules, and the hundredth would eventually be forgotten. With
/// them, <c>{boardKey}:top100</c> is one row in the admin UI that already exists.
/// </para>
/// <para>
/// A rank matches every band it falls inside, coarsest last, and **every matching rule pays** —
/// the same composition the lesson rules use. First place collects <c>:1</c>, <c>:top3</c>,
/// <c>:top10</c> and <c>:top100</c> if all four are authored, which is what "a bigger prize for a
/// better rank" means when expressed as data rather than as branching.
/// </para>
/// </summary>
public static class LeaderboardRankBands
{
    /// <summary>
    /// Bands are checked in this order, tightest first, so a reference key list reads the way an
    /// operator thinks about a prize table.
    /// </summary>
    private static readonly (int MaxRank, string Band)[] Bands =
    [
        (1, "1"),
        (2, "2"),
        (3, "3"),
        (10, "top10"),
        (50, "top50"),
        (100, "top100"),
        (1000, "top1000")
    ];

    /// <summary>
    /// Every reward scope a rank qualifies for, as <c>{boardKey}:{band}</c>.
    /// <para>
    /// Returns nothing for rank 0 — an entry that has never been reindexed is not a placing, and
    /// paying one would hand a prize to whoever happened to be unranked when the job ran.
    /// </para>
    /// </summary>
    public static IEnumerable<string> ReferenceKeysFor(string boardKey, int finalRank)
    {
        if (finalRank <= 0)
            yield break;

        foreach (var (maxRank, band) in Bands)
        {
            if (finalRank <= maxRank)
                yield return $"{boardKey}:{band}";
        }
    }

    /// <summary>
    /// The tightest band a rank falls in, for recording on the settlement row. Null when the rank
    /// is outside every band, which is the ordinary case for most of a large board.
    /// </summary>
    public static string? TightestBandFor(string boardKey, int finalRank) =>
        ReferenceKeysFor(boardKey, finalRank).FirstOrDefault();
}
