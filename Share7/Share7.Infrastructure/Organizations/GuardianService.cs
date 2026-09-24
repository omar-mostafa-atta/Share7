using Microsoft.EntityFrameworkCore;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Organizations.Models;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Organizations;

/// <inheritdoc cref="IGuardianService"/>
public class GuardianService : IGuardianService
{
    private readonly ApplicationDbContext _dbContext;

    public GuardianService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<GuardianLinkDto>> ListForGuardianAsync(
        Guid guardianUserId, CancellationToken cancellationToken = default) =>
        ListAsync(g => g.GuardianUserId == guardianUserId, cancellationToken);

    public Task<IReadOnlyList<GuardianLinkDto>> ListForLearnerAsync(
        Guid learnerUserId, CancellationToken cancellationToken = default) =>
        ListAsync(g => g.LearnerUserId == learnerUserId, cancellationToken);

    private async Task<IReadOnlyList<GuardianLinkDto>> ListAsync(
        System.Linq.Expressions.Expression<Func<GuardianLink, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var links = await _dbContext.GuardianLinks
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(links, cancellationToken);
    }

    private async Task<IReadOnlyList<GuardianLinkDto>> DescribeAsync(
        List<GuardianLink> links, CancellationToken cancellationToken)
    {
        var userIds = links.SelectMany(l => new[] { l.GuardianUserId, l.LearnerUserId })
            .Distinct().ToList();

        var names = await _dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);

        return links.Select(l => new GuardianLinkDto(
            l.Id,
            l.GuardianUserId,
            names.GetValueOrDefault(l.GuardianUserId) ?? string.Empty,
            l.LearnerUserId,
            names.GetValueOrDefault(l.LearnerUserId) ?? string.Empty,
            l.Relationship,
            l.ConsentScope,
            l.VerifiedAtUtc,
            l.RevokedAtUtc,
            l.CreatedAtUtc)).ToList();
    }

    public async Task<GuardianLinkDto> CreateAsync(
        CreateGuardianLinkRequest request, CancellationToken cancellationToken = default)
    {
        if (request.GuardianUserId == request.LearnerUserId)
            throw new InvalidOperationException("A learner cannot be their own guardian.");

        var bothExist = await _dbContext.Users
            .CountAsync(u => u.Id == request.GuardianUserId || u.Id == request.LearnerUserId,
                cancellationToken) == 2;

        if (!bothExist) throw new InvalidOperationException("Guardian or learner not found.");

        var existing = await _dbContext.GuardianLinks
            .FirstOrDefaultAsync(g => g.GuardianUserId == request.GuardianUserId
                                      && g.LearnerUserId == request.LearnerUserId
                                      && g.RevokedAtUtc == null, cancellationToken);

        if (existing is not null)
            return (await DescribeAsync([existing], cancellationToken))[0];

        var link = new GuardianLink
        {
            Id = Guid.NewGuid(),
            GuardianUserId = request.GuardianUserId,
            LearnerUserId = request.LearnerUserId,
            Relationship = request.Relationship,
            ConsentScope = request.ConsentScope,

            // Created unverified, always. Verification is a separate act by somebody who can
            // actually check the relationship, and that is the entire value of the field — anyone
            // can type a child's user name.
            VerifiedAtUtc = null,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.GuardianLinks.Add(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([link], cancellationToken))[0];
    }

    public async Task<GuardianLinkDto> VerifyAsync(
        Guid linkId, Guid verifiedByUserId, CancellationToken cancellationToken = default)
    {
        var link = await Load(linkId, cancellationToken);

        if (link.RevokedAtUtc is not null)
            throw new InvalidOperationException("A revoked link cannot be verified.");

        link.VerifiedAtUtc ??= DateTime.UtcNow;
        link.VerifiedByUserId = verifiedByUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([link], cancellationToken))[0];
    }

    public async Task<GuardianLinkDto> SetConsentAsync(
        Guid linkId, GuardianConsentScope scope, CancellationToken cancellationToken = default)
    {
        var link = await Load(linkId, cancellationToken);

        if (link.RevokedAtUtc is not null)
            throw new InvalidOperationException("A revoked link carries no consent.");

        link.ConsentScope = scope;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([link], cancellationToken))[0];
    }

    public async Task RevokeAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await Load(linkId, cancellationToken);

        if (link.RevokedAtUtc is not null) return;

        // A timestamp, not a deletion. The period during which data was legitimately shared is
        // exactly what an audit has to be able to reconstruct (§18.3).
        link.RevokedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<GuardianLink> Load(Guid linkId, CancellationToken cancellationToken) =>
        await _dbContext.GuardianLinks.FirstOrDefaultAsync(g => g.Id == linkId, cancellationToken)
        ?? throw new InvalidOperationException("Guardian link not found.");
}
