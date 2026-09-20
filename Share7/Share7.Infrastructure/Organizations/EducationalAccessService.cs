using Microsoft.EntityFrameworkCore;
using Share7.Application.Organizations.Interfaces;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Organizations;

/// <inheritdoc cref="IEducationalAccessService"/>
///
/// <remarks>
/// <para><b>Every org-side, teacher-side and guardian-side read in the platform comes through
/// here.</b> That is the design: §9.3 is one sentence, and one sentence deserves one
/// implementation. A second place that decided who may see a child would be a second answer, and
/// the two would diverge on the first feature that forgot about one of them.</para>
///
/// <para><b>The order of the checks is the order of the strength of the claim</b> — self, guardian,
/// teacher, org admin — and the first one that holds wins. A teacher who is also a parent of a
/// child in their class gets the guardian scope, which is wider, and that is correct: the
/// relationship they hold is real and does not weaken because they also teach.</para>
/// </remarks>
public class EducationalAccessService : IEducationalAccessService
{
    private readonly ApplicationDbContext _dbContext;

    public EducationalAccessService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<EducationalAccessScope> ResolveAsync(
        Guid viewerUserId,
        Guid learnerUserId,
        bool viewerIsPlatformAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (viewerUserId == Guid.Empty || learnerUserId == Guid.Empty) return EducationalAccessScope.Denied;

        // ── self ────────────────────────────────────────────────────────────
        if (viewerUserId == learnerUserId)
        {
            return new EducationalAccessScope
            {
                Basis = AccessBasis.Self,
                LearnerId = learnerUserId,
                AllEnrollments = true,
                MayReadRawEvidence = true
            };
        }

        // ── platform admin ──────────────────────────────────────────────────
        // Last-resort and deliberately unqualified: a platform admin can see everything, which is
        // why §9.3 says this route is audited and never routine. The audit belongs at the endpoint
        // that sets the flag, not here — this method answers "may they", not "should they".
        if (viewerIsPlatformAdmin)
        {
            return new EducationalAccessScope
            {
                Basis = AccessBasis.PlatformAdmin,
                LearnerId = learnerUserId,
                AllEnrollments = true,
                MayReadRawEvidence = true
            };
        }

        // ── guardian ────────────────────────────────────────────────────────
        var link = await _dbContext.GuardianLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                g => g.GuardianUserId == viewerUserId
                     && g.LearnerUserId == learnerUserId
                     && g.VerifiedAtUtc != null
                     && g.RevokedAtUtc == null,
                cancellationToken);

        if (link is not null && link.Permits(GuardianConsentScope.ViewProgress))
        {
            return new EducationalAccessScope
            {
                Basis = AccessBasis.Guardian,
                LearnerId = learnerUserId,
                AllEnrollments = true,

                // A parent sees learning, not behaviour. Session timestamps, device data and
                // engagement metrics are telemetry and belong to nobody outside the platform's own
                // analytics (§18.2) — so the widest relationship outside the learner themselves
                // still does not carry the raw feed.
                MayReadRawEvidence = false
            };
        }

