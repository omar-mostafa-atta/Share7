using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Guidance.Interfaces;
using Share7.Application.Guidance.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Guidance;

public sealed class GuidanceCatalogService : IGuidanceCatalogService
{
    private readonly ApplicationDbContext _context;

    public GuidanceCatalogService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ServiceResult<GuidanceCatalogClientDto>> GetPublishedCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var flows = await _context.GuidanceFlows
            .Include(f => f.Versions)
            .AsNoTracking()
            .Where(f => !f.IsKillSwitched && f.ActiveVersionNumber > 0)
            .OrderBy(f => f.Priority)
            .ToListAsync(cancellationToken);

        var clientFlows = new List<GuidanceFlowClientDto>();

        foreach (var flow in flows)
        {
            var activeVersion = flow.Versions
                .FirstOrDefault(v => v.Status == "Published" && v.VersionNumber == flow.ActiveVersionNumber);

            if (activeVersion == null) continue;

            clientFlows.Add(new GuidanceFlowClientDto
            {
                Id = flow.Key,
                Version = activeVersion.VersionNumber,
                Priority = flow.Priority,
                Replay = flow.ReplayPolicy,
                Skippable = flow.Skippable,
                SkipAfterStep = flow.SkipAfterStep,
                Resumable = flow.Resumable,
                StepsJson = activeVersion.StepsJson,
                TriggerConditionsJson = activeVersion.TriggerConditionsJson,
                TargetAudienceJson = flow.TargetAudienceJson
            });
        }

        var catalog = new GuidanceCatalogClientDto
        {
            SchemaVersion = 1,
            Flows = clientFlows
        };

        return ServiceResult<GuidanceCatalogClientDto>.Success(catalog);
    }
}
