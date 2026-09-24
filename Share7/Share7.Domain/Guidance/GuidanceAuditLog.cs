using System;

namespace Share7.Domain.Guidance;

/// <summary>
/// Audit trail for all changes to guidance flows (draft updates, version publishes, kill-switch toggles, user wipes).
/// </summary>
public class GuidanceAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? FlowId { get; set; }

    public GuidanceFlow? Flow { get; set; }

    /// <summary>
    /// Action performed: "Created", "DraftUpdated", "Published", "KillSwitchEnabled", "KillSwitchDisabled", "UserReset".
    /// </summary>
    public string Action { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string? UserEmail { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Details, reason, or before/after snapshot diff.
    /// </summary>
    public string? DetailsJson { get; set; }
}
