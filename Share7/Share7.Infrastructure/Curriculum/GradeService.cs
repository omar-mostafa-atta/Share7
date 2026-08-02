using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class GradeService : IGradeService
{
    private readonly ApplicationDbContext _dbContext;

    public GradeService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<GradeDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Grades
            .OrderBy(g => g.Order)
            .Select(g => new GradeDto { Id = g.Id, NameEn = g.NameEn, NameAr = g.NameAr })
            .ToListAsync(cancellationToken);
    }
}
