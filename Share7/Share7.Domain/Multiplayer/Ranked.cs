namespace Share7.Domain.Multiplayer;

/// <summary>A skill estimate: a mean and how unsure of it the model still is.</summary>
public readonly record struct Rating(double Mu, double Sigma)
{
    /// <summary>
    /// The conservative estimate — "at least this good, almost certainly". What visible tiers are
    /// read from, so a new player climbs as the model grows sure of them rather than starting high.
    /// </summary>
    public double Ordinal => Mu - 3 * Sigma;
}

/// <summary>
/// The rating model: Weng and Lin's Bayesian approximation, Bradley–Terry full-pair variant
/// (<i>A Bayesian Approximation Method for Online Ranking</i>, JMLR 12, 2011) — the published,
/// unpatented model the OpenSkill libraries implement, with their default parameters.
/// <para>
/// Chosen because it rates **any number of players with ties** in one update — a four-player match
/// placing 1, 1, 3, 4 is one call — which an Elo or Glicko made for pairs only approximates.
/// </para>
/// <para>
/// **Never shown to a player.** It decides who meets whom; the visible rank is a seasonal projection
/// of it (<see cref="RankedTiers"/>).
/// </para>
/// </summary>
public static class RatingModel
{
    public const double InitialMu = 25.0;
    public const double InitialSigma = 25.0 / 3.0;

    /// <summary>How much one match's performance varies around a player's true skill.</summary>
    public const double Beta = 25.0 / 6.0;

    /// <summary>The floor on how far one match may shrink uncertainty — keeps sigma positive.</summary>
    public const double Kappa = 0.0001;

    /// <summary>
    /// Added uncertainty before each match, so a rating never freezes: a child who improves over a
    /// term can still move.
    /// </summary>
    public const double Tau = 25.0 / 300.0;

    public static Rating Initial => new(InitialMu, InitialSigma);

    /// <summary>
    /// New ratings for one match. <paramref name="placements"/> pairs each player's rating with
    /// their placement — 1 is first, and equal placements are ties. Returned in the same order.
    /// </summary>
    public static IReadOnlyList<Rating> Rate(IReadOnlyList<(Rating Rating, int Placement)> placements)
    {
        if (placements.Count < 2)
            return placements.Select(p => p.Rating).ToList();

        var prior = placements
            .Select(p => new Rating(p.Rating.Mu, Math.Sqrt(p.Rating.Sigma * p.Rating.Sigma + Tau * Tau)))
            .ToArray();

        var rated = new Rating[prior.Length];

        for (var i = 0; i < prior.Length; i++)
        {
            var varianceI = prior[i].Sigma * prior[i].Sigma;
            double omega = 0, delta = 0;

            for (var q = 0; q < prior.Length; q++)
            {
                if (q == i)
                    continue;

                var varianceQ = prior[q].Sigma * prior[q].Sigma;
                var c = Math.Sqrt(varianceI + varianceQ + 2 * Beta * Beta);

                // Probability that i beats q, given what is known of both.
                var p = 1.0 / (1.0 + Math.Exp((prior[q].Mu - prior[i].Mu) / c));

                // Placement 1 is best: q placing worse than i is a win for i.
                var score = placements[q].Placement > placements[i].Placement ? 1.0
                    : placements[q].Placement == placements[i].Placement ? 0.5
                    : 0.0;

                var gamma = Math.Sqrt(varianceI) / c;

                omega += varianceI / c * (score - p);
                delta += gamma * varianceI / (c * c) * p * (1 - p);
            }

            rated[i] = new Rating(
                prior[i].Mu + omega,
                Math.Sqrt(varianceI * Math.Max(1 - delta, Kappa)));
        }

        return rated;
    }
}

/// <summary>
/// What a player sees: a tier and a division, read from the conservative estimate, ratcheted to the
/// season's peak.
/// <para>
/// **It never goes down within a season.** A child who loses three in a row keeps the tier they
/// reached; the hidden rating still moves, so they meet fairer opponents, but nothing on screen is
/// taken away. That is the no-loss-aversion rule (MultiplayerPlatform.md §16), applied to ranked.
/// </para>
/// </summary>
public static class RankedTiers
{
    public static readonly IReadOnlyList<string> Tiers = ["bronze", "silver", "gold", "platinum", "diamond"];

