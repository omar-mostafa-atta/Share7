using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Hand-typed question publishing, over the real database.
/// <para>
/// The behaviour worth pinning is not that a form can be submitted — it is that the manual path
/// obeys the same rules the Excel path always has: a published set is never edited in place, a bad
/// question rejects the whole request, and the version counter moves exactly once per publish.
/// A publisher that quietly diverged on any of those would leave two ways to author a lesson that
/// disagree about what a lesson is.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ManualQuestionEntryTests
{
    private readonly SqlServerFixture _fixture;

    public ManualQuestionEntryTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- publishing ------------------------------------------------------------------------

    [Fact]
    public async Task Replacing_publishes_version_one_when_nothing_was_published_before()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        var result = await Service(context).PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Capital of Egypt?", "Cairo")));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Version);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.ReplacedCount);

        var published = await ActiveAsync(context, lessonId);
        var question = Assert.Single(published);
        Assert.Equal("Capital of Egypt?", question.Text);

        // Correctness is positional on the way in and by id on the way out — this is the join
        // between the two, and it is the one thing a mis-wired publisher would silently invert.
        var correct = question.Choices.Single(c => c.Id == question.CorrectChoiceId);
        Assert.Equal("Cairo", correct.Text);
        Assert.Equal(3, question.Choices.Count);
    }

    [Fact]
    public async Task Appending_keeps_the_published_questions_and_adds_the_new_ones_after_them()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);
        var service = Service(context);

        await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("First?", "1")));

        var result = await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Append, Question("Second?", "2")));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Version);

        // Both questions are in the new version, so both are "imported" — the count describes what
        // was written, not only what was typed.
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.ReplacedCount);

        var published = await ActiveAsync(context, lessonId);
        Assert.Equal(["First?", "Second?"], published.Select(q => q.Text));
        Assert.Equal([1, 2], published.Select(q => q.RowNumber));
    }

    [Fact]
    public async Task Appending_carries_the_correct_answer_forward_rather_than_assuming_the_first_one()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);
        var service = Service(context);

        await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Symbol for iron?", "Fe", "Ir", "F")));

        // Move the correct answer off the first slot, the way a corrected row could legitimately
        // sit in storage. An append that trusted position would re-key the question to "Ir".
        var stored = await context.Questions
            .Include(q => q.Choices)
            .SingleAsync(q => q.LessonId == lessonId && q.IsActive);

        stored.CorrectChoiceId = stored.Choices.Single(c => c.OrderIndex == 1).Id;
        await context.SaveChangesAsync();

        await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Append, Question("Second?", "2")));

        var carried = (await ActiveAsync(context, lessonId)).First();
        Assert.Equal("Symbol for iron?", carried.Text);
        Assert.Equal("Ir", carried.Choices.Single(c => c.Id == carried.CorrectChoiceId).Text);
    }

    [Fact]
    public async Task Replacing_retires_the_previous_questions_instead_of_deleting_them()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);
        var service = Service(context);

        await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Old?", "old")));

        var originalId = await context.Questions.Where(q => q.LessonId == lessonId).Select(q => q.Id).SingleAsync();

        await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("New?", "new")));

        // Student progress references question ids, so the row has to survive its retirement.
        // Read untracked on purpose: retirement is an ExecuteUpdate, which goes straight to the
        // database and leaves the change tracker holding the pre-retirement copy — a tracked read
        // here returns that stale instance and the assertion passes against nothing real.
        var retired = await context.Questions.AsNoTracking().SingleAsync(q => q.Id == originalId);
        Assert.False(retired.IsActive);
        Assert.NotNull(retired.DeactivatedAt);
    }

    [Fact]
    public async Task Publishing_one_language_leaves_the_other_untouched()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);
        var service = Service(context);

        await service.PublishManualAsync(
            lessonId, LanguageIds.Arabic, Request(ManualQuestionMode.Replace, Question("سؤال؟", "نعم")));

        var english = await service.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Question?", "yes")));

        // Versions are per language: the English set starts at 1 even though Arabic already has one.
        Assert.Equal(1, english.Version);
        Assert.Equal(0, english.ReplacedCount);

        var arabic = await context.Questions
            .CountAsync(q => q.LessonId == lessonId && q.LangId == LanguageIds.Arabic && q.IsActive);

        Assert.Equal(1, arabic);
    }

    // ---- the audit trail -------------------------------------------------------------------

    [Fact]
    public async Task A_manual_publish_is_recorded_as_manual_with_no_file_name()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        await Service(context).PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Typed?", "yes")));

        var audit = await context.LessonQuestionUploads.SingleAsync(u => u.LessonId == lessonId);

        // "Why did this change" has to be answerable, and an empty file name alone cannot say
        // whether a file was missing or never involved.
        Assert.Equal(QuestionSetSource.ManualEntry, audit.Source);
        Assert.Equal(string.Empty, audit.FileName);
        Assert.Equal(1, audit.QuestionCount);
    }

    // ---- validation ------------------------------------------------------------------------

    [Fact]
    public async Task A_question_whose_answers_repeat_is_refused_and_nothing_is_published()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        var result = await Service(context).PublishManualAsync(
            lessonId,
            LanguageIds.English,
            Request(ManualQuestionMode.Replace,
                Question("Fine?", "yes", "no", "maybe"),
                Question("Broken?", "same", "same", "other")));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Row == 2 && e.Message.Contains("different from each other"));

        // All-or-nothing: the valid first question must not have been published on its own.
        Assert.Empty(await ActiveAsync(context, lessonId));
        Assert.False(await context.LessonQuestionSets.AnyAsync(s => s.LessonId == lessonId));
    }

    [Fact]
    public async Task Case_alone_makes_two_answers_different()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        // The rule the Excel path was deliberately loosened for: a real science sheet asks for
        // iron's symbol with Fe / FE / fe, where capitalisation is the thing being tested.
        var result = await Service(context).PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Symbol for iron?", "Fe", "FE", "fe")));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task An_empty_choice_names_which_one_is_missing()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        var result = await Service(context).PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Half typed?", "yes", "no", "   ")));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Row == 1 && e.Message.Contains("Wrong choice 2"));
    }

    [Fact]
    public async Task A_request_without_a_mode_is_refused_rather_than_assumed()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        var result = await Service(context).PublishManualAsync(
            lessonId,
            LanguageIds.English,
            new ManualQuestionSetRequest { Mode = null, Questions = [Question("Anything?", "yes")] });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Message.Contains("mode is required"));
    }

    [Fact]
    public async Task A_request_with_no_questions_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        var result = await Service(context).PublishManualAsync(
            lessonId, LanguageIds.English, new ManualQuestionSetRequest { Mode = ManualQuestionMode.Append, Questions = [] });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Message.Contains("no questions"));
    }

    [Fact]
    public async Task An_unknown_lesson_is_refused_before_anything_is_validated()
    {
        await using var context = _fixture.CreateContext();

        var result = await Service(context).PublishManualAsync(
            Guid.NewGuid(), LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Anywhere?", "no")));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Message.Contains("Lesson not found"));
    }

    // ---- the recovery pool -----------------------------------------------------------------

    [Fact]
    public async Task Recovery_questions_publish_the_same_way_on_their_own_counter()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await CreateEmptyLessonAsync(context);

        await Service(context).PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Main?", "yes")));

        var recovery = new RecoveryQuestionImportService(context);

        var first = await recovery.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Replace, Question("Recovery?", "yes")));

        // Independent versioning is the reason the two pools are separate tables at all: a lesson
        // can sit at questions v1 / recovery v1 with neither publish having moved the other.
        Assert.Equal(1, first.Version);
        Assert.Equal(1, await context.LessonQuestionSets
            .Where(s => s.LessonId == lessonId && s.LangId == LanguageIds.English)
            .Select(s => s.Version)
            .SingleAsync());

        var appended = await recovery.PublishManualAsync(
            lessonId, LanguageIds.English, Request(ManualQuestionMode.Append, Question("Another recovery?", "yes")));

        Assert.Equal(2, appended.Version);
        Assert.Equal(2, appended.ImportedCount);

        // The main set is still where it was.
        Assert.Equal(1, await context.Questions.CountAsync(q => q.LessonId == lessonId && q.IsActive));

        var audit = await context.LessonRecoveryQuestionUploads
            .Where(u => u.LessonId == lessonId)
            .OrderBy(u => u.Version)
            .ToListAsync();

        Assert.All(audit, row => Assert.Equal(QuestionSetSource.ManualEntry, row.Source));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static QuestionImportService Service(ApplicationDbContext context) => new(context);

    private static ManualQuestionInput Question(
        string text, string correct, string wrong1 = "wrong one", string wrong2 = "wrong two") =>
        new() { Text = text, CorrectChoice = correct, WrongChoice1 = wrong1, WrongChoice2 = wrong2 };

    private static ManualQuestionSetRequest Request(ManualQuestionMode mode, params ManualQuestionInput[] questions) =>
        new() { Mode = mode, Questions = [.. questions] };

    private static async Task<List<Question>> ActiveAsync(ApplicationDbContext context, Guid lessonId) =>
        await context.Questions
            .AsNoTracking()
            .Include(q => q.Choices)
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .OrderBy(q => q.RowNumber)
            .ToListAsync();

    /// <summary>
    /// A lesson with no questions at all. <see cref="TestData.CreateCurriculumPathAsync"/> seeds
    /// one, which would make "published version 1" indistinguishable from "the fixture's leftover".
    /// </summary>
    private static async Task<Guid> CreateEmptyLessonAsync(ApplicationDbContext context)
    {
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.Questions
            .Where(q => q.LessonId == path.LessonId)
            .ExecuteDeleteAsync();

        return path.LessonId;
    }
}
