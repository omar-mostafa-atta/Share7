using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

public interface IGradeService
{
    Task<IReadOnlyList<GradeDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
