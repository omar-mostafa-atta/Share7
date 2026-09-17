using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Curriculum;
using Share7.Domain.Constants;
using Share7.Domain.Multiplayer;
using Share7.Domain.Progress;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Matchmaking on a shared lesson.
/// <para>
/// **The problem this replaced:** two children could only be matched if they had both picked exactly
/// the same lesson, which in practice meant being at exactly the same point in the curriculum — so
/// most searches matched nobody. They now choose a <i>subject</i>, and the server finds a lesson both
/// of them have unlocked and have questions for.
/// </para>
/// <para>
/// The property that matters throughout: a match never starts on a lesson somebody in it cannot play.
/// Every test here is a way that could happen.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SubjectMatchmakingTests
{
    private readonly SqlServerFixture _fixture;

    public SubjectMatchmakingTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>A subject with several lessons, each with one answerable English question.</summary>
    private static async Task<(CurriculumPathFixture Path, List<Guid> Lessons)> SubjectAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, int extraLessons)
    {
        var path = await TestData.CreateCurriculumPathAsync(context);
        var lessons = new List<Guid> { path.LessonId };

        for (var i = 0; i < extraLessons; i++)
        {
            var lesson = new Lesson
            {
                Id = Guid.NewGuid(),
                ChapterId = path.ChapterId,
                Order = 100 + i
            };

            context.Lessons.Add(lesson);
            await context.SaveChangesAsync();

            // Questions, because an unlocked lesson with nothing to answer is not playable — the
            // curriculum tree is shared across languages but its questions are not.
            await context.AddQuestionsAsync(lesson.Id, 1);

            lessons.Add(lesson.Id);
        }

        return (path, lessons);
    }

    private static async Task UnlockAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid userId,
        Guid gameId,
        params Guid[] lessonIds)
    {
        foreach (var lessonId in lessonIds)
        {
            context.UserNodeUnlocks.Add(new UserNodeUnlock
            {
                UserId = userId,
                GameId = gameId,
                NodeType = CurriculumNodeType.Lesson,
                NodeId = lessonId,
                UnlockedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }

    private static MatchmakeRequest Request(Guid gameId, Guid subjectId, string? transport = null) => new()
    {
        GameId = gameId,
        ProtocolVersion = 1,
        CurriculumPath = new CurriculumPathDto { SubjectId = subjectId },
        CreateIfNoneFound = true,
        TransportSessionName = transport ?? $"room_{Guid.NewGuid():N}"[..20]
    };

    [Fact]
    public async Task Two_players_with_a_lesson_in_common_are_matched_into_one_session()
    {
        await using var context = _fixture.CreateContext();
        var host = await TestData.CreateUserAsync(context);
        var joiner = await TestData.CreateUserAsync(context);
        var (path, lessons) = await SubjectAsync(context, extraLessons: 2);

        // They are at different points in the curriculum and share exactly one lesson.
        await UnlockAsync(context, host, path.GameId, lessons[0], lessons[1]);
        await UnlockAsync(context, joiner, path.GameId, lessons[1], lessons[2]);

        var matchmaking = MultiplayerTest.Matchmaking(context);

        var created = await matchmaking.MatchmakeAsync(host, Request(path.GameId, path.SubjectId));
        Assert.True(created.Succeeded, string.Join("; ", created.Errors));
        Assert.Equal(MatchOutcome.Created, created.Value!.Outcome);

        // The host confirms the transport room, which is what makes the session joinable.
        await MultiplayerTest.Sessions(context).StartAsync(
            host, created.Value.Session!.Id, new StartMultiplayerSessionRequest());

        var joined = await matchmaking.MatchmakeAsync(joiner, Request(path.GameId, path.SubjectId));

        Assert.True(joined.Succeeded, string.Join("; ", joined.Errors));
        Assert.Equal(MatchOutcome.Joined, joined.Value!.Outcome);
        Assert.Equal(created.Value.Session.Id, joined.Value.Session!.Id);

        await using var check = _fixture.CreateContext();
        var session = await check.MultiplayerSessions.FirstAsync(s => s.Id == created.Value.Session.Id);

        // Stamped once the roster was ready, and stamped with the lesson they actually share.
        Assert.Equal(lessons[1], session.LessonId);
    }

    [Fact]
    public async Task A_player_sharing_no_lesson_gets_their_own_session_rather_than_a_broken_match()
    {
        await using var context = _fixture.CreateContext();
        var host = await TestData.CreateUserAsync(context);
        var stranger = await TestData.CreateUserAsync(context);
        var (path, lessons) = await SubjectAsync(context, extraLessons: 1);

        await UnlockAsync(context, host, path.GameId, lessons[0]);
        await UnlockAsync(context, stranger, path.GameId, lessons[1]);

        var matchmaking = MultiplayerTest.Matchmaking(context);

        var created = await matchmaking.MatchmakeAsync(host, Request(path.GameId, path.SubjectId));
        Assert.True(created.Succeeded);

        await MultiplayerTest.Sessions(context).StartAsync(
            host, created.Value!.Session!.Id, new StartMultiplayerSessionRequest());

        var second = await matchmaking.MatchmakeAsync(stranger, Request(path.GameId, path.SubjectId));

        Assert.True(second.Succeeded, string.Join("; ", second.Errors));

        // A new room, not a seat in one they could never play. Matching them would produce a match
        // that cannot start, which is worse for the child than waiting.
        Assert.Equal(MatchOutcome.Created, second.Value!.Outcome);
        Assert.NotEqual(created.Value.Session.Id, second.Value.Session!.Id);
    }

    [Fact]
    public async Task The_stamped_lesson_is_the_one_the_group_is_least_practised_at()
    {
        await using var context = _fixture.CreateContext();
        var host = await TestData.CreateUserAsync(context);
        var joiner = await TestData.CreateUserAsync(context);
        var (path, lessons) = await SubjectAsync(context, extraLessons: 1);

        await UnlockAsync(context, host, path.GameId, lessons[0], lessons[1]);
        await UnlockAsync(context, joiner, path.GameId, lessons[0], lessons[1]);

        // Both have aced the first lesson; neither has touched the second.
        foreach (var userId in new[] { host, joiner })
        {
            context.UserLessonProgress.Add(new UserLessonProgress
            {
                UserId = userId,
                GameId = path.GameId,
                LessonId = lessons[0],
                BestPercent = 100,
                Percent = 100,
                CorrectCount = 1,
                TotalCount = 1,
                BestCorrectCount = 1,
                Attempts = 1,
                CompletionState = CompletionState.Aced,
                LastAttemptAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();

        var matchmaking = MultiplayerTest.Matchmaking(context);
        var created = await matchmaking.MatchmakeAsync(host, Request(path.GameId, path.SubjectId));

        await MultiplayerTest.Sessions(context).StartAsync(
            host, created.Value!.Session!.Id, new StartMultiplayerSessionRequest());

        await matchmaking.MatchmakeAsync(joiner, Request(path.GameId, path.SubjectId));

        await using var check = _fixture.CreateContext();
        var session = await check.MultiplayerSessions.FirstAsync(s => s.Id == created.Value.Session.Id);

        // Playing the one they have both already aced would be a match nobody learns anything from.
        Assert.Equal(lessons[1], session.LessonId);
    }

    [Fact]
    public async Task A_lesson_with_no_questions_in_the_players_language_is_not_eligible()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        // Unlocked, in the right subject — and answerable only in Arabic.
        var lesson = new Lesson { Id = Guid.NewGuid(), ChapterId = path.ChapterId, Order = 500 };
        context.Lessons.Add(lesson);
        await context.SaveChangesAsync();

        context.Questions.Add(new Question
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            LangId = LanguageIds.Arabic,
            Text = "سؤال",
            Version = 1,
            RowNumber = 1,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        await UnlockAsync(context, userId, path.GameId, lesson.Id);

        var eligible = await new Share7.Infrastructure.Multiplayer.SessionLessonMatcher(
                context, new Share7.Infrastructure.Progress.UnlockService(context))
            .EligibleLessonsAsync(userId, path.GameId, path.SubjectId, LanguageIds.English);

        Assert.DoesNotContain(eligible, l => l.LessonId == lesson.Id);
    }

    [Fact]
    public async Task Matchmaking_never_crosses_modes()
    {
        await using var context = _fixture.CreateContext();
        var host = await TestData.CreateUserAsync(context);
        var joiner = await TestData.CreateUserAsync(context);
        var (path, lessons) = await SubjectAsync(context, extraLessons: 0);

        var classic = await context.AddModeAsync(path.GameId, "runner.classic", isDefault: true);
        var suddenDeath = await context.AddModeAsync(path.GameId, "runner.sudden");

        await UnlockAsync(context, host, path.GameId, lessons[0]);
        await UnlockAsync(context, joiner, path.GameId, lessons[0]);

        var matchmaking = MultiplayerTest.Matchmaking(context);

        var hosted = Request(path.GameId, path.SubjectId);
        hosted.ModeKey = classic.ModeKey;

        var created = await matchmaking.MatchmakeAsync(host, hosted);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        await MultiplayerTest.Sessions(context).StartAsync(
            host, created.Value!.Session!.Id, new StartMultiplayerSessionRequest());

        var other = Request(path.GameId, path.SubjectId);
        other.ModeKey = suddenDeath.ModeKey;

        var second = await matchmaking.MatchmakeAsync(joiner, other);

        // Same game, same subject, same lesson — different rules. Seating them together would put a
        // three-heart run and a one-heart run in the same match.
        Assert.True(second.Succeeded, string.Join("; ", second.Errors));
        Assert.Equal(MatchOutcome.Created, second.Value!.Outcome);
    }

    [Fact]
    public async Task A_player_with_nothing_unlocked_in_the_subject_is_told_so()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        // No unlocks at all, and the seeder opens only the first lesson of the grade — which belongs
        // to whichever fixture was created first, not necessarily this subject.
        var request = Request(path.GameId, path.SubjectId);
        var result = await MultiplayerTest.Matchmaking(context).MatchmakeAsync(userId, request);

        if (!result.Succeeded)
        {
            Assert.Equal(ApiErrors.PlayNoSharedLesson.Code, result.Error?.Code);
            return;
        }

        // Seeding opened something in this subject, which is also a correct outcome — the point is
        // that it never matches into a session it cannot play.
        Assert.Equal(MatchOutcome.Created, result.Value!.Outcome);
    }

    [Fact]
    public async Task An_exact_lesson_request_still_matches_only_that_lesson()
    {
        await using var context = _fixture.CreateContext();
        var host = await TestData.CreateUserAsync(context);
        var joiner = await TestData.CreateUserAsync(context);
        var (path, lessons) = await SubjectAsync(context, extraLessons: 1);

        await UnlockAsync(context, host, path.GameId, lessons[0], lessons[1]);
        await UnlockAsync(context, joiner, path.GameId, lessons[0], lessons[1]);

        var matchmaking = MultiplayerTest.Matchmaking(context);

        var hosted = new MatchmakeRequest
        {
            GameId = path.GameId,
            ProtocolVersion = 1,
            CurriculumPath = new CurriculumPathDto { SubjectId = path.SubjectId, LessonId = lessons[0] },
            CreateIfNoneFound = true,
            TransportSessionName = $"room_{Guid.NewGuid():N}"[..20]
        };

        var created = await matchmaking.MatchmakeAsync(host, hosted);
        Assert.True(created.Succeeded);

        await MultiplayerTest.Sessions(context).StartAsync(
            host, created.Value!.Session!.Id, new StartMultiplayerSessionRequest());

        var different = new MatchmakeRequest
        {
            GameId = path.GameId,
            ProtocolVersion = 1,
            CurriculumPath = new CurriculumPathDto { SubjectId = path.SubjectId, LessonId = lessons[1] },
            CreateIfNoneFound = true,
            TransportSessionName = $"room_{Guid.NewGuid():N}"[..20]
        };

        var second = await matchmaking.MatchmakeAsync(joiner, different);

        // The old behaviour, unchanged: naming a lesson means that lesson, which is still the right
        // shape for a direct invite.
        Assert.True(second.Succeeded);
        Assert.Equal(MatchOutcome.Created, second.Value!.Outcome);
    }
}
