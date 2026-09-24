using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Progress.Models;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;
using Share7.Domain.Play;
using Share7.Infrastructure.Users;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The append-only educational evidence log — Phase 0 of the educational rebuild.
/// <para>
/// **Every test here pins something that was previously destroyed on write.** Before this log
/// existed, <c>UserQuestionProgress</c> held one row per question with <c>IsCorrect</c> overwritten
/// on every attempt: the chosen distractor arrived on the wire and was written nowhere, timing was
/// never captured at all, and a practice run recorded nothing whatsoever. None of it was
/// recoverable after the fact, which is why this shipped ahead of the rest of the rebuild.
/// </para>
/// <para>See <c>Docs/EducationalArchitecture.md</c> §1.2 and §12.</para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class EvidenceLogTests
{
    private readonly SqlServerFixture _fixture;

    public EvidenceLogTests(SqlServerFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------- the response is kept at all

    [Fact]
    public async Task An_attempt_records_one_response_per_question()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var result = await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await using var check = _fixture.CreateContext();
        var responses = await ResponsesAsync(check, userId);

        // Four added by the fixture plus the one the path itself creates.
        Assert.Equal(5, responses.Count);
        Assert.All(responses, r => Assert.True(r.IsCorrect));
        Assert.All(responses, r => Assert.Equal(path.LessonId, r.NodeId));
        Assert.All(responses, r => Assert.Equal(path.GameId, r.GameId));
    }

    /// <summary>
    /// The one that matters most. Which wrong answer a child picks is the difference between a
    /// careless slip and a misconception, and the old schema computed it, returned it to the client
    /// and then discarded it.
    /// </summary>
    [Fact]
    public async Task The_chosen_distractor_is_preserved_not_just_the_verdict()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var wrong = await WrongRunAsync(context, path.LessonId);
        await SubmitAsync(context, userId, path, wrong);

        await using var check = _fixture.CreateContext();
        var responses = await ResponsesAsync(check, userId);

        Assert.All(responses, r => Assert.False(r.IsCorrect));

        // Not merely "a wrong answer" — *which* wrong answer, matching what was submitted.
        foreach (var submitted in wrong)
        {
            var stored = responses.Single(r => r.ItemLocalizationId == submitted.QuestionId);
            Assert.Equal(submitted.ChoiceId, stored.ChoiceId);
        }
    }

    [Fact]
    public async Task A_question_that_was_never_reached_is_recorded_as_unanswered_not_omitted()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = await PerfectRunAsync(context, path.LessonId);
        var skipped = answers[0].QuestionId;
        answers.RemoveAt(0);

        await SubmitAsync(context, userId, path, answers);

        await using var check = _fixture.CreateContext();
        var responses = await ResponsesAsync(check, userId);

        // A run shows every question in the lesson, so an absent answer is a fact about the run
        // rather than a gap in it.
        Assert.Equal(5, responses.Count);

        var unreached = responses.Single(r => r.ItemLocalizationId == skipped);
        Assert.Null(unreached.ChoiceId);
        Assert.False(unreached.IsCorrect);
        Assert.False(unreached.WasUnrecognised);
    }

    // ------------------------------------------------------- practice, the biggest change

    /// <summary>
    /// **The largest behavioural change in Phase 0.** A practice run returns before any progress
    /// row is touched — correctly, because practice must never risk a child's record — and that
    /// early return previously took the evidence with it. A child grinding the questions they got
    /// wrong is the richest diagnostic signal the product collects, and all of it was discarded.
    /// </summary>
    [Fact]
    public async Task A_practice_attempt_records_evidence_while_recording_no_progress()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);
        await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await SubmitAsync(
            context, userId, path,
            await PerfectRunAsync(context, path.LessonId),
            contextKey: PlayContextTokens.Practice);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await using var check = _fixture.CreateContext();

        // Settlement: untouched, exactly as before.
        Assert.Equal(0, await check.UserLessonProgress.CountAsync(p => p.UserId == userId));
        Assert.Equal(0, await check.UserQuestionProgress.CountAsync(p => p.UserId == userId));

        // Evidence: kept. "Worth nothing" is a settlement verdict, not a reason to forget what
        // happened.
        var responses = await ResponsesAsync(check, userId);
        Assert.Equal(5, responses.Count);
        Assert.All(responses, r => Assert.Equal(PlayContextKind.Practice, r.PlayContext));
    }

    [Fact]
    public async Task A_free_play_attempt_records_evidence_too()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);
        await context.AddModeAsync(path.GameId, isDefault: true);

        await SubmitAsync(
            context, userId, path,
            await PerfectRunAsync(context, path.LessonId),
            contextKey: PlayContextTokens.FreePlay);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.UserLessonProgress.CountAsync(p => p.UserId == userId));
        Assert.Equal(5, (await ResponsesAsync(check, userId)).Count);
    }

    // ------------------------------------------------------- conditions

    [Fact]
    public async Task The_attempt_ordinal_counts_responses_per_item_and_only_the_first_is_an_encounter()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await WrongRunAsync(context, path.LessonId), requestId: "r1");
        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId), requestId: "r2");
        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId), requestId: "r3");

        await using var check = _fixture.CreateContext();
        var forOneItem = (await ResponsesAsync(check, userId))
            .Where(r => r.ItemLocalizationId == path.QuestionId)
            .OrderBy(r => r.Sequence)
            .ToList();

        Assert.Equal([1, 2, 3], forOneItem.Select(r => r.AttemptOrdinal));
        Assert.Equal([true, false, false], forOneItem.Select(r => r.IsFirstEncounter));

        // The trajectory the old schema collapsed: wrong, then right, then right.
        Assert.Equal([false, true, true], forOneItem.Select(r => r.IsCorrect));
    }

    [Fact]
    public async Task Timing_and_hints_are_stored_as_given()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = await PerfectRunAsync(context, path.LessonId);
        var timed = answers.Select(a => new SubmittedAnswer
        {
            QuestionId = a.QuestionId,
            ChoiceId = a.ChoiceId,
            ElapsedMs = 4200,
            HintsUsed = 1,
            TimeLimitMs = 10_000
        }).ToList();

        await SubmitAsync(context, userId, path, timed);

        await using var check = _fixture.CreateContext();
        var responses = await ResponsesAsync(check, userId);

        Assert.All(responses, r => Assert.Equal(4200, r.ElapsedMs));
        Assert.All(responses, r => Assert.Equal(1, r.HintsUsed));
        Assert.All(responses, r => Assert.Equal(10_000, r.TimeLimitMs));
    }

    /// <summary>
    /// A backgrounded app reports an hour on one question. Storing that number would skew every
    /// timing statistic that ever reads it; storing null says "we do not know", which is a
    /// different and honest claim.
    /// </summary>
    [Fact]
    public async Task An_implausible_duration_is_stored_as_unknown_rather_than_as_a_number()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = (await PerfectRunAsync(context, path.LessonId))
            .Select(a => new SubmittedAnswer
            {
                QuestionId = a.QuestionId,
                ChoiceId = a.ChoiceId,
                // Past the 30-minute bound the recorder applies. The request DTO's own Range
                // attribute stops this at the API edge; the recorder is the backstop for every
                // other caller.
                ElapsedMs = 60 * 60 * 1000
            })
            .ToList();

        await SubmitAsync(context, userId, path, answers);

        await using var check = _fixture.CreateContext();
        Assert.All(await ResponsesAsync(check, userId), r => Assert.Null(r.ElapsedMs));
    }

    // ------------------------------------------------------- strength

    /// <summary>
    /// Strength is **derived**, never stored: a function of the contract version and the conditions,
    /// both immutable, so it recomputes identically forever. These assertions run the same function
    /// the measurement layer will.
    /// </summary>
    [Fact]
    public async Task A_first_unhinted_curriculum_answer_is_assessment_grade_and_a_replay_is_not()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId), requestId: "r1");
        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId), requestId: "r2");

        await using var check = _fixture.CreateContext();
        var contract = await PlatformContractAsync(check);

        var forOneItem = (await ResponsesAsync(check, userId))
            .Where(r => r.ItemLocalizationId == path.QuestionId)
            .OrderBy(r => r.Sequence)
            .ToList();

        Assert.Equal(
            EvidenceStrength.Assessment,
            StrengthOf(contract, forOneItem[0]));

        // The same child, the same item, the same answer — and not the same claim. Having seen it
        // before is what makes the second one practice.
        Assert.Equal(
            EvidenceStrength.Practice,
            StrengthOf(contract, forOneItem[1]));
    }

    [Fact]
    public async Task A_hinted_answer_is_practice_even_on_a_first_encounter()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var hinted = (await PerfectRunAsync(context, path.LessonId))
            .Select(a => new SubmittedAnswer { QuestionId = a.QuestionId, ChoiceId = a.ChoiceId, HintsUsed = 2 })
            .ToList();

        await SubmitAsync(context, userId, path, hinted);

        await using var check = _fixture.CreateContext();
        var contract = await PlatformContractAsync(check);

        Assert.All(
            await ResponsesAsync(check, userId),
            r =>
            {
                Assert.True(r.IsFirstEncounter);
                Assert.Equal(EvidenceStrength.Practice, StrengthOf(contract, r));
            });
    }

    [Fact]
    public async Task Offline_delivery_cannot_produce_assessment_grade_evidence()
    {
        await using var check = _fixture.CreateContext();
        var contract = await PlatformContractAsync(check);

        // Perfectly controlled conditions — and unverifiable ones. A client clock that cannot be
        // observed cannot support an exam-grade claim however good it says the conditions were.
        Assert.Equal(
            EvidenceStrength.Practice,
            contract.StrengthFor(
                isFirstEncounter: true, hintsUsed: 0, retryPermitted: false,
                delivery: EvidenceDeliveryMode.Offline));

        Assert.Equal(
            EvidenceStrength.Assessment,
            contract.StrengthFor(
                isFirstEncounter: true, hintsUsed: 0, retryPermitted: false,
                delivery: EvidenceDeliveryMode.Solo));
    }

    // ------------------------------------------------------- the contract is the gate

    /// <summary>
    /// The enforcement of the rule in <c>EvidenceContract</c>: no contract, no evidence. Retiring
    /// the platform contract must stop evidence being recorded entirely rather than falling back to
    /// recording it unattributed.
    /// </summary>
    [Fact]
    public async Task With_no_published_contract_an_attempt_records_no_evidence_at_all()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var version = await context.EvidenceContractVersions
            .FirstAsync(v => v.Id == EvidenceContractIds.PlatformLessonAttemptV1);

        // The seeded contract is shared by every test in this collection, so retiring it without
        // putting it back makes every later test silently record nothing — which is exactly the
        // failure this test is asserting, arriving in the wrong place.
        try
        {
            version.RetiredAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();

            var result = await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId));

            // The attempt itself still works — settlement does not depend on evidence.
            Assert.True(result.Succeeded, string.Join("; ", result.Errors));

            await using var check = _fixture.CreateContext();
            Assert.Empty(await ResponsesAsync(check, userId));
            Assert.Equal(1, await check.UserLessonProgress.CountAsync(p => p.UserId == userId));
        }
        finally
        {
            version.RetiredAtUtc = null;
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Every_response_names_the_contract_version_that_admitted_it()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId));

        await using var check = _fixture.CreateContext();
        Assert.All(
            await ResponsesAsync(check, userId),
            r => Assert.Equal(EvidenceContractIds.PlatformLessonAttemptV1, r.EvidenceContractVersionId));
    }

    // ------------------------------------------------------- idempotency and transactions

    [Fact]
    public async Task A_replayed_request_id_does_not_duplicate_the_evidence()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var answers = await PerfectRunAsync(context, path.LessonId);
        await SubmitAsync(context, userId, path, answers, requestId: "same-run");
        await SubmitAsync(context, userId, path, answers, requestId: "same-run");

        await using var check = _fixture.CreateContext();

        // A retry of one run is one run. Two copies would make the child look twice as practised
        // as they are, and would double-count every item statistic built over this.
        Assert.Equal(5, (await ResponsesAsync(check, userId)).Count);
    }

    /// <summary>
    /// Evidence commits with the attempt or not at all. A refused submission that had already
    /// written responses would record gameplay that never happened.
    /// </summary>
    [Fact]
    public async Task A_refused_attempt_records_no_evidence()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var result = await RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                ModeKey = "mode.that.does.not.exist",
                Answers = await PerfectRunAsync(context, path.LessonId)
            });

        Assert.False(result.Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.Empty(await ResponsesAsync(check, userId));
    }

    // ------------------------------------------------------- history outlives the content

    /// <summary>
    /// Publishing a lesson retires its question rows and inserts new ones. The responses given
    /// against the retired rows must survive that, because the whole point of the log is that a
    /// child's history stays explicable after the question is rewritten.
    /// </summary>
    [Fact]
    public async Task Responses_survive_the_retirement_of_the_item_they_were_given_against()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId));

        await context.Questions
            .Where(q => q.LessonId == path.LessonId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.IsActive, false)
                .SetProperty(q => q.DeactivatedAt, DateTime.UtcNow));

        await using var check = _fixture.CreateContext();
        var responses = await ResponsesAsync(check, userId);

        Assert.Equal(5, responses.Count);

        // Stamped from the lesson's question-set version at the moment of the attempt, so a
        // response stays interpretable against the content it was actually given.
        var versionAtAttempt = await check.LessonQuestionSets
            .Where(s => s.LessonId == path.LessonId && s.LangId == LanguageIds.English)
            .Select(s => (int?)s.Version)
            .FirstOrDefaultAsync() ?? 0;

        Assert.All(responses, r => Assert.Equal(versionAtAttempt, r.ContentVersion));
    }

    [Fact]
    public async Task The_sequence_is_monotonic_so_a_projection_can_watermark_over_it()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId), requestId: "r1");
        await SubmitAsync(context, userId, path, await PerfectRunAsync(context, path.LessonId), requestId: "r2");

        await using var check = _fixture.CreateContext();
        var sequences = (await ResponsesAsync(check, userId)).Select(r => r.Sequence).ToList();

        Assert.Equal(sequences.Count, sequences.Distinct().Count());
        Assert.Equal(sequences.OrderBy(s => s), sequences.OrderBy(s => s));
        Assert.All(sequences, s => Assert.True(s > 0));
    }

    // ------------------------------------------------------- erasure

    /// <summary>
    /// A child who asks to be forgotten takes their evidence with them.
    /// <para>
    /// A response carries the timing, the ordering and the exact wrong answer a named child gave,
    /// which is re-identifiable to anyone who knows what they were studying that week — so it is
    /// deleted rather than anonymised, like their leaderboard standings and for the same reason.
    /// </para>
    /// <para>
    /// **The constraint this creates for Phase 2**: item statistics must be running aggregates,
    /// never a recomputation over these rows, or erasing one child would silently shift every
    /// item's difficulty and therefore every other child's measurements.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Deleting_an_account_removes_that_learners_evidence_and_nobody_elses()
    {
        await using var context = _fixture.CreateContext();
        var (leaver, path) = await ReadyLessonAsync(context);

        var bystander = await TestData.CreateUserAsync(context);
        await context.UnlockLessonAsync(bystander, path);

        await SubmitAsync(context, leaver, path, await PerfectRunAsync(context, path.LessonId));
        await SubmitAsync(context, bystander, path, await PerfectRunAsync(context, path.LessonId));

        Assert.Equal(5, (await ResponsesAsync(context, leaver)).Count);

        await using (var deleting = _fixture.CreateContext())
        {
            await new AccountDeletionService(
                IdentityTestHost.CreateUserManager(deleting), deleting)
                .DeleteOwnAccountAsync(leaver);
        }

        await using var check = _fixture.CreateContext();
        Assert.Empty(await ResponsesAsync(check, leaver));
        Assert.Equal(5, (await ResponsesAsync(check, bystander)).Count);
    }

    // ------------------------------------------------------- helpers

    private static EvidenceStrength StrengthOf(EvidenceContractVersion contract, LearnerResponse r) =>
        contract.StrengthFor(r.IsFirstEncounter, r.HintsUsed, r.RetryPermitted, r.DeliveryMode);

    private static Task<EvidenceContractVersion> PlatformContractAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context) =>
        context.EvidenceContractVersions
            .AsNoTracking()
            .FirstAsync(v => v.Id == EvidenceContractIds.PlatformLessonAttemptV1);

    private static Task<List<LearnerResponse>> ResponsesAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid userId) =>
        context.LearnerResponses
            .AsNoTracking()
            .Where(r => r.LearnerId == userId)
            .OrderBy(r => r.Sequence)
            .ToListAsync();

    private static async Task<(Guid UserId, CurriculumPathFixture Path)> ReadyLessonAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context)
    {
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);
        return (userId, path);
    }

    private static Task<ServiceResult<AttemptResultDto>> SubmitAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid userId,
        CurriculumPathFixture path,
        List<SubmittedAnswer> answers,
        string? requestId = null,
        string? contextKey = null) =>
        RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers,
                RequestId = requestId,
                ContextKey = contextKey
            });

    private static Task<List<SubmittedAnswer>> PerfectRunAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: true);

    private static Task<List<SubmittedAnswer>> WrongRunAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: false);

    private static async Task<List<SubmittedAnswer>> AnswersAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid lessonId,
        bool correct)
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

        return questions
            .Select(q => new SubmittedAnswer
            {
                QuestionId = q.Id,
                ChoiceId = correct ? q.CorrectChoiceId : q.WrongChoiceId
            })
            .ToList();
    }
}
