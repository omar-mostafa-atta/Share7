using Share7.Domain.Assessment;

namespace Share7.Application.Assessment.Interfaces;

/// <summary>
/// Decides what the learner sees next in a sitting.
/// <para>
/// <b>The seam, not the engine.</b> Adaptive testing is not being built now and should not be —
/// it needs calibrated items, and the platform has none. What it needs from the data model, it
/// already has: the blueprint carries target weights, a difficulty distribution and a stopping
/// rule, so an <c>AdaptiveSelector</c> arriving in Phase 5 changes nothing about the schema and
/// nothing about the sitting endpoints. Version one walks a fixed form in order, which is what
/// ships (§4.4).
/// </para>
/// </summary>
public interface IItemSelector
{
    /// <summary>
    /// The next position to serve, or null when the sitting is done. Returning a **position**
    /// rather than an item version is what lets a later adaptive implementation append to the form
    /// as it goes without the caller knowing the difference.
    /// </summary>
    Task<AssessmentFormItem?> NextAsync(
        AdministrationContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything a selector may look at. Deliberately small: a selector that could see the learner's
/// whole history would be a measurement engine wearing a selector's interface.
/// </summary>
public sealed record AdministrationContext
{
    public required Guid AdministrationId { get; init; }
    public required Guid LearnerId { get; init; }
    public required Guid FormId { get; init; }

    /// <summary>Positions already answered in this sitting, in order.</summary>
    public required IReadOnlyList<int> AnsweredPositions { get; init; }

    /// <summary>The blueprint the form was built from, when there is one. What an adaptive selector reads.</summary>
    public Guid? BlueprintId { get; init; }
}
