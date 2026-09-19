using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Guidance.Models;
using Share7.Domain.Guidance;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Guidance;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class GuidanceAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public GuidanceAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task CreateFlowAsync_creates_flow_and_initial_draft_with_audit()
    {
        await using var context = CreateContext();
        var service = new GuidanceAdminService(context);
        var adminId = await TestData.CreateUserAsync(context);

        var key = $"tour.test.{Guid.NewGuid():N}"[..24];
        var request = new CreateGuidanceFlowRequest
        {
            Key = key,
            Title = "Test Flow",
            Description = "A test onboarding flow",
            Kind = "Tour",
            Priority = 3,
            ReplayPolicy = 0,
            InitialStepsJson = "[{\"stepId\":\"step.1\"}]"
        };

        var result = await service.CreateFlowAsync(request, adminId, "admin@share7.com");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(key, result.Value.Key);
        Assert.Equal(0, result.Value.ActiveVersionNumber);
        Assert.NotNull(result.Value.DraftVersion);
        Assert.Equal(1, result.Value.DraftVersion.VersionNumber);
        Assert.Equal("Draft", result.Value.DraftVersion.Status);

        var inDb = await context.GuidanceFlows
            .Include(f => f.Versions)
            .Include(f => f.AuditLogs)
            .FirstOrDefaultAsync(f => f.Id == result.Value.Id);

        Assert.NotNull(inDb);
        Assert.Single(inDb.Versions);
        Assert.Single(inDb.AuditLogs);
        Assert.Equal("Created", inDb.AuditLogs.First().Action);
    }

    [Fact]
    public async Task PublishVersionAsync_archives_old_version_and_sets_active()
    {
        await using var context = CreateContext();
        var service = new GuidanceAdminService(context);
        var catalogService = new GuidanceCatalogService(context);
        var adminId = await TestData.CreateUserAsync(context);

        var key = $"flow.publish.{Guid.NewGuid():N}"[..24];
        var createResult = await service.CreateFlowAsync(new CreateGuidanceFlowRequest
        {
            Key = key,
            Title = "Publishable Flow",
            InitialStepsJson = "[{\"stepId\":\"step.1\"}]"
        }, adminId, "admin@share7.com");

        var flowId = createResult.Value!.Id;

        // Publish Version 1
        var publishResult = await service.PublishVersionAsync(flowId, new PublishGuidanceFlowRequest
        {
            ChangeSummary = "First release"
        }, adminId, "admin@share7.com");

        Assert.True(publishResult.Succeeded);
        Assert.Equal(1, publishResult.Value!.ActiveVersionNumber);
        Assert.Null(publishResult.Value.DraftVersion);
        Assert.NotNull(publishResult.Value.ActiveVersion);
        Assert.Equal("Published", publishResult.Value.ActiveVersion.Status);

        // Catalog should now return it
        var catalog = await catalogService.GetPublishedCatalogAsync();
        Assert.True(catalog.Succeeded);
        var inCatalog = catalog.Value!.Flows.FirstOrDefault(f => f.Id == key);
        Assert.NotNull(inCatalog);
        Assert.Equal(1, inCatalog.Version);

        // Update draft (creates Version 2 Draft)
        await service.UpdateDraftAsync(flowId, new UpdateGuidanceFlowDraftRequest
        {
            StepsJson = "[{\"stepId\":\"step.1\"},{\"stepId\":\"step.2\"}]",
            ChangeSummary = "Added step 2"
        }, adminId, "admin@share7.com");

        var checkDraft = await service.GetFlowAsync(flowId);
        Assert.NotNull(checkDraft.Value!.DraftVersion);
        Assert.Equal(2, checkDraft.Value.DraftVersion.VersionNumber);
        Assert.Equal("Draft", checkDraft.Value.DraftVersion.Status);

        // Publish Version 2
        var publishV2 = await service.PublishVersionAsync(flowId, new PublishGuidanceFlowRequest
        {
            ChangeSummary = "Release v2"
        }, adminId, "admin@share7.com");

        Assert.True(publishV2.Succeeded);
        Assert.Equal(2, publishV2.Value!.ActiveVersionNumber);

        var versions = publishV2.Value.Versions;
        Assert.Equal(2, versions.Count);
        var v1 = versions.First(v => v.VersionNumber == 1);
        var v2 = versions.First(v => v.VersionNumber == 2);
        Assert.Equal("Archived", v1.Status);
        Assert.Equal("Published", v2.Status);
    }

    [Fact]
    public async Task ToggleKillSwitch_removes_flow_from_public_catalog()
    {
        await using var context = CreateContext();
        var service = new GuidanceAdminService(context);
        var catalogService = new GuidanceCatalogService(context);
        var adminId = await TestData.CreateUserAsync(context);

        var key = $"flow.kill.{Guid.NewGuid():N}"[..24];
        var flow = await service.CreateFlowAsync(new CreateGuidanceFlowRequest
        {
            Key = key,
            Title = "Kill-switchable Flow",
            InitialStepsJson = "[{\"stepId\":\"s1\"}]"
        }, adminId, "admin@share7.com");

        await service.PublishVersionAsync(flow.Value!.Id, new PublishGuidanceFlowRequest(), adminId, "admin@share7.com");

        // Available in catalog
        var catBefore = await catalogService.GetPublishedCatalogAsync();
        Assert.Contains(catBefore.Value!.Flows, f => f.Id == key);

        // Engage kill switch
        await service.ToggleKillSwitchAsync(flow.Value.Id, isKillSwitched: true, "Emergency bug found", adminId, "admin@share7.com");

        // Immediately absent from catalog
        var catAfter = await catalogService.GetPublishedCatalogAsync();
        Assert.DoesNotContain(catAfter.Value!.Flows, f => f.Id == key);

        // Disengage kill switch
        await service.ToggleKillSwitchAsync(flow.Value.Id, isKillSwitched: false, "Bug fixed", adminId, "admin@share7.com");
        var catRestored = await catalogService.GetPublishedCatalogAsync();
        Assert.Contains(catRestored.Value!.Flows, f => f.Id == key);
    }

    [Fact]
    public async Task ResetUserGuidanceAsync_increments_generation_and_clears_progress()
    {
        await using var context = CreateContext();
        var adminService = new GuidanceAdminService(context);
        var stateService = new GuidanceStateService(context);
        var adminId = await TestData.CreateUserAsync(context);
        var targetUser = await TestData.CreateUserAsync(context);

        // Push some progress
        var snapshot = new GuidanceStateSnapshot { sessionOrdinal = 5 };
        snapshot.flows.Add(new GuidanceStateSnapshot.FlowState { id = "onboarding.home", finished = true });
        await stateService.PushStateAsync(targetUser, snapshot);

        var stateBefore = await stateService.GetStateAsync(targetUser);
        Assert.Equal(1, stateBefore.Value!.Generation);
        Assert.Single(stateBefore.Value.Snapshot.flows);

        // Admin resets guidance
        var resetResult = await adminService.ResetUserGuidanceAsync(targetUser, "User requested redo", adminId, "admin@share7.com");
        Assert.True(resetResult.Succeeded);

        // State is wiped and generation is incremented to 2
        var stateAfter = await stateService.GetStateAsync(targetUser);
        Assert.Equal(2, stateAfter.Value!.Generation);
        Assert.Empty(stateAfter.Value.Snapshot.flows);
    }

    [Fact]
    public async Task GetFlowFunnelAsync_computes_conversion_and_step_dropoffs()
    {
        await using var context = CreateContext();
        var service = new GuidanceAdminService(context);
        var adminId = await TestData.CreateUserAsync(context);
        var user1 = await TestData.CreateUserAsync(context);
        var user2 = await TestData.CreateUserAsync(context);

        var key = $"funnel.{Guid.NewGuid():N}"[..24];
        var stepsJson = "[{\"stepId\":\"step_welcome\",\"beat\":{\"anchor\":\"anchor_welcome\"}},{\"stepId\":\"step_task\",\"beat\":{\"anchor\":\"anchor_btn\"}}]";

        var flowResult = await service.CreateFlowAsync(new CreateGuidanceFlowRequest
        {
            Key = key,
            Title = "Funnel Flow",
            InitialStepsJson = stepsJson
        }, adminId, "admin@share7.com");

        var flowId = flowResult.Value!.Id;
        await service.PublishVersionAsync(flowId, new PublishGuidanceFlowRequest { ChangeSummary = "v1" }, adminId, "admin@share7.com");

        var now = DateTime.UtcNow;
        var today = now.Date;
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        // User 1 starts, finishes step 0, finishes step 1, completes flow
        context.TelemetryEvents.AddRange(
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                SessionId = session1,
                Name = TelemetryNames.GuidanceFlowStart,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-10),
                ReceivedAtUtc = now.AddMinutes(-10),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"flow_version\":1,\"session_ordinal\":1}}"
            },
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                SessionId = session1,
                Name = TelemetryNames.GuidanceStep,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-9),
                ReceivedAtUtc = now.AddMinutes(-9),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"step_id\":\"step_welcome\",\"step_index\":0,\"duration_ms\":1500}}"
            },
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                SessionId = session1,
                Name = TelemetryNames.GuidanceStep,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-8),
                ReceivedAtUtc = now.AddMinutes(-8),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"step_id\":\"step_task\",\"step_index\":1,\"duration_ms\":2500}}"
            },
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                SessionId = session1,
                Name = TelemetryNames.GuidanceFlowEnd,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-7),
                ReceivedAtUtc = now.AddMinutes(-7),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"outcome\":\"completed\",\"last_step_index\":1}}"
            }
        );

        // User 2 starts, finishes step 0, then abandons
        context.TelemetryEvents.AddRange(
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user2,
                SessionId = session2,
                Name = TelemetryNames.GuidanceFlowStart,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-5),
                ReceivedAtUtc = now.AddMinutes(-5),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"flow_version\":1,\"session_ordinal\":1}}"
            },
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user2,
                SessionId = session2,
                Name = TelemetryNames.GuidanceStep,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-4),
                ReceivedAtUtc = now.AddMinutes(-4),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"step_id\":\"step_welcome\",\"step_index\":0,\"duration_ms\":1800}}"
            },
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = user2,
                SessionId = session2,
                Name = TelemetryNames.GuidanceFlowEnd,
                Category = TelemetryCategory.Behavioural,
                OccurredAtUtc = now.AddMinutes(-3),
                ReceivedAtUtc = now.AddMinutes(-3),
                DayUtc = today,
                ParamsJson = $"{{\"flow_id\":\"{key}\",\"outcome\":\"abandoned\",\"last_step_index\":0}}"
            }
        );

        await context.SaveChangesAsync();

        var funnelResult = await service.GetFlowFunnelAsync(flowId, version: 1);
        Assert.True(funnelResult.Succeeded);
        var funnel = funnelResult.Value!;

        Assert.Equal(key, funnel.FlowKey);
        Assert.Equal(2, funnel.TotalStarted);
        Assert.Equal(1, funnel.TotalCompleted);
        Assert.Equal(1, funnel.TotalAbandoned);
        Assert.Equal(0.5, funnel.CompletionRate);

        Assert.Equal(2, funnel.Steps.Count);

        // Step 0
        var step0 = funnel.Steps[0];
        Assert.Equal(0, step0.StepIndex);
        Assert.Equal("step_welcome", step0.StepId);
        Assert.Equal("anchor_welcome", step0.Anchor);
        Assert.Equal(2, step0.ReachedCount);
        Assert.Equal(1.0, step0.ConversionFromStart);
        Assert.Equal(1650.0, step0.AverageDurationMs);

        // Step 1
        var step1 = funnel.Steps[1];
        Assert.Equal(1, step1.StepIndex);
        Assert.Equal("step_task", step1.StepId);
        Assert.Equal("anchor_btn", step1.Anchor);
        Assert.Equal(1, step1.ReachedCount);
        Assert.Equal(0.5, step1.ConversionFromStart);
        Assert.Equal(0.5, step1.ConversionFromPrevious);
        Assert.Equal(1, step1.DropOffCount);
        Assert.Equal(0.5, step1.DropOffRate);
        Assert.Equal(2500.0, step1.AverageDurationMs);
    }

    [Fact]
    public async Task GetMissingAnchorsAsync_aggregates_diagnostics()
    {
        await using var context = CreateContext();
        var service = new GuidanceAdminService(context);
        var adminId = await TestData.CreateUserAsync(context);

        var key = $"missing.{Guid.NewGuid():N}"[..24];
        var anchor = "btn_missing_test";
        var now = DateTime.UtcNow;

        context.TelemetryEvents.AddRange(
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                SessionId = Guid.NewGuid(),
                Name = TelemetryNames.GuidanceAnchorMissing,
                Category = TelemetryCategory.Operational,
                Platform = "android",
                AppVersion = "1.0.0",
                OccurredAtUtc = now.AddMinutes(-10),
                ReceivedAtUtc = now.AddMinutes(-10),
                DayUtc = now.Date,
                ParamsJson = $"{{\"anchor_id\":\"{anchor}\",\"flow_id\":\"{key}\",\"step_index\":2}}"
            },
            new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                SessionId = Guid.NewGuid(),
                Name = TelemetryNames.GuidanceAnchorMissing,
                Category = TelemetryCategory.Operational,
                Platform = "ios",
                AppVersion = "1.0.1",
                OccurredAtUtc = now.AddMinutes(-5),
                ReceivedAtUtc = now.AddMinutes(-5),
                DayUtc = now.Date,
                ParamsJson = $"{{\"anchor_id\":\"{anchor}\",\"flow_id\":\"{key}\",\"step_index\":2}}"
            }
        );

        await context.SaveChangesAsync();

        var missingResult = await service.GetMissingAnchorsAsync(flowKey: key);
        Assert.True(missingResult.Succeeded);
        var list = missingResult.Value!;

        Assert.Single(list);
        var item = list[0];
        Assert.Equal(anchor, item.AnchorId);
        Assert.Equal(key, item.FlowKey);
        Assert.Equal(2, item.StepIndex);
        Assert.Equal(2, item.OccurrenceCount);
        Assert.Contains("android", item.Platforms);
        Assert.Contains("ios", item.Platforms);
        Assert.Contains("1.0.0", item.AppVersions);
        Assert.Contains("1.0.1", item.AppVersions);
    }

    [Fact]
    public async Task GetFlowsSummaryStatsAsync_returns_high_level_metrics()
    {
        await using var context = CreateContext();
        var service = new GuidanceAdminService(context);
        var adminId = await TestData.CreateUserAsync(context);

        var key = $"summary.{Guid.NewGuid():N}"[..24];
        var flowResult = await service.CreateFlowAsync(new CreateGuidanceFlowRequest
        {
            Key = key,
            Title = "Summary Flow"
        }, adminId, "admin@share7.com");

        var summaryResult = await service.GetFlowsSummaryStatsAsync();
        Assert.True(summaryResult.Succeeded);
        var flowStats = summaryResult.Value!.FirstOrDefault(f => f.FlowKey == key);

        Assert.NotNull(flowStats);
        Assert.Equal(0, flowStats.TotalStarted);
        Assert.Equal(0, flowStats.TotalCompleted);
        Assert.Equal(0, flowStats.CompletionRate);
        Assert.Equal(0, flowStats.MissingAnchorCount);
    }
}
