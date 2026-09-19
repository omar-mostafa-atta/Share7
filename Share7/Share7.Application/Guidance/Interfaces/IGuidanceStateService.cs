using System;
using System.Threading;
using System.Threading.Tasks;
using Share7.Application.Common.Models;
using Share7.Application.Guidance.Models;
using Share7.Domain.Guidance;

namespace Share7.Application.Guidance.Interfaces;

public interface IGuidanceStateService
{
    Task<ServiceResult<GuidanceStateResponseDto>> GetStateAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<GuidanceStateResponseDto>> PushStateAsync(Guid userId, GuidanceStateSnapshot incoming, CancellationToken cancellationToken = default);
}
