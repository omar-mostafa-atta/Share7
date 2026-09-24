using System;
using System.Collections.Generic;

namespace Share7.Domain.Guidance;

/// <summary>
/// Combines two guidance state snapshots of the same account.
/// Ported from Unity's Game.Core.Guidance.GuidanceStateMerge.
/// The merge is commutative, associative, and idempotent (CRDT grow-only union).
/// </summary>
public static class GuidanceStateMerge
{
    public static GuidanceStateSnapshot Combine(GuidanceStateSnapshot? a, GuidanceStateSnapshot? b)
    {
        if (a == null && b == null) return new GuidanceStateSnapshot();
        if (a == null) return Copy(b!);
        if (b == null) return Copy(a);

        a = a.Normalized();
        b = b.Normalized();

        var merged = new GuidanceStateSnapshot
        {
            schemaVersion = Math.Max(a.schemaVersion, b.schemaVersion),
            sessionOrdinal = Math.Max(a.sessionOrdinal, b.sessionOrdinal),
            lastSessionDayUtc = LaterDay(a.lastSessionDayUtc, b.lastSessionDayUtc),
            revision = Math.Max(a.revision, b.revision)
        };

        MergeFlows(merged, a.flows);
        MergeFlows(merged, b.flows);

        MergeShown(merged, a.shown);
        MergeShown(merged, b.shown);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddSuppressed(merged, a.suppressed, seen);
        AddSuppressed(merged, b.suppressed, seen);

        return merged;
    }

    private static void MergeFlows(
        GuidanceStateSnapshot into, List<GuidanceStateSnapshot.FlowState>? flows)
    {
        if (flows == null) return;

        foreach (var incoming in flows)
        {
            if (incoming == null || string.IsNullOrEmpty(incoming.id)) continue;

            var existing = FindFlow(into, incoming.id);

            if (existing == null)
            {
                into.flows.Add(new GuidanceStateSnapshot.FlowState
                {
                    id = incoming.id,
                    furthestStep = incoming.furthestStep,
                    completedVersion = incoming.completedVersion,
                    completion = incoming.finished ? incoming.completion : 0,
                    finished = incoming.finished
                });
                continue;
            }

            existing.furthestStep = Math.Max(existing.furthestStep, incoming.furthestStep);
            existing.completedVersion = Math.Max(existing.completedVersion, incoming.completedVersion);
            existing.completion = ResolveCompletion(existing, incoming);
            existing.finished = existing.finished || incoming.finished;
        }
    }

    private static int ResolveCompletion(
        GuidanceStateSnapshot.FlowState a, GuidanceStateSnapshot.FlowState b)
    {
        if (a.finished && b.finished) return BetterCompletion(a.completion, b.completion);
        if (a.finished) return a.completion;
        if (b.finished) return b.completion;

        return 0;
    }

    private static int BetterCompletion(int left, int right) =>
        Rank(left) <= Rank(right) ? left : right;

    private static int Rank(int completion) => completion switch
    {
        0 => 0, // Completed
        1 => 1, // Skipped
        2 => 2, // Abandoned
        _ => 3  // Ineligible / Other
    };

    private static void MergeShown(
        GuidanceStateSnapshot into, List<GuidanceStateSnapshot.ShownState>? shown)
    {
        if (shown == null) return;

        foreach (var incoming in shown)
        {
            if (incoming == null || string.IsNullOrEmpty(incoming.id)) continue;

            var existing = FindShown(into, incoming.id);

            if (existing == null)
            {
                into.shown.Add(new GuidanceStateSnapshot.ShownState
                {
                    id = incoming.id,
                    count = incoming.count,
                    sessionOrdinal = incoming.sessionOrdinal,
                    hasServerTime = incoming.hasServerTime,
                    serverTicksUtc = incoming.serverTicksUtc,
                    dayKey = incoming.dayKey,
                    dayCount = incoming.dayCount
                });
                continue;
            }

            existing.count = Math.Max(existing.count, incoming.count);
            existing.sessionOrdinal = Math.Max(existing.sessionOrdinal, incoming.sessionOrdinal);

            if (incoming.hasServerTime
                && (!existing.hasServerTime || incoming.serverTicksUtc > existing.serverTicksUtc))
            {
                existing.hasServerTime = true;
                existing.serverTicksUtc = incoming.serverTicksUtc;
            }

            MergeDayCount(existing, incoming);
        }
    }

    private static void MergeDayCount(
        GuidanceStateSnapshot.ShownState existing, GuidanceStateSnapshot.ShownState incoming)
    {
        if (string.Equals(existing.dayKey, incoming.dayKey, StringComparison.Ordinal))
        {
            existing.dayCount = Math.Max(existing.dayCount, incoming.dayCount);
            return;
        }

        bool existingDated = IsDate(existing.dayKey);
        bool incomingDated = IsDate(incoming.dayKey);

        if (existingDated && !incomingDated) return;

        if (!existingDated && incomingDated)
        {
            existing.dayKey = incoming.dayKey;
            existing.dayCount = incoming.dayCount;
            return;
        }

        if (string.CompareOrdinal(incoming.dayKey ?? string.Empty, existing.dayKey ?? string.Empty) <= 0)
            return;

        existing.dayKey = incoming.dayKey;
        existing.dayCount = incoming.dayCount;
    }

    private static void AddSuppressed(
        GuidanceStateSnapshot into, List<string>? suppressed, HashSet<string> seen)
    {
        if (suppressed == null) return;

        foreach (string id in suppressed)
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            into.suppressed.Add(id);
        }
    }

    private static string LaterDay(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b ?? string.Empty;
        if (string.IsNullOrEmpty(b)) return a;
        return string.CompareOrdinal(a, b) >= 0 ? a : b;
    }

    private static bool IsDate(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length == 10 && key[4] == '-' && key[7] == '-';

    private static GuidanceStateSnapshot.FlowState? FindFlow(GuidanceStateSnapshot state, string id)
    {
        for (int i = 0; i < state.flows.Count; i++)
            if (string.Equals(state.flows[i].id, id, StringComparison.Ordinal))
                return state.flows[i];
        return null;
    }

    private static GuidanceStateSnapshot.ShownState? FindShown(GuidanceStateSnapshot state, string id)
    {
        for (int i = 0; i < state.shown.Count; i++)
            if (string.Equals(state.shown[i].id, id, StringComparison.Ordinal))
                return state.shown[i];
        return null;
    }

    private static GuidanceStateSnapshot Copy(GuidanceStateSnapshot source) =>
        Combine(new GuidanceStateSnapshot(), source.Normalized());
}
