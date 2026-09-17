using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Progress.Models;
using Share7.Domain.Constants;
using Share7.Domain.Progress;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The two attempt-path guarantees leaderboards rest on: a retry is not a second attempt, and the
/// completion ladder only climbs.
/// <para>
/// Both were defects before ranking existed and both were survivable then — a duplicated attempt
/// was a private counter being wrong, and a lost ace was a private badge. Projected onto a public
/// board they become free points and a public demotion for playing more, so they are fixed at the
/// source rather than compensated for downstream.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ProgressAttemptIntegrityTests
{
    private readonly SqlServerFixture _fixture;

    public ProgressAttemptIntegrityTests(SqlServerFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------- idempotency

    [Fact]
    public async Task Re_posting_the_same_request_id_records_one_attempt_not_two()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);
        var answers = await PerfectRunAsync(context, path.LessonId);

        var first = await SubmitAsync(context, userId, path, answers, "run-1");
        var second = await SubmitAsync(context, userId, path, answers, "run-1");

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.True(second.Succeeded, string.Join("; ", second.Errors));

        Assert.Equal(1, first.Value!.Attempts);
        Assert.Equal(1, second.Value!.Attempts);

        await using var check = _fixture.CreateContext();
        var row = await check.UserLessonProgress.SingleAsync(
            p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task A_replay_returns_what_the_first_call_returned_including_what_it_unlocked()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);
        var answers = await PerfectRunAsync(context, path.LessonId);

        var first = await SubmitAsync(context, userId, path, answers, "run-1");
        var replay = await SubmitAsync(context, userId, path, answers, "run-1");

        // `unlocked` is the reason the body is stored rather than recomputed: a second run of the
        // same lesson opens nothing, so a replay that recalculated would report an empty list for
        // a run that really did open the next lesson.
        Assert.Equal(
            first.Value!.Unlocked.Select(u => u.NodeId),
            replay.Value!.Unlocked.Select(u => u.NodeId));

        Assert.Equal(first.Value.Percent, replay.Value.Percent);
        Assert.Equal(first.Value.CompletionState, replay.Value.CompletionState);
        Assert.Equal(first.Value.Answers.Count, replay.Value.Answers.Count);
    }

    [Fact]
    public async Task Two_simultaneous_retries_of_one_run_still_produce_one_attempt()
    {
        await using var setup = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(setup);
        var answers = await PerfectRunAsync(setup, path.LessonId);

        // Separate contexts, because the race this guards is two requests, not two calls.
        await using var left = _fixture.CreateContext();
        await using var right = _fixture.CreateContext();

        var both = await Task.WhenAll(
            SubmitAsync(left, userId, path, answers, "race-1"),
            SubmitAsync(right, userId, path, answers, "race-1"));

        Assert.All(both, r => Assert.True(r.Succeeded, string.Join("; ", r.Errors)));
        Assert.All(both, r => Assert.Equal(1, r.Value!.Attempts));

        await using var check = _fixture.CreateContext();
        var row = await check.UserLessonProgress.SingleAsync(
            p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(1, row.Attempts);
        Assert.Equal(1, await check.ProgressRequestLogs.CountAsync(l => l.UserId == userId));
    }

    [Fact]
    public async Task Without_a_request_id_a_resubmission_is_still_a_new_attempt()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);
        var answers = await PerfectRunAsync(context, path.LessonId);

        // Unchanged behaviour, on purpose. A client that sends no key is telling us it cannot
        // distinguish a retry from a replay, and guessing on its behalf would be worse.
        await SubmitAsync(context, userId, path, answers, requestId: null);
        var second = await SubmitAsync(context, userId, path, answers, requestId: null);

        Assert.Equal(2, second.Value!.Attempts);
    }

    [Fact]
    public async Task A_refused_attempt_does_not_burn_its_request_id()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);

        // Locked: this is the refusal commerce learned to keep out of the idempotency index.
        var refused = await SubmitAsync(context, userId, path, [], "same-key");
        Assert.False(refused.Succeeded);

        await context.UnlockLessonAsync(userId, path);
        var answers = await PerfectRunAsync(context, path.LessonId);

        // The same key, now that the lesson is open. A store that logged the refusal would replay
        // "still locked" forever to a child who is no longer locked out.
        var allowed = await SubmitAsync(context, userId, path, answers, "same-key");

        Assert.True(allowed.Succeeded, string.Join("; ", allowed.Errors));
        Assert.Equal(1, allowed.Value!.Attempts);
    }

    // ------------------------------------------------------------- monotonic completion

    [Fact]
    public async Task An_ace_survives_a_worse_replay()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var perfect = await PerfectRunAsync(context, path.LessonId);
        var failed = await WrongRunAsync(context, path.LessonId);

        var aced = await SubmitAsync(context, userId, path, perfect, "run-1");
        Assert.Equal("Aced", aced.Value!.CompletionState);

        var replayed = await SubmitAsync(context, userId, path, failed, "run-2");

        // The run scored zero and says so; the record it belongs to is still an ace.
        Assert.Equal(0, replayed.Value!.Percent);
        Assert.Equal("Aced", replayed.Value.CompletionState);

        await using var check = _fixture.CreateContext();
        var row = await check.UserLessonProgress.SingleAsync(
            p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(CompletionState.Aced, row.CompletionState);
        Assert.Equal(100, row.BestPercent);
        Assert.Equal(0, row.Percent);
        Assert.Equal(2, row.Attempts);
    }

    [Fact]
    public async Task Best_percent_climbs_and_never_falls()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var perfect = await PerfectRunAsync(context, path.LessonId);
        var failed = await WrongRunAsync(context, path.LessonId);

        await SubmitAsync(context, userId, path, failed, "a");
        await SubmitAsync(context, userId, path, perfect, "b");
        await SubmitAsync(context, userId, path, failed, "c");

        await using var check = _fixture.CreateContext();
        var row = await check.UserLessonProgress.SingleAsync(
            p => p.UserId == userId && p.LessonId == path.LessonId);

        Assert.Equal(100, row.BestPercent);
        Assert.Equal(CompletionState.Aced, row.CompletionState);
    }

    [Fact]
    public async Task A_replay_of_an_aced_lesson_does_not_pay_the_ace_reward_again()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var coins = await context.CreateCurrencyAsync();

        // EveryTime, so nothing but the attempt's own state can stop it firing twice.
        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 10)],
            repeatPolicy: RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        var perfect = await PerfectRunAsync(context, path.LessonId);
        var failed = await WrongRunAsync(context, path.LessonId);

        var aced = await SubmitAsync(context, userId, path, perfect, "run-1");
        Assert.Single(aced.Value!.Rewards);

        // The record still says Aced. The *run* scored zero, and rewards follow the run — paying
        // for an ace that did not happen is the mirror image of demoting one that did.
        var replayed = await SubmitAsync(context, userId, path, failed, "run-2");

        Assert.Empty(replayed.Value!.Rewards);
        Assert.Equal(10, await context.BalanceOfAsync(userId, coins.Id));
    }

    // ------------------------------------------------------------- rollups

    /// <summary>
    /// The rollup and the lesson beside it must measure the same thing.
    /// <para>
    /// They did not: a lesson's <c>CompletionState</c> comes from <c>BestPercent</c>, while every
    /// subject, chapter and term summed <c>CorrectCount</c> — the *latest* attempt. Acing a lesson
    /// and then replaying it for fun dropped the subject to 95% with the lesson still showing Aced,
    /// which is the app disagreeing with itself in front of the child who earned it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_replayed_lesson_does_not_lower_the_subject_it_belongs_to()
    {
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        var perfect = await PerfectRunAsync(context, path.LessonId);
        var failed = await WrongRunAsync(context, path.LessonId);

        await SubmitAsync(context, userId, path, perfect, "run-1");

        var aced = await SnapshotSubjectPercentAsync(context, userId, path);
        Assert.Equal(100, aced);

        // Played again, badly. The record still says Aced, so the subject must still say 100.
        await SubmitAsync(context, userId, path, failed, "run-2");

        Assert.Equal(100, await SnapshotSubjectPercentAsync(context, userId, path));
    }

    [Fact]
    public async Task An_unattempted_lesson_leaves_its_subject_at_zero()
    {
        // The other end of the same rule: best-of must not invent progress for a lesson nobody has
        // opened.
        await using var context = _fixture.CreateContext();
        var (userId, path) = await ReadyLessonAsync(context);

        Assert.Equal(0, await SnapshotSubjectPercentAsync(context, userId, path));
    }

    private static async Task<int> SnapshotSubjectPercentAsync(
        ApplicationDbContext context, Guid userId, CurriculumPathFixture path)
    {
        var snapshot = await RewardTestExtensions.CreateProgressService(context, userId)
            .GetSnapshotAsync(userId, path.GameId, null);

        Assert.True(snapshot.Succeeded, string.Join("; ", snapshot.Errors));

        var subject = snapshot.Value!.Terms
            .SelectMany(term => term.Subjects)
            .Single(s => s.Id == path.SubjectId);

        return subject.Percent;
    }

    // ------------------------------------------------------------- helpers

    private static async Task<(Guid UserId, CurriculumPathFixture Path)> ReadyLessonAsync(
        ApplicationDbContext context)
    {
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 4);
        await context.UnlockLessonAsync(userId, path);
        return (userId, path);
    }

    private static Task<ServiceResult<AttemptResultDto>> SubmitAsync(
        ApplicationDbContext context,
        Guid userId,
        CurriculumPathFixture path,
        List<SubmittedAnswer> answers,
        string? requestId) =>
        RewardTestExtensions.CreateProgressService(context, userId)
            .SubmitAttemptAsync(userId, new SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                Answers = answers,
                RequestId = requestId
            });

    private static Task<List<SubmittedAnswer>> PerfectRunAsync(ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: true);

    private static Task<List<SubmittedAnswer>> WrongRunAsync(ApplicationDbContext context, Guid lessonId) =>
        AnswersAsync(context, lessonId, correct: false);

    private static async Task<List<SubmittedAnswer>> AnswersAsync(
        ApplicationDbContext context,
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
                WrongChoiceId = q.Choices.Where(c => c.Id != q.CorrectChoiceId).Select(c => c.Id).FirstOrDefault()
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
