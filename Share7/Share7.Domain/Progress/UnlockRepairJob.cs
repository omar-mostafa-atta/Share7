namespace Share7.Domain.Progress;

/// <summary>
/// Work queued by a structural change so that no student is left stranded by it.
/// <para>
/// **Why this exists.** Unlocks are a ledger of what a student earned, walked forward one lesson at
/// a time. Change the tree underneath and the walk can break: retire the lesson a student was on
/// and nothing will ever open the one after it; move a lesson ahead of the one a student reached
/// and it sits locked behind them. A job is written in the <b>same transaction</b> as the change,
/// then worked off straight after commit (and retried by a background sweep), granting only — the
/// ledger never loses a row. Re-running a job is harmless.
/// </para>
/// </summary>
public class UnlockRepairJob
{
    public Guid Id { get; set; }

    public UnlockRepairKind Kind { get; set; }

    /// <summary>The node that was retired, or whose children were reordered or received a moved node.</summary>
    public Guid NodeId { get; set; }

    /// <summary>For <see cref="UnlockRepairKind.PassForward"/>: the retired node's parent and position.</summary>
    public Guid? ParentNodeId { get; set; }

    public int? FormerOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Students granted something by the job, for the release's impact record.</summary>
    public int StudentsRepaired { get; set; }
}

public enum UnlockRepairKind
{
    /// <summary>
    /// A node was retired, or moved out from under its parent. Everyone who held it gets what
    /// finishing it would have given them: the next lesson, or the next chapter or term when it was
    /// the last one.
    /// </summary>
    PassForward = 0,

    /// <summary>
    /// A parent's children were put in a new order, or received a moved node. Anyone holding a later
    /// child also gets every earlier one, so nothing is locked behind what they already reached.
    /// </summary>
    FillGaps = 1
}
