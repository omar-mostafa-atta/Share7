namespace Share7.Application.Objectives.Models;

/// <summary>
/// What deleting an objective would destroy, reported before it happens.
/// <para>
/// Mirrors <c>GameDeletionImpact</c>: a delete that would take somebody's record with it is refused
/// with this breakdown attached, and only proceeds when the caller resends with <c>force=true</c>.
/// Retiring (<c>isActive: false</c>) is the reversible alternative and stays the recommended one.
/// </para>
/// </summary>
public class ObjectiveDeletionImpact
{
    /// <summary>Counter rows across every cycle — yesterday's daily is its own row.</summary>
    public int ProgressRows { get; set; }

    /// <summary>Distinct players holding any of those rows.</summary>
    public int Students { get; set; }

    /// <summary>Rows that reached the target but have not been paid yet.</summary>
    public int Completed { get; set; }

    /// <summary>
    /// Rows already paid. The worst of the four: the reward transactions that paid them key on this
    /// objective's <c>Key</c>, so deleting it leaves ledger entries nothing explains.
    /// </summary>
    public int Claimed { get; set; }

    /// <summary>
    /// Reward rules on <c>OBJECTIVE_COMPLETED</c> whose reference is this objective's key. They are
    /// not deleted with it — they simply stop being reachable, so they are worth naming.
    /// </summary>
    public int RewardRules { get; set; }

    public bool HasProgress => ProgressRows > 0 || Claimed > 0;

    public string Describe()
    {
        if (!HasProgress)
            return RewardRules > 0
                ? $"no recorded progress, but {RewardRules} reward rule(s) key on it"
                : "no recorded progress";

        return $"{ProgressRows} progress row(s) across {Students} student(s), of which " +
               $"{Completed} completed and {Claimed} already paid";
    }
}