        // ── teacher, through a shared cohort ────────────────────────────────
        // The learner's cohorts, then the viewer's teaching memberships over the same ones. Both
        // sides must be current: a teacher who left in October may not read November's work, and a
        // learner who left keeps their evidence attached to the cohort that collected it while the
        // teacher's view of them ends.
        var learnerCohortIds = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.UserId == learnerUserId
                         && cm.Role == CohortRole.Learner
                         && cm.LeftAtUtc == null)
            .Select(cm => cm.CohortId)
            .ToListAsync(cancellationToken);

        if (learnerCohortIds.Count > 0)
        {
            var taughtCohortIds = await _dbContext.CohortMemberships
                .AsNoTracking()
                .Where(cm => cm.UserId == viewerUserId
                             && cm.LeftAtUtc == null
                             && (cm.Role == CohortRole.Teacher || cm.Role == CohortRole.Assistant)
                             && learnerCohortIds.Contains(cm.CohortId))
                .Select(cm => cm.CohortId)
                .ToListAsync(cancellationToken);

            if (taughtCohortIds.Count > 0)
            {
                // The enrolments those shared cohorts provisioned, and nothing else. A teacher sees
                // the work done under their own class's enrolment — not the learner's private
                // curriculum, and not another school's (§9.2).
                var enrollmentIds = await _dbContext.CohortMemberships
                    .AsNoTracking()
                    .Where(cm => cm.UserId == learnerUserId
                                 && taughtCohortIds.Contains(cm.CohortId)
                                 && cm.EnrollmentId != null)
                    .Select(cm => cm.EnrollmentId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var orgIds = await _dbContext.Cohorts
                    .AsNoTracking()
                    .Where(c => taughtCohortIds.Contains(c.Id))
                    .Select(c => c.OrgId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                return new EducationalAccessScope
                {
                    Basis = AccessBasis.Teacher,
                    LearnerId = learnerUserId,
                    AllEnrollments = false,
                    EnrollmentIds = enrollmentIds,
                    OrgIds = orgIds,
                    MayReadRawEvidence = false
                };
            }
        }

        // ── org admin, through an owned enrolment ───────────────────────────
        var adminOrgIds = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == viewerUserId
                        && m.Role == OrgRole.OrgAdmin
                        && m.Status == MembershipStatus.Active
                        && m.RevokedAtUtc == null)
            .Select(m => m.OrgId)
            .ToListAsync(cancellationToken);

        if (adminOrgIds.Count > 0)
        {
            // An admin's reach extends down their hierarchy: a district administrator administers
            // its schools. It never extends up or sideways.
            var scopeOrgIds = await ExpandDownwardsAsync(adminOrgIds, cancellationToken);

            var enrollmentIds = await _dbContext.Enrollments
                .AsNoTracking()
                .Where(e => e.LearnerId == learnerUserId
                            && e.OwnerOrgId != null
                            && scopeOrgIds.Contains(e.OwnerOrgId.Value))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (enrollmentIds.Count > 0)
            {
                return new EducationalAccessScope
                {
                    Basis = AccessBasis.OrgAdmin,
                    LearnerId = learnerUserId,
                    AllEnrollments = false,
                    EnrollmentIds = enrollmentIds,
                    OrgIds = adminOrgIds,
                    MayReadRawEvidence = false
                };
            }
        }

        return EducationalAccessScope.Denied;
    }

    public async Task<IReadOnlyList<Guid>> ListVisibleLearnersAsync(
        Guid viewerUserId, CancellationToken cancellationToken = default)
    {
        var learners = new HashSet<Guid>();

        // Through guardianship.
        var wards = await _dbContext.GuardianLinks
            .AsNoTracking()
            .Where(g => g.GuardianUserId == viewerUserId
                        && g.VerifiedAtUtc != null
                        && g.RevokedAtUtc == null
                        && (g.ConsentScope & GuardianConsentScope.ViewProgress) == GuardianConsentScope.ViewProgress)
            .Select(g => g.LearnerUserId)
            .ToListAsync(cancellationToken);

        foreach (var ward in wards) learners.Add(ward);

        // Through teaching.
        var taughtCohortIds = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.UserId == viewerUserId
                         && cm.LeftAtUtc == null
                         && (cm.Role == CohortRole.Teacher || cm.Role == CohortRole.Assistant))
            .Select(cm => cm.CohortId)
            .ToListAsync(cancellationToken);

        if (taughtCohortIds.Count > 0)
        {
            var pupils = await _dbContext.CohortMemberships
                .AsNoTracking()
                .Where(cm => taughtCohortIds.Contains(cm.CohortId)
                             && cm.Role == CohortRole.Learner
                             && cm.LeftAtUtc == null)
                .Select(cm => cm.UserId)
                .ToListAsync(cancellationToken);

            foreach (var pupil in pupils) learners.Add(pupil);
        }

        // Through administering an organization that owns enrolments.
        var adminOrgIds = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == viewerUserId
                        && m.Role == OrgRole.OrgAdmin
                        && m.Status == MembershipStatus.Active
                        && m.RevokedAtUtc == null)
            .Select(m => m.OrgId)
            .ToListAsync(cancellationToken);

        if (adminOrgIds.Count > 0)
        {
            var scopeOrgIds = await ExpandDownwardsAsync(adminOrgIds, cancellationToken);

            var enrolled = await _dbContext.Enrollments
                .AsNoTracking()
                .Where(e => e.OwnerOrgId != null
                            && scopeOrgIds.Contains(e.OwnerOrgId.Value)
                            && e.EndedAtUtc == null)
                .Select(e => e.LearnerId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var learner in enrolled) learners.Add(learner);
        }

        return learners.ToList();
    }

    public async Task<bool> HasOrgRoleAsync(
        Guid viewerUserId, Guid orgId, OrgRole role, CancellationToken cancellationToken = default)
    {
        // Checked against the org itself and against every ancestor: an administrator of a district
        // administers its schools, and requiring a separate grant per school would make the
        // hierarchy decorative.
        var ancestors = await AncestorsOfAsync(orgId, cancellationToken);

        return await _dbContext.Memberships
            .AsNoTracking()
            .AnyAsync(m => m.UserId == viewerUserId
                           && ancestors.Contains(m.OrgId)
                           && m.Role == role
                           && m.Status == MembershipStatus.Active
                           && m.RevokedAtUtc == null,
                cancellationToken);
    }

    // ───────────────────────────────────────────────────────────── hierarchy

    /// <summary>The named orgs plus everything beneath them.</summary>
    private async Task<List<Guid>> ExpandDownwardsAsync(
        List<Guid> roots, CancellationToken cancellationToken)
    {
        var edges = await _dbContext.Organizations
            .AsNoTracking()
            .Select(o => new { o.Id, o.ParentOrgId })
            .ToListAsync(cancellationToken);

        var childrenOf = edges
            .Where(e => e.ParentOrgId != null)
            .GroupBy(e => e.ParentOrgId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>(roots);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            if (!childrenOf.TryGetValue(current, out var children)) continue;

            foreach (var child in children) queue.Enqueue(child);
        }

        return seen.ToList();
    }

    /// <summary>One org and every org above it.</summary>
    private async Task<List<Guid>> AncestorsOfAsync(Guid orgId, CancellationToken cancellationToken)
    {
        var parentOf = await _dbContext.Organizations
            .AsNoTracking()
            .Select(o => new { o.Id, o.ParentOrgId })
            .ToDictionaryAsync(o => o.Id, o => o.ParentOrgId, cancellationToken);

        var chain = new List<Guid>();
        var current = (Guid?)orgId;

        while (current is { } id && !chain.Contains(id))
        {
            chain.Add(id);
            current = parentOf.GetValueOrDefault(id);
        }

        return chain;
    }
}
