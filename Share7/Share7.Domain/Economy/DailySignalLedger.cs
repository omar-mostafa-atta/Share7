namespace Share7.Domain.Economy;

/// <summary>
/// How many of one signal kind one user has been **paid for** in one UTC day, across every session
/// and both reporting surfaces. The counter <see cref="SignalValuation.MaxPerDay"/> is checked
/// against.
/// <para>
/// **Paid, not claimed.** A run capped down to 20 must not spend 47 of the day's allowance, or the
/// cap becomes a punishment for having collected too much.
/// </para>
/// <para>
/// **This exists for the read, not the write.** The daily figure used to be a group-by over
/// <c>RunPayouts</c> joined to <c>Runs</c> — correct, and a scan whose cost grows with every run the
/// platform has ever settled, on the hot path of every settlement. At a million daily sessions that
/// is the query that falls over first. This is one keyed row per (user, kind, day), read and written
/// by primary key, and it is the only shape that also spans both surfaces: an attempt's payouts are
/// not run payouts and would never have appeared in that scan at all.
/// </para>
/// </summary>
public class DailySignalLedger
{
    public Guid UserId { get; set; }

    /// <summary>A <see cref="SignalKinds"/> token, normalised.</summary>
    public string SignalKind { get; set; } = string.Empty;

    /// <summary>Midnight UTC of the day being counted. Date-only; the time component is always zero.</summary>
    public DateTime DayUtc { get; set; }

    /// <summary>Signals of this kind actually paid for today. Never decremented.</summary>
    public int PaidCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
