using Microsoft.EntityFrameworkCore;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Organizations.Models;
using Share7.Domain.Content;
using Share7.Domain.Evidence;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Organizations;

/// <inheritdoc cref="IOrganizationService"/>
public class OrganizationService : IOrganizationService
{
    /// <summary>
    /// The trust ceiling a new organization's bank starts at.
    ///
    /// <para><b>Practice, not Assessment, and it is the whole teacher-authoring safety
    /// mechanism.</b> A school's own unreviewed material produces practice-class evidence however
    /// controlled the sitting was, so nothing a teacher writes on a Tuesday afternoon can move a
    /// national examination projection. An organization that puts its content through review has
    /// its ceiling raised deliberately, by somebody who decided to (§10.2, §17.2).</para>
    /// </summary>
    public const EvidenceStrength DefaultOrgBankCeiling = EvidenceStrength.Practice;

    private readonly ApplicationDbContext _dbContext;

    public OrganizationService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // ─────────────────────────────────────────────────────────────── reading

    public async Task<IReadOnlyList<OrganizationDto>> ListAsync(
        Guid? parentOrgId = null, CancellationToken cancellationToken = default)
    {
        var orgs = await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => parentOrgId == null || o.ParentOrgId == parentOrgId)
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(orgs, cancellationToken);
    }

    public async Task<OrganizationDto?> GetAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);

        if (org is null) return null;

        return (await DescribeAsync([org], cancellationToken))[0];
    }

    /// <summary>
    /// Counts are read in one grouped pass per dimension rather than per organization: a district
    /// listing is a handful of rows and three queries, not a handful of rows and three queries each.
    /// </summary>
    private async Task<IReadOnlyList<OrganizationDto>> DescribeAsync(
        List<Organization> orgs, CancellationToken cancellationToken)
    {
        var ids = orgs.Select(o => o.Id).ToList();

        var memberCounts = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => ids.Contains(m.OrgId) && m.RevokedAtUtc == null)
            .GroupBy(m => m.OrgId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, cancellationToken);

        var cohortCounts = await _dbContext.Cohorts
            .AsNoTracking()
            .Where(c => ids.Contains(c.OrgId) && c.Status == CohortStatus.Active)
            .GroupBy(c => c.OrgId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, cancellationToken);

        // Learners counted through org-owned enrolments rather than through memberships, because
        // the enrolment is what the org can actually see — a membership with no enrolment behind it
        // grants visibility of nothing (§9.2).
        var learnerCounts = await _dbContext.Enrollments
            .AsNoTracking()
            .Where(e => e.OwnerOrgId != null && ids.Contains(e.OwnerOrgId.Value) && e.EndedAtUtc == null)
            .GroupBy(e => e.OwnerOrgId!.Value)
            .Select(g => new { OrgId = g.Key, Count = g.Select(e => e.LearnerId).Distinct().Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, cancellationToken);

        return orgs.Select(o => new OrganizationDto(
            o.Id,
            o.OrgKey,
            o.ParentOrgId,
            o.Kind,
            o.Name,
            o.CountryCode,
            o.Status,
            o.ItemBankId,
            o.CreatedAtUtc,
            memberCounts.GetValueOrDefault(o.Id),
            cohortCounts.GetValueOrDefault(o.Id),
            learnerCounts.GetValueOrDefault(o.Id))).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetSubtreeIdsAsync(
        Guid orgId, CancellationToken cancellationToken = default)
    {
        // Walked in memory over the whole parent map. Organizations number in the thousands at
        // most and the tree is three deep in practice, so a recursive CTE would buy nothing and
        // cost a raw-SQL escape hatch in a query every report runs.
        var edges = await _dbContext.Organizations
            .AsNoTracking()
            .Select(o => new { o.Id, o.ParentOrgId })
            .ToListAsync(cancellationToken);

        var childrenOf = edges
            .Where(e => e.ParentOrgId != null)
            .GroupBy(e => e.ParentOrgId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var result = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(orgId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            // A cycle would be a data fault, not a shape; guarding here keeps one bad row from
            // hanging every org-scoped report in the product.
            if (result.Contains(current)) continue;

            result.Add(current);

            if (!childrenOf.TryGetValue(current, out var children)) continue;

            foreach (var child in children) queue.Enqueue(child);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────── writing

    public async Task<OrganizationDto> CreateAsync(
        CreateOrganizationRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var key = (request.OrgKey ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("An organization needs a key.");

        if (await _dbContext.Organizations.AnyAsync(o => o.OrgKey == key, cancellationToken))
            throw new InvalidOperationException($"Organization key '{key}' is already taken.");

        if (request.ParentOrgId is { } parentId &&
            !await _dbContext.Organizations.AnyAsync(o => o.Id == parentId, cancellationToken))
        {
            throw new InvalidOperationException("The parent organization does not exist.");
        }

        var now = DateTime.UtcNow;

        // The bank is created with the organization, in the same transaction, so there is never a
        // window in which an org can author content with nowhere of its own to put it.
        var bank = new ItemBank
        {
            Id = Guid.NewGuid(),
            BankKey = $"org.{key}",
            Name = request.Name,
            OwnerScope = ItemBankOwnerScope.Organization,
            ReviewPolicy = ItemBankReviewPolicy.Unreviewed,
            MaxEvidenceStrength = DefaultOrgBankCeiling,

            // One school's cohort is not a calibration sample. Pooling its statistics would move
            // every other learner's item difficulties (§10.2).
            PoolsStatisticsGlobally = false,
            CreatedAtUtc = now
        };

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            OrgKey = key,
            ParentOrgId = request.ParentOrgId,
            Kind = request.Kind,
            Name = request.Name,
            CountryCode = (request.CountryCode ?? string.Empty).Trim().ToUpperInvariant(),
            Status = OrganizationStatus.Active,
            ItemBankId = bank.Id,
            CreatedAtUtc = now
        };

        _dbContext.ItemBanks.Add(bank);
        _dbContext.Organizations.Add(org);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([org], cancellationToken))[0];
    }

    public async Task<OrganizationDto> SetStatusAsync(
        Guid orgId, OrganizationStatus status, CancellationToken cancellationToken = default)
    {
        var org = await _dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken)
            ?? throw new InvalidOperationException("Organization not found.");

        org.Status = status;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([org], cancellationToken))[0];
    }

    // ────────────────────────────────────────────────────────────── membership

    public async Task<IReadOnlyList<MembershipDto>> ListMembersAsync(
        Guid orgId, CancellationToken cancellationToken = default)
    {
        var memberships = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.OrgId == orgId)
            .OrderBy(m => m.Role)
            .ToListAsync(cancellationToken);

        return await DescribeMembershipsAsync(memberships, cancellationToken);
    }

    public async Task<IReadOnlyList<MembershipDto>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.RevokedAtUtc == null && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken);

        return await DescribeMembershipsAsync(memberships, cancellationToken);
    }

    private async Task<IReadOnlyList<MembershipDto>> DescribeMembershipsAsync(
        List<Membership> memberships, CancellationToken cancellationToken)
    {
        var userIds = memberships.Select(m => m.UserId).Distinct().ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);

        // The display name lives on StudentProfile, not on the identity user, so it is read
        // separately and is absent for staff accounts — which have no profile and do not need one.
        var names = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FullName })
            .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        return memberships.Select(m => new MembershipDto(
            m.Id,
            m.UserId,
            users.GetValueOrDefault(m.UserId) ?? string.Empty,
            names.GetValueOrDefault(m.UserId),
            m.OrgId,
            m.Role,
            m.Status,
            m.GrantedAtUtc,
            m.RevokedAtUtc)).ToList();
    }

    public async Task<MembershipDto> GrantAsync(
        Guid orgId, GrantMembershipRequest request, Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Organizations.AnyAsync(o => o.Id == orgId, cancellationToken))
            throw new InvalidOperationException("Organization not found.");

        if (!await _dbContext.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken))
            throw new InvalidOperationException("User not found.");

        var now = DateTime.UtcNow;

        // Reviving the existing row rather than adding a second keeps "who could see this child's
        // data, and when" a single readable history instead of a pile of overlapping grants.
        var existing = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == request.UserId
                                      && m.Role == request.Role, cancellationToken);

        if (existing is not null)
        {
            existing.Status = MembershipStatus.Active;
            existing.RevokedAtUtc = null;
            existing.GrantedByUserId = actingUserId;
            existing.GrantedAtUtc = now;
        }
        else
        {
            existing = new Membership
            {
                Id = Guid.NewGuid(),
                OrgId = orgId,
                UserId = request.UserId,
                Role = request.Role,
                Status = MembershipStatus.Active,
                GrantedByUserId = actingUserId,
                GrantedAtUtc = now
            };

            _dbContext.Memberships.Add(existing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeMembershipsAsync([existing], cancellationToken))[0];
    }

    public async Task RevokeAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken)
            ?? throw new InvalidOperationException("Membership not found.");

        if (membership.RevokedAtUtc is not null) return;

        membership.Status = MembershipStatus.Revoked;
        membership.RevokedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
