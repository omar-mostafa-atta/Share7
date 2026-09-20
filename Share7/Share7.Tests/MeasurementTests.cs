using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Progress.Models;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;
using Share7.Domain.Measurement;
using Share7.Infrastructure.Measurement;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Users;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The measurement layer — Phase 2 of the educational rebuild.
/// <para>
/// **Everything here is derived and disposable, and that is the property under test.** Observations
/// come from responses, measurements from observations, verdicts from measurements; delete any of
/// them and the pipeline rebuilds them from the immutable log. The one thing that must *not* be
/// rebuildable from raw rows is <c>ItemStatistics</c>, and the reason has nothing to do with speed —
/// see <see cref="Erasing_a_learner_does_not_move_an_items_statistics"/>.
/// </para>
/// <para>See <c>Docs/EducationalArchitecture.md</c> §5, §12.3 and §12.4.</para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MeasurementTests
{
    private readonly SqlServerFixture _fixture;

    public MeasurementTests(SqlServerFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------- the interval itself

    /// <summary>
    /// The single most important line of arithmetic in the system.
    /// <para>
    /// Four right out of four is **not** 100% proficiency. The textbook normal approximation says
    /// 100% ± 0 — certainty, from four questions — and that is how an education product ends up
    /// telling a parent something it cannot possibly know. Wilson says "somewhere above half",
    /// which is the truth.
    /// </para>
    /// </summary>
    [Fact]
    public void A_perfect_score_on_four_questions_is_not_certainty()
    {
        var (low, high) = WilsonInterval.For(4, 4);

        Assert.Equal(1.0, high, 3);

        // Wide, and honestly so: four answers cannot separate an excellent learner from a lucky one.
        Assert.InRange(low, 0.45, 0.60);
        Assert.True(high - low > 0.40, "four observations must not produce a narrow interval");
    }

    [Fact]
    public void The_interval_narrows_as_evidence_accumulates()
    {
        var (fewLow, fewHigh) = WilsonInterval.For(8, 10);
        var (manyLow, manyHigh) = WilsonInterval.For(80, 100);

        // Same proportion, ten times the evidence, far less doubt — which is exactly the
        // information the bare number 0.8 throws away.
        Assert.True(manyHigh - manyLow < fewHigh - fewLow);
        Assert.True(manyLow > fewLow);
    }

    [Fact]
    public void No_evidence_reports_the_whole_range_rather_than_a_half()
    {
        var (low, high) = WilsonInterval.For(0, 0);

        // Not 0.5. With nothing observed, every proficiency is equally consistent with what we know,
        // and a midpoint would be an invention.
        Assert.Equal(0d, low);
        Assert.Equal(1d, high);
    }

    // ------------------------------------------------------- responses become observations

    [Fact]
    public async Task Every_answered_response_becomes_an_observation_against_its_target()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await AnswersAsync(context, path.LessonId, correct: true));

        var report = await Projector(context).ProjectForLearnerAsync(userId);

        Assert.Equal(5, report.ResponsesRead);
        Assert.Equal(5, report.ObservationsWritten);
        Assert.Equal(0, report.UnmappedResponses);

        await using var check = _fixture.CreateContext();
        var observations = await ObservationsAsync(check, userId);

        Assert.Equal(5, observations.Count);
        Assert.All(observations, o => Assert.Equal(ObservationOutcome.Correct, o.Outcome));

        // Every question in the lesson maps to that lesson's placeholder target, so five answers
        // are five observations about one claim.
        Assert.Single(observations.Select(o => o.TargetId).Distinct());
    }

    /// <summary>
    /// Running the projector twice must not double anybody's evidence. The guard is a unique index
    /// on (response, target) rather than a watermark somebody has to remember to advance.
    /// </summary>
    [Fact]
    public async Task Projecting_twice_writes_nothing_the_second_time()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await AnswersAsync(context, path.LessonId, correct: true));

        await Projector(context).ProjectForLearnerAsync(userId);

        await using var second = _fixture.CreateContext();
        var again = await Projector(second).ProjectForLearnerAsync(userId);

        Assert.Equal(0, again.ResponsesRead);
        Assert.Equal(0, again.ObservationsWritten);

        await using var check = _fixture.CreateContext();
        Assert.Equal(5, (await ObservationsAsync(check, userId)).Count);
    }

    /// <summary>
    /// A child who ran out of road in a runner did not get the question wrong. Treating an
    /// unreached item as incorrect would let **gameplay difficulty masquerade as not knowing the
    /// answer**, which is the exact contamination the evidence contract exists to prevent.
    /// </summary>
    [Fact]
    public async Task An_unreached_question_is_no_response_and_counts_neither_way()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = await AnswersAsync(context, path.LessonId, correct: true);
        answers.RemoveAt(0);

        await SubmitAsync(context, userId, path, answers);
        await Projector(context).ProjectForLearnerAsync(userId);

        await using var check = _fixture.CreateContext();
        var observations = await ObservationsAsync(check, userId);

        Assert.Equal(5, observations.Count);
        Assert.Single(observations, o => o.Outcome == ObservationOutcome.NoResponse);

        // The measurement reads four out of four, not four out of five.
        await new MeasurementService(check, Projector(check)).RecomputeForLearnerAsync(userId);

        var measurement = await check.Measurements.AsNoTracking().FirstAsync(m => m.LearnerId == userId);
        Assert.Equal(4, measurement.ObservationCount);
        Assert.Equal(4, measurement.CorrectCount);
    }

    // ------------------------------------------------------- the sufficiency gate

    /// <summary>
    /// **The gate is the feature.** Five answers cannot support a claim about a child, and the
    /// honest output is a stated absence of evidence rather than a small number.
    /// </summary>
    [Fact]
    public async Task Below_the_gate_the_verdict_is_insufficient_and_no_estimate_is_reported()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await AnswersAsync(context, path.LessonId, correct: true));

        await using var check = _fixture.CreateContext();
        var targets = await new MeasurementService(check, Projector(check))
            .GetForLearnerAsync(userId, LanguageIds.English);

        var target = Assert.Single(targets);

        Assert.Equal(MasteryState.Insufficient, target.State);

        // Null, not zero. "We do not know yet" and "they scored nothing" are different claims, and
        // only one of them is true.
        Assert.Null(target.Estimate);
        Assert.Null(target.IntervalLow);
        Assert.Null(target.IntervalHigh);

        // The count is still reported, and so is how far off being sayable it is.
        Assert.Equal(5, target.ObservationCount);
        Assert.Equal(3, target.ObservationsUntilReportable);
    }

    [Fact]
    public async Task Above_the_gate_a_verdict_arrives_with_its_interval_and_its_rule()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = await AnswersAsync(context, path.LessonId, correct: true);
        await SubmitAsync(context, userId, path, answers, requestId: "run-1");
        await SubmitAsync(context, userId, path, answers, requestId: "run-2");

        await using var check = _fixture.CreateContext();
        var target = Assert.Single(
            await new MeasurementService(check, Projector(check))
                .GetForLearnerAsync(userId, LanguageIds.English));

        Assert.Equal(10, target.ObservationCount);
        Assert.Equal(0, target.ObservationsUntilReportable);
        Assert.NotEqual(MasteryState.Insufficient, target.State);

        Assert.NotNull(target.Estimate);
        Assert.NotNull(target.IntervalLow);
        Assert.NotNull(target.IntervalHigh);
        Assert.True(target.IntervalLow < target.Estimate && target.Estimate <= target.IntervalHigh);

        // The verdict names the rule that produced it, so it can be explained rather than trusted.
        Assert.Equal("platform.default", target.RuleKey);
        Assert.Equal(1, target.RuleVersion);

        // And the target says plainly that it is a lesson standing in for a real competency.
        Assert.True(target.IsPlaceholder);
    }

    /// <summary>
    /// Ten out of ten clears the bar even at the interval's lower bound; the same proportion on
    /// fewer answers does not. Mastery is claimed from the **lower bound**, which is what stops
    /// four-out-of-four reading as mastery.
    /// </summary>
    [Fact]
    public async Task A_learner_who_gets_everything_wrong_is_not_met_rather_than_unmeasured()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var wrong = await AnswersAsync(context, path.LessonId, correct: false);
        await SubmitAsync(context, userId, path, wrong, requestId: "run-1");
        await SubmitAsync(context, userId, path, wrong, requestId: "run-2");

        await using var check = _fixture.CreateContext();
        var target = Assert.Single(
            await new MeasurementService(check, Projector(check))
                .GetForLearnerAsync(userId, LanguageIds.English));

        Assert.Equal(MasteryState.NotMet, target.State);
        Assert.Equal(0m, target.Estimate);

        // Zero out of ten is still an interval, not a point — it is evidence, not proof.
        Assert.True(target.IntervalHigh > 0m);
    }

    // ------------------------------------------------------- item statistics

    [Fact]
    public async Task Item_statistics_record_which_distractor_was_chosen()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var wrong = await AnswersAsync(context, path.LessonId, correct: false);
        await SubmitAsync(context, userId, path, wrong);
        await Projector(context).ProjectForLearnerAsync(userId);

        await using var check = _fixture.CreateContext();

        // Scoped to this lesson: the fixture database is shared by the whole collection, so an
        // unscoped read counts every other test's items too.
        var stats = await check.ItemStatistics
            .AsNoTracking()
            .Where(s => check.Questions.Any(q => q.LessonId == path.LessonId && q.ItemVersionId == s.ItemVersionId))
            .ToListAsync();

        Assert.Equal(5, stats.Count);
        Assert.All(stats, s => Assert.Equal(1, s.NTotal));
        Assert.All(stats, s => Assert.Equal(0, s.NCorrect));
        Assert.All(stats, s => Assert.Equal(1, s.NFirstEncounter));

        // The chosen wrong answer, by id — a misconception with a name rather than "not correct".
        foreach (var submitted in wrong)
        {
            var row = stats.Single(s => check.Questions
                .Any(q => q.Id == submitted.QuestionId && q.ItemVersionId == s.ItemVersionId));

            Assert.NotNull(row.ChoiceFrequency);
            Assert.Contains(submitted.ChoiceId!.Value.ToString("D"), row.ChoiceFrequency);
        }
    }

    /// <summary>
    /// **The reason item statistics are running aggregates rather than a query over the responses.**
    /// <para>
    /// Erasure requests are real and must hard-delete a child's answers. If an item's difficulty
    /// were recomputed from the surviving rows, one child exercising their rights would silently
    /// move every other child's measurements — and nobody would ever know, because the number would
    /// simply be different afterwards.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Erasing_a_learner_does_not_move_an_items_statistics()
    {
        await using var context = _fixture.CreateContext();
        var (leaver, path) = await ReadyLessonAsync(context);

        var bystander = await TestData.CreateUserAsync(context);
        await context.UnlockLessonAsync(bystander, path);

        await SubmitAsync(context, leaver, path, await AnswersAsync(context, path.LessonId, correct: true));
        await SubmitAsync(context, bystander, path, await AnswersAsync(context, path.LessonId, correct: false));

        await Projector(context).ProjectPendingAsync();

        var before = await StatisticsSnapshotAsync(_fixture, path.LessonId);
        Assert.All(before.Values, s => Assert.Equal(2, s.NTotal));

        await using (var deleting = _fixture.CreateContext())
        {
            await new AccountDeletionService(
                IdentityTestHost.CreateUserManager(deleting), deleting)
                .DeleteOwnAccountAsync(leaver);
        }

        await using var check = _fixture.CreateContext();

        // Their evidence and their interpretations of it are gone, as they must be.
        Assert.Empty(await check.LearnerResponses.Where(r => r.LearnerId == leaver).ToListAsync());
        Assert.Empty(await ObservationsAsync(check, leaver));

        // The item's accumulated picture is not, because it is nobody's personal data and
        // everybody's measurement depends on it.
        var after = await StatisticsSnapshotAsync(_fixture, path.LessonId);

        foreach (var (versionId, expected) in before)
        {
            Assert.Equal(expected.NTotal, after[versionId].NTotal);
            Assert.Equal(expected.NCorrect, after[versionId].NCorrect);
            Assert.Equal(expected.NFirstEncounter, after[versionId].NFirstEncounter);
        }
    }

    // ------------------------------------------------------- correcting a mistake

    /// <summary>
    /// A mis-keyed item after fifty thousand answers. You do **not** delete the responses — the
    /// children did answer, and that is a fact. You exclude the observations, stamp the reason, and
    /// recompute. The numbers move; the history does not; the audit trail says why.
    /// </summary>
    [Fact]
    public async Task Excluding_a_mis_keyed_items_observations_moves_the_measurement_and_keeps_the_answers()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = await AnswersAsync(context, path.LessonId, correct: true);
        await SubmitAsync(context, userId, path, answers, requestId: "run-1");
        await SubmitAsync(context, userId, path, answers, requestId: "run-2");

        await using var measured = _fixture.CreateContext();

        // The read path, because RecomputeForLearnerAsync deliberately does not project: it
        // recomputes from the observations that exist, and something has to have made them.
        await new MeasurementService(measured, Projector(measured))
            .GetForLearnerAsync(userId, LanguageIds.English);

        var before = await measured.Measurements.AsNoTracking().FirstAsync(m => m.LearnerId == userId);
        Assert.Equal(10, before.ObservationCount);

        // Withdraw one item's evidence, with a reason.
        var versionId = await measured.Questions
            .Where(q => q.Id == answers[0].QuestionId)
            .Select(q => q.ItemVersionId)
            .FirstAsync();

        var excluded = await new ContentQualityService(measured).ExcludeItemObservationsAsync(
            versionId, ObservationExclusionReason.MisKeyedItem, excludedByUserId: null,
            note: "Answer key was wrong.");

        Assert.Equal(2, excluded);

        await using var check = _fixture.CreateContext();
        await new MeasurementService(check, Projector(check)).RecomputeForLearnerAsync(userId);

        var after = await check.Measurements.AsNoTracking().FirstAsync(m => m.LearnerId == userId);
        Assert.Equal(8, after.ObservationCount);

        // The responses are untouched. The observations are annotated, not deleted, and they carry
        // the reason — which is the only thing that makes the change in the numbers explainable.
        Assert.Equal(10, await check.LearnerResponses.CountAsync(r => r.LearnerId == userId));
        Assert.Equal(10, (await ObservationsAsync(check, userId)).Count);

        var annotated = await check.Observations
            .AsNoTracking()
            .Where(o => o.ItemVersionId == versionId)
            .ToListAsync();

        Assert.All(annotated, o => Assert.NotNull(o.ExcludedAtUtc));
        Assert.All(annotated, o => Assert.Equal(ObservationExclusionReason.MisKeyedItem, o.ExclusionReason));
    }

    // ------------------------------------------------------- the trust ceiling

    /// <summary>
    /// The teacher-authoring safety mechanism, tested at the only place it is enforced. An item in
    /// an unreviewed bank produces practice-class evidence **however controlled the sitting was** —
    /// a teacher may write anything, and nothing they wrote can move an exam-grade claim until
    /// somebody qualified has looked at it.
    /// </summary>
    [Fact]
    public async Task An_unreviewed_banks_items_cannot_produce_assessment_grade_observations()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        // Drop the platform bank's ceiling to Practice, which is what a teacher's own bank carries.
        await context.ItemBanks
            .Where(b => b.Id == ContentIds.PlatformCurriculumBank)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.MaxEvidenceStrength, EvidenceStrength.Practice));

        try
        {
            await SubmitAsync(context, userId, path, await AnswersAsync(context, path.LessonId, correct: true));
            await Projector(context).ProjectForLearnerAsync(userId);

            await using var check = _fixture.CreateContext();
            var observations = await ObservationsAsync(check, userId);

            // First encounter, unhinted, no retry — perfect conditions, and still capped.
            Assert.All(observations, o => Assert.True(o.Strength <= EvidenceStrength.Practice));
        }
        finally
        {
            // The bank is shared by every test in this collection, so the ceiling goes back.
            await using var restore = _fixture.CreateContext();
            await restore.ItemBanks
                .Where(b => b.Id == ContentIds.PlatformCurriculumBank)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.MaxEvidenceStrength, EvidenceStrength.Assessment));
        }
    }

    // ------------------------------------------------------- helpers

    private static ObservationProjector Projector(ApplicationDbContext context) =>
        new(context, NullLogger<ObservationProjector>.Instance);

    private static Task<List<Observation>> ObservationsAsync(ApplicationDbContext context, Guid userId) =>
        context.Observations
            .AsNoTracking()
            .Where(o => o.LearnerId == userId)
            .OrderBy(o => o.Sequence)
            .ToListAsync();

    private static async Task<Dictionary<Guid, ItemStatistics>> StatisticsSnapshotAsync(
        SqlServerFixture fixture, Guid lessonId)
    {
        await using var context = fixture.CreateContext();

        return await context.ItemStatistics
            .AsNoTracking()
            .Where(s => context.Questions.Any(q => q.LessonId == lessonId && q.ItemVersionId == s.ItemVersionId))
            .ToDictionaryAsync(s => s.ItemVersionId);
    }

    private static async Task<(Guid UserId, CurriculumPathFixture Path)> ReadyLessonAsync(
        ApplicationDbContext context)
    {
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);
        return (userId, path);
    }

    private static async Task SubmitAsync(
        ApplicationDbContext context,
        Guid userId,
        CurriculumPathFixture path,
        List<SubmittedAnswer> answers,
        string? requestId = null)
    {
        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers,
                RequestId = requestId
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private static async Task<List<SubmittedAnswer>> AnswersAsync(
        ApplicationDbContext context, Guid lessonId, bool correct)
    {
        var questions = await context.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.LangId == LanguageIds.English && q.IsActive)
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

        return
        [
            .. questions.Select(q => new SubmittedAnswer
            {
                QuestionId = q.Id,
                ChoiceId = correct ? q.CorrectChoiceId : q.WrongChoiceId
            })
        ];
    }
}
