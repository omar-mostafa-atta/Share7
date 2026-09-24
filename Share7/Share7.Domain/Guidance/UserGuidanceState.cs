using System;

namespace Share7.Domain.Guidance;

/// <summary>
/// Authoritative user guidance progress and CRDT state snapshot in table UserGuidanceStates.
/// One row per user.
/// </summary>
public class UserGuidanceState
{
    /// <summary>
    /// Owner. Cascades from AspNetUsers.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Monotonically increasing generation number.
    /// Bumped by an administrator hard-resetting a user's guidance state.
    /// Tells devices to discard older local journal progress.
    /// </summary>
    public int Generation { get; set; } = 1;

    /// <summary>
    /// Wire schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Last recorded session ordinal.
    /// </summary>
    public int SessionOrdinal { get; set; }

    /// <summary>
    /// UTC day string ("yyyy-MM-dd") of the last active session.
    /// </summary>
    public string LastSessionDayUtc { get; set; } = string.Empty;

    /// <summary>
    /// Full serialized GuidanceStateSnapshot wire payload.
    /// </summary>
    public string StateJson { get; set; } = "{}";

    /// <summary>
    /// Indexed projection: whether onboarding.home has been completed.
    /// Enables rapid filtering in admin without deserializing JSON.
    /// </summary>
    public bool CompletedOnboarding { get; set; }

    /// <summary>
    /// Indexed projection: total count of finished flows.
    /// </summary>
    public int TotalFlowsCompleted { get; set; }

    /// <summary>
    /// Timestamp of the last state update.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optimistic concurrency token.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
