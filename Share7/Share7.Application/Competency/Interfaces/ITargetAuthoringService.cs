using Share7.Domain.Competency;

namespace Share7.Application.Competency.Interfaces;

/// <summary>
/// Replaces lesson placeholders with real, authored learning targets — subject by subject, which
/// is the only way it can be done.
/// <para>
/// <b>This is the recompute line paying for itself, and it is the first time it does.</b> Items
/// reference targets through a mapping table rather than owning them, so re-targeting a thousand
/// questions is an UPDATE on a join table; and because observations are derived from the immutable
/// response log rather than stored as the truth, every piece of historical evidence follows the
/// remap automatically. A child who answered forty questions last March has those forty answers
/// re-interpreted against the real claim they were always about, without anybody replaying a
/// single lesson (§20.5).
/// </para>
/// <para>
/// In a design where progress was current-state rows, this operation is impossible: the evidence
/// that would have to be re-interpreted was overwritten the moment it was recorded.
/// </para>
/// </summary>
public interface ITargetAuthoringService
{
    /// <summary>
    /// The placeholders under one curriculum node, with how much evidence each is carrying — so an
    /// author works on the claims that matter rather than alphabetically.
    /// </summary>
    Task<IReadOnlyList<PlaceholderTargetDto>> GetPlaceholdersAsync(
        Guid nodeId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints one authored target and moves the named placeholders' content onto it.
    /// <para>
    /// Four things happen, and the order matters: the real target is created in the authored
    /// framework; a <see cref="LearningTargetEdgeKind.SupersededBy"/> edge is written from each
    /// placeholder so the old claim stays readable and traceable; every
    /// <c>ItemTargetMapping</c> pointing at a placeholder is repointed; and the affected items'
    /// observations are regenerated from the responses.
    /// </para>
    /// <para>
    /// The placeholders are <b>deprecated, never deleted</b>. A measurement taken against one last
    /// month was a real measurement and its row still names a target that has to resolve.
    /// </para>
    /// </summary>
    Task<TargetPromotionReport> PromoteAsync(
        PromoteTargetRequest request, CancellationToken cancellationToken = default);

    /// <summary>Marks an authored target as reviewed by a subject specialist, or back to unreviewed.</summary>
    Task<bool> SetReviewStateAsync(
        Guid targetId, TargetReviewState state, CancellationToken cancellationToken = default);

    /// <summary>Authored targets in one framework, for picking blueprint lines.</summary>
    Task<IReadOnlyList<AuthoredTargetDto>> ListAuthoredAsync(
        Guid frameworkId, Guid langId, string? search = null, CancellationToken cancellationToken = default);
}

/// <summary>A lesson placeholder as the authoring surface shows it.</summary>
public sealed record PlaceholderTargetDto
{
    public required Guid TargetId { get; init; }
    public required string Statement { get; init; }
    public required Guid? NodeId { get; init; }
    public required string? NodeTitle { get; init; }

    /// <summary>Items mapped to it. What a promotion would move.</summary>
    public required int ItemCount { get; init; }

    /// <summary>Observations it is carrying. What a promotion would re-interpret.</summary>
    public required int ObservationCount { get; init; }

    /// <summary>True once something has superseded it. Kept in the list so the work is visible.</summary>
    public required bool IsSuperseded { get; init; }
}

public sealed record AuthoredTargetDto
{
    public required Guid TargetId { get; init; }
    public required string TargetKey { get; init; }
    public required string Statement { get; init; }
    public required string TargetKindKey { get; init; }
    public required TargetReviewState ReviewState { get; init; }
    public int? DifficultyBand { get; init; }
    public required int ItemCount { get; init; }
    public required int SupersededPlaceholders { get; init; }
}

/// <summary>
/// What an author submits: the real claim, and the placeholders it replaces.
/// <para>
/// The statement is required in both languages the platform serves, because a target with no
/// Arabic statement is a target half the learners cannot be shown.
/// </para>
/// </summary>
public sealed record PromoteTargetRequest
{
    public required string TargetKey { get; init; }

    /// <summary>The claim, phrased as one: "Can order fractions with unlike denominators."</summary>
    public required IReadOnlyDictionary<Guid, string> Statements { get; init; }

    public string TargetKindKey { get; init; } = TargetKinds.Skill;
    public int? DifficultyBand { get; init; }

    /// <summary>The placeholders whose items move onto the new target. At least one.</summary>
    public required IReadOnlyList<Guid> ReplacesTargetIds { get; init; }

    /// <summary>Mark it reviewed straight away. Only true when a specialist is the one authoring.</summary>
    public bool MarkReviewed { get; init; }

    /// <summary>
    /// Which framework the real claim belongs to. Null means Share7's own, which is right while
    /// nobody has imported an official curriculum — and wrong the moment somebody has, because a
    /// claim minted beside the ministry's own wording is a second vocabulary for the same thing.
    /// </summary>
    public Guid? IntoFrameworkId { get; init; }

    /// <summary>
    /// A skill that already exists — the ordinary case once the official outcomes are imported.
    /// The stand-ins' questions move onto it and nothing is minted; <see cref="TargetKey"/> and
    /// <see cref="Statements"/> are then ignored, because the claim is already written and this
    /// operation has no business rewording somebody else's curriculum.
    /// </summary>
    public Guid? UseExistingTargetId { get; init; }
}

/// <summary>What a promotion actually moved. Every number here is checkable in SQL afterwards.</summary>
public sealed record TargetPromotionReport
{
    public required Guid TargetId { get; init; }
    public required string TargetKey { get; init; }
    public required int PlaceholdersSuperseded { get; init; }
    public required int ItemMappingsMoved { get; init; }
    public required int NodeMappingsMoved { get; init; }

    /// <summary>
    /// Observations regenerated against the new claim. **Historical evidence, re-interpreted** —
    /// and the reason this phase's most consequential operation is an UPDATE and a rebuild rather
    /// than a data loss event.
    /// </summary>
    public required int ObservationsRebuilt { get; init; }

    /// <summary>Exclusion annotations carried across the rebuild. Human decisions are not derived.</summary>
    public required int ExclusionsPreserved { get; init; }
}
