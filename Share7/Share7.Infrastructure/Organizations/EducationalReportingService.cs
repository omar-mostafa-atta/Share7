using Microsoft.EntityFrameworkCore;
using Share7.Application.Measurement.Interfaces;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Organizations.Models;
using Share7.Domain.Measurement;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Organizations;

/// <inheritdoc cref="IEducationalReportingService"/>
///
/// <remarks>
/// <para><b>Every figure here is computed from observations the viewer is allowed to see, not from
/// the learner's stored verdicts.</b> That distinction is the whole of §9.2 in practice. A
/// <c>MasteryVerdict</c> row is computed over a learner's entire history — personal play, a
/// previous school, a tutoring centre — and a cohort report built on those rows would hand a school
/// a summary of work it has no right to. So the mastery rule is re-applied over the scoped subset,
/// which is cheap because it is arithmetic and is the only version of this that is correct.</para>
///
/// <para><b>Reports are audience-shaped here rather than in a client.</b> The alternative — handing
/// a raw evidence feed to a UI and letting the UI choose — puts the child-safety boundary in
/// whichever portal was written last (§9.5).</para>
/// </remarks>
public class EducationalReportingService : IEducationalReportingService
{
    /// <summary>
    /// The smallest cohort that gets a percentage.
    ///
    /// <para><b>Five, and both reasons matter.</b> A share over four learners is statistically
    /// meaningless, and it de-anonymises them — in a class of four, "one learner has not met this"
    /// names a child to anyone who knows the other three (§19.2).</para>
    /// </summary>
    public const int SmallCellThreshold = 5;

    /// <summary>How many struggling targets a cohort report carries. A list, not a ranking of children.</summary>
    private const int StrugglingCount = 5;

    private readonly ApplicationDbContext _dbContext;
    private readonly IEducationalAccessService _access;
    private readonly IObservationProjector _projector;

    public EducationalReportingService(
        ApplicationDbContext dbContext,
        IEducationalAccessService access,
        IObservationProjector projector)
    {
        _dbContext = dbContext;
        _access = access;
        _projector = projector;
    }

    /// <summary>
    /// Brings the learners' observations up to date with their responses before anything is counted.
    ///
    /// <para><b>Projection is lazy, and a report is a read path like any other.</b> The learner's
    /// own screen and the exam projection both project before they read, so without this a class
    /// report answers "no evidence yet" for a child who worked this morning and simply has not
    /// opened their own progress screen since. A teacher would read that as the child not having
    /// done the work.</para>
    ///
    /// <para>Idempotent and watermarked — a learner with nothing pending costs one indexed query —
    /// so this is cheap to do on every read and there is nothing to schedule or to fall behind.</para>
    /// </summary>
    private async Task ProjectAsync(IReadOnlyCollection<Guid> learnerIds, CancellationToken cancellationToken)
    {
        foreach (var learnerId in learnerIds)
            await _projector.ProjectForLearnerAsync(learnerId, cancellationToken);
    }

    // ───────────────────────────────────────────────────────────── cohort

    public async Task<CohortReportDto> GetCohortReportAsync(
        Guid cohortId, Guid viewerUserId, Guid langId,
        bool viewerIsPlatformAdmin = false, CancellationToken cancellationToken = default)
    {
        var cohort = await _dbContext.Cohorts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cohortId, cancellationToken)
            ?? throw new InvalidOperationException("Cohort not found.");

