using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// A win rule on the wire, in one place — the client's mode read, the authoring console and every
/// match result render it identically.
/// </summary>
internal static class MatchRuleMapping
{
    public static List<MatchWinCriterionDto> ToDto(MatchWinRule? rule) =>
        rule?.Criteria
            .Select(c => new MatchWinCriterionDto
            {
                Metric = c.Metric,
                Order = MatchWinRule.OrderToken(c.Order),
                Trust = TrustToken(MatchMetrics.TrustOf(c.Metric))
            })
            .ToList() ?? [];

    public static string TrustToken(MatchMetricTrust trust) => trust switch
    {
        MatchMetricTrust.Verified => "verified",
        MatchMetricTrust.Bounded => "bounded",
        MatchMetricTrust.Reported => "reported",
        _ => "unknown"
    };
}
