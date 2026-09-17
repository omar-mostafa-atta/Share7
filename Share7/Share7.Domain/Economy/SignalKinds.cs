using System.Text.RegularExpressions;

namespace Share7.Domain.Economy;

/// <summary>
/// Which reporting surface is allowed to say a signal happened.
/// <para>
/// **This is the ownership split, made structural.** A runner session produces two independent
/// server calls — a graded lesson attempt and a settled run — and both can pay. Without an owner per
/// kind, a correct answer is paid once by the attempt and once again by the run, from two
/// transactions with two idempotency keys that cannot see each other. The rule is not a comment
/// anybody has to remember: <see cref="SignalKinds.OwnerOf"/> decides, and each surface drops what it
/// does not own before pricing.
/// </para>
/// </summary>
public enum SignalSurface
{
    /// <summary>
    /// Reported by a settled run. Motion-derived: things only the client could have observed —
    /// a coin taken, an obstacle dodged, ground covered. Bounded by the run's own duration.
    /// </summary>
    Run = 1,

    /// <summary>
    /// Derived by the server from a graded attempt. Question-derived: the server holds the answer
    /// key, so it never takes the client's word for how many were right.
    /// </summary>
    Attempt = 2
}

/// <summary>
/// What a gameplay signal kind is allowed to look like — <c>coin</c>, <c>near_miss</c>,
/// <c>distance_m</c>, <c>mg147_starfish</c> — and who is allowed to report each one.
/// <para>
/// **A free token rather than an enum, and the reasoning is the opposite of
/// <see cref="Rewards.RewardEventType"/>'s.** An event type must be an enum because the *server*
/// raises it, so a value with no producer is dead configuration. A signal kind is raised by a
/// mini-game and only ever priced by the server — an enum here would mean every mini-game's bespoke
/// collectible needs a backend migration and a deploy before it can be worth anything, which is
/// precisely the coupling this feature exists to remove.
/// </para>
/// <para>
/// It is also what makes an unpriced kind expressible at all. A kind the valuation table has never
/// heard of pays zero and does not fail the run; an enum would refuse it at model binding and lose a
/// child's whole run over a design oversight.
/// </para>
/// <para>
/// **Renamed from <c>PickupKinds</c> (2026-08-25).** It stopped being about pickups the moment a
/// dodge and a metre of ground became priceable: "pickup" described one member of the set and
/// mis-described the rest. The constants below are the kinds the platform itself names; everything
/// else is a token a mini-game invents and an operator prices.
/// </para>
/// </summary>
public static class SignalKinds
{
    public const int MaxLength = 32;

    /// <summary>The runner's coin. Run-owned.</summary>
    public const string Coin = "coin";

    /// <summary>
    /// An obstacle passed close enough to count as a dodge. Run-owned, and the archetype of a
    /// signal the server cannot derive: nothing but the client saw it happen.
    /// </summary>
    public const string NearMiss = "near_miss";

    /// <summary>
    /// Whole metres covered in one run. Run-owned.
    /// <para>
    /// The reason <c>SignalValuation.MaxPerSecond</c> exists: a runner covers ten metres a second and
    /// twenty coins a minute, so one global per-second bound cannot be right for both.
    /// </para>
    /// </summary>
    public const string DistanceM = "distance_m";

    /// <summary>
    /// A question answered correctly. **Attempt-owned** — the server re-grades from its own answer
    /// key, so this is a count it derives, never one a client reports.
    /// </summary>
    public const string CorrectAnswer = "correct_answer";

    private static readonly Regex Shape = new(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Who may report each kind the platform names. A kind that is **not** in here — a mini-game's
    /// own collectible — has no declared owner and is treated as run-owned, because a run is the only
    /// surface on which a client reports anything at all.
    /// </summary>
    private static readonly Dictionary<string, SignalSurface> Owners = new(StringComparer.Ordinal)
    {
        [Coin] = SignalSurface.Run,
        [NearMiss] = SignalSurface.Run,
        [DistanceM] = SignalSurface.Run,
        [CorrectAnswer] = SignalSurface.Attempt
    };

    /// <summary>
    /// Same shape as a currency key, for the same reason: it is a stable identifier that appears in
    /// both a client prefab and a database row, and case-folding or punctuation differences between
    /// the two are indistinguishable from a kind that was never priced.
    /// </summary>
    public static bool IsValid(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && kind.Length <= MaxLength && Shape.IsMatch(kind);

    /// <summary>
    /// Trims and lowercases so <c>"Coin"</c> and <c>"coin"</c> resolve to the same valuation row.
    /// Returns null for anything that is not a legal token.
    /// </summary>
    public static string? Normalise(string? kind)
    {
        var trimmed = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return IsValid(trimmed) ? trimmed : null;
    }

    /// <summary>
    /// The surface allowed to report this kind. Unknown kinds are <see cref="SignalSurface.Run"/>:
    /// a mini-game inventing <c>mg147_starfish</c> must not need a backend deploy to be able to
    /// report it, and a run is the only place a client reports anything.
    /// </summary>
    public static SignalSurface OwnerOf(string kind) =>
        Owners.TryGetValue(kind, out var owner) ? owner : SignalSurface.Run;

    /// <summary>
    /// Whether <paramref name="surface"/> may report <paramref name="kind"/>. A surface that is
    /// handed a kind it does not own drops it — it pays zero rather than failing, exactly as an
    /// unpriced kind does, because a mismatch is a client or configuration mistake and neither is
    /// worth losing a child's session over.
    /// </summary>
    public static bool IsReportableBy(string kind, SignalSurface surface) =>
        OwnerOf(kind) == surface;
}
