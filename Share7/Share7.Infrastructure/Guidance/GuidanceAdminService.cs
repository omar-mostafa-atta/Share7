using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Guidance.Interfaces;
using Share7.Application.Guidance.Models;
using Share7.Domain.Guidance;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Guidance;

public sealed class GuidanceAdminService : IGuidanceAdminService
{
    private readonly ApplicationDbContext _context;

    public GuidanceAdminService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ServiceResult<List<GuidanceFlowAdminDto>>> ListFlowsAsync(
        bool includeKillSwitched = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.GuidanceFlows
            .Include(f => f.Versions)
            .AsNoTracking();

        if (!includeKillSwitched)
            query = query.Where(f => !f.IsKillSwitched);

        var flows = await query
            .OrderBy(f => f.Priority)
            .ThenBy(f => f.Key)
            .ToListAsync(cancellationToken);

        var dtos = flows.Select(MapToAdminDto).ToList();
        return ServiceResult<List<GuidanceFlowAdminDto>>.Success(dtos);
    }

    public async Task<ServiceResult<GuidanceFlowAdminDto>> GetFlowAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var flow = await _context.GuidanceFlows
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (flow == null)
            return ServiceResult<GuidanceFlowAdminDto>.NotFound($"Guidance flow '{id}' was not found.");

