using System.Text.Json.Nodes;
using Share7.Domain.Constants;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// <b>The frozen game contract.</b> Every endpoint the Unity client calls, called the way it calls
/// them, compared with a reviewed baseline.
/// <para>
/// The curriculum engine is about to be rebuilt underneath these routes — the generic node tree and
/// item bank become the source of truth, and a draft/review/release layer goes on top — with one
/// hard requirement: <b>the current Unity build keeps working with no changes</b>. These tests are
/// how that is proven rather than promised. If one fails, the client would see something different;
/// that is a stop, not a baseline to re-record, unless the client team has agreed to the change.
/// </para>
/// <para>
/// See <c>Share7/ContentStudioPhase0.md</c> for how to run and re-record them.
/// </para>
/// </summary>
/// <remarks>
/// **Run three times, against three API processes**, one per <c>Curriculum:ReadModel</c>
/// (<c>GameContractModes.cs</c>): the typed tables the game has always been served from, the node
/// tree and item bank that replace them, and Shadow, which serves the first and checks the second
/// against it on every read. All three are held to the same reviewed baselines — which is the
/// zero-Unity-change guarantee for the engine rebuild, proven rather than promised.
/// </remarks>
public abstract class GameContractScenarios
{
    private readonly ContractHost _host;
    private ContractData Data => _host.Data;

    protected GameContractScenarios(ContractHost host) => _host = host;

    /// <summary>Runs after each scenario has matched its baseline. Shadow uses it to check the tally.</summary>
    protected virtual Task AfterScenarioAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lookups_before_sign_in()
    {
        var snapshot = new ContractSnapshot(Data);

        await snapshot.GetAsync(_host.Http, "/api/languages", "anonymous");
        await snapshot.GetAsync(_host.Http, "/api/grades", "anonymous");
        await snapshot.GetAsync(_host.Http, $"/api/grades?langId={LanguageIds.Arabic}", "anonymous");
        await snapshot.GetAsync(_host.Http, "/api/time", "anonymous");

        // Everything else needs a token — the shape of the refusal is part of the contract too.
        await snapshot.GetAsync(_host.Http, $"/api/terms?gradeId={Data.GradeId}", "anonymous");

        snapshot.Verify("lookups");
        await AfterScenarioAsync();
    }

