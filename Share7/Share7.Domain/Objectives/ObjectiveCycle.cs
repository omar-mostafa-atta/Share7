using System.Globalization;

namespace Share7.Domain.Objectives;

/// <summary>
/// Which window an objective is counting in, derived from the clock rather than stored in a table.
/// <para>
/// **There is no cycle table and no rollover job.** A daily quest's cycle is a pure function of the
/// instant, so midnight needs nothing to run — and therefore nothing can fail to run. There is no
/// "the cron did not fire so nobody has quests today" incident available, no state machine to get
/// stuck, and rollover is automatically correct across restarts, deploys and a clock that jumped.
/// </para>
/// <para>
/// Leaderboards do keep cycle rows, and that is not an inconsistency: a board cycle has a
/// settlement job, frozen ranks and a lifecycle that has to be observable. A quest cycle has none
/// of those, and extracting cycles out of a shipped, settled leaderboard system to share a table
/// would be a risky refactor for no gain. The *logic* is shared here; the tables are not.
/// </para>
/// </summary>
public static class ObjectiveCycle
{
    /// <summary>The key every never-resetting objective counts under.</summary>
    public const string AllTime = "all";

    /// <summary>
    /// Hours to shift the day boundary by, so a reset can land at a sensible local hour for the
    /// primary market instead of at UTC midnight — which is the middle of the afternoon in some
    /// places and 2am in others.
    /// <para>
    /// **Changing this cannot rewrite history**, because the resolved key is stored on each
    /// progress row. A child's finished Tuesday quest stays on Tuesday.
    /// </para>
    /// <para>
    /// Per-user timezones are the real answer and need a profile field; this is the honest
    /// one-value approximation until then.
    /// </para>
    /// </summary>
    public static int ResetOffsetHours { get; set; }

    /// <summary>
    /// The cycle key for <paramref name="kind"/> at <paramref name="instantUtc"/>.
    /// <para>
    /// <paramref name="seasonKey"/> is required for <see cref="ObjectiveKind.Seasonal"/> and
    /// ignored otherwise — a season is authored, not computed.
    /// </para>
    /// </summary>
    public static string KeyFor(ObjectiveKind kind, DateTime instantUtc, string? seasonKey = null)
    {
        var local = instantUtc.AddHours(ResetOffsetHours);

        return kind switch
        {
            ObjectiveKind.Daily => $"d:{local:yyyy-MM-dd}",
            ObjectiveKind.Weekly => $"w:{IsoWeekKey(local)}",
            ObjectiveKind.Monthly => $"m:{local:yyyy-MM}",

            // Falls back to all-time rather than throwing: an unkeyed season is a configuration
            // slip, and one objective counting too broadly is a far smaller failure than a
            // submitted attempt that throws on its way through the projector.
            ObjectiveKind.Seasonal => string.IsNullOrWhiteSpace(seasonKey)
                ? AllTime
                : $"s:{seasonKey.Trim()}",

            _ => AllTime
        };
    }

    /// <summary>
    /// When the given cycle stops accepting progress, or null for one that never ends.
    /// <para>
    /// Used to set <c>UserObjectiveProgress.ClaimableUntilUtc</c>, and only ever to expire rows
    /// still <see cref="ObjectiveState.InProgress"/>.
    /// </para>
    /// </summary>
    public static DateTime? EndsAtUtc(ObjectiveKind kind, DateTime instantUtc)
    {
        var local = instantUtc.AddHours(ResetOffsetHours);

        var localEnd = kind switch
        {
            ObjectiveKind.Daily => local.Date.AddDays(1),
            ObjectiveKind.Weekly => StartOfIsoWeek(local).AddDays(7),
            ObjectiveKind.Monthly => new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1),
            _ => (DateTime?)null
        };

        // Back out of the offset so the answer is in the same clock the caller asked with.
        return localEnd?.AddHours(-ResetOffsetHours);
    }

    /// <summary>
    /// ISO-8601 week, as <c>yyyy-Www</c>. ISO rather than a naive day-of-year division because the
    /// week a Sunday belongs to is otherwise a coin toss, and a child's weekly quest resetting a
    /// day early once a year is the kind of bug nobody ever finds deliberately.
    /// </summary>
    private static string IsoWeekKey(DateTime local)
    {
        var week = ISOWeek.GetWeekOfYear(local);
        var year = ISOWeek.GetYear(local);

        return $"{year:D4}-W{week:D2}";
    }

    private static DateTime StartOfIsoWeek(DateTime local)
    {
        // Monday is day 1 in ISO; DayOfWeek puts Sunday at 0, so it needs remapping.
        var day = (int)local.DayOfWeek;
        var offset = day == 0 ? 6 : day - 1;

        return local.Date.AddDays(-offset);
    }
}
