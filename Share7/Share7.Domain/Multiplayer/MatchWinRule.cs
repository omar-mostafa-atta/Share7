using System.Text.Json;
using System.Text.Json.Serialization;
using Share7.Domain.Economy;

namespace Share7.Domain.Multiplayer;

/// <summary>
/// How far the server can vouch for a match metric. Shown to whoever authors a rule, because a
/// competition is only as trustworthy as the least trustworthy thing it ranks on.
/// </summary>
public enum MatchMetricTrust
{
    Unknown = 0,

    /// <summary>
    /// Computed by the server from its own data — answers graded against its own key. A modified
    /// client cannot move it. **The only kind a prize-bearing competition should rank on.**
    /// </summary>
    Verified = 1,

    /// <summary>
    /// Reported by the client, then bounded by what is physically possible: a signal count by its
    /// per-second rate over the server-bounded play time, a duration by real elapsed time. A player
    /// who reports past the bound is ranked below every player who did not.
    /// </summary>
    Bounded = 2,

    /// <summary>Reported by the client and not checkable at all. Fine for fun; never for prizes.</summary>
    Reported = 3
}

/// <summary>
/// Everything a match can be decided on. **The vocabulary is small on purpose and one entry is open:**
/// <c>signal:&lt;kind&gt;</c> covers every count any mini-game reports — kills, coins, metres,
/// starfish — so a new game's "most kills wins" is a mode setting, not a backend change.
/// <para>
/// Adding a fixed token here means adding the code that computes it in <c>MatchResultService</c> in
/// the same change — the same rule <c>LeaderboardMetrics</c> keeps, for the same reason: a token
/// nothing computes is a rule an operator authors and waits on forever.
/// </para>
/// </summary>
public static class MatchMetrics
{
    /// <summary>
    /// 1 when the player's run ended <c>Completed</c> — finished the course, survived to the end —
    /// otherwise 0. The first criterion of a last-one-standing mode. <see cref="MatchMetricTrust.Reported"/>.
    /// </summary>
    public const string Outcome = "outcome";

    /// <summary>
    /// Play time in milliseconds: the run's own duration, clamped by the server to real elapsed time
    /// and to the match's own start and end. Higher for survival, lower for a race.
    /// <see cref="MatchMetricTrust.Bounded"/>.
    /// </summary>
    public const string DurationMs = "duration_ms";

    /// <summary>
    /// Questions answered correctly in the match's lesson, graded by the server.
    /// <see cref="MatchMetricTrust.Verified"/>.
    /// </summary>
    public const string CorrectAnswers = "correct_answers";

    /// <summary>
    /// Whole percent of the match's lesson answered correctly, graded by the server.
    /// <see cref="MatchMetricTrust.Verified"/>.
    /// </summary>
    public const string Accuracy = "accuracy";

    /// <summary>
    /// The prefix of the open entry: <c>signal:kill</c>, <c>signal:coin</c>, <c>signal:distance_m</c>.
    /// <see cref="MatchMetricTrust.Bounded"/>.
    /// </summary>
    public const string SignalPrefix = "signal:";

    private static readonly IReadOnlyDictionary<string, MatchMetricTrust> Fixed =
        new Dictionary<string, MatchMetricTrust>(StringComparer.Ordinal)
        {
            [Outcome] = MatchMetricTrust.Reported,
            [DurationMs] = MatchMetricTrust.Bounded,
            [CorrectAnswers] = MatchMetricTrust.Verified,
            [Accuracy] = MatchMetricTrust.Verified
        };

    /// <summary>The fixed tokens, in the order an authoring screen should offer them.</summary>
    public static IReadOnlyList<string> FixedTokens { get; } =
        [CorrectAnswers, Accuracy, DurationMs, Outcome];

    /// <summary>
    /// The metric in its stored form, or null when it names nothing a match can be decided on.
    /// A signal kind is normalised the way settlement normalises it, and must be one a run may report
    /// — <c>signal:correct_answer</c> is refused, because answers are graded, and
    /// <see cref="CorrectAnswers"/> is the graded version.
    /// </summary>
    public static string? Normalise(string? token)
    {
        var trimmed = (token ?? string.Empty).Trim().ToLowerInvariant();

        if (Fixed.ContainsKey(trimmed))
            return trimmed;

        if (!trimmed.StartsWith(SignalPrefix, StringComparison.Ordinal))
            return null;

        var kind = SignalKinds.Normalise(trimmed[SignalPrefix.Length..]);

        return kind is not null && SignalKinds.IsReportableBy(kind, SignalSurface.Run)
            ? SignalPrefix + kind
            : null;
    }

    /// <summary>The signal kind a <c>signal:</c> token names, or null for a fixed token.</summary>
    public static string? SignalKindOf(string metric) =>
        metric.StartsWith(SignalPrefix, StringComparison.Ordinal) ? metric[SignalPrefix.Length..] : null;

    /// <summary>How far the server can vouch for a (normalised) metric.</summary>
    public static MatchMetricTrust TrustOf(string metric) =>
        Fixed.TryGetValue(metric, out var trust)
            ? trust
            : metric.StartsWith(SignalPrefix, StringComparison.Ordinal) ? MatchMetricTrust.Bounded : MatchMetricTrust.Unknown;