    [Fact]
    public async Task Signing_in_and_refreshing()
    {
        var snapshot = new ContractSnapshot(Data);
        var student = Data.EnglishStudent;

        var login = await snapshot.PostAsync(_host.Http, "/api/auth/login",
            new { username = student.Username, password = ContractData.Password }, "anonymous");

        await snapshot.PostAsync(_host.Http, "/api/auth/login",
            new { username = student.Username, password = "not-the-password" }, "anonymous");

        await snapshot.PostAsync(_host.Http, "/api/auth/refresh",
            new { refreshToken = login!["refreshToken"]!.GetValue<string>() }, "anonymous");

        using var client = await _host.SignedInAsync(student);
        await snapshot.GetAsync(client, "/api/auth/me", "student-en");

        snapshot.Verify("auth");
        await AfterScenarioAsync();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Browsing_the_curriculum(string language)
    {
        var snapshot = new ContractSnapshot(Data);
        var (student, caller) = Student(language);
        using var client = await _host.SignedInAsync(student);

        await snapshot.GetAsync(client, $"/api/terms?gradeId={Data.GradeId}", caller);
        await snapshot.GetAsync(client, "/api/terms", caller);
        await snapshot.GetAsync(client, $"/api/subjects?termId={Data.FirstTermId}", caller);
        await snapshot.GetAsync(client, $"/api/chapters?subjectId={Data.ScienceId}", caller);
        await snapshot.GetAsync(client, $"/api/lessons?chapterId={Data.MatterChapterId}", caller);

        snapshot.Verify($"curriculum.{language}");
        await AfterScenarioAsync();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Downloading_and_caching_questions(string language)
    {
        var snapshot = new ContractSnapshot(Data);
        var (student, caller) = Student(language);
        using var client = await _host.SignedInAsync(student);

        var lessons = new[] { Data.SolidsLessonId, Data.LiquidsLessonId, Data.GasesLessonId, Data.UnknownLessonId };

        await snapshot.GetAsync(client, $"/api/lessons/{Data.SolidsLessonId}/questions/version", caller);
        await snapshot.PostAsync(client, "/api/lessons/questions/versions", new { lessonIds = lessons }, caller);
        await snapshot.GetAsync(client, $"/api/lessons/{Data.SolidsLessonId}/questions", caller);
        await snapshot.GetAsync(client, $"/api/lessons/{Data.LiquidsLessonId}/questions", caller);
        await snapshot.GetAsync(client, $"/api/lessons/{Data.GasesLessonId}/questions", caller);
        await snapshot.GetAsync(client, $"/api/lessons/{Data.UnknownLessonId}/questions", caller);

        await snapshot.GetAsync(client, $"/api/lessons/{Data.SolidsLessonId}/recovery-questions/version", caller);
        await snapshot.PostAsync(client, "/api/lessons/recovery-questions/versions", new { lessonIds = lessons }, caller);
        await snapshot.GetAsync(client, $"/api/lessons/{Data.SolidsLessonId}/recovery-questions", caller);

        snapshot.Verify($"questions.{language}");
        await AfterScenarioAsync();
    }

    /// <summary>
    /// A whole journey for one student: first contact, a refused attempt, a perfect run, its retry,
    /// and every progress read. One test because each step depends on the one before it.
    /// </summary>
    [Fact]
    public async Task Playing_a_lesson_and_reading_progress()
    {
        var snapshot = new ContractSnapshot(Data);
        var student = Data.EnglishStudent;
        const string caller = "student-en";
        using var client = await _host.SignedInAsync(student);

        var game = Data.GameId;
        var progress = $"/api/progress/games/{game}";

        // First contact opens the start of the grade.
        await snapshot.GetAsync(client, $"{progress}/snapshot?gradeId={Data.GradeId}", caller);

        // Refusals: a lesson with nothing to play, and one that is still locked.
        await snapshot.PostAsync(client, "/api/progress/attempts", Attempt(Data.GasesLessonId, [], "contract-empty"), caller);
        await snapshot.PostAsync(client, "/api/progress/attempts", Attempt(Data.LiquidsLessonId, [], "contract-locked"), caller);

        // A perfect run, then the client retrying it — the retry must be answered, identically, and
        // must not count twice.
        var perfect = Attempt(Data.SolidsLessonId, Data.Answers[(Data.SolidsLessonId, LanguageIds.English)], "contract-run-1");
        var first = await snapshot.PostAsync(client, "/api/progress/attempts", perfect, caller);
        var retry = await snapshot.PostAsync(client, "/api/progress/attempts", perfect, caller);

        Assert.True(JsonNode.DeepEquals(first, retry), "A retried attempt must return exactly what the original returned.");

        await snapshot.GetAsync(client, $"{progress}/lessons/{Data.SolidsLessonId}", caller);
        await snapshot.GetAsync(client, $"{progress}/lessons/{Data.SolidsLessonId}/wrong-questions", caller);
        await snapshot.GetAsync(client, $"{progress}/chapters/{Data.MatterChapterId}", caller);
        await snapshot.GetAsync(client, $"{progress}/subjects/{Data.ScienceId}", caller);
        await snapshot.GetAsync(client, $"{progress}/terms/{Data.FirstTermId}", caller);
        await snapshot.GetAsync(client, $"{progress}/grades/{Data.GradeId}", caller);
        await snapshot.GetAsync(client, $"{progress}/snapshot?gradeId={Data.GradeId}", caller);

        snapshot.Verify("progress.en");
        await AfterScenarioAsync();
    }

    /// <summary>
    /// The Arabic reader's version of the journey, with one wrong answer — and the lesson that has
    /// no Arabic questions, which must not block the chapter for them.
    /// </summary>
    [Fact]
    public async Task Playing_in_arabic_with_a_mistake()
    {
        var snapshot = new ContractSnapshot(Data);
        var student = Data.ArabicStudent;
        const string caller = "student-ar";
        using var client = await _host.SignedInAsync(student);

        var progress = $"/api/progress/games/{Data.GameId}";
        await snapshot.GetAsync(client, $"{progress}/snapshot?gradeId={Data.GradeId}", caller);

        var answers = Data.Answers[(Data.SolidsLessonId, LanguageIds.Arabic)];

        // Two right, the third answered with a choice that is not its correct one.
        var run = answers
            .Select((a, i) => i < 2
                ? new { questionId = a.QuestionId, choiceId = (Guid?)a.ChoiceId, elapsedMs = 4000 }
                : new { questionId = a.QuestionId, choiceId = (Guid?)null, elapsedMs = 4000 })
            .ToArray();

        await snapshot.PostAsync(client, "/api/progress/attempts",
            new { gameId = Data.GameId, lessonId = Data.SolidsLessonId, answers = run, requestId = "contract-run-ar" }, caller);

        await snapshot.GetAsync(client, $"{progress}/lessons/{Data.SolidsLessonId}/wrong-questions", caller);
        await snapshot.GetAsync(client, $"{progress}/chapters/{Data.MatterChapterId}", caller);
        await snapshot.GetAsync(client, $"{progress}/snapshot?gradeId={Data.GradeId}", caller);

        snapshot.Verify("progress.ar");
        await AfterScenarioAsync();
    }

    [Fact]
    public async Task Games_and_learning_reads()
    {
        var snapshot = new ContractSnapshot(Data);
        using var client = await _host.SignedInAsync(Data.EnglishStudent);
        const string caller = "student-en";

        await snapshot.GetAsync(client, "/api/games", caller);
        await snapshot.GetAsync(client, $"/api/games/{Data.GameId}", caller);
        await snapshot.GetAsync(client, $"/api/learning/targets?nodeId={Data.SolidsLessonId}", caller);
        await snapshot.GetAsync(client, "/api/learning/exams", caller);

        snapshot.Verify("games-and-learning");
        await AfterScenarioAsync();
    }

    // ------------------------------------------------------------- helpers

    private (ContractStudent Student, string Caller) Student(string language) =>
        language == "ar" ? (Data.ArabicStudent, "student-ar") : (Data.EnglishStudent, "student-en");

    private object Attempt(Guid lessonId, IReadOnlyList<(Guid QuestionId, Guid ChoiceId)> answers, string requestId) => new
    {
        gameId = Data.GameId,
        lessonId,
        answers = answers.Select(a => new { questionId = a.QuestionId, choiceId = a.ChoiceId, elapsedMs = 4000 }).ToArray(),
        requestId
    };
}
