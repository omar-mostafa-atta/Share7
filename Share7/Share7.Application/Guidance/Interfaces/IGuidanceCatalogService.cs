using System.Threading;
using System.Threading.Tasks;
using Share7.Application.Common.Models;
using Share7.Application.Guidance.Models;

namespace Share7.Application.Guidance.Interfaces;

public interface IGuidanceCatalogService
{
    Task<ServiceResult<GuidanceCatalogClientDto>> GetPublishedCatalogAsync(CancellationToken cancellationToken = default);
}
