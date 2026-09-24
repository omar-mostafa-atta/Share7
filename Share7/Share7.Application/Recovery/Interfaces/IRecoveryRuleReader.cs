namespace Share7.Application.Recovery.Interfaces;

/// <summary>
/// Answers one question: **when this child gets questions wrong in this lesson, what happens?**
/// <para>
/// There is exactly one implementation and everything reads through it — the game's opt-in
/// endpoint, the Studio board that shows a team what a node inherits, and the draft machinery that
/// records what a rule was before somebody proposed changing it. A second copy of "most specific
/// wins" is a second answer to the same question, and the two would drift.
/// </para>
/// </summary>
public interface IRecoveryRuleReader
{
    /// <summary>
    /// The rule governing one node. Never null: where nobody has written anything, the platform's
    /// own defaults come back, saying so.
    /// </summary>
    Task<RecoveryRuleInForce> InForceAsync(Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for many nodes in two queries rather than one per node — what the Studio's list of
    /// a subject's lessons reads, and what a batch of drafts is checked against.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RecoveryRuleInForce>> InForceAsync(
        IReadOnlyList<Guid> nodeIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The rule that applies somewhere, and where it came from. The provenance is not decoration: a
/// team looking at a lesson has to be able to tell "nobody has decided this" from "somebody decided
/// this, three levels up", and those two look identical if only the numbers come back.
/// </summary>
public sealed record RecoveryRuleInForce
{
    /// <summary>Wrong answers in the main pool before the second-chance pool opens.</summary>
    public required int AfterWrongAnswers { get; init; }

    /// <summary>How many second-chance questions to serve, capped by how many the lesson has.</summary>
    public required int QuestionsToServe { get; init; }

    public required bool AllowRepeats { get; init; }

    /// <summary>The rule row, or null when these are the platform's defaults.</summary>
    public Guid? RuleId { get; init; }

    /// <summary>The node the rule was written at. Null for the defaults.</summary>
    public Guid? FromNodeId { get; init; }

    /// <summary>grade, subject, lesson — or null for the defaults.</summary>
    public string? FromNodeKind { get; init; }

    /// <summary>True when the rule was written at the node asked about rather than inherited.</summary>
    public bool IsOwn { get; init; }

    /// <summary>
    /// What "this has not moved underneath the draft" is compared against. Changes whenever the
    /// winning rule changes, including when a rule appears above one that did not exist before.
    /// </summary>
    public string Fingerprint => RuleId is { } id ? $"rule:{id:N}" : "default";
}