        return ServiceResult<GuidanceFlowAdminDto>.Success(MapToAdminDto(flow));
    }

    public async Task<ServiceResult<GuidanceFlowAdminDto>> CreateFlowAsync(
        CreateGuidanceFlowRequest request,
        Guid userId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return ServiceResult<GuidanceFlowAdminDto>.Invalid("Flow key is required.");

        var normalizedKey = request.Key.Trim();

        var exists = await _context.GuidanceFlows
            .AnyAsync(f => f.Key == normalizedKey, cancellationToken);

        if (exists)
            return ServiceResult<GuidanceFlowAdminDto>.Conflict($"Flow with key '{normalizedKey}' already exists.");

        var flow = new GuidanceFlow
        {
            Key = normalizedKey,
            Title = string.IsNullOrWhiteSpace(request.Title) ? normalizedKey : request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Kind = string.IsNullOrWhiteSpace(request.Kind) ? "Tour" : request.Kind.Trim(),
            Priority = request.Priority,
            ReplayPolicy = request.ReplayPolicy,
            Skippable = request.Skippable,
            SkipAfterStep = request.SkipAfterStep,
            Resumable = request.Resumable,
            TargetAudienceJson = request.TargetAudienceJson,
            ActiveVersionNumber = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var initialDraft = new GuidanceFlowVersion
        {
            FlowId = flow.Id,
            VersionNumber = 1,
            Status = "Draft",
            StepsJson = string.IsNullOrWhiteSpace(request.InitialStepsJson) ? "[]" : request.InitialStepsJson,
            TriggerConditionsJson = request.InitialTriggerConditionsJson,
            ChangeSummary = "Initial draft",
            CreatedAtUtc = DateTime.UtcNow
        };

        flow.Versions.Add(initialDraft);

        var audit = new GuidanceAuditLog
        {
            FlowId = flow.Id,
            Action = "Created",
            UserId = userId,
            UserEmail = userEmail,
            TimestampUtc = DateTime.UtcNow,
            DetailsJson = $"Created flow '{flow.Key}' with initial draft."
        };

        _context.GuidanceFlows.Add(flow);
        _context.GuidanceAuditLogs.Add(audit);
        await _context.SaveChangesAsync(cancellationToken);

        return ServiceResult<GuidanceFlowAdminDto>.Success(MapToAdminDto(flow));
    }

    public async Task<ServiceResult<GuidanceFlowAdminDto>> UpdateDraftAsync(
        Guid id,
        UpdateGuidanceFlowDraftRequest request,
        Guid userId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var flow = await _context.GuidanceFlows
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (flow == null)
            return ServiceResult<GuidanceFlowAdminDto>.NotFound($"Guidance flow '{id}' was not found.");

        if (!string.IsNullOrWhiteSpace(request.Title)) flow.Title = request.Title.Trim();
        if (request.Description != null) flow.Description = request.Description.Trim();
        if (!string.IsNullOrWhiteSpace(request.Kind)) flow.Kind = request.Kind.Trim();
        if (request.Priority.HasValue) flow.Priority = request.Priority.Value;
        if (request.ReplayPolicy.HasValue) flow.ReplayPolicy = request.ReplayPolicy.Value;
        if (request.Skippable.HasValue) flow.Skippable = request.Skippable.Value;
        if (request.SkipAfterStep.HasValue) flow.SkipAfterStep = request.SkipAfterStep.Value;
        if (request.Resumable.HasValue) flow.Resumable = request.Resumable.Value;
        if (request.TargetAudienceJson != null) flow.TargetAudienceJson = request.TargetAudienceJson;

        flow.UpdatedAtUtc = DateTime.UtcNow;

        var draft = flow.Versions.FirstOrDefault(v => v.Status == "Draft");
        if (draft == null)
        {
            int nextVersion = flow.Versions.Count > 0 ? flow.Versions.Max(v => v.VersionNumber) + 1 : 1;
            draft = new GuidanceFlowVersion
            {
                FlowId = flow.Id,
                VersionNumber = nextVersion,
                Status = "Draft",
                StepsJson = request.StepsJson,
                TriggerConditionsJson = request.TriggerConditionsJson,
                ChangeSummary = request.ChangeSummary,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.GuidanceFlowVersions.Add(draft);
            if (!flow.Versions.Contains(draft))
                flow.Versions.Add(draft);
        }
        else
        {
            draft.StepsJson = request.StepsJson;
            draft.TriggerConditionsJson = request.TriggerConditionsJson;
            if (!string.IsNullOrWhiteSpace(request.ChangeSummary))
                draft.ChangeSummary = request.ChangeSummary.Trim();
        }

        var audit = new GuidanceAuditLog
        {
            FlowId = flow.Id,
            Action = "DraftUpdated",
            UserId = userId,
            UserEmail = userEmail,
            TimestampUtc = DateTime.UtcNow,
            DetailsJson = request.ChangeSummary ?? "Updated draft steps or properties."
        };
        _context.GuidanceAuditLogs.Add(audit);

        await _context.SaveChangesAsync(cancellationToken);
        return ServiceResult<GuidanceFlowAdminDto>.Success(MapToAdminDto(flow));
    }

    public async Task<ServiceResult<GuidanceFlowAdminDto>> PublishVersionAsync(
        Guid id,
        PublishGuidanceFlowRequest request,
        Guid userId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var flow = await _context.GuidanceFlows
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (flow == null)
            return ServiceResult<GuidanceFlowAdminDto>.NotFound($"Guidance flow '{id}' was not found.");

        var draft = flow.Versions.FirstOrDefault(v => v.Status == "Draft");
        if (draft == null)
            return ServiceResult<GuidanceFlowAdminDto>.Invalid("No draft version exists to publish.");

        // Archive previous published versions
        foreach (var version in flow.Versions.Where(v => v.Status == "Published"))
        {
            version.Status = "Archived";
        }

        draft.Status = "Published";
        draft.PublishedAtUtc = DateTime.UtcNow;
        draft.PublishedByUserId = userId;
        if (!string.IsNullOrWhiteSpace(request.ChangeSummary))
            draft.ChangeSummary = request.ChangeSummary.Trim();

        flow.ActiveVersionNumber = draft.VersionNumber;
        flow.UpdatedAtUtc = DateTime.UtcNow;

        var audit = new GuidanceAuditLog
        {
            FlowId = flow.Id,
            Action = "Published",
            UserId = userId,
            UserEmail = userEmail,
            TimestampUtc = DateTime.UtcNow,
            DetailsJson = $"Published version {draft.VersionNumber}. Summary: {draft.ChangeSummary}"
        };
        _context.GuidanceAuditLogs.Add(audit);

        await _context.SaveChangesAsync(cancellationToken);
        return ServiceResult<GuidanceFlowAdminDto>.Success(MapToAdminDto(flow));
    }

    public async Task<ServiceResult<GuidanceFlowAdminDto>> ToggleKillSwitchAsync(
        Guid id,
        bool isKillSwitched,
        string reason,
        Guid userId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var flow = await _context.GuidanceFlows
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (flow == null)
            return ServiceResult<GuidanceFlowAdminDto>.NotFound($"Guidance flow '{id}' was not found.");

        flow.IsKillSwitched = isKillSwitched;
        flow.UpdatedAtUtc = DateTime.UtcNow;

        var audit = new GuidanceAuditLog
        {
            FlowId = flow.Id,
            Action = isKillSwitched ? "KillSwitchEnabled" : "KillSwitchDisabled",
            UserId = userId,
            UserEmail = userEmail,
            TimestampUtc = DateTime.UtcNow,
            DetailsJson = $"Kill switch {(isKillSwitched ? "activated" : "deactivated")}. Reason: {reason}"
        };
        _context.GuidanceAuditLogs.Add(audit);

        await _context.SaveChangesAsync(cancellationToken);
        return ServiceResult<GuidanceFlowAdminDto>.Success(MapToAdminDto(flow));
    }

    public async Task<ServiceResult<bool>> ResetUserGuidanceAsync(
        Guid targetUserId,
        string reason,
        Guid adminUserId,
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.UserGuidanceStates
            .FirstOrDefaultAsync(s => s.UserId == targetUserId, cancellationToken);

        int newGeneration;
        if (record == null)
        {
            newGeneration = 2;
            record = new UserGuidanceState
            {
                UserId = targetUserId,
                Generation = newGeneration,
                SchemaVersion = 1,
                SessionOrdinal = 0,
                LastSessionDayUtc = string.Empty,
                CompletedOnboarding = false,
                TotalFlowsCompleted = 0,
                StateJson = "{}",
                UpdatedAtUtc = DateTime.UtcNow
            };
            _context.UserGuidanceStates.Add(record);
        }
        else
        {
            newGeneration = record.Generation + 1;
            record.Generation = newGeneration;
            record.CompletedOnboarding = false;
            record.TotalFlowsCompleted = 0;
            record.SessionOrdinal = 0;
            record.LastSessionDayUtc = string.Empty;
            record.StateJson = "{}";
            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        var audit = new GuidanceAuditLog
        {
            FlowId = null,
            Action = "UserReset",
            UserId = adminUserId,
            UserEmail = adminEmail,
            TimestampUtc = DateTime.UtcNow,
            DetailsJson = $"Admin reset guidance state for user '{targetUserId}' to generation {newGeneration}. Reason: {reason}"
        };
        _context.GuidanceAuditLogs.Add(audit);

        await _context.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<List<GuidanceAuditLogDto>>> GetAuditLogsAsync(
        Guid? flowId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.GuidanceAuditLogs
            .Include(a => a.Flow)
            .AsNoTracking();

        if (flowId.HasValue)
            query = query.Where(a => a.FlowId == flowId.Value);

        var logs = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Take(100)
            .Select(a => new GuidanceAuditLogDto
            {
                Id = a.Id,
                FlowId = a.FlowId,
                FlowKey = a.Flow != null ? a.Flow.Key : null,
                Action = a.Action,
                UserId = a.UserId,
                UserEmail = a.UserEmail,
                TimestampUtc = a.TimestampUtc,
                DetailsJson = a.DetailsJson
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<List<GuidanceAuditLogDto>>.Success(logs);
    }

    private static GuidanceFlowAdminDto MapToAdminDto(GuidanceFlow flow)
    {
        var versions = flow.Versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new GuidanceFlowVersionAdminDto
            {
                Id = v.Id,
                FlowId = v.FlowId,
                VersionNumber = v.VersionNumber,
                Status = v.Status,
                StepsJson = v.StepsJson,
                TriggerConditionsJson = v.TriggerConditionsJson,
                ChangeSummary = v.ChangeSummary,
                CreatedAtUtc = v.CreatedAtUtc,
                PublishedAtUtc = v.PublishedAtUtc,
                PublishedByUserId = v.PublishedByUserId
            })
            .ToList();

        var activeVersion = versions.FirstOrDefault(v => v.Status == "Published" && v.VersionNumber == flow.ActiveVersionNumber);
        var draftVersion = versions.FirstOrDefault(v => v.Status == "Draft");

        return new GuidanceFlowAdminDto
        {
            Id = flow.Id,
            Key = flow.Key,
            Title = flow.Title,
            Description = flow.Description,
            Kind = flow.Kind,
            Priority = flow.Priority,
            ReplayPolicy = flow.ReplayPolicy,
            Skippable = flow.Skippable,
            SkipAfterStep = flow.SkipAfterStep,
            Resumable = flow.Resumable,
            IsKillSwitched = flow.IsKillSwitched,
            ActiveVersionNumber = flow.ActiveVersionNumber,
            TargetAudienceJson = flow.TargetAudienceJson,
            CreatedAtUtc = flow.CreatedAtUtc,
            UpdatedAtUtc = flow.UpdatedAtUtc,
            ActiveVersion = activeVersion,
            DraftVersion = draftVersion,
            Versions = versions
        };
    }

    public async Task<ServiceResult<GuidanceFlowFunnelDto>> GetFlowFunnelAsync(
        Guid flowId,
        int? version = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var flow = await _context.GuidanceFlows
            .Include(f => f.Versions)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == flowId, cancellationToken);

        if (flow == null)
            return ServiceResult<GuidanceFlowFunnelDto>.NotFound($"Guidance flow '{flowId}' was not found.");

        var toDay = (toUtc ?? DateTime.UtcNow).Date;
        var fromDay = (fromUtc ?? toDay.AddDays(-30)).Date;
        if (fromDay > toDay)
            (fromDay, toDay) = (toDay, fromDay);

        GuidanceFlowVersion? targetVersion = null;
        if (version.HasValue)
        {
            targetVersion = flow.Versions.FirstOrDefault(v => v.VersionNumber == version.Value);
        }
        targetVersion ??= flow.Versions.FirstOrDefault(v => v.Status == "Published" && v.VersionNumber == flow.ActiveVersionNumber)
                          ?? flow.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

        var authoredSteps = ParseAuthoredSteps(targetVersion?.StepsJson);
        var flowKey = flow.Key;

        var guidanceNames = new[]
        {
            TelemetryNames.GuidanceFlowStart,
            TelemetryNames.GuidanceFlowEnd,
            TelemetryNames.GuidanceStep
        };

        var events = await _context.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DayUtc >= fromDay && e.DayUtc <= toDay && guidanceNames.Contains(e.Name) && e.ParamsJson.Contains(flowKey))
            .Select(e => new { e.UserId, e.SessionId, e.Name, e.ParamsJson, e.OccurredAtUtc })
            .ToListAsync(cancellationToken);

        var startedUsers = new HashSet<Guid>();
        var completedUsers = new HashSet<Guid>();
        var abandonedUsers = new HashSet<Guid>();
        var skippedUsers = new HashSet<Guid>();
        var userStepReached = new Dictionary<int, HashSet<Guid>>();
        var stepDurations = new Dictionary<int, List<long>>();
        var observedStepIds = new Dictionary<int, string>();

        HashSet<Guid>? matchingSessions = null;
        HashSet<Guid>? matchingUsers = null;
        if (version.HasValue)
        {
            matchingSessions = new HashSet<Guid>();
            matchingUsers = new HashSet<Guid>();
            foreach (var ev in events)
            {
                try
                {
                    using var doc = JsonDocument.Parse(ev.ParamsJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("flow_id", out var fIdProp) && fIdProp.GetString() == flowKey &&
                        ev.Name == TelemetryNames.GuidanceFlowStart &&
                        root.TryGetProperty("flow_version", out var vProp) && vProp.GetInt32() == version.Value)
                    {
                        matchingSessions.Add(ev.SessionId);
                        matchingUsers.Add(ev.UserId);
                    }
                }
                catch (JsonException) { }
            }
        }

        foreach (var ev in events)
        {
            try
            {
                using var doc = JsonDocument.Parse(ev.ParamsJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("flow_id", out var fIdProp) || fIdProp.GetString() != flowKey)
                    continue;

                if (version.HasValue && matchingSessions != null && !matchingSessions.Contains(ev.SessionId) && (matchingUsers == null || !matchingUsers.Contains(ev.UserId)))
                    continue;

                if (ev.Name == TelemetryNames.GuidanceFlowStart)
                {
                    startedUsers.Add(ev.UserId);
                }
                else if (ev.Name == TelemetryNames.GuidanceFlowEnd)
                {
                    var outcome = root.TryGetProperty("outcome", out var outProp) ? outProp.GetString()?.ToLowerInvariant() : null;
                    if (outcome == "completed")
                        completedUsers.Add(ev.UserId);
                    else if (outcome == "skipped")
                        skippedUsers.Add(ev.UserId);
                    else
                        abandonedUsers.Add(ev.UserId);
                }
                else if (ev.Name == TelemetryNames.GuidanceStep)
                {
                    if (root.TryGetProperty("step_index", out var idxProp))
                    {
                        var idx = idxProp.GetInt32();
                        if (!userStepReached.TryGetValue(idx, out var users))
                        {
                            users = new HashSet<Guid>();
                            userStepReached[idx] = users;
                        }
                        users.Add(ev.UserId);

                        if (root.TryGetProperty("step_id", out var sIdProp))
                        {
                            var sId = sIdProp.GetString();
                            if (!string.IsNullOrEmpty(sId))
                                observedStepIds[idx] = sId;
                        }

                        if (root.TryGetProperty("duration_ms", out var durProp))
                        {
                            var dur = durProp.GetInt64();
                            if (dur >= 0)
                            {
                                if (!stepDurations.TryGetValue(idx, out var list))
                                {
                                    list = new List<long>();
                                    stepDurations[idx] = list;
                                }
                                list.Add(dur);
                            }
                        }
                    }
                }
            }
            catch (JsonException) { }
        }

        var totalStarted = startedUsers.Count;
        if (userStepReached.Count > 0)
        {
            var maxStepUsers = userStepReached.Values.Max(u => u.Count);
            if (maxStepUsers > totalStarted)
                totalStarted = maxStepUsers;
        }
        var totalCompleted = completedUsers.Count;
        var totalAbandoned = abandonedUsers.Count;
        var totalSkipped = skippedUsers.Count;
        var completionRate = totalStarted > 0 ? Math.Round((double)totalCompleted / totalStarted, 4) : 0;

        var maxIndex = Math.Max(
            authoredSteps.Count > 0 ? authoredSteps.Count - 1 : 0,
            userStepReached.Count > 0 ? userStepReached.Keys.Max() : 0);

        var stepDtos = new List<GuidanceStepFunnelDto>();
        for (var i = 0; i <= maxIndex; i++)
        {
            var authored = i < authoredSteps.Count ? authoredSteps[i] : null;
            var stepId = authored?.StepId ?? (observedStepIds.TryGetValue(i, out var obsId) ? obsId : $"step_{i + 1}");
            var anchor = authored?.Anchor;
            var locKey = authored?.LocKey;

            var reached = userStepReached.TryGetValue(i, out var uSet) ? uSet.Count : 0;
            var prevReached = i == 0 ? totalStarted : (userStepReached.TryGetValue(i - 1, out var pSet) ? pSet.Count : 0);
            var convFromStart = totalStarted > 0 ? Math.Round((double)reached / totalStarted, 4) : 0;
            var convFromPrev = prevReached > 0 ? Math.Round((double)reached / prevReached, 4) : (i == 0 && reached > 0 ? 1.0 : 0);
            var dropOffCount = Math.Max(0, prevReached - reached);
            var dropOffRate = prevReached > 0 ? Math.Round((double)dropOffCount / prevReached, 4) : 0;

            var avgDur = 0.0;
            if (stepDurations.TryGetValue(i, out var durs) && durs.Count > 0)
            {
                avgDur = Math.Round(durs.Average(), 1);
            }

            stepDtos.Add(new GuidanceStepFunnelDto
            {
                StepIndex = i,
                StepId = stepId,
                Anchor = anchor,
                LocKey = locKey,
                ReachedCount = reached,
                DropOffCount = dropOffCount,
                DropOffRate = dropOffRate,
                ConversionFromStart = convFromStart,
                ConversionFromPrevious = convFromPrev,
                AverageDurationMs = avgDur
            });
        }

        var totalAvgDurSec = Math.Round(stepDtos.Sum(s => s.AverageDurationMs) / 1000.0, 1);

        return ServiceResult<GuidanceFlowFunnelDto>.Success(new GuidanceFlowFunnelDto
        {
            FlowId = flow.Id,
            FlowKey = flow.Key,
            FlowTitle = flow.Title,
            Version = version,
            FromDayUtc = fromDay,
            ToDayUtc = toDay,
            TotalStarted = totalStarted,
            TotalCompleted = totalCompleted,
            TotalAbandoned = totalAbandoned,
            TotalSkipped = totalSkipped,
            CompletionRate = completionRate,
            AverageDurationSeconds = totalAvgDurSec,
            Steps = stepDtos
        });
    }

    public async Task<ServiceResult<List<GuidanceMissingAnchorSummaryDto>>> GetMissingAnchorsAsync(
        string? flowKey = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var toDay = (toUtc ?? DateTime.UtcNow).Date;
        var fromDay = (fromUtc ?? toDay.AddDays(-30)).Date;
        if (fromDay > toDay)
            (fromDay, toDay) = (toDay, fromDay);

        var query = _context.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DayUtc >= fromDay && e.DayUtc <= toDay && e.Name == TelemetryNames.GuidanceAnchorMissing);

        if (!string.IsNullOrWhiteSpace(flowKey))
        {
            var trimmedKey = flowKey.Trim();
            query = query.Where(e => e.ParamsJson.Contains(trimmedKey));
        }

        var events = await query
            .Select(e => new { e.ParamsJson, e.Platform, e.AppVersion, e.OccurredAtUtc })
            .ToListAsync(cancellationToken);

        var groups = new Dictionary<(string Anchor, string Flow, int StepIndex), (int Count, DateTime FirstSeen, DateTime LastSeen, HashSet<string> Platforms, HashSet<string> Versions)>();

        foreach (var ev in events)
        {
            try
            {
                using var doc = JsonDocument.Parse(ev.ParamsJson);
                var root = doc.RootElement;
                var anchor = root.TryGetProperty("anchor_id", out var aProp) ? aProp.GetString() : null;
                var fKey = root.TryGetProperty("flow_id", out var fProp) ? fProp.GetString() : null;
                var sIdx = root.TryGetProperty("step_index", out var sProp) ? sProp.GetInt32() : 0;

                if (string.IsNullOrEmpty(anchor) || string.IsNullOrEmpty(fKey))
                    continue;

                if (!string.IsNullOrWhiteSpace(flowKey) && !string.Equals(fKey, flowKey.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = (anchor, fKey, sIdx);
                if (!groups.TryGetValue(key, out var entry))
                {
                    entry = (0, ev.OccurredAtUtc, ev.OccurredAtUtc, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }

                entry.Count++;
                if (ev.OccurredAtUtc < entry.FirstSeen) entry.FirstSeen = ev.OccurredAtUtc;
                if (ev.OccurredAtUtc > entry.LastSeen) entry.LastSeen = ev.OccurredAtUtc;
                if (!string.IsNullOrWhiteSpace(ev.Platform)) entry.Platforms.Add(ev.Platform);
                if (!string.IsNullOrWhiteSpace(ev.AppVersion)) entry.Versions.Add(ev.AppVersion);

                groups[key] = entry;
            }
            catch (JsonException) { }
        }

        var dtos = groups
            .OrderByDescending(g => g.Value.Count)
            .ThenByDescending(g => g.Value.LastSeen)
            .Select(g => new GuidanceMissingAnchorSummaryDto
            {
                AnchorId = g.Key.Anchor,
                FlowKey = g.Key.Flow,
                StepIndex = g.Key.StepIndex,
                OccurrenceCount = g.Value.Count,
                FirstSeenUtc = g.Value.FirstSeen,
                LastSeenUtc = g.Value.LastSeen,
                Platforms = g.Value.Platforms.OrderBy(p => p).ToList(),
                AppVersions = g.Value.Versions.OrderBy(v => v).ToList()
            })
            .ToList();

        return ServiceResult<List<GuidanceMissingAnchorSummaryDto>>.Success(dtos);
    }

    public async Task<ServiceResult<List<GuidanceFlowSummaryStatsDto>>> GetFlowsSummaryStatsAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var flows = await _context.GuidanceFlows
            .AsNoTracking()
            .OrderBy(f => f.Priority)
            .ThenBy(f => f.Key)
            .ToListAsync(cancellationToken);

        var toDay = (toUtc ?? DateTime.UtcNow).Date;
        var fromDay = (fromUtc ?? toDay.AddDays(-30)).Date;
        if (fromDay > toDay)
            (fromDay, toDay) = (toDay, fromDay);

        var guidanceNames = new[]
        {
            TelemetryNames.GuidanceFlowStart,
            TelemetryNames.GuidanceFlowEnd,
            TelemetryNames.GuidanceAnchorMissing
        };

        var events = await _context.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DayUtc >= fromDay && e.DayUtc <= toDay && guidanceNames.Contains(e.Name))
            .Select(e => new { e.UserId, e.Name, e.ParamsJson, e.OccurredAtUtc })
            .ToListAsync(cancellationToken);

        var startsByFlow = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        var completedByFlow = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        var missingAnchorsByFlow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastActivityByFlow = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in events)
        {
            try
            {
                using var doc = JsonDocument.Parse(ev.ParamsJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("flow_id", out var fProp))
                    continue;

                var fKey = fProp.GetString();
                if (string.IsNullOrEmpty(fKey))
                    continue;

                if (!lastActivityByFlow.TryGetValue(fKey, out var last) || ev.OccurredAtUtc > last)
                {
                    lastActivityByFlow[fKey] = ev.OccurredAtUtc;
                }

                if (ev.Name == TelemetryNames.GuidanceFlowStart)
                {
                    if (!startsByFlow.TryGetValue(fKey, out var starts))
                    {
                        starts = new HashSet<Guid>();
                        startsByFlow[fKey] = starts;
                    }
                    starts.Add(ev.UserId);
                }
                else if (ev.Name == TelemetryNames.GuidanceFlowEnd)
                {
                    var outcome = root.TryGetProperty("outcome", out var oProp) ? oProp.GetString()?.ToLowerInvariant() : null;
                    if (outcome == "completed")
                    {
                        if (!completedByFlow.TryGetValue(fKey, out var comp))
                        {
                            comp = new HashSet<Guid>();
                            completedByFlow[fKey] = comp;
                        }
                        comp.Add(ev.UserId);
                    }
                }
                else if (ev.Name == TelemetryNames.GuidanceAnchorMissing)
                {
                    missingAnchorsByFlow[fKey] = missingAnchorsByFlow.GetValueOrDefault(fKey) + 1;
                }
            }
            catch (JsonException) { }
        }

        var results = flows.Select(f =>
        {
            var started = startsByFlow.TryGetValue(f.Key, out var sSet) ? sSet.Count : 0;
            var completed = completedByFlow.TryGetValue(f.Key, out var cSet) ? cSet.Count : 0;
            var missing = missingAnchorsByFlow.GetValueOrDefault(f.Key);
            var lastActivity = lastActivityByFlow.TryGetValue(f.Key, out var la) ? (DateTime?)la : null;
            var compRate = started > 0 ? Math.Round((double)completed / started, 4) : 0;

            return new GuidanceFlowSummaryStatsDto
            {
                FlowId = f.Id,
                FlowKey = f.Key,
                Title = f.Title,
                ActiveVersion = f.ActiveVersionNumber,
                IsKillSwitched = f.IsKillSwitched,
                TotalStarted = started,
                TotalCompleted = completed,
                CompletionRate = compRate,
                MissingAnchorCount = missing,
                LastActivityUtc = lastActivity
            };
        }).ToList();

        return ServiceResult<List<GuidanceFlowSummaryStatsDto>>.Success(results);
    }

    private sealed record AuthoredStep(string StepId, string? Anchor, string? LocKey);

    private static List<AuthoredStep> ParseAuthoredSteps(string? stepsJson)
    {
        var list = new List<AuthoredStep>();
        if (string.IsNullOrWhiteSpace(stepsJson))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(stepsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;

            var idx = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                idx++;
                var stepId = el.TryGetProperty("stepId", out var sProp) ? sProp.GetString() : null;
                stepId ??= el.TryGetProperty("id", out var idProp) ? idProp.GetString() : $"step_{idx}";

                string? anchor = null;
                string? locKey = null;

                if (el.TryGetProperty("beat", out var bProp) && bProp.ValueKind == JsonValueKind.Object)
                {
                    if (bProp.TryGetProperty("anchor", out var aProp))
                        anchor = aProp.GetString();

                    if (bProp.TryGetProperty("lines", out var lProp) && lProp.ValueKind == JsonValueKind.Array)
                    {
                        var firstLine = lProp.EnumerateArray().FirstOrDefault();
                        if (firstLine.ValueKind == JsonValueKind.Object && firstLine.TryGetProperty("key", out var kProp))
                            locKey = kProp.GetString();
                    }
                }

                list.Add(new AuthoredStep(stepId ?? $"step_{idx}", anchor, locKey));
            }
        }
        catch (JsonException) { }

        return list;
    }
}
