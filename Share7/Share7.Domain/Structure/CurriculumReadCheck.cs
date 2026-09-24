namespace Share7.Domain.Structure;

/// <summary>
/// One day's tally for one curriculum read, while the node readers run in the shadow of the typed
/// ones. Composite key (Day, ReadName).
/// <para>
/// **This table is the evidence for the switch-over.** In <c>Shadow</c> mode every sampled game
/// read is answered by the typed tables (what the game has always received) and also by the node
/// tables, and the two answers are compared byte for byte. The plan's gate is fourteen days in a
/// row with <see cref="Differed"/> and <see cref="Failed"/> at zero across every read — then the
/// read model is set to <c>Generic</c>, and back again with one config change if anything is wrong.
/// </para>
/// </summary>
public class CurriculumReadCheck
{
    public DateOnly Day { get; set; }

    /// <summary>Which read: <c>grades</c>, <c>terms</c>, <c>lesson-questions</c>…</summary>
    public string ReadName { get; set; } = string.Empty;

    public long Compared { get; set; }

    /// <summary>Both paths answered, and the answers were not identical.</summary>
    public long Differed { get; set; }

    /// <summary>The node path threw. The caller still got the typed answer.</summary>
    public long Failed { get; set; }

    public DateTime? LastDifferenceAtUtc { get; set; }

    /// <summary>The first differing line and a little context, for whoever investigates.</summary>
    public string? LastDifferenceSample { get; set; }
}
