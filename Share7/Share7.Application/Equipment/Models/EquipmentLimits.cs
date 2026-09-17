using System.Text.RegularExpressions;

namespace Share7.Application.Equipment.Models;

/// <summary>
/// The bounds on a stored outfit, in one place so the service, the tests and the docs cannot
/// disagree about them.
/// <para>
/// **Why any limits at all.** Keys are never checked against a catalogue — there isn't one, and
/// adding one would stop cosmetics shipping ahead of a backend deploy. That makes this endpoint an
/// authenticated, unbounded, free-text store keyed entirely by client-supplied strings, on a
/// product used by children. These caps are the whole of its abuse surface control: they bound how
/// much a single account can write and keep the stored values to an inert character set.
/// </para>
/// </summary>
public static class EquipmentLimits
{
    /// <summary>
    /// Worn items, and so also the number of stored rows one account can hold. Comfortably above
    /// any plausible avatar rig.
    /// <para>
    /// There is no separate colour cap any more. Colours are nested inside the equipped entries, so
    /// there can never be more of them than there are items — the old standalone limit of 256 had
    /// nothing left to bound once a colour stopped being submittable on its own.
    /// </para>
    /// </summary>
    public const int MaxEquipped = 32;

    public const int MaxKeyLength = 64;

    /// <summary>
    /// Allowed key characters.
    /// <para>
    /// ⚠ **The underscore is a deliberate addition to the specified charset.** The spec gave
    /// <c>^[A-Za-z0-9.-]+$</c>, but every example key in that same spec — <c>hat_wizard</c>,
    /// <c>Dia_Hel</c>, <c>gold_Shield</c> — contains an underscore and would have been rejected by
    /// it, making the endpoint unusable against its own worked example on day one. Excluding
    /// <c>_</c> while permitting <c>.</c> and <c>-</c> reads as an oversight rather than a
    /// decision: it is inert in every context these strings reach, and strictly less dangerous
    /// than the dot and hyphen already allowed. Delete the <c>_</c> here if the narrower set was
    /// meant — it is the only place it is defined.
    /// </para>
    /// <para>
    /// Anchored and given a timeout: it runs against attacker-supplied input on every save.
    /// </para>
    /// </summary>
    public static readonly Regex KeyPattern =
        new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>True when a key is present, short enough, and made only of allowed characters.</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && KeyPattern.IsMatch(key);
}
