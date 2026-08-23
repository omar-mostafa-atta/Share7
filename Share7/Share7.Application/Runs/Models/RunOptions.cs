namespace Share7.Application.Runs.Models;

/// <summary>
/// Configuration for runs, bound from the <c>Runs</c> section.
/// <para>
/// Everything here is a **bound**, not a tuning knob for the economy. What a pickup is worth lives in
/// <c>PickupValuation</c>, in the database, so rebalancing never needs a deploy; these are the limits
/// that keep the shape of a run sane, and they change roughly never.
/// </para>
/// <para>
/// Every one of them **caps and flags rather than refusing**. A child on a device with a bad clock, or
/// one whose session dropped and resumed, will trip these legitimately — losing their run and being
/// unable to explain why is a worse outcome than paying them a capped amount and marking the run for
/// somebody to look at.
/// </para>
/// </summary>
public class RunOptions
{
    public const string SectionName = "Runs";

    /// <summary>
    /// How long a started run stays settleable.
    /// <para>
    /// Generous on purpose. The offline queue exists precisely so a run finished in a car with no
    /// signal survives until the device reconnects, and an hour comfortably covers a commute — a
    /// window sized to a normal session would throw away exactly the runs the queue was built to
    /// save. It is a bound on how long a run can be *held open and batched*, not a play timer.
    /// </para>
    /// </summary>
    public int RunLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// How many runs one account may hold open at once. Beyond it, the **oldest is expired** rather
    /// than the new one refused — the child in front of the device is trying to play now, and the
    /// stale run they abandoned twenty minutes ago is the one that should give way.
    /// <para>
    /// Without this a client opens ten thousand runs and settles them as a batch, which is a farming
    /// pattern that every per-run bound is blind to.
    /// </para>
    /// </summary>
    public int MaxConcurrentOpenRuns { get; set; } = 3;

    /// <summary>
    /// Runs one account may **settle** in a UTC day. Past it a run still settles and still records
    /// what was collected, but pays nothing and says <c>daily_run_limit</c>.
    /// <para>
    /// Sized so no child reaches it by playing. A two-minute run every two minutes for eight hours
    /// straight is 240 — this is what a script looks like, not a school holiday.
    /// </para>
    /// </summary>
    public int MaxRunsPerDay { get; set; } = 300;

    /// <summary>
    /// Below this, a run is flagged as suspiciously instant. **Flagged only, never capped on its
    /// own** — a genuine crash or an instant fail is short and legitimate, and it is
    /// <see cref="MaxPickupsPerSecond"/> that decides whether the *claim* was possible in the time.
    /// </summary>
    public int MinRunDurationMs { get; set; } = 1_000;

    /// <summary>
    /// The per-second plausibility bound: most pickups of one kind that could be collected per second
    /// of run time. A four-second run claiming 300 coins settles at the bound and is flagged.
    /// <para>
    /// This is what makes the duration clamp load-bearing rather than cosmetic — the client's claimed
    /// duration is already bounded by real elapsed server time, so inflating it to buy headroom here
    /// does not work.
    /// </para>
    /// <para>
    /// Applied per kind, with a floor of one second, so a legitimate run shorter than a second is not
    /// paid zero for arithmetic reasons.
    /// </para>
    /// </summary>
    public int MaxPickupsPerSecond { get; set; } = 20;
}