        var orgName = await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Id == cohort.OrgId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var learnerIds = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.CohortId == cohortId && cm.Role == CohortRole.Learner && cm.LeftAtUtc == null)
            .Select(cm => cm.UserId)
            .ToListAsync(cancellationToken);

        var empty = new CohortReportDto
        {
            CohortId = cohortId,
            CohortName = cohort.Name,
            AcademicPeriod = cohort.AcademicPeriod,
            OrgId = cohort.OrgId,
            OrgName = orgName,
            LearnerCount = learnerIds.Count,
            ActiveLearnerCount = 0,
            SmallCellThreshold = SmallCellThreshold,
            Suppression = SuppressionReason.OutOfScope,
            Targets = [],
            StrugglingTargets = [],
            ObservationCount = 0
        };

        // The viewer must be able to see this cohort at all. Checked through the teaching or
        // administering relationship rather than through a role claim: a "Teacher" role says
        // nothing about *whose* children.
        var mayRead = viewerIsPlatformAdmin
            || await _dbContext.CohortMemberships.AnyAsync(
                cm => cm.CohortId == cohortId && cm.UserId == viewerUserId && cm.LeftAtUtc == null
                      && (cm.Role == CohortRole.Teacher || cm.Role == CohortRole.Assistant),
                cancellationToken)
            || await _access.HasOrgRoleAsync(viewerUserId, cohort.OrgId, OrgRole.OrgAdmin, cancellationToken);

        if (!mayRead) return empty;

        if (learnerIds.Count == 0)
            return empty with { Suppression = SuppressionReason.NoEvidence };

        await ProjectAsync(learnerIds, cancellationToken);

        var rule = await ActiveRuleAsync(cancellationToken);

        if (rule is null)
            return empty with { Suppression = SuppressionReason.NoEvidence };

        // The scope is the cohort's organization whoever is reading. A platform administrator is
        // allowed to *open* this report; they are not a second, wider audience for it. Widening it
        // for them would mean the console showed a school a number the school itself could never
        // see — support staff and the head teacher would be reading different classes under one
        // name, and the one on screen would include work the school does not own (§9.2).
        var rows = await TallyScopedAsync(
            learnerIds,
            [cohort.OrgId],
            rule.MinStrength,
            cancellationToken);

        if (rows.Count == 0)
            return empty with { Suppression = SuppressionReason.NoEvidence };

        var statements = await StatementsAsync(
            rows.Select(r => r.TargetId).Distinct().ToList(), langId, cancellationToken);

        var placeholders = await _dbContext.LearningTargets
            .AsNoTracking()
            .Where(t => rows.Select(r => r.TargetId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.IsPlaceholder, cancellationToken);

        var smallCohort = learnerIds.Count < SmallCellThreshold;

        var targets = rows
            .GroupBy(r => r.TargetId)
            .Select(g =>
            {
                var mastered = 0;
                var developing = 0;
                var notMet = 0;
                var insufficient = 0;

                foreach (var learner in g)
                {
                    var (estimate, low, _) = WilsonInterval.ForStorage(learner.Correct, learner.Count);

                    switch (rule.Decide(learner.Count, estimate, low))
                    {
                        case MasteryState.Mastered: mastered++; break;
                        case MasteryState.Developing: developing++; break;
                        case MasteryState.NotMet: notMet++; break;
                        default: insufficient++; break;
                    }
                }

                var reportable = mastered + developing + notMet;

                // Two independent reasons to withhold a share, and they are reported as reasons
                // rather than as a blank — a reader who learns that blank means zero will act on a
                // zero eventually.
                var suppression =
                    smallCohort ? SuppressionReason.SmallCell
                    : reportable == 0 ? SuppressionReason.NoEvidence
                    : SuppressionReason.None;

                return new CohortTargetRowDto
                {
                    TargetId = g.Key,
                    Statement = statements.GetValueOrDefault(g.Key) ?? string.Empty,
                    IsPlaceholder = placeholders.GetValueOrDefault(g.Key),
                    LearnerCount = learnerIds.Count,
                    ReportableCount = reportable,
                    MasteredCount = mastered,
                    DevelopingCount = developing,
                    NotMetCount = notMet,
                    InsufficientCount = insufficient + (learnerIds.Count - g.Count()),
                    MasteredShare = suppression == SuppressionReason.None
                        ? Math.Round((decimal)mastered / reportable, 4)
                        : null,
                    Suppression = suppression,
                    LastObservedAtUtc = g.Max(r => r.LastObservedAtUtc)
                };
            })
            .OrderBy(t => t.Statement)
            .ToList();

        // Worst first, among rows that have enough evidence to say anything. A cohort failing one
        // target together is far more often a teaching signal than twenty-eight simultaneous
        // misconceptions, which is why this list exists at all (§19.2).
        var struggling = targets
            .Where(t => t.Suppression == SuppressionReason.None && t.MasteredShare is not null)
            .OrderBy(t => t.MasteredShare)
            .ThenByDescending(t => t.NotMetCount)
            .Take(StrugglingCount)
            .ToList();

        return new CohortReportDto
        {
            CohortId = cohortId,
            CohortName = cohort.Name,
            AcademicPeriod = cohort.AcademicPeriod,
            OrgId = cohort.OrgId,
            OrgName = orgName,
            LearnerCount = learnerIds.Count,
            ActiveLearnerCount = rows.Select(r => r.LearnerId).Distinct().Count(),
            SmallCellThreshold = SmallCellThreshold,
            Suppression = smallCohort ? SuppressionReason.SmallCell : SuppressionReason.None,
            Targets = targets,
            StrugglingTargets = struggling,
            ObservationCount = rows.Sum(r => r.Count)
        };
    }

    public async Task<IReadOnlyList<CohortLearnerRowDto>> GetCohortLearnersAsync(
        Guid cohortId, Guid viewerUserId,
        bool viewerIsPlatformAdmin = false, CancellationToken cancellationToken = default)
    {
        var cohort = await _dbContext.Cohorts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cohortId, cancellationToken)
            ?? throw new InvalidOperationException("Cohort not found.");

        var mayRead = viewerIsPlatformAdmin
            || await _dbContext.CohortMemberships.AnyAsync(
                cm => cm.CohortId == cohortId && cm.UserId == viewerUserId && cm.LeftAtUtc == null
                      && (cm.Role == CohortRole.Teacher || cm.Role == CohortRole.Assistant),
                cancellationToken)
            || await _access.HasOrgRoleAsync(viewerUserId, cohort.OrgId, OrgRole.OrgAdmin, cancellationToken);

        if (!mayRead) return [];

        var learnerIds = await _dbContext.CohortMemberships
            .AsNoTracking()
            .Where(cm => cm.CohortId == cohortId && cm.Role == CohortRole.Learner && cm.LeftAtUtc == null)
            .Select(cm => cm.UserId)
            .ToListAsync(cancellationToken);

        if (learnerIds.Count == 0) return [];

        await ProjectAsync(learnerIds, cancellationToken);

        var rule = await ActiveRuleAsync(cancellationToken);
        if (rule is null) return [];

        // The scope is the cohort's organization whoever is reading. A platform administrator is
        // allowed to *open* this report; they are not a second, wider audience for it. Widening it
        // for them would mean the console showed a school a number the school itself could never
        // see — support staff and the head teacher would be reading different classes under one
        // name, and the one on screen would include work the school does not own (§9.2).
        var rows = await TallyScopedAsync(
            learnerIds,
            [cohort.OrgId],
            rule.MinStrength,
            cancellationToken);

        var userNames = await _dbContext.Users
            .AsNoTracking()
            .Where(u => learnerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);

        var fullNames = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => learnerIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FullName })
            .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        return learnerIds.Select(learnerId =>
        {
            var mine = rows.Where(r => r.LearnerId == learnerId).ToList();

            var mastered = 0;
            var developing = 0;
            var notMet = 0;

            foreach (var row in mine)
            {
                var (estimate, low, _) = WilsonInterval.ForStorage(row.Correct, row.Count);

                switch (rule.Decide(row.Count, estimate, low))
                {
                    case MasteryState.Mastered: mastered++; break;
                    case MasteryState.Developing: developing++; break;
                    case MasteryState.NotMet: notMet++; break;
                }
            }

            var last = mine.Count == 0 ? (DateTime?)null : mine.Max(r => r.LastObservedAtUtc);

            return new CohortLearnerRowDto
            {
                LearnerId = learnerId,
                UserName = userNames.GetValueOrDefault(learnerId) ?? string.Empty,
                FullName = fullNames.GetValueOrDefault(learnerId),
                ReportableTargetCount = mastered + developing + notMet,
                MasteredCount = mastered,
                DevelopingCount = developing,
                NotMetCount = notMet,
                ObservationCount = mine.Sum(r => r.Count),

                // A date, never a timestamp. "Has not worked on this for two weeks" is a teaching
                // fact; "was answering at 23:41" is not the school's business (§9.5).
                LastActiveOn = last is { } d ? DateOnly.FromDateTime(d) : null
            };
        })
        .OrderByDescending(r => r.ReportableTargetCount)
        .ToList();
    }

    // ─────────────────────────────────────────────────────────── guardian

    public async Task<GuardianReportDto?> GetGuardianReportAsync(
        Guid learnerUserId, Guid viewerUserId, Guid langId,
        bool viewerIsPlatformAdmin = false, CancellationToken cancellationToken = default)
    {
        var scope = await _access.ResolveAsync(
            viewerUserId, learnerUserId, viewerIsPlatformAdmin, cancellationToken);

        // A guardian whose link is unverified, revoked, or lacks ViewProgress gets exactly what a
        // stranger gets. Null rather than an empty report: "you may not see this" and "there is
        // nothing to see" are different answers and a portal must be able to say which.
        if (scope.Basis is not (AccessBasis.Guardian or AccessBasis.Self or AccessBasis.PlatformAdmin))
            return null;

        await ProjectAsync([learnerUserId], cancellationToken);

        var rule = await ActiveRuleAsync(cancellationToken);
        if (rule is null) return null;

        var consent = await _dbContext.GuardianLinks
            .AsNoTracking()
            .Where(g => g.GuardianUserId == viewerUserId && g.LearnerUserId == learnerUserId
                        && g.VerifiedAtUtc != null && g.RevokedAtUtc == null)
            .Select(g => g.ConsentScope)
            .FirstOrDefaultAsync(cancellationToken);

        var learnerName = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => p.UserId == learnerUserId)
            .Select(p => p.FullName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == learnerUserId).Select(u => u.UserName)
                .FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        // A guardian sees the whole child, so no org filter — the relationship is with the learner
        // rather than with one school's enrolment.
        var rows = await TallyScopedAsync([learnerUserId], null, rule.MinStrength, cancellationToken);

        var statements = await StatementsAsync(
            rows.Select(r => r.TargetId).Distinct().ToList(), langId, cancellationToken);

        var strengths = new List<PlainClaimDto>();
        var gaps = new List<PlainClaimDto>();
        var notYet = 0;

        foreach (var row in rows)
        {
            var (estimate, low, _) = WilsonInterval.ForStorage(row.Correct, row.Count);
            var state = rule.Decide(row.Count, estimate, low);

            var claim = new PlainClaimDto
            {
                TargetId = row.TargetId,
                Statement = statements.GetValueOrDefault(row.TargetId) ?? string.Empty,
                State = state,

                // The count, never the estimate. It is the part a parent can actually reason about,
                // and it cannot be mistaken for a grade (§18.2).
                ObservationCount = row.Count
            };

            switch (state)
            {
                case MasteryState.Mastered: strengths.Add(claim); break;
                case MasteryState.NotMet:
                case MasteryState.Developing: gaps.Add(claim); break;
                default: notYet++; break;
            }
        }

        var last = rows.Count == 0 ? (DateTime?)null : rows.Max(r => r.LastObservedAtUtc);

        return new GuardianReportDto
        {
            LearnerId = learnerUserId,
            LearnerName = learnerName,
            ConsentScope = scope.Basis == AccessBasis.Guardian ? consent : GuardianConsentScope.ViewProgress,
            Strengths = strengths.OrderBy(c => c.Statement).ToList(),

            // Weakest first: the gap with the most evidence behind it is the one a parent can act
            // on with most confidence.
            Gaps = gaps.OrderByDescending(c => c.ObservationCount).ThenBy(c => c.Statement).ToList(),
            NotYetObservedCount = notYet,
            ObservationCount = rows.Sum(r => r.Count),
            LastActiveOn = last is { } d ? DateOnly.FromDateTime(d) : null
        };
    }

    // ─────────────────────────────────────────────────────────── plumbing

    /// <summary>One learner's admitted observations on one target, within a scope.</summary>
    private sealed record ScopedTally(
        Guid LearnerId, Guid TargetId, int Count, int Correct, DateTime LastObservedAtUtc);

    /// <summary>
    /// Tallies observations for a set of learners, optionally restricted to the organizations that
    /// own the evidence.
    ///
    /// <para><b>The org filter joins to the response, not to the observation.</b>
    /// <c>LearnerResponse.OrgId</c> is the denormalised scoping column the evidence layer has
    /// carried since Phase 0 for exactly this, and it records which organization's enrolment the
    /// work was done under. Passing null means "everything", which is only ever used for a viewer
    /// entitled to the whole learner.</para>
    /// </summary>
    private async Task<List<ScopedTally>> TallyScopedAsync(
        List<Guid> learnerIds,
        List<Guid>? orgIds,
        Domain.Evidence.EvidenceStrength minStrength,
        CancellationToken cancellationToken)
    {
        var query =
            from o in _dbContext.Observations.AsNoTracking()
            where learnerIds.Contains(o.LearnerId)
                  && o.ExcludedAtUtc == null
                  && o.Strength >= minStrength
                  && o.Outcome != ObservationOutcome.NoResponse
            join r in _dbContext.LearnerResponses.AsNoTracking()
                on o.LearnerResponseId equals r.Id
            where orgIds == null || (r.OrgId != null && orgIds.Contains(r.OrgId.Value))
            select new { o.LearnerId, o.TargetId, o.Outcome, r.OccurredAtUtc };

        var grouped = await query
            .GroupBy(x => new { x.LearnerId, x.TargetId })
            .Select(g => new ScopedTally(
                g.Key.LearnerId,
                g.Key.TargetId,
                g.Count(),
                g.Count(x => x.Outcome == ObservationOutcome.Correct),
                g.Max(x => x.OccurredAtUtc)))
            .ToListAsync(cancellationToken);

        return grouped;
    }

    private async Task<Dictionary<Guid, string>> StatementsAsync(
        List<Guid> targetIds, Guid langId, CancellationToken cancellationToken) =>
        await _dbContext.LearningTargetTranslations
            .AsNoTracking()
            .Where(t => targetIds.Contains(t.TargetId) && t.LangId == langId)
            .ToDictionaryAsync(t => t.TargetId, t => t.Statement, cancellationToken);

    private Task<MasteryRule?> ActiveRuleAsync(CancellationToken cancellationToken) =>
        _dbContext.MasteryRules
            .AsNoTracking()
            .Where(r => r.PublishedAtUtc != null && r.RetiredAtUtc == null)
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
}
