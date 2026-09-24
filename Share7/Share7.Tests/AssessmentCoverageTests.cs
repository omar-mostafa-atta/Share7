using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Application.Competency.Interfaces;
using Share7.Application.Progress.Models;
using Share7.Domain.Assessment;
using Share7.Domain.Competency;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;
using Share7.Domain.Measurement;
using Share7.Infrastructure.Assessment;
using Share7.Infrastructure.Competency;
using Share7.Infrastructure.Measurement;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Assessment and coverage — Phase 3 of the educational rebuild.
/// <para>
/// **The subject under test is what the system refuses to say.** Coverage is arithmetic and the
/// arithmetic is easy; the difficult and valuable part is the set of gates that stop a plausible
/// number reaching a child two months before an examination. A blind spot must fail even when the
/// average passes, a lesson placeholder must contribute nothing however much evidence it carries,
/// and an uncalibrated exam must produce no predicted outcome at all — not a hedged one.
/// </para>
/// <para>See <c>Docs/EducationalArchitecture.md</c> §4, §6 and §20.5.</para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AssessmentCoverageTests
{
    private readonly SqlServerFixture _fixture;

    public AssessmentCoverageTests(SqlServerFixture fixture) => _fixture = fixture;

    // ------------------------------------------------- conditions decide what evidence is worth

    /// <summary>
    /// The correctness fix Phase 3 uncovered. <c>WasAided</c> was recorded on every response from
    /// Phase 0 and read by nothing, so an answer produced with a teacher leaning over the desk
    /// would have counted as exam-grade evidence.
    /// </summary>
    [Fact]
    public void An_aided_answer_cannot_be_exam_grade_however_controlled_everything_else_was()
    {
        var contract = new EvidenceContractVersion
        {
            StrengthWhenControlled = EvidenceStrength.Assessment,
            StrengthOtherwise = EvidenceStrength.Practice,
            RequiresFirstEncounter = true,
            RequiresUnhinted = true,
            RequiresNoRetry = true
        };

        var unaided = contract.StrengthFor(
            isFirstEncounter: true, hintsUsed: 0, retryPermitted: false,
            EvidenceDeliveryMode.Supervised, wasAided: false);

        var aided = contract.StrengthFor(
            isFirstEncounter: true, hintsUsed: 0, retryPermitted: false,
            EvidenceDeliveryMode.Supervised, wasAided: true);

        Assert.Equal(EvidenceStrength.Assessment, unaided);

        // Practice, not Assessment. Everything else about the sitting was perfect and it does not
        // matter: what was measured was the learner plus the help.
        Assert.Equal(EvidenceStrength.Practice, aided);
    }

    // -------------------------------------------------------------- placeholders cover nothing

    /// <summary>
    /// A lesson placeholder carries real evidence and still covers none of an examination.
    /// <para>
    /// This is §20.5 enforced rather than intended. A placeholder is a lesson wearing a target's
    /// clothes; rolling one into a coverage figure would put a completion percentage behind an
    /// exam-readiness claim, which is the precise fabrication the whole architecture exists to
    /// prevent.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_lesson_placeholder_covers_nothing_however_much_evidence_it_carries()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await AnswerEverythingAsync(context, userId, path, correct: true);

        var placeholder = await PlaceholderForAsync(context, path.LessonId);
        var blueprint = await BlueprintOverAsync(context, ("Only area", [placeholder.Id]));

        var projection = await Coverage(context)
            .GetBlueprintCoverageAsync(userId, blueprint, LanguageIds.English);

        Assert.NotNull(projection);
        Assert.Equal(0m, projection!.CoverageRatio);
        Assert.Equal(ProjectionSufficiency.InsufficientCoverage, projection.Sufficiency);

        // And no band, because there is nothing a placeholder could support a band about.
        Assert.Null(projection.ProficiencyBandLow);

        // The gap still names the observations the learner really has. The evidence is real; what
        // it cannot do is answer a question about an examination.
        var gap = Assert.Single(projection.Gaps);
        Assert.True(gap.ObservationCount > 0);
    }

    [Fact]
    public async Task A_blueprint_naming_a_placeholder_cannot_be_published()
    {
        await using var context = _fixture.CreateContext();
        var (_, path) = await ReadyLessonAsync(context);

        var placeholder = await PlaceholderForAsync(context, path.LessonId);
        var blueprint = await BlueprintOverAsync(context, ("Only area", [placeholder.Id]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Authoring(context).PublishBlueprintAsync(blueprint));

        Assert.Contains("placeholder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------- the gates

    /// <summary>
    /// **The condition that matters**, and the reason an average is not enough on its own.
    /// <para>
    /// A learner with excellent evidence on three quarters of a paper and nothing at all on the
    /// remaining quarter has an overall figure that clears any sensible bar, and is about to walk
    /// into an examination with a blind spot nobody told them about. §6.3 puts a floor under every
    /// individual area for exactly this case.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_blind_spot_fails_even_when_the_average_passes()
    {
        await using var context = _fixture.CreateContext();
        var (userId, covered, untouched) = await TwoLessonsOneAnsweredAsync(context);

        var blueprint = await BlueprintOverAsync(
            context,
            ("Covered", [covered]),
            ("Blind spot", [untouched]));

        var projection = await Coverage(context)
            .GetBlueprintCoverageAsync(userId, blueprint, LanguageIds.English);

        Assert.NotNull(projection);

        // Half the paper is fully covered, and it does not save the verdict.
        Assert.True(projection!.CoverageRatio > 0m);
        Assert.Equal(0m, projection.WeakestAreaCoverage);
        Assert.Equal(ProjectionSufficiency.InsufficientCoverage, projection.Sufficiency);

        // Worth-most first, so the list reads as advice.
        var top = projection.Gaps[0];
        Assert.Equal("Blind spot", top.AreaLabel);
        Assert.Equal(CoverageGapKind.NoEvidence, top.Kind);
        Assert.Equal(1, top.Rank);
    }

    /// <summary>
    /// An insufficient projection is a normal, useful response — never an error and never a
    /// degraded number.
    /// </summary>
    [Fact]
    public async Task An_insufficient_projection_returns_gaps_rather_than_a_smaller_number()
    {
        await using var context = _fixture.CreateContext();
        var (userId, covered, untouched) = await TwoLessonsOneAnsweredAsync(context);

        var blueprint = await BlueprintOverAsync(
            context, ("Covered", [covered]), ("Blind spot", [untouched]));

        var projection = await Coverage(context)
            .GetBlueprintCoverageAsync(userId, blueprint, LanguageIds.English);

        Assert.NotNull(projection);

        // No band at all, rather than a cautious one.
        Assert.Null(projection!.ProficiencyBandLow);
        Assert.Null(projection.ProficiencyBandHigh);
        Assert.Null(projection.Confidence);

        // And the actionable payload is present and concrete.
        Assert.NotEmpty(projection.Gaps);
        Assert.All(projection.Gaps, g => Assert.True(g.WeightInExam > 0m));
        Assert.Contains(projection.Gaps, g => g.ObservationsNeeded > 0);

        // The thresholds travel with the verdict, so the gate can explain itself.
        Assert.True(projection.Thresholds.MinAreaCoverageRatio > 0m);
    }

    /// <summary>
    /// Everything about the learner holds and there is still no predicted outcome, because nobody
    /// has collected a single pair of (what we said, what happened).
    /// </summary>
    [Fact]
    public async Task Full_coverage_without_calibration_reports_uncalibrated_and_no_outcome_band()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context, questionCount: 24);

        await AnswerEverythingAsync(context, userId, path, correct: true);

        // Promoted to a real target, because a placeholder can never clear the gate by design.
        var target = await PromoteLessonAsync(context, path.LessonId, "maths.place_value");

        var blueprint = await BlueprintOverAsync(
            context, ("Number", [target]), tune: b =>
            {
                b.MinObservationsOverall = 8;
                b.MinObservationsPerArea = 4;
            });

        var projection = await Coverage(context)
            .GetBlueprintCoverageAsync(userId, blueprint, LanguageIds.English);

        Assert.NotNull(projection);
        Assert.Equal(1m, projection!.CoverageRatio);

        // Uncalibrated, not Sufficient: outputs A and B are available, C is not and cannot be.
        Assert.Equal(ProjectionSufficiency.Uncalibrated, projection.Sufficiency);

        Assert.NotNull(projection.ProficiencyBandLow);
        Assert.NotNull(projection.ProficiencyBandHigh);

        // **No branch in the system can fill these.** Output C needs observed pairs, and reasoning
        // does not produce one.
        Assert.Null(projection.OutcomeBandLow);
        Assert.Null(projection.OutcomeBandHigh);
    }

    /// <summary>
    /// Practice evidence covers nothing exam-facing, and the gap says so in those words rather
    /// than reporting an absence. The learner has done the work; they have not yet done it the way
    /// the examination will ask.
    /// </summary>
    [Fact]
    public async Task Practice_only_evidence_is_reported_as_such_rather_than_as_no_evidence()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        // Answered twice: the second pass is a replay, so every observation on it is practice.
        await AnswerEverythingAsync(context, userId, path, correct: true);
        await AnswerEverythingAsync(context, userId, path, correct: true, requestId: "second");

        var target = await PromoteLessonAsync(context, path.LessonId, "maths.practice_only");

        // Withdraw the first-encounter evidence so only the replays are left, which is the state a
        // learner who has ground a lesson repeatedly is actually in on a target they met long ago.
        await context.Observations
            .Where(o => o.LearnerId == userId && o.Strength == EvidenceStrength.Assessment)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Strength, EvidenceStrength.Practice));

        var blueprint = await BlueprintOverAsync(context, ("Number", [target]));

        var projection = await Coverage(context)
            .GetBlueprintCoverageAsync(userId, blueprint, LanguageIds.English);

        Assert.NotNull(projection);
        Assert.Equal(0m, projection!.CoverageRatio);

        var gap = Assert.Single(projection.Gaps);
        Assert.Equal(CoverageGapKind.PracticeOnly, gap.Kind);
        Assert.Equal(0, gap.ExamLikeObservationCount);
    }

    // ------------------------------------------------- real targets replace placeholders

    /// <summary>
    /// **The recompute line paying for itself, and the first time it does.**
    /// <para>
    /// Re-targeting a lesson's questions is an UPDATE on a join table, and every historical answer
    /// is re-interpreted against the real claim it was always about — without anybody replaying a
    /// lesson. In a system that stored progress as current state this operation does not exist,
    /// because the evidence it needs was overwritten the moment it was recorded (§20.5).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Promoting_a_placeholder_re_interprets_historical_evidence_against_the_real_claim()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await AnswerEverythingAsync(context, userId, path, correct: true);

        var placeholder = await PlaceholderForAsync(context, path.LessonId);

        var before = await context.Observations
            .CountAsync(o => o.LearnerId == userId && o.TargetId == placeholder.Id);

        Assert.True(before > 0, "the learner must have evidence on the placeholder before promotion");

        var report = await Authoringtargets(context).PromoteAsync(new PromoteTargetRequest
        {
            TargetKey = "maths.order_fractions",
            Statements = new Dictionary<Guid, string>
            {
                [LanguageIds.English] = "Can order fractions with unlike denominators",
                [LanguageIds.Arabic] = "يستطيع ترتيب الكسور ذات المقامات المختلفة"
            },
            ReplacesTargetIds = [placeholder.Id],
            MarkReviewed = true
        });

        Assert.Equal(1, report.PlaceholdersSuperseded);
        Assert.True(report.ItemMappingsMoved > 0);

        // Every one of them, rebuilt from responses nobody re-answered.
        Assert.Equal(before, report.ObservationsRebuilt);

        var after = await context.Observations
            .AsNoTracking()
            .CountAsync(o => o.LearnerId == userId && o.TargetId == report.TargetId);

        Assert.Equal(before, after);

        // The old claim stops carrying evidence but stays readable — a measurement taken against
        // it last month was a real measurement and its row still has to resolve.
        Assert.Equal(0, await context.Observations
            .AsNoTracking()
            .CountAsync(o => o.LearnerId == userId && o.TargetId == placeholder.Id));

        var superseded = await context.LearningTargets.AsNoTracking()
            .FirstAsync(t => t.Id == placeholder.Id);

        Assert.Equal(TargetReviewState.Deprecated, superseded.ReviewState);

        Assert.True(await context.LearningTargetEdges.AsNoTracking().AnyAsync(
            e => e.FromTargetId == placeholder.Id
                 && e.ToTargetId == report.TargetId
                 && e.EdgeKind == LearningTargetEdgeKind.SupersededBy));
    }

    /// <summary>
    /// A rebuild must not readmit answers somebody withdrew.
    /// <para>
    /// The exclusion is the one thing on an observation that was never derived — a human looked at
    /// a mis-keyed item and said these do not count. Regenerating over it would silently put fifty
    /// thousand wrong answers back into everybody's measurements, and nothing would report that it
    /// had happened.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Rebuilding_observations_carries_human_exclusions_across()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await AnswerEverythingAsync(context, userId, path, correct: true);

        var placeholder = await PlaceholderForAsync(context, path.LessonId);

        var excludedVersion = await context.Observations
            .AsNoTracking()
            .Where(o => o.LearnerId == userId && o.TargetId == placeholder.Id)
            .Select(o => o.ItemVersionId)
            .FirstAsync();

        var withdrawn = await context.Observations
            .Where(o => o.ItemVersionId == excludedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ExcludedAtUtc, DateTime.UtcNow)
                .SetProperty(o => o.ExclusionReason, ObservationExclusionReason.MisKeyedItem)
                .SetProperty(o => o.ExclusionNote, "key was wrong"));

        Assert.True(withdrawn > 0);

        var report = await Authoringtargets(context).PromoteAsync(new PromoteTargetRequest
        {
            TargetKey = "maths.carried_exclusion",
            Statements = new Dictionary<Guid, string> { [LanguageIds.English] = "A real claim" },
            ReplacesTargetIds = [placeholder.Id]
        });

        Assert.Equal(withdrawn, report.ExclusionsPreserved);

        var rebuilt = await context.Observations
            .AsNoTracking()
            .Where(o => o.TargetId == report.TargetId && o.ItemVersionId == excludedVersion)
            .ToListAsync();

        Assert.NotEmpty(rebuilt);
        Assert.All(rebuilt, o =>
        {
            Assert.NotNull(o.ExcludedAtUtc);
            Assert.Equal(ObservationExclusionReason.MisKeyedItem, o.ExclusionReason);
            Assert.Equal("key was wrong", o.ExclusionNote);
        });
    }

    /// <summary>
    /// A remap changes which claim an answer is about. It changes nothing about the question
    /// itself, so refolding the statistics would double every count for no reason.
    /// </summary>
    [Fact]
    public async Task Re_targeting_does_not_disturb_item_statistics()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await AnswerEverythingAsync(context, userId, path, correct: true);

        var before = await StatisticsAsync(context, path.LessonId);
        Assert.NotEmpty(before);

        var placeholder = await PlaceholderForAsync(context, path.LessonId);

        await Authoringtargets(context).PromoteAsync(new PromoteTargetRequest
        {
            TargetKey = "maths.statistics_untouched",
            Statements = new Dictionary<Guid, string> { [LanguageIds.English] = "A real claim" },
            ReplacesTargetIds = [placeholder.Id]
        });

        var after = await StatisticsAsync(context, path.LessonId);

        Assert.Equal(before.Count, after.Count);

        foreach (var (versionId, stats) in before)
        {
            Assert.Equal(stats.NTotal, after[versionId].NTotal);
            Assert.Equal(stats.NCorrect, after[versionId].NCorrect);
        }
    }

    // --------------------------------------------------------------------------- sittings

    /// <summary>
    /// A sitting is how practice becomes exam-grade evidence, and its declared conditions are what
    /// decide whether it can.
    /// </summary>
    [Fact]
    public async Task A_controlled_sitting_produces_assessment_grade_evidence()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var formId = await FormOverLessonAsync(context, path.LessonId);
        var service = Assessments(context);

        var sitting = await service.StartAsync(userId, new StartAdministrationRequest
        {
            FormId = formId,
            Purpose = AssessmentPurpose.Summative,
            DeliveryMode = EvidenceDeliveryMode.Supervised,
            RetryPermitted = false,
            WasAided = false,
            LangId = LanguageIds.English
        });

        // Visible before a single question is served, while there is still time to change it.
        Assert.Equal(EvidenceStrength.Assessment, sitting.ExpectedStrength);

        var items = await service.GetItemsAsync(sitting.AdministrationId, userId);
        Assert.NotEmpty(items);

        // The paper carries no correctness field anywhere in it.
        Assert.All(items, i => Assert.NotEmpty(i.Choices));

        foreach (var item in items)
        {
            var result = await service.AnswerAsync(
                sitting.AdministrationId, userId,
                new AdministrationAnswerRequest { Position = item.Position, ChoiceId = item.Choices[0].ChoiceId });

            Assert.True(result.Accepted);
        }

        var closed = await service.CompleteAsync(sitting.AdministrationId, userId);

        Assert.NotNull(closed);
        Assert.Equal(AdministrationState.Submitted, closed!.State);
        Assert.Equal(items.Count, closed.AnsweredCount);
        Assert.NotNull(closed.PointsAvailable);

        await Projector(context).ProjectForLearnerAsync(userId);

        var strengths = await context.Observations
            .AsNoTracking()
            .Where(o => o.LearnerId == userId)
            .Select(o => o.Strength)
            .ToListAsync();

        Assert.NotEmpty(strengths);
        Assert.All(strengths, s => Assert.Equal(EvidenceStrength.Assessment, s));
    }

    [Fact]
    public async Task A_sitting_that_permits_retries_can_never_be_exam_grade()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var formId = await FormOverLessonAsync(context, path.LessonId);

        var sitting = await Assessments(context).StartAsync(userId, new StartAdministrationRequest
        {
            FormId = formId,
            RetryPermitted = true,
            LangId = LanguageIds.English
        });

        Assert.Equal(EvidenceStrength.Practice, sitting.ExpectedStrength);
    }

    [Fact]
    public async Task An_expired_sitting_refuses_answers_rather_than_accepting_them_late()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var formId = await FormOverLessonAsync(context, path.LessonId);
        var service = Assessments(context);

        var sitting = await service.StartAsync(userId, new StartAdministrationRequest
        {
            FormId = formId,
            LangId = LanguageIds.English
        });

        // The server's clock decides, so moving the deadline is the only way to simulate time
        // passing — and a client could not have done this.
        await context.AssessmentAdministrations
            .Where(a => a.Id == sitting.AdministrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));

        // ExecuteUpdate writes behind the change tracker, and the sitting is still tracked from
        // StartAsync — without this the service would re-read its own stale copy and never see the
        // deadline move.
        context.ChangeTracker.Clear();

        var items = await service.GetItemsAsync(sitting.AdministrationId, userId);

        var result = await service.AnswerAsync(
            sitting.AdministrationId, userId,
            new AdministrationAnswerRequest { Position = items[0].Position, ChoiceId = items[0].Choices[0].ChoiceId });

        Assert.False(result.Accepted);
        Assert.Equal("expired", result.Rejection);
    }

    [Fact]
    public async Task Answering_the_same_position_twice_is_refused_unless_retries_were_declared()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var formId = await FormOverLessonAsync(context, path.LessonId);
        var service = Assessments(context);

        var sitting = await service.StartAsync(userId, new StartAdministrationRequest
        {
            FormId = formId,
            RetryPermitted = false,
            LangId = LanguageIds.English
        });

        var items = await service.GetItemsAsync(sitting.AdministrationId, userId);
        var answer = new AdministrationAnswerRequest
        {
            Position = items[0].Position,
            ChoiceId = items[0].Choices[0].ChoiceId
        };

        Assert.True((await service.AnswerAsync(sitting.AdministrationId, userId, answer)).Accepted);

        var second = await service.AnswerAsync(sitting.AdministrationId, userId, answer);

        Assert.False(second.Accepted);
        Assert.Equal("already_answered", second.Rejection);
    }

    /// <summary>
    /// A paper the bank cannot fill is reported, never quietly shortened. A form short on geometry
    /// because no geometry items exist is not the paper the blueprint describes.
    /// </summary>
    [Fact]
    public async Task Form_generation_reports_the_lines_it_could_not_fill()
    {
        await using var context = _fixture.CreateContext();
        var (_, path) = await ReadyLessonAsync(context);

        var placeholder = await PlaceholderForAsync(context, path.LessonId);

        var orphan = new LearningTarget
        {
            Id = Guid.NewGuid(),
            FrameworkId = AssessmentIds.Share7CoreFramework,
            TargetKey = $"orphan/{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow
        };

        context.LearningTargets.Add(orphan);
        await context.SaveChangesAsync();

        var blueprint = await BlueprintOverAsync(
            context, ("Served", [placeholder.Id]), ("Unservable", [orphan.Id]));

        var assessment = new Domain.Assessment.Assessment
        {
            Id = Guid.NewGuid(),
            AssessmentKey = $"test/{Guid.NewGuid():N}",
            Name = "Mid-term",
            BlueprintId = blueprint,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Add(assessment);
        await context.SaveChangesAsync();

        var report = await Assessments(context).GenerateFormAsync(assessment.Id, blueprint);

        Assert.True(report.ItemsPlaced > 0);

        var unfilled = Assert.Single(report.Unfilled);
        Assert.Equal(orphan.Id, unfilled.TargetId);
        Assert.Equal(0, unfilled.Available);
    }

    // ------------------------------------------------------------------ reported outcomes

    /// <summary>
    /// A minor's own tick is not consent for a secondary use of their examination result. The
    /// result is still recorded — it is the child's own record of their own examination — and it
    /// does not enter the calibration sample.
    /// </summary>
    [Fact]
    public async Task A_minor_cannot_consent_to_their_exam_result_being_used_for_calibration()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SetAgeAsync(context, userId, 12);

        var version = await PublishedExamAsync(context, path.LessonId);

        var outcome = await Outcomes(context).ReportAsync(userId, new ReportOutcomeRequest
        {
            ExamSpecificationVersionId = version,
            ReportedScore = 82m,
            SittingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ConsentToCalibrationUse = true
        });

        // Stored, because they asked for it to be.
        Assert.Equal(82m, outcome.ReportedScore);

        // And refused for the purpose that needs a guardian.
        Assert.False(outcome.ConsentedToCalibrationUse);
        Assert.False(outcome.IsCalibrationUsable);
    }

    [Fact]
    public async Task Withdrawing_consent_keeps_the_row_and_stops_it_counting()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SetAgeAsync(context, userId, 19);

        var version = await PublishedExamAsync(context, path.LessonId);
        var service = Outcomes(context);

        var outcome = await service.ReportAsync(userId, new ReportOutcomeRequest
        {
            ExamSpecificationVersionId = version,
            ReportedScore = 74m,
            SittingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ConsentToCalibrationUse = true
        });

        Assert.True(outcome.IsCalibrationUsable);

        Assert.True(await service.WithdrawConsentAsync(userId, outcome.Id));

        var after = await context.ReportedExamOutcomes.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == outcome.Id);

        // The row stays. Deleting it would destroy the record that it was ever part of a sample.
        Assert.NotNull(after);
        Assert.Equal(74m, after!.ReportedScore);
        Assert.NotNull(after.ConsentWithdrawnAtUtc);
        Assert.False(after.IsCalibrationUsable);
    }

    [Fact]
    public async Task Calibration_status_reports_the_distance_rather_than_staying_silent()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SetAgeAsync(context, userId, 19);

        var version = await PublishedExamAsync(context, path.LessonId);
        var service = Outcomes(context);

        await service.ReportAsync(userId, new ReportOutcomeRequest
        {
            ExamSpecificationVersionId = version,
            ReportedScore = 60m,
            SittingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ConsentToCalibrationUse = true
        });

        var status = (await service.GetCalibrationStatusAsync())
            .First(s => s.ExamSpecificationVersionId == version);

        Assert.Equal(1, status.ReportedOutcomes);
        Assert.Equal(1, status.UsableOutcomes);

        // "Not yet" with a distance attached, rather than nothing at all.
        Assert.Equal(ExamCoverageService.MinimumCalibrationSample, status.RequiredForCalibration);
        Assert.False(status.IsCalibrated);
    }

    // -------------------------------------------------------------------------- helpers

    private static ObservationProjector Projector(ApplicationDbContext context) =>
        new(context, NullLogger<ObservationProjector>.Instance);

    private static MeasurementService Measurements(ApplicationDbContext context) =>
        new(context, Projector(context));

    private static ExamCoverageService Coverage(ApplicationDbContext context) =>
        new(context, Projector(context), Measurements(context));

    private static BlueprintAuthoringService Authoring(ApplicationDbContext context) => new(context);

    private static ExamOutcomeService Outcomes(ApplicationDbContext context) =>
        new(context, NullLogger<ExamOutcomeService>.Instance);

    private static TargetAuthoringService Authoringtargets(ApplicationDbContext context) =>
        new(context, Projector(context), NullLogger<TargetAuthoringService>.Instance);

    private static AssessmentService Assessments(ApplicationDbContext context) =>
        new(context, new FixedFormSelector(context),
            new Share7.Infrastructure.Evidence.EvidenceRecorder(
                context, NullLogger<Share7.Infrastructure.Evidence.EvidenceRecorder>.Instance),
            NullLogger<AssessmentService>.Instance);

    private static async Task<(Guid UserId, CurriculumPathFixture Path)> ReadyLessonAsync(
        ApplicationDbContext context, int questionCount = 8)
    {
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, questionCount);
        await context.UnlockLessonAsync(userId, path);
        return (userId, path);
    }

    /// <summary>Two lessons, one answered fully and one never touched. The blind-spot fixture.</summary>
    private static async Task<(Guid UserId, Guid Covered, Guid Untouched)> TwoLessonsOneAnsweredAsync(
        ApplicationDbContext context)
    {
        var (userId, path) = await ReadyLessonAsync(context, questionCount: 12);
        await AnswerEverythingAsync(context, userId, path, correct: true);

        var covered = await PromoteLessonAsync(context, path.LessonId, $"covered/{Guid.NewGuid():N}");

        var untouched = new LearningTarget
        {
            Id = Guid.NewGuid(),
            FrameworkId = AssessmentIds.Share7CoreFramework,
            TargetKey = $"untouched/{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow
        };

        context.LearningTargets.Add(untouched);
        await context.SaveChangesAsync();

        return (userId, covered, untouched.Id);
    }

    private static async Task<LearningTarget> PlaceholderForAsync(
        ApplicationDbContext context, Guid lessonId)
    {
        var key = Share7.Infrastructure.Content.ItemIdentityMinter.PlaceholderTargetKeyFor(lessonId);

        return await context.LearningTargets
            .AsNoTracking()
            .FirstAsync(t => t.FrameworkId == EducationIds.PlaceholderFramework && t.TargetKey == key);
    }

    private static async Task<Guid> PromoteLessonAsync(
        ApplicationDbContext context, Guid lessonId, string key)
    {
        var placeholder = await PlaceholderForAsync(context, lessonId);

        var report = await Authoringtargets(context).PromoteAsync(new PromoteTargetRequest
        {
            TargetKey = key,
            Statements = new Dictionary<Guid, string> { [LanguageIds.English] = $"Claim {key}" },
            ReplacesTargetIds = [placeholder.Id],
            MarkReviewed = true
        });

        return report.TargetId;
    }

    /// <summary>A blueprint with one area per tuple, equal weights, saved and returned by id.</summary>
    private static async Task<Guid> BlueprintOverAsync(
        ApplicationDbContext context,
        params (string Label, Guid[] Targets)[] areas) =>
        await BlueprintOverAsync(context, areas, tune: null);

    private static async Task<Guid> BlueprintOverAsync(
        ApplicationDbContext context,
        (string Label, Guid[] Targets) area,
        Action<AssessmentBlueprint> tune) =>
        await BlueprintOverAsync(context, [area], tune);

    private static async Task<Guid> BlueprintOverAsync(
        ApplicationDbContext context,
        (string Label, Guid[] Targets)[] areas,
        Action<AssessmentBlueprint>? tune)
    {
        var report = await Authoring(context).SaveBlueprintAsync(new SaveBlueprintRequest
        {
            BlueprintKey = $"test/{Guid.NewGuid():N}",
            Name = "Test blueprint",
            FrameworkId = AssessmentIds.Share7CoreFramework,
            Areas =
            [
                .. areas.Select((a, i) => new SaveBlueprintAreaRequest
                {
                    AreaKey = $"a{i}",
                    Label = a.Label,
                    Weight = 1.0m,
                    Order = i,
                    Lines = [.. a.Targets.Select(t => new SaveBlueprintLineRequest { TargetId = t, ItemCount = 2 })]
                })
            ]
        });

        if (tune is not null)
        {
            var blueprint = await context.AssessmentBlueprints.FirstAsync(b => b.Id == report.Id);
            tune(blueprint);
            await context.SaveChangesAsync();
        }

        return report.Id;
    }

    /// <summary>A published examination over one lesson's promoted target, for the outcome tests.</summary>
    private static async Task<Guid> PublishedExamAsync(ApplicationDbContext context, Guid lessonId)
    {
        var target = await PromoteLessonAsync(context, lessonId, $"exam/{Guid.NewGuid():N}");
        var blueprintId = await BlueprintOverAsync(context, ("Only area", [target]));

        var authoring = Authoring(context);
        await authoring.PublishBlueprintAsync(blueprintId);

        var key = $"exam/{Guid.NewGuid():N}";

        var report = await authoring.SaveExamSpecificationAsync(new SaveExamSpecificationRequest
        {
            SpecificationKey = key,
            Name = "Test examination",
            VersionLabel = "2026",
            BlueprintId = blueprintId,
            MaxScore = 100
        });

        await authoring.PublishExamVersionAsync(report.Id);
        return report.Id;
    }

    private static async Task<Guid> FormOverLessonAsync(ApplicationDbContext context, Guid lessonId)
    {
        var versionIds = await context.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.LangId == LanguageIds.English && q.IsActive)
            .Select(q => q.ItemVersionId)
            .Distinct()
            .ToListAsync();

        var assessment = new Domain.Assessment.Assessment
        {
            Id = Guid.NewGuid(),
            AssessmentKey = $"form/{Guid.NewGuid():N}",
            Name = "Unit test",
            CreatedAtUtc = DateTime.UtcNow
        };

        var form = new AssessmentForm
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessment.Id,
            FormNumber = 1,
            Label = "Form 1",
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Add(assessment);
        context.AssessmentForms.Add(form);

        var position = 0;

        foreach (var versionId in versionIds)
        {
            context.AssessmentFormItems.Add(new AssessmentFormItem
            {
                Id = Guid.NewGuid(),
                FormId = form.Id,
                Position = ++position,
                ItemVersionId = versionId,
                Points = 1.0m
            });
        }

        await context.SaveChangesAsync();
        return form.Id;
    }

    private static async Task AnswerEverythingAsync(
        ApplicationDbContext context, Guid userId, CurriculumPathFixture path, bool correct,
        string? requestId = null)
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
                RequestId = requestId,
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

    private static Task<Dictionary<Guid, ItemStatistics>> StatisticsAsync(
        ApplicationDbContext context, Guid lessonId) =>
        context.ItemStatistics
            .AsNoTracking()
            .Where(s => context.Questions.Any(q => q.LessonId == lessonId && q.ItemVersionId == s.ItemVersionId))
            .ToDictionaryAsync(s => s.ItemVersionId);

    /// <summary>
    /// Gives the learner a profile carrying an age. Test users have none by default, and an
    /// unknown age is refused consent on purpose — so a test about what an adult may agree to has
    /// to say how old they are.
    /// </summary>
    private static async Task SetAgeAsync(ApplicationDbContext context, Guid userId, int age)
    {
        var profile = await context.StudentProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null)
        {
            context.StudentProfiles.Add(new Domain.Entities.StudentProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FullName = "Test learner",
                Age = age,
                GradeId = await context.Grades.Select(g => g.Id).FirstAsync(),
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            profile.Age = age;
        }

        await context.SaveChangesAsync();
    }
}
