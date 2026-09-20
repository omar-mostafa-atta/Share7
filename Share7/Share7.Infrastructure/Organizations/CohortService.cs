using Microsoft.EntityFrameworkCore;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Organizations.Models;
using Share7.Domain.Organizations;
using Share7.Domain.Structure;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Organizations;

/// <inheritdoc cref="ICohortService"/>
public class CohortService : ICohortService
{
    private readonly ApplicationDbContext _dbContext;

    public CohortService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // ────────────────────────────────────────────────────────────── cohorts

    public async Task<IReadOnlyList<CohortDto>> ListAsync(
        Guid orgId, Guid langId, bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var cohorts = await _dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.OrgId == orgId && (includeArchived || c.Status == CohortStatus.Active))
            .OrderBy(c => c.AcademicPeriod).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(cohorts, langId, cancellationToken);
    }

    public async Task<CohortDto?> GetAsync(
        Guid cohortId, Guid langId, CancellationToken cancellationToken = default)
    {
        var cohort = await _dbContext.Cohorts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cohortId, cancellationToken);

        if (cohort is null) return null;

        return (await DescribeAsync([cohort], langId, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<CohortDto>> ListForUserAsync(
        Guid userId, Guid langId, CancellationToken cancellationToken = default)
    {
        var cohortIds = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.UserId == userId && cm.LeftAtUtc == null)
            .Select(cm => cm.CohortId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var cohorts = await _dbContext.Cohorts
            .AsNoTracking()
            .Where(c => cohortIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return await DescribeAsync(cohorts, langId, cancellationToken);
    }

    private async Task<IReadOnlyList<CohortDto>> DescribeAsync(
        List<Cohort> cohorts, Guid langId, CancellationToken cancellationToken)
    {
        var ids = cohorts.Select(c => c.Id).ToList();
        var orgIds = cohorts.Select(c => c.OrgId).Distinct().ToList();

        var orgNames = await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);

        var counts = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => ids.Contains(cm.CohortId) && cm.LeftAtUtc == null)
            .GroupBy(cm => new { cm.CohortId, cm.Role })
            .Select(g => new { g.Key.CohortId, g.Key.Role, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var nodeIds = cohorts.Where(c => c.PlacementNodeId != null)
            .Select(c => c.PlacementNodeId!.Value).Distinct().ToList();

        var nodeTitles = await _dbContext.CurriculumNodeTranslations
            .AsNoTracking()
            .Where(t => nodeIds.Contains(t.NodeId) && t.LangId == langId)
            .ToDictionaryAsync(t => t.NodeId, t => t.Title, cancellationToken);

        return cohorts.Select(c => new CohortDto(
            c.Id,
            c.OrgId,
            orgNames.GetValueOrDefault(c.OrgId) ?? string.Empty,
            c.Name,
            c.AcademicPeriod,
            c.CurriculumVersionId,
            c.PlacementNodeId,
            c.PlacementNodeId is { } n ? nodeTitles.GetValueOrDefault(n) : null,
            c.OverlayId,
            c.Status,
            c.CreatedAtUtc,
            counts.Where(x => x.CohortId == c.Id && x.Role == CohortRole.Learner).Sum(x => x.Count),
            counts.Where(x => x.CohortId == c.Id && x.Role != CohortRole.Learner).Sum(x => x.Count)))
            .ToList();
    }

    public async Task<CohortDto> CreateAsync(
        CreateCohortRequest request, Guid langId, Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Organizations.AnyAsync(o => o.Id == request.OrgId, cancellationToken))
            throw new InvalidOperationException("Organization not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("A cohort needs a name.");

        if (request.CurriculumVersionId is { } versionId &&
            !await _dbContext.CurriculumVersions.AnyAsync(v => v.Id == versionId, cancellationToken))
        {
            throw new InvalidOperationException("The curriculum version does not exist.");
        }

        var cohort = new Cohort
        {
            Id = Guid.NewGuid(),
            OrgId = request.OrgId,
            Name = request.Name.Trim(),
            AcademicPeriod = (request.AcademicPeriod ?? string.Empty).Trim(),
            CurriculumVersionId = request.CurriculumVersionId,
            PlacementNodeId = request.PlacementNodeId,
            Status = CohortStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Cohorts.Add(cohort);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([cohort], langId, cancellationToken))[0];
    }

    public async Task<CohortDto> SetStatusAsync(
        Guid cohortId, CohortStatus status, Guid langId, CancellationToken cancellationToken = default)
    {
        var cohort = await _dbContext.Cohorts.FirstOrDefaultAsync(c => c.Id == cohortId, cancellationToken)
            ?? throw new InvalidOperationException("Cohort not found.");

        cohort.Status = status;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAsync([cohort], langId, cancellationToken))[0];
    }

    // ────────────────────────────────────────────────────────────── roster

    public async Task<IReadOnlyList<CohortMemberDto>> ListMembersAsync(
        Guid cohortId, bool includeLeft = false, CancellationToken cancellationToken = default)
    {
        var members = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.CohortId == cohortId && (includeLeft || cm.LeftAtUtc == null))
            .OrderBy(cm => cm.Role)
            .ToListAsync(cancellationToken);

        var userIds = members.Select(m => m.UserId).Distinct().ToList();

        var userNames = await _dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);

        var fullNames = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FullName })
            .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        return members.Select(m => new CohortMemberDto(
            m.Id,
            m.UserId,
            userNames.GetValueOrDefault(m.UserId) ?? string.Empty,
            fullNames.GetValueOrDefault(m.UserId),
            m.Role,
            m.JoinedAtUtc,
            m.LeftAtUtc,
            m.EnrollmentId)).ToList();
    }

    public async Task<CohortMemberDto> AddMemberAsync(
        Guid cohortId, AddCohortMemberRequest request, CancellationToken cancellationToken = default)
    {
        var cohort = await _dbContext.Cohorts.FirstOrDefaultAsync(c => c.Id == cohortId, cancellationToken)
            ?? throw new InvalidOperationException("Cohort not found.");

        if (cohort.Status != CohortStatus.Active)
            throw new InvalidOperationException("An archived cohort cannot take new members.");

        if (!await _dbContext.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken))
            throw new InvalidOperationException("User not found.");

        var existing = await _dbContext.CohortMemberships
            .FirstOrDefaultAsync(cm => cm.CohortId == cohortId
                                       && cm.UserId == request.UserId
                                       && cm.LeftAtUtc == null, cancellationToken);

        if (existing is not null)
            return (await ListMembersAsync(cohortId, cancellationToken: cancellationToken))
                .First(m => m.Id == existing.Id);

        var now = DateTime.UtcNow;

        var membership = new CohortMembership
        {
            Id = Guid.NewGuid(),
            CohortId = cohortId,
            UserId = request.UserId,
            Role = request.Role,
            JoinedAtUtc = now
        };

        // Adding a learner provisions the org-owned enrolment. **This is the act that creates
        // visibility** — the organization sees evidence through enrolments it owns, and there is no
        // other way to grant it (§9.2).
        if (request.Role == CohortRole.Learner && cohort.CurriculumVersionId is { } versionId)
        {
            var enrollment = new Enrollment
            {
                Id = Guid.NewGuid(),
                LearnerId = request.UserId,
                CurriculumVersionId = versionId,
                PlacementNodeId = cohort.PlacementNodeId,
                Source = EnrollmentSource.Organization,
                OwnerOrgId = cohort.OrgId,

                // Never primary. A learner who already follows a curriculum of their own keeps that
                // answer to "what are you studying"; a school adding them to a class does not
                // overwrite it, and the filtered unique index would refuse it anyway.
                IsPrimary = false,
                StartedAtUtc = now,
                CreatedAtUtc = now
            };

            _dbContext.Enrollments.Add(enrollment);
            membership.EnrollmentId = enrollment.Id;
        }

        _dbContext.CohortMemberships.Add(membership);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await ListMembersAsync(cohortId, cancellationToken: cancellationToken))
            .First(m => m.Id == membership.Id);
    }

    public async Task RemoveMemberAsync(
        Guid cohortMembershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.CohortMemberships
            .FirstOrDefaultAsync(cm => cm.Id == cohortMembershipId, cancellationToken)
            ?? throw new InvalidOperationException("Cohort membership not found.");

        if (membership.LeftAtUtc is not null) return;

        var now = DateTime.UtcNow;
        membership.LeftAtUtc = now;

        // The enrolment ends with the membership, and neither row is deleted: the evidence
        // collected while the learner was in the cohort is real and stays attached to the enrolment
        // that collected it. The org stops seeing new work, not old work it legitimately saw.
        if (membership.EnrollmentId is { } enrollmentId)
        {
            var enrollment = await _dbContext.Enrollments
                .FirstOrDefaultAsync(e => e.Id == enrollmentId, cancellationToken);

            if (enrollment is { EndedAtUtc: null }) enrollment.EndedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // ────────────────────────────────────────────────────────── assignments

    public async Task<IReadOnlyList<AssignmentDto>> ListAssignmentsAsync(
        Guid cohortId, Guid langId, CancellationToken cancellationToken = default)
    {
        var assignments = await _dbContext.Assignments
            .AsNoTracking()
            .Where(a => a.CohortId == cohortId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync(cancellationToken);

        return await DescribeAssignmentsAsync(assignments, langId, cancellationToken);
    }

    public async Task<IReadOnlyList<AssignmentDto>> ListForLearnerAsync(
        Guid learnerId, Guid langId, CancellationToken cancellationToken = default)
    {
        var cohortIds = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.UserId == learnerId
                         && cm.Role == CohortRole.Learner
                         && cm.LeftAtUtc == null)
            .Select(cm => cm.CohortId)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count == 0) return [];

        var assignments = await _dbContext.Assignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CohortId) && a.WithdrawnAtUtc == null)
            .OrderBy(a => a.DueAtUtc ?? DateTime.MaxValue)
            .ThenByDescending(a => a.AssignedAtUtc)
            .ToListAsync(cancellationToken);

        return await DescribeAssignmentsAsync(assignments, langId, cancellationToken);
    }

    private async Task<IReadOnlyList<AssignmentDto>> DescribeAssignmentsAsync(
        List<Assignment> assignments, Guid langId, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0) return [];

        var nodeIds = assignments.Where(a => a.NodeId != null).Select(a => a.NodeId!.Value).Distinct().ToList();

        var nodeTitles = await _dbContext.CurriculumNodeTranslations
            .AsNoTracking()
            .Where(t => nodeIds.Contains(t.NodeId) && t.LangId == langId)
            .ToDictionaryAsync(t => t.NodeId, t => t.Title, cancellationToken);

        var cohortIds = assignments.Select(a => a.CohortId).Distinct().ToList();

        var learnerCounts = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cohortIds.Contains(cm.CohortId)
                         && cm.Role == CohortRole.Learner
                         && cm.LeftAtUtc == null)
            .GroupBy(cm => cm.CohortId)
            .Select(g => new { CohortId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CohortId, x => x.Count, cancellationToken);

        return assignments.Select(a => new AssignmentDto(
            a.Id,
            a.CohortId,
            a.Title,
            a.NodeId,
            a.NodeId is { } n ? nodeTitles.GetValueOrDefault(n) : null,
            a.AssessmentFormId,
            a.AssignedAtUtc,
            a.DueAtUtc,
            a.IsSupervised,
            a.WithdrawnAtUtc,
            learnerCounts.GetValueOrDefault(a.CohortId),

            // Completion is deliberately not computed here yet: "did this child do the homework"
            // is a question about evidence collected under an assignment context, and the play
            // pipeline does not carry an assignment id on a response. Reporting a fabricated zero
            // would be worse than reporting nothing, so the field is honest and unfilled.
            0)).ToList();
    }

    public async Task<AssignmentDto> CreateAssignmentAsync(
        CreateAssignmentRequest request, Guid langId, Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var cohort = await _dbContext.Cohorts
            .FirstOrDefaultAsync(c => c.Id == request.CohortId, cancellationToken)
            ?? throw new InvalidOperationException("Cohort not found.");

        if (cohort.Status != CohortStatus.Active)
            throw new InvalidOperationException("An archived cohort cannot be assigned work.");

        var hasNode = request.NodeId is not null;
        var hasForm = request.AssessmentFormId is not null;

        if (hasNode == hasForm)
            throw new InvalidOperationException("An assignment names exactly one of a node or a form.");

        if (request.NodeId is { } nodeId &&
            !await _dbContext.CurriculumNodes.AnyAsync(n => n.Id == nodeId, cancellationToken))
        {
            throw new InvalidOperationException("The curriculum node does not exist.");
        }

        if (request.AssessmentFormId is { } formId &&
            !await _dbContext.AssessmentForms.AnyAsync(f => f.Id == formId, cancellationToken))
        {
            throw new InvalidOperationException("The assessment form does not exist.");
        }

        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            CohortId = request.CohortId,
            Title = (request.Title ?? string.Empty).Trim(),
            NodeId = request.NodeId,
            AssessmentFormId = request.AssessmentFormId,
            AssignedAtUtc = DateTime.UtcNow,
            DueAtUtc = request.DueAtUtc,
            IsSupervised = request.IsSupervised,
            CreatedByUserId = actingUserId
        };

        _dbContext.Assignments.Add(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await DescribeAssignmentsAsync([assignment], langId, cancellationToken))[0];
    }

    public async Task WithdrawAssignmentAsync(
        Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await _dbContext.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken)
            ?? throw new InvalidOperationException("Assignment not found.");

        if (assignment.WithdrawnAtUtc is not null) return;

        assignment.WithdrawnAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
