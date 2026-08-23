using Share7.Domain.Leaderboards;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Works out where a board's windows fall.
/// <para>
/// **Boundaries are computed from the calendar, not from when the board happened to be created.**
/// A weekly board authored on a Wednesday still rolls over on Monday, because "this week" has to
/// mean the same thing to every child on the platform — otherwise two boards created a day apart
/// would be answering slightly different questions and nobody would be able to say why.
/// </para>
/// <para>
/// Everything here is UTC. A single timezone is the only honest choice for a global board: any
/// other rule means a cycle ends at a different moment for different children, and the one whose
/// midnight came first gets an extra day to climb.
/// </para>
/// </summary>
public static class LeaderboardCycleFactory
{
    /// <summary>
    /// The window containing <paramref name="atUtc"/>, or null for an event board, whose bounds
    /// are authored by hand rather than derived.
    /// </summary>
    public static (DateTime StartsAtUtc, DateTime EndsAtUtc)? WindowFor(
        LeaderboardPeriod period, DateTime atUtc)
    {
        var day = new DateTime(atUtc.Year, atUtc.Month, atUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        return period switch
        {
            LeaderboardPeriod.Daily => (day, day.AddDays(1)),

            // ISO weeks: Monday to Monday. Sunday-start is a regional convention and this is not a
            // regional product.
            LeaderboardPeriod.Weekly => WeekOf(day),

            LeaderboardPeriod.Monthly => MonthOf(day),

            // One window that never ends. MaxValue rather than null so every range query stays a
            // single comparison with no special case for "forever".
            LeaderboardPeriod.AllTime => (DateTime.UnixEpoch, DateTime.MaxValue),

            _ => null
        };
    }

    private static (DateTime, DateTime) WeekOf(DateTime day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7;
        var monday = day.AddDays(-offset);

        return (monday, monday.AddDays(7));
    }

    private static (DateTime, DateTime) MonthOf(DateTime day)
    {
        var first = new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return (first, first.AddMonths(1));
    }
}
