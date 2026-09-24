using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share7.Application.Common.Models;
using Share7.Application.Guidance.Models;

namespace Share7.Application.Guidance.Interfaces;

public interface IGuidanceAdminService
{
    Task<ServiceResult<List<GuidanceFlowAdminDto>>> ListFlowsAsync(bool includeKillSwitched = true, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceFlowAdminDto>> GetFlowAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceFlowAdminDto>> CreateFlowAsync(CreateGuidanceFlowRequest request, Guid userId, string? userEmail, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceFlowAdminDto>> UpdateDraftAsync(Guid id, UpdateGuidanceFlowDraftRequest request, Guid userId, string? userEmail, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceFlowAdminDto>> PublishVersionAsync(Guid id, PublishGuidanceFlowRequest request, Guid userId, string? userEmail, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceFlowAdminDto>> ToggleKillSwitchAsync(Guid id, bool isKillSwitched, string reason, Guid userId, string? userEmail, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ResetUserGuidanceAsync(Guid targetUserId, string reason, Guid adminUserId, string? adminEmail, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<GuidanceAuditLogDto>>> GetAuditLogsAsync(Guid? flowId = null, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceFlowFunnelDto>> GetFlowFunnelAsync(Guid flowId, int? version = null, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<GuidanceMissingAnchorSummaryDto>>> GetMissingAnchorsAsync(string? flowKey = null, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<GuidanceFlowSummaryStatsDto>>> GetFlowsSummaryStatsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
}
