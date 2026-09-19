using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Guidance;
using Share7.Infrastructure.Guidance;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class GuidanceStateServiceTests
{
    private readonly SqlServerFixture _fixture;

    public GuidanceStateServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task GetStateAsync_returns_empty_snapshot_and_generation_1_when_no_state_exists()
    {
        await using var context = CreateContext();
        var service = new GuidanceStateService(context);
        var userId = Guid.NewGuid();

        var result = await service.GetStateAsync(userId);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Generation);
        Assert.Empty(result.Value.Snapshot.flows);
        Assert.Empty(result.Value.Snapshot.shown);
    }

    [Fact]
    public async Task PushStateAsync_creates_new_row_on_first_push()
    {
        await using var context = CreateContext();
        var service = new GuidanceStateService(context);
        var userId = await TestData.CreateUserAsync(context);

        var snapshot = new GuidanceStateSnapshot
        {
            sessionOrdinal = 3,
            lastSessionDayUtc = "2026-09-19"
        };
        snapshot.flows.Add(new GuidanceStateSnapshot.FlowState
        {
            id = "onboarding.home",
            furthestStep = 2,
            finished = true,
            completedVersion = 1,
            completion = 0
        });

        var result = await service.PushStateAsync(userId, snapshot);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Generation);
        Assert.Equal(3, result.Value.Snapshot.sessionOrdinal);
        Assert.Single(result.Value.Snapshot.flows);

        var inDb = await context.UserGuidanceStates.FirstOrDefaultAsync(s => s.UserId == userId);
        Assert.NotNull(inDb);
        Assert.True(inDb.CompletedOnboarding);
        Assert.Equal(1, inDb.TotalFlowsCompleted);
    }

    [Fact]
    public async Task PushStateAsync_merges_monotonically_with_existing_state()
    {
        await using var context = CreateContext();
        var service = new GuidanceStateService(context);
        var userId = await TestData.CreateUserAsync(context);

        // Push 1 from Device A
        var snapA = new GuidanceStateSnapshot { sessionOrdinal = 2 };
        snapA.flows.Add(new GuidanceStateSnapshot.FlowState
        {
            id = "flow.1",
            furthestStep = 3,
            finished = true,
            completion = 0
        });
        await service.PushStateAsync(userId, snapA);

        // Push 2 from Device B with a different flow and higher session
        var snapB = new GuidanceStateSnapshot { sessionOrdinal = 4 };
        snapB.flows.Add(new GuidanceStateSnapshot.FlowState
        {
            id = "flow.2",
            furthestStep = 1,
            finished = false,
            completion = 0
        });
        var result = await service.PushStateAsync(userId, snapB);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Value!.Snapshot.sessionOrdinal);
        Assert.Equal(2, result.Value.Snapshot.flows.Count);

        var flow1 = result.Value.Snapshot.flows.Find(f => f.id == "flow.1");
        var flow2 = result.Value.Snapshot.flows.Find(f => f.id == "flow.2");
        Assert.NotNull(flow1);
        Assert.True(flow1.finished);
        Assert.NotNull(flow2);
        Assert.False(flow2.finished);
    }
}
