using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Social;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

/// <summary>The existing verified guardian link remains the sole source of social consent.</summary>
public sealed class GuardianSocialConsentService(ApplicationDbContext db, IAuditLog audit) : IGuardianSocialConsentService
{
    public async Task<IReadOnlyList<GuardianSocialConsentDto>> ListAsync(Guid guardian, CancellationToken token = default) =>
        await db.GuardianLinks.AsNoTracking().Where(g => g.GuardianUserId == guardian && g.VerifiedAtUtc != null && g.RevokedAtUtc == null)
            .OrderBy(g => g.CreatedAtUtc).Take(100).Select(g => new GuardianSocialConsentDto(g.Id, g.LearnerUserId,
                (g.ConsentScope & GuardianConsentScope.SocialPlay) == GuardianConsentScope.SocialPlay)).ToListAsync(token);

    public async Task<ServiceResult> SetAsync(Guid guardian, Guid link, bool enabled, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [GuardianLinks] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {link}", token);
        var row = await db.GuardianLinks.FirstOrDefaultAsync(g => g.Id == link && g.GuardianUserId == guardian
            && g.VerifiedAtUtc != null && g.RevokedAtUtc == null, token);
        if (row is null) return ServiceResult.NotFound("Verified guardian link unavailable.");
        var scope = enabled ? row.ConsentScope | GuardianConsentScope.SocialPlay : row.ConsentScope & ~GuardianConsentScope.SocialPlay;
        if (scope == row.ConsentScope) return ServiceResult.Success();
        row.ConsentScope = scope;
        audit.Record(new("guardian.social-consent.changed", "organizations", "Changed verified guardian social-play consent.", "guardian-link", link.ToString(), new { enabled }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
}