    /// <summary>Ordinal width of one tier. Diamond is open-ended.</summary>
    public const double TierWidth = 8.0;

    public const int DivisionsPerTier = 3;

    /// <summary>
    /// The tier and division (3 is the lowest, 1 the highest; diamond has no divisions) for an
    /// ordinal.
    /// </summary>
    public static (string Tier, int? Division) For(double ordinal)
    {
        var tierIndex = (int)Math.Floor(Math.Max(0, ordinal) / TierWidth);

        if (tierIndex >= Tiers.Count - 1)
            return (Tiers[^1], null);

        var within = Math.Max(0, ordinal) - tierIndex * TierWidth;
        var step = TierWidth / DivisionsPerTier;
        var division = DivisionsPerTier - Math.Min(DivisionsPerTier - 1, (int)Math.Floor(within / step));

        return (Tiers[tierIndex], division);
    }

    /// <summary>The tier as a number, 1 bronze to 5 diamond — what <c>RANKED_TIER</c> records.</summary>
    public static int TierNumber(double ordinal) => Tiers.ToList().IndexOf(For(ordinal).Tier) + 1;

    /// <summary>A comparable position on the ladder: higher is better. For "did this match promote".</summary>
    public static int Step(double ordinal)
    {
        var (tier, division) = For(ordinal);
        var tierIndex = Tiers.ToList().IndexOf(tier);
        return tierIndex * DivisionsPerTier + (DivisionsPerTier - (division ?? 1));
    }
}

/// <summary>Seasons are calendar months, UTC — the unit a child understands as "this month's rank".</summary>
public static class RankedSeasons
{
    public static string KeyFor(DateTime utc) => $"{utc:yyyy-MM}";

    public static DateTime EndOf(string seasonKey)
    {
        var start = DateTime.SpecifyKind(DateTime.ParseExact(seasonKey + "-01", "yyyy-MM-dd", null), DateTimeKind.Utc);
        return start.AddMonths(1);
    }
}

/// <summary>
/// One player's hidden rating for one mode. Updated only by the server's own verdict on a rated match,
/// once per match, under an update lock taken in user order.
/// </summary>
public class PlayerRating
{
    public Guid UserId { get; set; }
    public Guid ModeId { get; set; }

    public double Mu { get; set; }
    public double Sigma { get; set; }

    public int MatchesPlayed { get; set; }

    /// <summary>The season of the last rated match — a new season's first match softens the rating first.</summary>
    public string SeasonKey { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// What one rated match did to one player's rating. **The idempotency key and the audit trail at
/// once**: one row per (match, player), so a match is never rated twice, and every change can be
/// explained — including the ones the anti-boosting rule declined to apply.
/// </summary>
public class PlayerRatingChange
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid ModeId { get; set; }

    public int Placement { get; set; }

    public double MuBefore { get; set; }
    public double SigmaBefore { get; set; }
    public double MuAfter { get; set; }
    public double SigmaAfter { get; set; }

    /// <summary>The rating did not move, and why — e.g. <c>repeat_opponent</c>.</summary>
    public string? SkippedReason { get; set; }

    /// <summary>The season the match counted towards.</summary>
    public string SeasonKey { get; set; } = string.Empty;

    /// <summary>This match raised the season's peak to a higher division or tier.</summary>
    public bool Promoted { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>A player's season in one ranked mode: how far they got, never how far they fell.</summary>
public class RankedSeasonStanding
{
    public Guid UserId { get; set; }
    public Guid ModeId { get; set; }
    public string SeasonKey { get; set; } = string.Empty;

    public int MatchesPlayed { get; set; }
    public int Wins { get; set; }

    /// <summary>The best conservative estimate reached after placements. Null while still placing.</summary>
    public double? PeakOrdinal { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
