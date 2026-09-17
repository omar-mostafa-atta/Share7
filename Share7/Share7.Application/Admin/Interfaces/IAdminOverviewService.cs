using Share7.Application.Admin.Models;

namespace Share7.Application.Admin.Interfaces;

/// <summary>
/// Platform-wide counters for the admin console's landing page.
/// </summary>
public interface IAdminOverviewService
{
    Task<AdminOverviewDto> GetAsync(CancellationToken cancellationToken = default);
}
