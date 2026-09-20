using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Organizations.Models;
using Share7.Application.Progress.Models;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Evidence;
using Share7.Domain.Organizations;
using Share7.Domain.Structure;
using Share7.Infrastructure.Measurement;
using Share7.Infrastructure.Organizations;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Organizations — Phase 4 of the educational rebuild.
///
/// <para><b>The subject under test is what an organization cannot see.</b> Everything else here is
/// bookkeeping: five tables, a join and a scope filter. The part that has to be right is that a
/// school which adopts Share7 gets the work done under its own enrolment and <i>nothing
/// retroactively</i> — a learner who used the platform privately before their school arrived does
/// not hand that history over by being added to a class.</para>
///
/// <para>See <c>Docs/EducationalArchitecture.md</c> §9, §17.4, §18 and §19.</para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class OrganizationScopingTests
{
    private readonly SqlServerFixture _fixture;

    public OrganizationScopingTests(SqlServerFixture fixture) => _fixture = fixture;

    // ───────────────────────────────────────────── the rule that matters

    /// <summary>
    /// <b>The B2B2C case, and the reason visibility is scoped by enrolment rather than by
    /// learner.</b> A child does homework of their own accord in March. In September their school
    /// adopts Share7 and puts them in a class. The school must see September onward and must not
    /// acquire March — and the mechanism that delivers it is one column, not a feature.
    /// </summary>
    [Fact]
    public async Task A_school_does_not_acquire_the_work_a_learner_did_before_it_arrived()
    {
        await using var context = _fixture.CreateContext();

        var (learnerId, path) = await ReadyLessonAsync(context);

        // March: personal play. No organization owns the enrolment it was done under.
        await AnswerEverythingAsync(context, learnerId, path, correct: true);

        var personalObservations = await context.Observations
            .AsNoTracking()
            .CountAsync(o => o.LearnerId == learnerId);

        Assert.True(personalObservations > 0, "the learner must have private evidence to withhold");

        // September: a school, a class, and the learner joins it.
        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: learnerId);

        var teacherId = await TestData.CreateUserAsync(context);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027", await FirstCurriculumVersionAsync(context), null),
            LanguageIds.English, teacherId);

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(teacherId, CohortRole.Teacher));

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(learnerId, CohortRole.Learner));

        // The teacher can see the learner at all...
        var scope = await AccessOf(context).ResolveAsync(teacherId, learnerId);

        Assert.Equal(AccessBasis.Teacher, scope.Basis);
        Assert.False(scope.AllEnrollments);

        // ...and sees none of March, because none of it was done under the school's enrolment.
        var report = await ReportingOf(context)
            .GetCohortLearnersAsync(cohort.Id, teacherId);

        var row = Assert.Single(report);

        Assert.Equal(learnerId, row.LearnerId);
        Assert.Equal(0, row.ObservationCount);
        Assert.Equal(0, row.ReportableTargetCount);
        Assert.Null(row.LastActiveOn);

        // The evidence is still there. It just is not the school's.
        Assert.Equal(personalObservations, await context.Observations
            .AsNoTracking()
            .CountAsync(o => o.LearnerId == learnerId));
    }

    /// <summary>
    /// A platform administrator opening a school's class report reads the school's report, not a
    /// wider one.
    ///
    /// <para><b>Found over HTTP, not here.</b> The original version passed the viewer's own scope
    /// into the tally, so a platform admin — the only viewer the superadmin console ever has — got
    /// an unfiltered one and saw the learner's pre-adoption work in a school's class summary.
    /// Every unit test read the report as a teacher or an org admin and so none of them noticed.
    /// The rule it violated: <b>who is reading decides whether the report opens, never what is in
    /// it.</b></para>
    /// </summary>
    [Fact]
    public async Task A_platform_admin_reads_the_schools_report_and_not_a_wider_one()
    {
        await using var context = _fixture.CreateContext();

        var (learnerId, path) = await ReadyLessonAsync(context);

        // Private play, before any school exists.
        await AnswerEverythingAsync(context, learnerId, path, correct: true);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: learnerId);

        var teacherId = await TestData.CreateUserAsync(context);
        var platformAdminId = await TestData.CreateUserAsync(context);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027",
                await FirstCurriculumVersionAsync(context), null),
            LanguageIds.English, teacherId);

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(teacherId, CohortRole.Teacher));
        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(learnerId, CohortRole.Learner));

        var asTeacher = await ReportingOf(context).GetCohortReportAsync(
            cohort.Id, teacherId, LanguageIds.English);

        var asPlatformAdmin = await ReportingOf(context).GetCohortReportAsync(
            cohort.Id, platformAdminId, LanguageIds.English, viewerIsPlatformAdmin: true);

        Assert.Equal(0, asTeacher.ObservationCount);
        Assert.Equal(asTeacher.ObservationCount, asPlatformAdmin.ObservationCount);
        Assert.Equal(asTeacher.Suppression, asPlatformAdmin.Suppression);

        var teacherRows = await ReportingOf(context).GetCohortLearnersAsync(cohort.Id, teacherId);
        var adminRows = await ReportingOf(context).GetCohortLearnersAsync(
            cohort.Id, platformAdminId, viewerIsPlatformAdmin: true);

        Assert.Equal(
            Assert.Single(teacherRows).ObservationCount,
            Assert.Single(adminRows).ObservationCount);
    }

    /// <summary>
    /// The same learner, the same school, after the school enrols them in the curriculum the work
    /// belongs to. Now the work <i>is</i> theirs to see — because the enrolment stamps the response
    /// as it is written, not because anybody granted a permission.
    /// </summary>
    [Fact]
    public async Task Work_done_under_a_school_enrolment_is_visible_to_that_school()
    {
        await using var context = _fixture.CreateContext();

        var (learnerId, path) = await ReadyLessonAsync(context);
        var teacherId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: teacherId);

        var versionId = await VersionOfLessonAsync(context, path.LessonId);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027", versionId, null),
            LanguageIds.English, teacherId);

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(teacherId, CohortRole.Teacher));

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(learnerId, CohortRole.Learner));

        // Now the work happens, and the recorder stamps it from the enrolment.
        await AnswerEverythingAsync(context, learnerId, path, correct: true);

        var stamped = await context.LearnerResponses
            .AsNoTracking()
            .CountAsync(r => r.LearnerId == learnerId && r.OrgId == org.Id);

        Assert.True(stamped > 0, "responses must carry the owning organization");

        var report = await ReportingOf(context).GetCohortLearnersAsync(cohort.Id, teacherId);
        var row = Assert.Single(report);

        Assert.True(row.ObservationCount > 0);
        Assert.NotNull(row.LastActiveOn);
    }

    /// <summary>
    /// A teacher of one class has no standing over another, however senior their global role. The
    /// platform's flat "Teacher" claim says nothing about <i>whose</i> children, which is the whole
    /// reason org membership exists beside it (§9.1).
    /// </summary>
    [Fact]
    public async Task A_teacher_of_one_class_cannot_see_a_learner_in_another()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var strangerId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: teacherId);

        var versionId = await FirstCurriculumVersionAsync(context);

        var mine = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6A", "2026/2027", versionId, null),
            LanguageIds.English, teacherId);

        var theirs = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027", versionId, null),
            LanguageIds.English, teacherId);

        await CohortsOf(context).AddMemberAsync(
            mine.Id, new AddCohortMemberRequest(teacherId, CohortRole.Teacher));

        await CohortsOf(context).AddMemberAsync(
            theirs.Id, new AddCohortMemberRequest(strangerId, CohortRole.Learner));

        var scope = await AccessOf(context).ResolveAsync(teacherId, strangerId);

        Assert.Equal(AccessBasis.None, scope.Basis);
        Assert.False(scope.IsPermitted);
    }

    /// <summary>
    /// An unverified guardian link grants nothing. Anyone can type a child's user name, which is
    /// the entire reason verification is a separate act from creation (§18.3).
    /// </summary>
    [Fact]
    public async Task An_unverified_guardian_link_grants_nothing()
    {
        await using var context = _fixture.CreateContext();

        var learnerId = await TestData.CreateUserAsync(context);
        var guardianId = await TestData.CreateUserAsync(context);

        var link = await GuardiansOf(context).CreateAsync(new CreateGuardianLinkRequest(
            guardianId, learnerId, GuardianRelationship.Parent,
            GuardianConsentScope.ViewProgress | GuardianConsentScope.CalibrationUse));

        Assert.Null(link.VerifiedAtUtc);

        Assert.Equal(AccessBasis.None,
            (await AccessOf(context).ResolveAsync(guardianId, learnerId)).Basis);

        Assert.Null(await ReportingOf(context)
            .GetGuardianReportAsync(learnerId, guardianId, LanguageIds.English));

        // Verified, it works — and the report still carries no overall figure, because there is no
        // field on it that one could be written into.
        await GuardiansOf(context).VerifyAsync(link.Id, verifiedByUserId: learnerId);

        Assert.Equal(AccessBasis.Guardian,
            (await AccessOf(context).ResolveAsync(guardianId, learnerId)).Basis);

        Assert.NotNull(await ReportingOf(context)
            .GetGuardianReportAsync(learnerId, guardianId, LanguageIds.English));
    }

    /// <summary>
    /// Revocation is a timestamp and takes effect immediately. The row stays, because the period
    /// during which data was legitimately shared is exactly what an audit has to reconstruct.
    /// </summary>
    [Fact]
    public async Task Revoking_a_guardian_link_ends_access_without_ending_the_record()
    {
        await using var context = _fixture.CreateContext();

        var learnerId = await TestData.CreateUserAsync(context);
        var guardianId = await TestData.CreateUserAsync(context);

        var link = await GuardiansOf(context).CreateAsync(new CreateGuardianLinkRequest(
            guardianId, learnerId, GuardianRelationship.Parent, GuardianConsentScope.ViewProgress));

        await GuardiansOf(context).VerifyAsync(link.Id, verifiedByUserId: learnerId);
        await GuardiansOf(context).RevokeAsync(link.Id);

        Assert.Equal(AccessBasis.None,
            (await AccessOf(context).ResolveAsync(guardianId, learnerId)).Basis);

        var stored = await context.GuardianLinks.AsNoTracking().FirstAsync(g => g.Id == link.Id);

        Assert.NotNull(stored.RevokedAtUtc);
        Assert.NotNull(stored.VerifiedAtUtc);
    }

    /// <summary>
    /// <b>Consent is per scope, not per relationship.</b> A guardian who may read a report has not
    /// thereby agreed to their child's examination result joining a calibration sample, and a
    /// boolean set at signup could not tell the two apart (§18.3).
    /// </summary>
    [Fact]
    public async Task Consent_to_read_a_report_is_not_consent_to_calibration()
    {
        await using var context = _fixture.CreateContext();

        var learnerId = await TestData.CreateUserAsync(context);
        var guardianId = await TestData.CreateUserAsync(context);

        var link = await GuardiansOf(context).CreateAsync(new CreateGuardianLinkRequest(
            guardianId, learnerId, GuardianRelationship.Parent, GuardianConsentScope.ViewProgress));

        await GuardiansOf(context).VerifyAsync(link.Id, verifiedByUserId: learnerId);

        var stored = await context.GuardianLinks.AsNoTracking().FirstAsync(g => g.Id == link.Id);

        Assert.True(stored.Permits(GuardianConsentScope.ViewProgress));
        Assert.False(stored.Permits(GuardianConsentScope.CalibrationUse));
        Assert.False(stored.Permits(GuardianConsentScope.ManageEnrollment));
    }

    // ───────────────────────────────────────────────── reporting shape

    /// <summary>
    /// <b>A class of four gets no percentage.</b> Both because a share over four learners is
    /// statistically meaningless and because it names a child: in a cohort that small, "one learner
    /// has not met this" is identifying to anyone who knows the other three (§19.2).
    /// </summary>
    [Fact]
    public async Task A_cohort_below_the_small_cell_floor_reports_no_share()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: teacherId);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "Tiny", "2026/2027",
                await FirstCurriculumVersionAsync(context), null),
            LanguageIds.English, teacherId);

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(teacherId, CohortRole.Teacher));

        for (var i = 0; i < EducationalReportingService.SmallCellThreshold - 1; i++)
        {
            await CohortsOf(context).AddMemberAsync(
                cohort.Id,
                new AddCohortMemberRequest(await TestData.CreateUserAsync(context), CohortRole.Learner));
        }

        var report = await ReportingOf(context)
            .GetCohortReportAsync(cohort.Id, teacherId, LanguageIds.English);

        Assert.Equal(EducationalReportingService.SmallCellThreshold, report.SmallCellThreshold);
        Assert.NotEqual(SuppressionReason.None, report.Suppression);

        // Every row that does come back carries a stated reason rather than a blank, so a reader
        // never learns that blank means zero.
        Assert.All(report.Targets, row =>
        {
            Assert.NotEqual(SuppressionReason.None, row.Suppression);
            Assert.Null(row.MasteredShare);
        });
    }

    /// <summary>
    /// A cohort report a viewer has no relationship to is empty and says why — not a 500, and not
    /// somebody else's data.
    /// </summary>
    [Fact]
    public async Task A_stranger_reading_a_cohort_report_gets_nothing_and_is_told_why()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var strangerId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: teacherId);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027",
                await FirstCurriculumVersionAsync(context), null),
            LanguageIds.English, teacherId);

        var report = await ReportingOf(context)
            .GetCohortReportAsync(cohort.Id, strangerId, LanguageIds.English);

        Assert.Equal(SuppressionReason.OutOfScope, report.Suppression);
        Assert.Empty(report.Targets);
        Assert.Equal(0, report.ObservationCount);

        Assert.Empty(await ReportingOf(context).GetCohortLearnersAsync(cohort.Id, strangerId));
    }

    // ────────────────────────────────────────────────── the org's bank

    /// <summary>
    /// <b>An organization's own bank cannot produce exam-grade evidence.</b> This is the teacher
    /// authoring safety mechanism: whatever a school writes on a Tuesday afternoon, however
    /// controlled the sitting, it is capped at practice class and cannot move a national exam
    /// projection until somebody qualified has reviewed it (§10.2, §17.2).
    /// </summary>
    [Fact]
    public async Task An_organizations_bank_is_capped_below_exam_grade_from_the_moment_it_exists()
    {
        await using var context = _fixture.CreateContext();

        var actingUserId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId);

        Assert.NotNull(org.ItemBankId);

        var bank = await context.ItemBanks.AsNoTracking().FirstAsync(b => b.Id == org.ItemBankId);

        Assert.Equal(ItemBankOwnerScope.Organization, bank.OwnerScope);
        Assert.Equal(EvidenceStrength.Practice, bank.MaxEvidenceStrength);
        Assert.True(bank.MaxEvidenceStrength < EvidenceStrength.Assessment);

        // One school's class is not a calibration sample; pooling it would move every other
        // learner's item difficulties.
        Assert.False(bank.PoolsStatisticsGlobally);
    }

    // ─────────────────────────────────────────────────── memberships

    /// <summary>
    /// Leaving a cohort ends the enrolment and deletes neither. The evidence collected while the
    /// learner was in the class is real, and stays attached to the enrolment that collected it.
    /// </summary>
    [Fact]
    public async Task Leaving_a_cohort_ends_the_enrolment_and_deletes_nothing()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var learnerId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: teacherId);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027",
                await FirstCurriculumVersionAsync(context), null),
            LanguageIds.English, teacherId);

        var member = await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(learnerId, CohortRole.Learner));

        Assert.NotNull(member.EnrollmentId);

        await CohortsOf(context).RemoveMemberAsync(member.Id);

        var membership = await context.CohortMemberships.AsNoTracking().FirstAsync(m => m.Id == member.Id);
        var enrollment = await context.Enrollments.AsNoTracking()
            .FirstAsync(e => e.Id == member.EnrollmentId!.Value);

        Assert.NotNull(membership.LeftAtUtc);
        Assert.NotNull(enrollment.EndedAtUtc);
        Assert.Equal(org.Id, enrollment.OwnerOrgId);
    }

    /// <summary>
    /// A school adding a learner to a class must not overwrite what that learner says they are
    /// studying. The org enrolment is never primary — and the filtered unique index would refuse it
    /// in any case, which is the schema saying the same thing.
    /// </summary>
    [Fact]
    public async Task A_school_enrolment_never_becomes_the_learners_primary_one()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var learnerId = await TestData.CreateUserAsync(context);
        var versionId = await FirstCurriculumVersionAsync(context);

        var now = DateTime.UtcNow;

        context.Enrollments.Add(new Enrollment
        {
            Id = Guid.NewGuid(),
            LearnerId = learnerId,
            CurriculumVersionId = versionId,
            Source = EnrollmentSource.SelfDeclared,
            IsPrimary = true,
            StartedAtUtc = now,
            CreatedAtUtc = now
        });

        await context.SaveChangesAsync();

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            actingUserId: teacherId);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027", versionId, null),
            LanguageIds.English, teacherId);

        var member = await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(learnerId, CohortRole.Learner));

        var provisioned = await context.Enrollments.AsNoTracking()
            .FirstAsync(e => e.Id == member.EnrollmentId!.Value);

        Assert.False(provisioned.IsPrimary);
        Assert.Equal(EnrollmentSource.Organization, provisioned.Source);

        Assert.Equal(1, await context.Enrollments
            .AsNoTracking()
            .CountAsync(e => e.LearnerId == learnerId && e.IsPrimary && e.EndedAtUtc == null));
    }

    /// <summary>
    /// An administrator of a district administers its schools. Requiring a separate grant per
    /// school would make the hierarchy decorative — and "everything under this org" is the query
    /// every org-scoped report starts from.
    /// </summary>
    [Fact]
    public async Task An_administrator_of_a_district_administers_its_schools()
    {
        await using var context = _fixture.CreateContext();

        var adminId = await TestData.CreateUserAsync(context);

        var district = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"dist-{Guid.NewGuid():N}"[..20], "Test District",
                OrganizationKind.District, "EG", null),
            adminId);

        var school = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", district.Id),
            adminId);

        await OrganizationsOf(context).GrantAsync(
            district.Id, new GrantMembershipRequest(adminId, OrgRole.OrgAdmin), adminId);

        Assert.True(await AccessOf(context).HasOrgRoleAsync(adminId, school.Id, OrgRole.OrgAdmin));
        Assert.True(await AccessOf(context).HasOrgRoleAsync(adminId, district.Id, OrgRole.OrgAdmin));

        var subtree = await OrganizationsOf(context).GetSubtreeIdsAsync(district.Id);

        Assert.Contains(district.Id, subtree);
        Assert.Contains(school.Id, subtree);

        // And never upwards: a head of one school does not administer the district above it.
        var schoolHeadId = await TestData.CreateUserAsync(context);

        await OrganizationsOf(context).GrantAsync(
            school.Id, new GrantMembershipRequest(schoolHeadId, OrgRole.OrgAdmin), adminId);

        Assert.False(await AccessOf(context).HasOrgRoleAsync(schoolHeadId, district.Id, OrgRole.OrgAdmin));
    }

    /// <summary>
    /// A revoked membership is kept and stops working. A report about last term has to be able to
    /// name who taught it; an access audit has to resolve against the grant that permitted it.
    /// </summary>
    [Fact]
    public async Task A_revoked_membership_stops_working_and_stays_on_the_record()
    {
        await using var context = _fixture.CreateContext();

        var adminId = await TestData.CreateUserAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            adminId);

        var membership = await OrganizationsOf(context).GrantAsync(
            org.Id, new GrantMembershipRequest(adminId, OrgRole.OrgAdmin), adminId);

        Assert.True(await AccessOf(context).HasOrgRoleAsync(adminId, org.Id, OrgRole.OrgAdmin));

        await OrganizationsOf(context).RevokeAsync(membership.Id);

        Assert.False(await AccessOf(context).HasOrgRoleAsync(adminId, org.Id, OrgRole.OrgAdmin));

        var stored = await context.Memberships.AsNoTracking().FirstAsync(m => m.Id == membership.Id);

        Assert.Equal(MembershipStatus.Revoked, stored.Status);
        Assert.NotNull(stored.RevokedAtUtc);

        // Re-granting revives the same row rather than stacking a second grant.
        var again = await OrganizationsOf(context).GrantAsync(
            org.Id, new GrantMembershipRequest(adminId, OrgRole.OrgAdmin), adminId);

        Assert.Equal(membership.Id, again.Id);

        Assert.Equal(1, await context.Memberships
            .AsNoTracking()
            .CountAsync(m => m.OrgId == org.Id && m.UserId == adminId && m.Role == OrgRole.OrgAdmin));
    }

    // ─────────────────────────────────────────────────── assignments

    /// <summary>
    /// <b>Naming an assignment is a claim, not an instruction.</b> The play boundary checks the
    /// caller is actually on the cohort's roster, because otherwise any client could credit its
    /// work to any class (§17.4).
    /// </summary>
    [Fact]
    public async Task An_assignment_set_for_another_class_is_refused_at_the_play_boundary()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var outsiderId = await TestData.CreateUserAsync(context);
        var (memberId, path) = await ReadyLessonAsync(context);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            teacherId);

        var cohort = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "6B", "2026/2027",
                await VersionOfLessonAsync(context, path.LessonId), null),
            LanguageIds.English, teacherId);

        await CohortsOf(context).AddMemberAsync(
            cohort.Id, new AddCohortMemberRequest(memberId, CohortRole.Learner));

        var assignment = await CohortsOf(context).CreateAssignmentAsync(
            new CreateAssignmentRequest(cohort.Id, "Fractions homework", path.LessonId, null, null, false),
            LanguageIds.English, teacherId);

        var resolver = new Share7.Infrastructure.Play.PlaySelectionResolver(
            context, new Share7.Infrastructure.Progression.LevelService(context));

        var refused = await resolver.ResolveAsync(outsiderId, new Application.Play.Models.PlaySelectionRequest
        {
            GameId = path.GameId,
            ContextKey = Domain.Play.PlayContextTokens.Assignment,
            AssignmentId = assignment.Id
        });

        Assert.False(refused.Succeeded);

        var allowed = await resolver.ResolveAsync(memberId, new Application.Play.Models.PlaySelectionRequest
        {
            GameId = path.GameId,
            ContextKey = Domain.Play.PlayContextTokens.Assignment,
            AssignmentId = assignment.Id
        });

        Assert.True(allowed.Succeeded, string.Join("; ", allowed.Errors ?? []));
        Assert.Equal(assignment.Id, allowed.Value!.AssignmentId);

        // An assignment is curriculum work with a teacher's name on it, and settles as such. What
        // it must not do is buy its evidence a stronger claim, and it does not: strength is the
        // contract's business, not the settlement's.
        Assert.True(allowed.Value.Policy.AffectsMastery);
    }

    /// <summary>
    /// The context and its id go together in both directions. An assignment id on a curriculum run
    /// would have been credited to nothing, so it is refused rather than ignored — the same rule
    /// the event context already follows.
    /// </summary>
    [Fact]
    public async Task An_assignment_context_and_an_assignment_id_are_required_of_each_other()
    {
        await using var context = _fixture.CreateContext();

        var (learnerId, path) = await ReadyLessonAsync(context);
        var resolver = new Share7.Infrastructure.Play.PlaySelectionResolver(
            context, new Share7.Infrastructure.Progression.LevelService(context));

        var noId = await resolver.ResolveAsync(learnerId, new Application.Play.Models.PlaySelectionRequest
        {
            GameId = path.GameId,
            ContextKey = Domain.Play.PlayContextTokens.Assignment
        });

        Assert.False(noId.Succeeded);

        var strayId = await resolver.ResolveAsync(learnerId, new Application.Play.Models.PlaySelectionRequest
        {
            GameId = path.GameId,
            ContextKey = Domain.Play.PlayContextTokens.Curriculum,
            AssignmentId = Guid.NewGuid()
        });

        Assert.False(strayId.Succeeded);
    }

    // ──────────────────────────────────────────────────── overlays

    /// <summary>
    /// An overlay changes what a class is shown without touching what anybody else sees. The
    /// official tree is identical afterwards, which is the property the whole mechanism exists for
    /// (§17.2).
    /// </summary>
    [Fact]
    public async Task An_overlay_hides_a_node_for_its_cohort_and_for_nobody_else()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var (_, path) = await ReadyLessonAsync(context);

        var lessonNode = await context.CurriculumNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == path.LessonId);

        Assert.NotNull(lessonNode);

        var parentId = lessonNode!.ParentNodeId!.Value;

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            teacherId);

        var overlay = await OverlaysOf(context).CreateAsync(
            new CreateOverlayRequest(org.Id, lessonNode.CurriculumVersionId,
                $"ov-{Guid.NewGuid():N}", "Our order", "We cover this in term 2."),
            LanguageIds.English, teacherId);

        await OverlaysOf(context).AddEditAsync(
            overlay.Id,
            new AddOverlayEditRequest(OverlayEditKind.Hide, path.LessonId, null, null, null, null, null),
            LanguageIds.English);

        await OverlaysOf(context).PublishAsync(overlay.Id, LanguageIds.English);

        var plain = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "No overlay", "2026/2027", lessonNode.CurriculumVersionId, null),
            LanguageIds.English, teacherId);

        var overlaid = await CohortsOf(context).CreateAsync(
            new CreateCohortRequest(org.Id, "Overlaid", "2026/2027", lessonNode.CurriculumVersionId, null),
            LanguageIds.English, teacherId);

        var overlaidEntity = await context.Cohorts.FirstAsync(c => c.Id == overlaid.Id);
        overlaidEntity.OverlayId = overlay.Id;
        await context.SaveChangesAsync();

        var withoutOverlay = await OverlaysOf(context)
            .ResolveChildrenAsync(plain.Id, parentId, LanguageIds.English);

        var withOverlay = await OverlaysOf(context)
            .ResolveChildrenAsync(overlaid.Id, parentId, LanguageIds.English);

        Assert.Contains(withoutOverlay, n => n.NodeId == path.LessonId);
        Assert.DoesNotContain(withOverlay, n => n.NodeId == path.LessonId);

        // And the official tree is untouched — the overlay is a diff, never a mutation.
        Assert.NotNull(await context.CurriculumNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == path.LessonId && n.RetiredAtUtc == null));
    }

    /// <summary>
    /// A published overlay is what a class actually reads, so it is versioned rather than edited —
    /// the same rule a published blueprint follows, and for the same reason.
    /// </summary>
    [Fact]
    public async Task A_published_overlay_cannot_be_edited_in_place()
    {
        await using var context = _fixture.CreateContext();

        var teacherId = await TestData.CreateUserAsync(context);
        var (_, path) = await ReadyLessonAsync(context);

        var lessonNode = await context.CurriculumNodes.AsNoTracking().FirstAsync(n => n.Id == path.LessonId);

        var org = await OrganizationsOf(context).CreateAsync(
            new CreateOrganizationRequest($"school-{Guid.NewGuid():N}"[..20], "Test School",
                OrganizationKind.School, "EG", null),
            teacherId);

        var overlay = await OverlaysOf(context).CreateAsync(
            new CreateOverlayRequest(org.Id, lessonNode.CurriculumVersionId,
                $"ov-{Guid.NewGuid():N}", "Our order", null),
            LanguageIds.English, teacherId);

        await OverlaysOf(context).AddEditAsync(
            overlay.Id,
            new AddOverlayEditRequest(OverlayEditKind.Hide, path.LessonId, null, null, null, null, null),
            LanguageIds.English);

        await OverlaysOf(context).PublishAsync(overlay.Id, LanguageIds.English);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OverlaysOf(context).AddEditAsync(
                overlay.Id,
                new AddOverlayEditRequest(OverlayEditKind.Hide, path.LessonId, null, null, null, null, null),
                LanguageIds.English));
    }

    // ───────────────────────────────────────────────────── erasure

    /// <summary>
    /// <b>A guardian link names the user twice.</b> Erasing the learner must take the row even
    /// though the erased person is in the second column, or an erased child stays named in their
    /// parent's account — the same disclosure by a different door.
    /// </summary>
    [Fact]
    public async Task Erasing_a_learner_removes_the_guardian_link_that_names_them_second()
    {
        await using var context = _fixture.CreateContext();

        var learnerId = await TestData.CreateUserAsync(context);
        var guardianId = await TestData.CreateUserAsync(context);

        await GuardiansOf(context).CreateAsync(new CreateGuardianLinkRequest(
            guardianId, learnerId, GuardianRelationship.Parent, GuardianConsentScope.ViewProgress));

        Assert.Equal(1, await context.GuardianLinks.CountAsync(g => g.LearnerUserId == learnerId));

        await Share7.Infrastructure.Users.UserOwnedData.PurgeAsync(context, learnerId);

        Assert.Equal(0, await context.GuardianLinks.CountAsync(
            g => g.LearnerUserId == learnerId || g.GuardianUserId == learnerId));
    }

    // ───────────────────────────────────────────────────── plumbing

    private static OrganizationService OrganizationsOf(ApplicationDbContext context) => new(context);

    private static CohortService CohortsOf(ApplicationDbContext context) => new(context);

    private static GuardianService GuardiansOf(ApplicationDbContext context) => new(context);

    private static EducationalAccessService AccessOf(ApplicationDbContext context) => new(context);

    private static CurriculumOverlayService OverlaysOf(ApplicationDbContext context) => new(context);

    private static EducationalReportingService ReportingOf(ApplicationDbContext context) =>
        new(context, AccessOf(context), Projector(context));

    private static ObservationProjector Projector(ApplicationDbContext context) =>
        new(context, NullLogger<ObservationProjector>.Instance);

    private static async Task<Guid> FirstCurriculumVersionAsync(ApplicationDbContext context) =>
        await context.CurriculumVersions.Select(v => v.Id).FirstAsync();

    private static async Task<Guid> VersionOfLessonAsync(ApplicationDbContext context, Guid lessonId) =>
        await context.CurriculumNodes
            .Where(n => n.Id == lessonId)
            .Select(n => n.CurriculumVersionId)
            .FirstAsync();

    private static async Task<(Guid UserId, CurriculumPathFixture Path)> ReadyLessonAsync(
        ApplicationDbContext context, int questionCount = 8)
    {
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, questionCount);
        await context.UnlockLessonAsync(userId, path);

        // The node tree is a projection of the legacy typed tables, and nothing in a test fixture
        // runs it. Everything org-scoped reads the projection — the enrolment stamp resolves a
        // node's curriculum version through it, and an overlay names nodes — so a fixture that
        // skipped this would be testing against a tree the product does not have.
        await new Share7.Infrastructure.Structure.CurriculumProjector(context).SyncAsync();

        return (userId, path);
    }

    private static async Task AnswerEverythingAsync(
        ApplicationDbContext context, Guid userId, CurriculumPathFixture path, bool correct)
    {
        var questions = await context.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == path.LessonId && q.LangId == LanguageIds.English && q.IsActive)
            .Select(q => new
            {
                q.Id,
                q.CorrectChoiceId,
                WrongChoiceId = q.Choices
                    .Where(c => c.Id != q.CorrectChoiceId)
                    .Select(c => c.Id)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers =
                [
                    .. questions.Select(q => new SubmittedAnswer
                    {
                        QuestionId = q.Id,
                        ChoiceId = correct ? q.CorrectChoiceId : q.WrongChoiceId
                    })
                ]
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await Projector(context).ProjectForLearnerAsync(userId);
    }
}
