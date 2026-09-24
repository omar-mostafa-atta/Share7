using Share7.Domain.Assessment;

namespace Share7.Application.Assessment.Models;

/// <summary>A blueprint as the admin console reads it, with its areas and lines resolved.</summary>
public sealed record BlueprintDto
{
    public required Guid BlueprintId { get; init; }
    public required string BlueprintKey { get; init; }
    public required int VersionNumber { get; init; }
    public required string Name { get; init; }
    public required Guid FrameworkId { get; init; }
    public required string FrameworkName { get; init; }
    public string? SourceNote { get; init; }
    public required bool IsPublished { get; init; }

    public required decimal MinCoverageRatio { get; init; }
    public required decimal MinAreaCoverageRatio { get; init; }
    public required int MinObservationsOverall { get; init; }
    public required int MinObservationsPerArea { get; init; }
    public required int MaxMedianEvidenceAgeDays { get; init; }

    public required IReadOnlyList<BlueprintAreaDto> Areas { get; init; }

    /// <summary>
    /// Lines naming a lesson placeholder. **While this is non-zero the blueprint cannot support an
    /// exam claim**, and the console says so rather than letting a coverage figure imply otherwise.
    /// </summary>
    public required int PlaceholderLines { get; init; }

    /// <summary>
    /// Blueprint lines whose target no item is mapped to. A requirement nothing in the bank can
    /// satisfy — the orphan a syllabus change produces, and the thing a blueprint exists to catch.
    /// </summary>
    public required int UnservableLines { get; init; }
}

public sealed record BlueprintAreaDto
{
    public required Guid AreaId { get; init; }
    public required string AreaKey { get; init; }
    public required string Label { get; init; }
    public required decimal Weight { get; init; }

    /// <summary>Normalised across the blueprint, so the console shows shares rather than raw marks.</summary>
    public required decimal NormalisedWeight { get; init; }

    public required int Order { get; init; }
    public required IReadOnlyList<BlueprintLineDto> Lines { get; init; }
}

public sealed record BlueprintLineDto
{
    public required Guid LineId { get; init; }
    public required Guid TargetId { get; init; }
    public required string Statement { get; init; }
    public required bool IsPlaceholder { get; init; }
    public required decimal Weight { get; init; }
    public required int ItemCount { get; init; }
    public int? DifficultyBandLow { get; init; }
    public int? DifficultyBandHigh { get; init; }

    /// <summary>How many items in the bank are mapped to this target. Zero means unservable.</summary>
    public required int AvailableItems { get; init; }
}

/// <summary>Authoring payload for a blueprint. Areas and lines are written whole, never patched.</summary>
public sealed record SaveBlueprintRequest
{
    public required string BlueprintKey { get; init; }
    public required string Name { get; init; }
    public required Guid FrameworkId { get; init; }
    public string? SourceNote { get; init; }

    public int? TimeLimitMs { get; init; }
    public bool RetryPermitted { get; init; }

    public decimal MinCoverageRatio { get; init; } = 0.70m;
    public decimal MinAreaCoverageRatio { get; init; } = 0.40m;
    public int MinObservationsOverall { get; init; } = 20;
    public int MinObservationsPerArea { get; init; } = 5;
    public int MaxMedianEvidenceAgeDays { get; init; } = 120;

    public required IReadOnlyList<SaveBlueprintAreaRequest> Areas { get; init; }
}

public sealed record SaveBlueprintAreaRequest
{
    public required string AreaKey { get; init; }
    public required string Label { get; init; }
    public decimal Weight { get; init; } = 1.0m;
    public int Order { get; init; }
    public required IReadOnlyList<SaveBlueprintLineRequest> Lines { get; init; }
}

public sealed record SaveBlueprintLineRequest
{
    public required Guid TargetId { get; init; }
    public decimal Weight { get; init; } = 1.0m;
    public int ItemCount { get; init; }
    public int? DifficultyBandLow { get; init; }
    public int? DifficultyBandHigh { get; init; }
}

/// <summary>Authoring payload for an examination specification and one of its sittings.</summary>
public sealed record SaveExamSpecificationRequest
{
    public required string SpecificationKey { get; init; }
    public required string Name { get; init; }
    public Guid? AuthorityId { get; init; }
    public string? CountryCode { get; init; }
    public string? SubjectLabel { get; init; }

    public required string VersionLabel { get; init; }
    public required Guid BlueprintId { get; init; }
    public DateOnly? SittingDate { get; init; }
    public int? MaxScore { get; init; }
    public int? PassingScore { get; init; }
}

/// <summary>
/// What an admin asks for when generating the worked example: one grade, one subject, built out of
/// the platform's own content.
/// </summary>
public sealed record GenerateBenchmarkRequest
{
    /// <summary>The subject node to build the paper from. Its chapters become the blueprint's areas.</summary>
    public required Guid SubjectNodeId { get; init; }

    public string? VersionLabel { get; init; }
}

/// <summary>What a blueprint or specification write did, in the terms the console reports.</summary>
public sealed record AuthoringReport
{
    public required Guid Id { get; init; }
    public required string Key { get; init; }
    public int Areas { get; init; }
    public int Lines { get; init; }
    public int PlaceholderLines { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// How far a specification version is from being able to say anything about an outcome.
/// <para>
/// The honest answer for every exam today is "not at all", and this record is what says so with a
/// number attached rather than with silence.
/// </para>
/// </summary>
public sealed record CalibrationStatusDto
{
    public required Guid ExamSpecificationVersionId { get; init; }
    public required string Name { get; init; }
    public required string VersionLabel { get; init; }

    public required int ReportedOutcomes { get; init; }
    public required int UsableOutcomes { get; init; }
    public required int WithdrawnConsent { get; init; }
    public required int Disputed { get; init; }

    public required IReadOnlyDictionary<OutcomeVerification, int> ByVerification { get; init; }

    /// <summary>
    /// How many matched pairs a fit needs before output C renders. A stated bar, so that "not yet"
    /// has a distance attached to it.
    /// </summary>
    public required int RequiredForCalibration { get; init; }

    public required bool IsCalibrated { get; init; }
}
