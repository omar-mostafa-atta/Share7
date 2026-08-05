using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

public interface IGradeService
{
    /// <summary>
    /// Grades in a single language. When <paramref name="langId"/> is null the caller's
    /// preferred language is used, falling back to English.
    /// </summary>
    Task<IReadOnlyList<GradeDto>> GetAllAsync(Guid? langId = null, CancellationToken cancellationToken = default);
}
