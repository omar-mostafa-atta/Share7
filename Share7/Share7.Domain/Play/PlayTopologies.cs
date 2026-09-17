namespace Share7.Domain.Play;

/// <summary>
/// Who is in the match: one player, players against each other, or players against the content
/// together. **The shape of the session, never the content of it.**
/// <para>
/// A flags set rather than a single value because one mode is routinely offered in more than one
/// shape — Classic is played alone and head to head, and it is the same rules either way. Modelling
/// it as one topology per row would split every such mode in two, give each half its own key, and
/// split its leaderboard with it.
/// </para>
/// <para>
/// <see cref="Coop"/> is declared and deliberately not implemented. It exists here so that adding it
/// is a mode edit rather than a schema change, and so nothing in this domain has to be re-shaped
/// when the first game defines what a shared run means — a mode offering it is refused at authoring
/// until then.
/// </para>
/// </summary>
[Flags]
public enum PlayTopologies
{
    None = 0,

    Solo = 1 << 0,

    Versus = 1 << 1,

    Coop = 1 << 2
}

/// <summary>
/// Converts <see cref="PlayTopologies"/> between the stored bitfield and the wire's token list.
/// <para>
/// **Stored as an integer, sent as strings.** A bitfield is one indexable column that matchmaking
/// can filter on; <c>["solo","versus"]</c> is what the Unity client's own <c>PlayTopologySet</c>
/// parses, and it survives a new member being added in the middle. The two forms are converted in
/// exactly one place — here — so the column and the contract cannot drift.
/// </para>
/// </summary>
public static class PlayTopologyTokens
{
    public const string Solo = "solo";
    public const string Versus = "versus";
    public const string Coop = "coop";

    /// <summary>Every token the set carries, in a fixed order so two reads are byte-identical.</summary>
    public static IReadOnlyList<string> ToTokens(PlayTopologies topologies)
    {
        var tokens = new List<string>(3);

        if (topologies.HasFlag(PlayTopologies.Solo)) tokens.Add(Solo);
        if (topologies.HasFlag(PlayTopologies.Versus)) tokens.Add(Versus);
        if (topologies.HasFlag(PlayTopologies.Coop)) tokens.Add(Coop);

        return tokens;
    }

    /// <summary>
    /// The set those tokens name, or null when any of them names nothing. Null rather than
    /// "the ones we understood": an authoring call that asked for a topology this server has never
    /// heard of has to be refused, not quietly narrowed to the subset it recognised.
    /// </summary>
    public static PlayTopologies? FromTokens(IEnumerable<string>? tokens)
    {
        if (tokens is null) return null;

        var set = PlayTopologies.None;

        foreach (var token in tokens)
        {
            if (!TryParse(token, out var one)) return null;

            set |= one;
        }

        return set;
    }

    public static bool TryParse(string? token, out PlayTopologies topology)
    {
        switch ((token ?? string.Empty).Trim().ToLowerInvariant())
        {
            case Solo: topology = PlayTopologies.Solo; return true;
            case Versus: topology = PlayTopologies.Versus; return true;
            case Coop: topology = PlayTopologies.Coop; return true;
            default: topology = PlayTopologies.None; return false;
        }
    }

    /// <summary>Whether this set can seat <paramref name="playerCount"/> players at all.</summary>
    public static bool AllowsPlayerCount(PlayTopologies topologies, int playerCount) =>
        playerCount <= 1
            ? topologies.HasFlag(PlayTopologies.Solo)
            : topologies.HasFlag(PlayTopologies.Versus) || topologies.HasFlag(PlayTopologies.Coop);
}
