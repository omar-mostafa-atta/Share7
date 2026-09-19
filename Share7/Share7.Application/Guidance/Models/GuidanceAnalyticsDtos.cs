using System;
using System.Collections.Generic;

namespace Share7.Application.Guidance.Models;

/// <summary>
/// Aggregated funnel and drop-off analytics for a specific guidance flow.
/// </summary>
public sealed class GuidanceFlowFunnelDto
{
    public Guid FlowId { get; set; }
    public string FlowKey { get; set; } = string.Empty;
    public string FlowTitle { get; set; } = string.Empty;
    public int? Version { get; set; }
    public DateTime FromDayUtc { get; set; }
    public DateTime ToDayUtc { get; set; }
    public int TotalStarted { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalAbandoned { get; set; }
    public int TotalSkipped { get; set; }
    public double CompletionRate { get; set; }
    public double AverageDurationSeconds { get; set; }
    public List<GuidanceStepFunnelDto> Steps { get; set; } = new();
}

/// <summary>
/// Step-level conversion, drop-off, and dwell duration within a flow funnel.
/// </summary>
public sealed class GuidanceStepFunnelDto
{
    public int StepIndex { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string? Anchor { get; set; }
    public string? LocKey { get; set; }
    public int ReachedCount { get; set; }
    public int DropOffCount { get; set; }
    public double DropOffRate { get; set; }
    public double ConversionFromStart { get; set; }
    public double ConversionFromPrevious { get; set; }
    public double AverageDurationMs { get; set; }
}

/// <summary>
/// Diagnostic report of anchors that could not be located in client scenes.
/// </summary>
public sealed class GuidanceMissingAnchorSummaryDto
{
    public string AnchorId { get; set; } = string.Empty;
    public string FlowKey { get; set; } = string.Empty;
    public int StepIndex { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public List<string> Platforms { get; set; } = new();
    public List<string> AppVersions { get; set; } = new();
}

/// <summary>
/// High-level performance summary for a guidance flow across the catalogue.
/// </summary>
public sealed class GuidanceFlowSummaryStatsDto
{
    public Guid FlowId { get; set; }
    public string FlowKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int ActiveVersion { get; set; }
    public bool IsKillSwitched { get; set; }
    public int TotalStarted { get; set; }
    public int TotalCompleted { get; set; }
    public double CompletionRate { get; set; }
    public int MissingAnchorCount { get; set; }
    public DateTime? LastActivityUtc { get; set; }
}
