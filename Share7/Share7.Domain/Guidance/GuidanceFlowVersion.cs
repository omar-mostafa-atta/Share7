using System;

namespace Share7.Domain.Guidance;

/// <summary>
/// A specific version of a guidance flow. Once Published, steps and trigger conditions are immutable.
/// Modifications create or update a new Draft version.
/// </summary>
public class GuidanceFlowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FlowId { get; set; }

    public GuidanceFlow Flow { get; set; } = null!;

    /// <summary>
    /// Sequential version number (1, 2, 3...).
    /// </summary>
    public int VersionNumber { get; set; } = 1;

    /// <summary>
    /// Status: "Draft", "Published", "Archived".
    /// </summary>
    public string Status { get; set; } = "Draft";

    /// <summary>
    /// Serialized JSON containing all GuidanceStep definitions (lines, anchors, mood, haptics, advances, branches).
    /// </summary>
    public string StepsJson { get; set; } = "[]";

    /// <summary>
    /// Serialized JSON containing Entry and Abort conditions.
    /// </summary>
    public string? TriggerConditionsJson { get; set; }

    /// <summary>
    /// Optional change notes entered by the editor.
    /// </summary>
    public string? ChangeSummary { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAtUtc { get; set; }

    public Guid? PublishedByUserId { get; set; }
}
