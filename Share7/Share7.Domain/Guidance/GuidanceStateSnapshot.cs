using System;
using System.Collections.Generic;

namespace Share7.Domain.Guidance;

/// <summary>
/// Wire contract for an account's guidance state snapshot.
/// Matches Unity's Game.Core.Guidance.GuidanceStateSnapshot wire format exactly.
/// </summary>
public sealed class GuidanceStateSnapshot
{
    public int schemaVersion { get; set; } = 1;
    public int sessionOrdinal { get; set; }
    public string lastSessionDayUtc { get; set; } = string.Empty;
    public List<FlowState> flows { get; set; } = new();
    public List<ShownState> shown { get; set; } = new();
    public List<string> suppressed { get; set; } = new();
    public long revision { get; set; }

    public sealed class FlowState
    {
        public string id { get; set; } = string.Empty;
        public int furthestStep { get; set; }
        public int completedVersion { get; set; }
        public int completion { get; set; }
        public bool finished { get; set; }
    }

    public sealed class ShownState
    {
        public string id { get; set; } = string.Empty;
        public int count { get; set; }
        public int sessionOrdinal { get; set; }
        public bool hasServerTime { get; set; }
        public long serverTicksUtc { get; set; }
        public string dayKey { get; set; } = string.Empty;
        public int dayCount { get; set; }
    }

    public GuidanceStateSnapshot Normalized()
    {
        flows ??= new List<FlowState>();
        shown ??= new List<ShownState>();
        suppressed ??= new List<string>();
        lastSessionDayUtc ??= string.Empty;
        return this;
    }
}
