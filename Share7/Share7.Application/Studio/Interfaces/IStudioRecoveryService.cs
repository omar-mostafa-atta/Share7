using Share7.Application.Common.Models;
using Share7.Application.Workspace.Models;

namespace Share7.Application.Studio.Interfaces;

/// <summary>
/// Second chances, as the team writes them: what happens at a place in the curriculum, where that
/// was decided, and what changing it would reach.
/// <para>
/// There is no write here on purpose. A rule is a draft, and drafts already have a service — this
/// only has to be able to say what is true now, well enough that somebody can tell whether it
/// should be.
/// </para>
/// </summary>
public interface IStudioRecoveryService
{
    /// <summary>
    /// What happens at one node, where it was decided, what a change here would reach, and whether
    /// somebody is already changing it.
    /// </summary>
    Task<ServiceResult<RecoveryAtNodeDto>> AtNodeAsync(
        StudioMember member, Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every place a rule has actually been written, newest first — the answer to "what have we
    /// decided anywhere?", which no tree walk will give you.
    /// </summary>
    Task<IReadOnlyList<RecoveryRuleRowDto>> WrittenAsync(
        StudioMember member, Guid langId, CancellationToken cancellationToken = default);
}

public sealed record RecoveryAtNodeDto
{
    public required Guid NodeId { get; init; }
    public required string NodeKind { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<TrailStepDto> Trail { get; init; }

    public required int AfterWrongAnswers { get; init; }
    public required int QuestionsToServe { get; init; }
    public required bool AllowRepeats { get; init; }

    /// <summary>True when the rule was written here. False means it is inherited, or nobody's.</summary>
    public required bool IsOwn { get; init; }

    /// <summary>Where it was written, when that is not here. Null for the platform's own defaults.</summary>
    public Guid? FromNodeId { get; init; }
    public string? FromNodeTitle { get; init; }

    /// <summary>True when nobody has written anything anywhere above: these are the platform's.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>Lessons under this node. What a rule written here would govern.</summary>
    public required int Lessons { get; init; }

    /// <summary>Lessons under here that have a rule of their own, which this one would not override.</summary>
    public required int LessonsWithOwnRule { get; init; }

    /// <summary>Lessons under here with no second-chance questions at all, where a rule changes nothing.</summary>
    public required int LessonsWithNoRecoveryQuestions { get; init; }

    /// <summary>The team's open draft for this node's rule, if there is one.</summary>
    public Guid? OpenDraftId { get; init; }

    public required bool CanPropose { get; init; }
}

public sealed record RecoveryRuleRowDto
{
    public required Guid RuleId { get; init; }
    public required Guid NodeId { get; init; }
    public required string NodeKind { get; init; }
    public required string Title { get; init; }
    public required int AfterWrongAnswers { get; init; }
    public required int QuestionsToServe { get; init; }
    public required bool AllowRepeats { get; init; }
    public required DateTime WrittenAtUtc { get; init; }
    public Guid? ReleaseId { get; init; }
}