    /// <summary>Whether the metric comes from graded answers rather than from the player's run.</summary>
    public static bool IsAnswerMetric(string metric) => metric is CorrectAnswers or Accuracy;
}

/// <summary>Which way a criterion ranks. Wire tokens <c>higher</c> and <c>lower</c>.</summary>
public enum MatchRankOrder
{
    Unknown = 0,

    /// <summary>More is better: most kills, most correct answers, longest survival.</summary>
    HigherWins = 1,

    /// <summary>Less is better: fastest finish.</summary>
    LowerWins = 2
}

/// <summary>One step of a win rule: a metric and which way it ranks.</summary>
public sealed record MatchWinCriterion(string Metric, MatchRankOrder Order);

/// <summary>
/// How a match of one mode is won: an ordered list of criteria, the first deciding and each later
/// one breaking the ties the earlier ones left. "Most correct answers, then fastest" is two criteria;
/// a last-one-standing mode is "survived, then longest alive, then most kills".
/// <para>
/// **Authored per mode, never hard-coded per game**, so a new mode chooses how it is won without a
/// deploy. A mode with no rule still records who played; it simply crowns nobody, because a winner
/// decided by a rule nobody chose is worse than no winner.
/// </para>
/// <para>
/// Stored as JSON on the mode and **snapshotted onto every result it decides**, so editing a mode's
/// rule later cannot re-decide matches already played under the old one.
/// </para>
/// </summary>
public sealed class MatchWinRule
{
    /// <summary>More than this and the rule stops being explainable to a child on a results screen.</summary>
    public const int MaxCriteria = 5;

    public const int MaxJsonLength = 1024;

    public const string HigherToken = "higher";
    public const string LowerToken = "lower";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MatchWinRule(IReadOnlyList<MatchWinCriterion> criteria) => Criteria = criteria;

    public IReadOnlyList<MatchWinCriterion> Criteria { get; }

    /// <summary>Whether any criterion is decided from graded answers.</summary>
    public bool UsesAnswers => Criteria.Any(c => MatchMetrics.IsAnswerMetric(c.Metric));

    /// <summary>Whether any criterion is decided from the player's run.</summary>
    public bool UsesRun => Criteria.Any(c => !MatchMetrics.IsAnswerMetric(c.Metric));

    /// <summary>The least trustworthy thing this rule ranks on — what the rule as a whole can be trusted to.</summary>
    public MatchMetricTrust Trust =>
        Criteria.Count == 0 ? MatchMetricTrust.Unknown : Criteria.Max(c => MatchMetrics.TrustOf(c.Metric));

    public static string OrderToken(MatchRankOrder order) => order == MatchRankOrder.LowerWins ? LowerToken : HigherToken;

    public static MatchRankOrder? ParseOrder(string? token) => (token ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        HigherToken => MatchRankOrder.HigherWins,
        LowerToken => MatchRankOrder.LowerWins,
        _ => null
    };

    /// <summary>
    /// A rule from authored rows, or the problems that stop it being one. Every problem is reported
    /// at once, so an operator fixes a form in one pass rather than one refusal at a time.
    /// </summary>
    public static (MatchWinRule? Rule, IReadOnlyList<string> Problems) Build(
        IEnumerable<(string? Metric, string? Order)> rows)
    {
        var problems = new List<string>();
        var criteria = new List<MatchWinCriterion>();

        foreach (var (metricToken, orderToken) in rows)
        {
            var metric = MatchMetrics.Normalise(metricToken);
            var order = ParseOrder(orderToken);

            if (metric is null)
                problems.Add($"'{metricToken}' is not something a match can be decided on. Use one of " +
                             $"{string.Join(", ", MatchMetrics.FixedTokens)}, or signal:<kind> for a count the game reports.");
            else if (criteria.Any(c => c.Metric == metric))
                problems.Add($"'{metric}' appears more than once; each metric can decide a match only once.");

            if (order is null)
                problems.Add($"'{orderToken}' is not a direction. Use '{HigherToken}' or '{LowerToken}'.");

            if (metric is not null && order is { } known)
                criteria.Add(new MatchWinCriterion(metric, known));
        }

        if (criteria.Count > MaxCriteria)
            problems.Add($"A rule can have at most {MaxCriteria} criteria.");

        return problems.Count > 0 ? (null, problems) : (new MatchWinRule(criteria), problems);
    }

    public string ToJson() =>
        JsonSerializer.Serialize(Criteria.Select(c => new Row { Metric = c.Metric, Order = OrderToken(c.Order) }), Json);

    /// <summary>
    /// The rule stored as <paramref name="json"/>, or null. **Never throws**: a rule this deployment
    /// cannot read degrades to "decides nothing" rather than failing every result of the mode.
    /// </summary>
    public static MatchWinRule? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var rows = JsonSerializer.Deserialize<List<Row>>(json, Json);

            if (rows is null || rows.Count == 0)
                return null;

            var (rule, _) = Build(rows.Select(r => (r.Metric, r.Order)));
            return rule;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class Row
    {
        public string? Metric { get; set; }
        public string? Order { get; set; }
    }
}
