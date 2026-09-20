using Microsoft.EntityFrameworkCore;
using Share7.Application.Assessment.Interfaces;
using Share7.Domain.Assessment;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Assessment;

/// <summary>
/// Walks the form in order. **Version one, and what ships.**
/// <para>
/// It looks trivial because it is, and that is the point of having the interface at all: an
/// adaptive selector needs calibrated items, the platform has none, and building one now would be
/// building a psychometric engine on top of data that cannot support it. What this costs today is
/// one class; what it buys is that Phase 5's <c>AdaptiveSelector</c> changes no schema, no
/// endpoint and no client (§4.4).
/// </para>
/// </summary>
public class FixedFormSelector : IItemSelector
{
    private readonly ApplicationDbContext _dbContext;

    public FixedFormSelector(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<AssessmentFormItem?> NextAsync(
        AdministrationContext context, CancellationToken cancellationToken = default)
    {
        var answered = context.AnsweredPositions.ToList();

        return await _dbContext.AssessmentFormItems
            .AsNoTracking()
            .Where(i => i.FormId == context.FormId && !answered.Contains(i.Position))
            .OrderBy(i => i.Position)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
