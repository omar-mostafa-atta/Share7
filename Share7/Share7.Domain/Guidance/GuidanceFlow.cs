using System;
using System.Collections.Generic;

namespace Share7.Domain.Guidance;

/// <summary>
/// Root aggregate for a remotely manageable guidance flow (e.g. "onboarding.home", "feature.shop").
/// Supports versioning, drafts, immutable publishing, targeting, and an emergency kill switch.
/// </summary>
public class GuidanceFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique flow identifier (e.g., "onboarding.home", "tip.streak").
    /// Matches client-side GuidanceId.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable title for SuperAdmin CMS.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Purpose, target segment, or design notes.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Presentation kind (e.g., "Tour", "Nudge", "Spotlight", "Banner", "Badge").
    /// </summary>
    public string Kind { get; set; } = "Tour";

    /// <summary>
    /// Priority tier: 0=Ambient, 1=Queued, 2=Immediate, 3=Blocking.
    /// </summary>
    public int Priority { get; set; } = 3;

    /// <summary>
    /// Replay policy: 0=Never, 1=Always, 2=OnVersionChange.
    /// </summary>
    public int ReplayPolicy { get; set; } = 0;

    /// <summary>
    /// When true, offers a skip control to the child.
    /// </summary>
    public bool Skippable { get; set; } = true;

    /// <summary>
    /// Step index after which the skip control appears.
    /// </summary>
    public int SkipAfterStep { get; set; } = 2;

    /// <summary>
    /// When true, resumes from the furthest step reached rather than restarting from step 0.
    /// </summary>
    public bool Resumable { get; set; }

    /// <summary>
    /// Emergency kill switch. When true, the flow is instantly hidden from all client catalogs.
    /// </summary>
    public bool IsKillSwitched { get; set; }

    /// <summary>
    /// The currently active published version number (0 if only drafts exist).
    /// </summary>
    public int ActiveVersionNumber { get; set; }

    /// <summary>
    /// Audience targeting rules in JSON (e.g. gradeIds, minLevel, maxLevel, userSegments).
    /// </summary>
    public string? TargetAudienceJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// All versions (drafts, published, archived).
    /// </summary>
    public ICollection<GuidanceFlowVersion> Versions { get; set; } = new List<GuidanceFlowVersion>();

    /// <summary>
    /// Audit log of all authoring and publishing actions.
    /// </summary>
    public ICollection<GuidanceAuditLog> AuditLogs { get; set; } = new List<GuidanceAuditLog>();
}
