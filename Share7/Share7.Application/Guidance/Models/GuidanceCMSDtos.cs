using System;
using System.Collections.Generic;

namespace Share7.Application.Guidance.Models;

public sealed class GuidanceFlowAdminDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = "Tour";
    public int Priority { get; set; } = 3;
    public int ReplayPolicy { get; set; }
    public bool Skippable { get; set; } = true;
    public int SkipAfterStep { get; set; } = 2;
    public bool Resumable { get; set; }
    public bool IsKillSwitched { get; set; }
    public int ActiveVersionNumber { get; set; }
    public string? TargetAudienceJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public GuidanceFlowVersionAdminDto? ActiveVersion { get; set; }
    public GuidanceFlowVersionAdminDto? DraftVersion { get; set; }
    public List<GuidanceFlowVersionAdminDto> Versions { get; set; } = new();
}

public sealed class GuidanceFlowVersionAdminDto
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public string StepsJson { get; set; } = "[]";
    public string? TriggerConditionsJson { get; set; }
    public string? ChangeSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
}

public sealed class CreateGuidanceFlowRequest
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = "Tour";
    public int Priority { get; set; } = 3;
    public int ReplayPolicy { get; set; }
    public bool Skippable { get; set; } = true;
    public int SkipAfterStep { get; set; } = 2;
    public bool Resumable { get; set; }
    public string? TargetAudienceJson { get; set; }
    public string InitialStepsJson { get; set; } = "[]";
    public string? InitialTriggerConditionsJson { get; set; }
}

public sealed class UpdateGuidanceFlowDraftRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Kind { get; set; }
    public int? Priority { get; set; }
    public int? ReplayPolicy { get; set; }
    public bool? Skippable { get; set; }
    public int? SkipAfterStep { get; set; }
    public bool? Resumable { get; set; }
    public string? TargetAudienceJson { get; set; }

    public string StepsJson { get; set; } = "[]";
    public string? TriggerConditionsJson { get; set; }
    public string? ChangeSummary { get; set; }
}

public sealed class PublishGuidanceFlowRequest
{
    public string? ChangeSummary { get; set; }
}

public sealed class ToggleKillSwitchRequest
{
    public bool IsKillSwitched { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ResetUserGuidanceRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class GuidanceAuditLogDto
{
    public Guid Id { get; set; }
    public Guid? FlowId { get; set; }
    public string? FlowKey { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? DetailsJson { get; set; }
}

/// <summary>
/// Client catalogue delivered to Unity (<c>GET /api/guidance/catalog</c>).
/// Contains all published, non-kill-switched flows.
/// </summary>
public sealed class GuidanceCatalogClientDto
{
    public int SchemaVersion { get; set; } = 1;
    public List<GuidanceFlowClientDto> Flows { get; set; } = new();
}

public sealed class GuidanceFlowClientDto
{
    public string Id { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public int Priority { get; set; } = 3;
    public int Replay { get; set; }
    public bool Skippable { get; set; } = true;
    public int SkipAfterStep { get; set; } = 2;
    public bool Resumable { get; set; }
    public string StepsJson { get; set; } = "[]";
    public string? TriggerConditionsJson { get; set; }
    public string? TargetAudienceJson { get; set; }
}
