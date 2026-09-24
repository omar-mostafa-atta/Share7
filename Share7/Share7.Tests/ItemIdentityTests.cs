using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Structure;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Item identity — Phase 1 of the educational rebuild.
/// <para>
/// **The fault these tests pin is the one that silently severed every child's history.** Before
/// item identity, the English and Arabic renderings of one question were two unrelated GUIDs, and
/// republishing a lesson minted a third set: switching language lost a learner's record of that
/// question, and so did fixing a typo. Nothing in the old schema could tell you that three rows
/// were the same question, because nothing in the old schema believed they were.
/// </para>
/// <para>See <c>Docs/EducationalArchitecture.md</c> §1.5, §10.1 and §20.4.</para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ItemIdentityTests
{
    private readonly SqlServerFixture _fixture;

    public ItemIdentityTests(SqlServerFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------- one item, two languages

    /// <summary>
    /// The heart of it. A paired publish writes English and Arabic from one sheet row, and both
    /// renderings must resolve to **one** item and **one** version — otherwise a child answering in
    /// Arabic in September and English in March has two disconnected halves of a history.
    /// </summary>
    [Fact]
    public async Task Both_language_renderings_of_one_sheet_row_share_one_item_version()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        await PublishPairedAsync(context, lessonId, rows: 3);

        await using var check = _fixture.CreateContext();
        var questions = await check.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .Select(q => new { q.RowNumber, q.LangId, q.ItemVersionId })
            .ToListAsync();

        Assert.Equal(6, questions.Count);

        foreach (var row in questions.GroupBy(q => q.RowNumber))
        {
            // Two renderings, two languages, one item version between them.
            Assert.Equal(2, row.Count());
            Assert.Equal(2, row.Select(q => q.LangId).Distinct().Count());
            Assert.Single(row.Select(q => q.ItemVersionId).Distinct());
        }

        // Three sheet rows are three items, not six.
        var itemIds = await check.ItemVersions
            .AsNoTracking()
            .Where(v => questions.Select(q => q.ItemVersionId).Contains(v.Id))
            .Select(v => v.ItemId)
            .Distinct()
            .ToListAsync();

        Assert.Equal(3, itemIds.Count);
    }

    // ------------------------------------------------------- lineage across a republish

    /// <summary>
    /// Republishing a lesson used to mint strangers: new GUIDs with no relationship to the rows
    /// they replaced. Now the same sheet row is recognised as the same item, and the republish
    /// becomes a new **version** of it — which is what makes an item's history survive a typo fix.
    /// </summary>
    [Fact]
    public async Task Republishing_a_lesson_keeps_the_item_and_adds_a_version()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        await PublishPairedAsync(context, lessonId, rows: 2);

        await using var second = _fixture.CreateContext();
        var before = await ItemIdsAsync(second, lessonId);

        await PublishPairedAsync(second, lessonId, rows: 2, suffix: " (reworded)");

        await using var check = _fixture.CreateContext();
        var after = await ItemIdsAsync(check, lessonId);

        // Same items, not new ones.
        Assert.Equal(before.Order(), after.Order());

        foreach (var itemId in after)
        {
            var versions = await check.ItemVersions
                .AsNoTracking()
                .Where(v => v.ItemId == itemId)
                .OrderBy(v => v.VersionNumber)
                .ToListAsync();

            Assert.Equal(2, versions.Count);
            Assert.Equal([1, 2], versions.Select(v => v.VersionNumber));

            // The old version is retired rather than edited, so a response against it stays
            // interpretable against exactly the wording the learner saw.
            Assert.NotNull(versions[0].RetiredAtUtc);
            Assert.Null(versions[1].RetiredAtUtc);

            // False for every historical link: nobody recorded whether the edit was cosmetic, and
            // a wrong `true` would silently pool statistics across a rewritten question.
            Assert.All(versions, v => Assert.False(v.PsychometricContinuity));
        }
    }

    // ------------------------------------------------------- evidence joins across languages

    /// <summary>
    /// The payoff. Answer a question in English, then the Arabic rendering of the same question,
    /// and the log must read as **one item answered twice** rather than two items answered once —
    /// which also means the second answer is correctly not a first encounter, and therefore not
    /// assessment-grade.
    /// </summary>
    [Fact]
    public async Task Answering_the_other_language_rendering_continues_the_same_item_history()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.UnlockLessonAsync(userId, path);

        // The Arabic twin of the fixture's English question, sharing its item version — exactly
        // what the paired importer writes.
        var english = await context.Questions
            .Include(q => q.Choices)
            .FirstAsync(q => q.Id == path.QuestionId);

        var arabic = new Question
        {
            Id = Guid.NewGuid(),
            ItemVersionId = english.ItemVersionId,
            LessonId = english.LessonId,
            LangId = LanguageIds.Arabic,
            Text = "رمز الحديد؟",
            Version = english.Version,
            RowNumber = english.RowNumber,
            CreatedAt = DateTime.UtcNow
        };

        arabic.Choices =
        [
            new QuestionChoice { Id = Guid.NewGuid(), QuestionId = arabic.Id, Text = "Fe", OrderIndex = 0 },
            new QuestionChoice { Id = Guid.NewGuid(), QuestionId = arabic.Id, Text = "Ir", OrderIndex = 1 },
            new QuestionChoice { Id = Guid.NewGuid(), QuestionId = arabic.Id, Text = "F", OrderIndex = 2 }
        ];

        arabic.CorrectChoiceId = arabic.Choices.First().Id;
        context.Questions.Add(arabic);
        await context.SaveChangesAsync();

        await SubmitAsync(context, userId, path, english.Id, english.CorrectChoiceId, "en-run");

        // The child switches language. Everything about the grading changes — a different row, a
        // different choice set, a different stem — and none of it should change whose question it is.
        //
        await SubmitAsync(
            context, userId, path, arabic.Id, arabic.CorrectChoiceId, "ar-run", LanguageIds.Arabic);

        await using var check = _fixture.CreateContext();
        var responses = await check.LearnerResponses
            .AsNoTracking()
            .Where(r => r.LearnerId == userId)
            .OrderBy(r => r.Sequence)
            .ToListAsync();

        Assert.Equal(2, responses.Count);

        // One item. Two renderings. Two ordinals.
        Assert.Single(responses.Select(r => r.ItemId).Distinct());
        Assert.Equal(2, responses.Select(r => r.ItemLocalizationId).Distinct().Count());
        Assert.Equal([1, 2], responses.Select(r => r.AttemptOrdinal));
        Assert.Equal([true, false], responses.Select(r => r.IsFirstEncounter));
    }

    // ------------------------------------------------------- the target bootstrap

    /// <summary>
    /// A question mapped to no learning target can never be measured, however many children answer
    /// it. So publishing mints the lesson's placeholder target and points the item at it — and the
    /// target says plainly that it is a placeholder.
    /// </summary>
    [Fact]
    public async Task Publishing_maps_every_item_to_its_lessons_placeholder_target()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        await PublishPairedAsync(context, lessonId, rows: 2);

        await using var check = _fixture.CreateContext();
        var itemIds = await ItemIdsAsync(check, lessonId);

        var mappings = await check.ItemTargetMappings
            .AsNoTracking()
            .Where(m => itemIds.Contains(m.ItemId))
            .ToListAsync();

        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, m => Assert.True(m.IsPrimary));

        // One target for the lesson, shared by both items, flagged as the stand-in it is.
        var targetId = Assert.Single(mappings.Select(m => m.TargetId).Distinct());
        var target = await check.LearningTargets.AsNoTracking().FirstAsync(t => t.Id == targetId);

        Assert.True(target.IsPlaceholder);
        Assert.Equal(TargetKindsKey, target.TargetKindKey);
        Assert.Equal(EducationIds.PlaceholderFramework, target.FrameworkId);

        // And the node knows what it teaches.
        Assert.True(await check.NodeItemMappings
            .AsNoTracking()
            .AnyAsync(m => m.NodeId == lessonId && itemIds.Contains(m.ItemId)));
    }

    private const string TargetKindsKey = "lesson_placeholder";

    // ------------------------------------------------------- the node projection

    /// <summary>
    /// The projection keeps the legacy ids. That single decision is what makes it safe to run on a
    /// live product: every stored reference, every client cache and every
    /// <c>UserLessonProgress.LessonId</c> keeps resolving, because the two shapes are the same
    /// identifiers rather than two sets that have to be kept in step.
    /// </summary>
    [Fact]
    public async Task The_node_projection_preserves_the_legacy_ids_and_the_tree_shape()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);

        await new CurriculumProjector(context).SyncAsync();

        await using var check = _fixture.CreateContext();

        var lesson = await check.CurriculumNodes.AsNoTracking().FirstAsync(n => n.Id == path.LessonId);
        Assert.Equal("lesson", lesson.KindKey);
        Assert.Equal(path.ChapterId, lesson.ParentNodeId);
        Assert.Equal(4, lesson.Depth);
        Assert.True(lesson.IsPlayable);

        // The materialised path walks all the way to the grade, so "everything under this subject"
        // is one prefix scan instead of a recursive query.
        Assert.Contains(path.GradeId.ToString("D"), lesson.Path);
        Assert.Contains(path.SubjectId.ToString("D"), lesson.Path);
        Assert.EndsWith(path.LessonId.ToString("D"), lesson.Path);

        var chapter = await check.CurriculumNodes.AsNoTracking().FirstAsync(n => n.Id == path.ChapterId);
        Assert.Equal("chapter", chapter.KindKey);
        Assert.False(chapter.IsPlayable);
    }

    [Fact]
    public async Task Syncing_the_projection_twice_changes_nothing_the_second_time()
    {
        await using var context = _fixture.CreateContext();
        await TestData.CreateCurriculumPathAsync(context);

        await new CurriculumProjector(context).SyncAsync();

        await using var second = _fixture.CreateContext();
        var report = await new CurriculumProjector(second).SyncAsync();

        Assert.Equal(0, report.Added);
        Assert.Equal(0, report.Updated);
        Assert.Equal(0, report.Retired);
        Assert.False(report.ChangedAnything);
    }

    /// <summary>
    /// A lesson removed from the tree is **retired, never deleted**. Responses, item mappings and
    /// target mappings name that id; removing the row would strand all of them and destroy the only
    /// record of what a child was studying when they studied it.
    /// </summary>
    [Fact]
    public async Task A_removed_lesson_retires_its_node_rather_than_deleting_it()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        await new CurriculumProjector(context).SyncAsync();

        await context.Questions.Where(q => q.LessonId == path.LessonId).ExecuteDeleteAsync();
        await context.Lessons.Where(l => l.Id == path.LessonId).ExecuteDeleteAsync();

        await using var second = _fixture.CreateContext();
        var report = await new CurriculumProjector(second).SyncAsync();

        Assert.Equal(1, report.Retired);

        await using var check = _fixture.CreateContext();
        var node = await check.CurriculumNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == path.LessonId);

        Assert.NotNull(node);
        Assert.NotNull(node.RetiredAtUtc);
    }

    // ------------------------------------------------------- helpers

    private static async Task<Guid> EmptyLessonAsync(ApplicationDbContext context)
    {
        var path = await TestData.CreateCurriculumPathAsync(context);

        // The fixture's own question would occupy row 1 and confuse the row counts below — and so
        // would the item identity minted for it, which a republish at row 1 would otherwise find by
        // its lineage key and continue. Removed whole, so the lesson is genuinely empty.
        var itemIds = await context.Questions
            .Where(q => q.LessonId == path.LessonId)
            .Select(q => q.ItemVersion!.ItemId)
            .ToListAsync();

        await context.Questions.Where(q => q.LessonId == path.LessonId).ExecuteDeleteAsync();
        await context.ItemVersions.Where(v => itemIds.Contains(v.ItemId)).ExecuteDeleteAsync();
        await context.NodeItemMappings.Where(m => itemIds.Contains(m.ItemId)).ExecuteDeleteAsync();
        await context.ItemTargetMappings.Where(m => itemIds.Contains(m.ItemId)).ExecuteDeleteAsync();
        await context.Items.Where(i => itemIds.Contains(i.Id)).ExecuteDeleteAsync();

        return path.LessonId;
    }

    private static async Task PublishPairedAsync(
        ApplicationDbContext context, Guid lessonId, int rows, string suffix = "")
    {
        var result = await EngineTest.Sheet(context)
            .SaveAsync(lessonId, Sheet(rows, suffix));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    private static SaveLessonSheetRequest Sheet(int rows, string suffix) => new()
    {
        Rows =
        [
            .. Enumerable.Range(1, rows).Select(i => new LessonSheetRow
            {
                RowNumber = i,
                IsRecovery = false,
                QuestionEn = $"English question {i}{suffix}",
                CorrectEn = $"right {i}",
                WrongEn1 = $"wrong {i}a",
                WrongEn2 = $"wrong {i}b",
                QuestionAr = $"سؤال {i}{suffix}",
                CorrectAr = $"صح {i}",
                WrongAr1 = $"خطأ {i}أ",
                WrongAr2 = $"خطأ {i}ب"
            }),

            // The importer refuses a lesson with no recovery pool, so the sheet carries one.
            // Its row number continues past the main pool: row numbers pair a question's two
            // languages and must be unique within the lesson across both pools.
            new LessonSheetRow
            {
                RowNumber = rows + 1,
                IsRecovery = true,
                QuestionEn = "Recovery question", CorrectEn = "right", WrongEn1 = "a", WrongEn2 = "b",
                QuestionAr = "سؤال تعويضي", CorrectAr = "صح", WrongAr1 = "أ", WrongAr2 = "ب"
            }
        ]
    };

    private static Task<List<Guid>> ItemIdsAsync(ApplicationDbContext context, Guid lessonId) =>
        context.NodeItemMappings
            .AsNoTracking()
            .Where(m => m.NodeId == lessonId && m.Role == NodeItemRole.Core)
            .Select(m => m.ItemId)
            .Distinct()
            .ToListAsync();

    private static async Task SubmitAsync(
        ApplicationDbContext context, Guid userId, CurriculumPathFixture path,
        Guid questionId, Guid choiceId, string requestId, Guid? langId = null)
    {
        var result = await RewardTestExtensions.CreateProgressService(context, userId, langId)
            .SubmitAttemptAsync(userId, new Application.Progress.Models.SubmitAttemptRequest
            {
                GameId = path.GameId,
                LessonId = path.LessonId,
                RequestId = requestId,
                Answers =
                [
                    new Application.Progress.Models.SubmittedAnswer
                    {
                        QuestionId = questionId,
                        ChoiceId = choiceId
                    }
                ]
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }
}
