using System;
using System.Collections.Generic;
using Share7.Domain.Guidance;
using Xunit;

namespace Share7.Tests;

public class GuidanceStateMergeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Merge_is_commutative(int seed)
    {
        var a = GenerateRandom(seed);
        var b = GenerateRandom(seed + 100);

        AssertSame(GuidanceStateMerge.Combine(a, b), GuidanceStateMerge.Combine(b, a));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Merge_is_associative(int seed)
    {
        var a = GenerateRandom(seed);
        var b = GenerateRandom(seed + 100);
        var c = GenerateRandom(seed + 200);

        AssertSame(
            GuidanceStateMerge.Combine(GuidanceStateMerge.Combine(a, b), c),
            GuidanceStateMerge.Combine(a, GuidanceStateMerge.Combine(b, c)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Merge_is_idempotent(int seed)
    {
        var a = GenerateRandom(seed);
        var once = GuidanceStateMerge.Combine(a, a);

        AssertSame(once, GuidanceStateMerge.Combine(once, a));
    }

    [Fact]
    public void An_unfinished_flow_has_no_ending_whichever_way_it_is_merged()
    {
        var a = UnfinishedFlow("flow.x", completion: 3);
        var b = UnfinishedFlow("flow.x", completion: 0);

        var merged1 = GuidanceStateMerge.Combine(a, b);
        var merged2 = GuidanceStateMerge.Combine(b, a);

        Assert.Equal(Flow(merged1, "flow.x")!.completion, Flow(merged2, "flow.x")!.completion);
    }

    [Fact]
    public void A_real_ending_outranks_an_unfinished_record()
    {
        var finished = new GuidanceStateSnapshot();
        finished.flows.Add(new GuidanceStateSnapshot.FlowState
        {
            id = "flow.x",
            finished = true,
            completion = 1 // Skipped
        });

        var unfinished = UnfinishedFlow("flow.x", completion: 0); // Completed default

        var merged1 = GuidanceStateMerge.Combine(finished, unfinished);
        var merged2 = GuidanceStateMerge.Combine(unfinished, finished);

        Assert.True(Flow(merged1, "flow.x")!.finished);
        Assert.Equal(1, Flow(merged1, "flow.x")!.completion);

        Assert.True(Flow(merged2, "flow.x")!.finished);
        Assert.Equal(1, Flow(merged2, "flow.x")!.completion);
    }

    [Fact]
    public void Merging_stale_state_never_loses_progress()
    {
        var fresh = GenerateRandom(7);
        var stale = GenerateRandom(7);

        stale.flows[0].finished = false;
        stale.flows[0].furthestStep = 0;
        stale.shown[0].count = 0;
        stale.suppressed.Clear();

        fresh.flows[0].finished = true;
        fresh.flows[0].furthestStep = 9;
        fresh.shown[0].count = 5;

        var merged = GuidanceStateMerge.Combine(stale, fresh);

        Assert.True(Flow(merged, fresh.flows[0].id)!.finished);
        Assert.Equal(9, Flow(merged, fresh.flows[0].id)!.furthestStep);
        Assert.Equal(5, Shown(merged, fresh.shown[0].id)!.count);
        Assert.NotEmpty(merged.suppressed);
    }

    private static GuidanceStateSnapshot GenerateRandom(int seed)
    {
        var random = new Random(seed);
        var state = new GuidanceStateSnapshot
        {
            schemaVersion = random.Next(1, 4),
            sessionOrdinal = random.Next(1, 40),
            lastSessionDayUtc = $"2026-09-{random.Next(1, 28):00}",
            revision = random.Next(0, 500)
        };

        for (int i = 0; i < 6; i++)
        {
            if (random.Next(3) == 0) continue;

            state.flows.Add(new GuidanceStateSnapshot.FlowState
            {
                id = "flow." + i,
                furthestStep = random.Next(0, 10),
                completedVersion = random.Next(0, 4),
                completion = random.Next(0, 4),
                finished = random.Next(2) == 0
            });
        }

        for (int i = 0; i < 6; i++)
        {
            if (random.Next(3) == 0) continue;

            bool hasServer = random.Next(2) == 0;

            state.shown.Add(new GuidanceStateSnapshot.ShownState
            {
                id = "tip." + i,
                count = random.Next(0, 12),
                sessionOrdinal = random.Next(0, 40),
                hasServerTime = hasServer,
                serverTicksUtc = hasServer ? random.Next(1, 1_000_000) : 0L,
                dayKey = random.Next(4) == 0
                    ? "session:" + random.Next(1, 9)
                    : $"2026-09-{random.Next(1, 28):00}",
                dayCount = random.Next(0, 5)
            });
        }

        for (int i = 0; i < 4; i++)
            if (random.Next(2) == 0)
                state.suppressed.Add("tip." + i);

        return state;
    }

    private static void AssertSame(GuidanceStateSnapshot left, GuidanceStateSnapshot right)
    {
        Assert.Equal(left.sessionOrdinal, right.sessionOrdinal);
        Assert.Equal(left.lastSessionDayUtc, right.lastSessionDayUtc);
        Assert.Equal(left.flows.Count, right.flows.Count);
        Assert.Equal(left.shown.Count, right.shown.Count);

        var leftSuppressed = new HashSet<string>(left.suppressed);
        var rightSuppressed = new HashSet<string>(right.suppressed);
        Assert.True(leftSuppressed.SetEquals(rightSuppressed));

        foreach (var flow in left.flows)
        {
            var other = Flow(right, flow.id);
            Assert.NotNull(other);
            Assert.Equal(flow.finished, other.finished);
            Assert.Equal(flow.furthestStep, other.furthestStep);
            Assert.Equal(flow.completedVersion, other.completedVersion);
            Assert.Equal(flow.completion, other.completion);
        }

        foreach (var shown in left.shown)
        {
            var other = Shown(right, shown.id);
            Assert.NotNull(other);
            Assert.Equal(shown.count, other.count);
            Assert.Equal(shown.sessionOrdinal, other.sessionOrdinal);
            Assert.Equal(shown.hasServerTime, other.hasServerTime);
            Assert.Equal(shown.serverTicksUtc, other.serverTicksUtc);
            Assert.Equal(shown.dayKey, other.dayKey);
            Assert.Equal(shown.dayCount, other.dayCount);
        }
    }

    private static GuidanceStateSnapshot UnfinishedFlow(string id, int completion)
    {
        var state = new GuidanceStateSnapshot();
        state.flows.Add(new GuidanceStateSnapshot.FlowState
        {
            id = id,
            finished = false,
            furthestStep = 2,
            completion = completion
        });
        return state;
    }

    private static GuidanceStateSnapshot.FlowState? Flow(GuidanceStateSnapshot state, string id) =>
        state.flows.Find(f => string.Equals(f.id, id, StringComparison.Ordinal));

    private static GuidanceStateSnapshot.ShownState? Shown(GuidanceStateSnapshot state, string id) =>
        state.shown.Find(s => string.Equals(s.id, id, StringComparison.Ordinal));
}
