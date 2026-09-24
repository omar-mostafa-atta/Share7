using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine;
using Share7.Application.Engine.Models;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The one writer of lesson content (Content Studio plan, Phase 2, steps A3–A5), over the real
/// database.
/// <para>
/// What is pinned here is what the game would notice: which question ids survive a publish, when a
/// set's version moves, that the recovery pool's old table stays in step, and that an old admin
/// path cannot slip past the rules it has always had.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ContentPublisherTests
{
    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    private readonly SqlServerFixture _fixture;

    public ContentPublisherTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Publishing_the_same_content_again_changes_nothing_the_game_sees()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        var first = await PublishSheetAsync(context, lessonId, Rows(3));
        var before = await ServedAsync(context, lessonId);

        var again = await PublishSheetAsync(context, lessonId, Rows(3));
        var after = await ServedAsync(context, lessonId);

        // Same rows, same ids, same versions: a device that cached version N has nothing to fetch.
        Assert.Equal(before, after);
        Assert.Equal(first.MainVersion, again.MainVersion);
        Assert.Equal(first.RecoveryVersion, again.RecoveryVersion);
        Assert.Equal(0, again.ReplacedCount);
    }

    [Fact]
    public async Task Rewording_one_question_keeps_every_other_question_id_and_moves_each_version_once()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        await PublishSheetAsync(context, lessonId, Rows(3));
        var before = await ServedAsync(context, lessonId);

        var reworded = Rows(3);
        reworded[1].QuestionEn += " (reworded)";
        reworded[1].QuestionAr += " (معدل)";

        await PublishSheetAsync(context, lessonId, reworded);
        var after = await ServedAsync(context, lessonId);

        // Questions 1 and 3, both languages: untouched rows.
        foreach (var row in new[] { 1, 3 })
        {
            Assert.Equal(before.Single(r => r.Row == row && r.LangId == En).Id, after.Single(r => r.Row == row && r.LangId == En).Id);
            Assert.Equal(before.Single(r => r.Row == row && r.LangId == Ar).Id, after.Single(r => r.Row == row && r.LangId == Ar).Id);
        }

        // Question 2: new rows, under a new version of the same item.
        var oldQ2 = before.Single(r => r.Row == 2 && r.LangId == En);
        var newQ2 = after.Single(r => r.Row == 2 && r.LangId == En);
        Assert.NotEqual(oldQ2.Id, newQ2.Id);
        Assert.Equal(oldQ2.ItemId, newQ2.ItemId);
        Assert.NotEqual(oldQ2.ItemVersionId, newQ2.ItemVersionId);

        var oldVersion = await context.ItemVersions.AsNoTracking().SingleAsync(v => v.Id == oldQ2.ItemVersionId);
        Assert.NotNull(oldVersion.RetiredAtUtc);

        // Each language's main set moved by exactly one; the recovery sets did not move at all.
        var sets = await context.PublishedItemSets.AsNoTracking().Where(s => s.NodeId == lessonId).ToListAsync();
        Assert.Equal(2, sets.Single(s => s.Role == NodeItemRole.Core && s.LangId == En).Version);
        Assert.Equal(2, sets.Single(s => s.Role == NodeItemRole.Core && s.LangId == Ar).Version);
        Assert.Equal(1, sets.Single(s => s.Role == NodeItemRole.Recovery && s.LangId == En).Version);

        // The compatibility copy agrees with the source of truth.
        var legacy = await context.LessonQuestionSets.AsNoTracking().Where(s => s.LessonId == lessonId).ToListAsync();
        Assert.Equal(2, legacy.Single(s => s.LangId == En).Version);
    }

    [Fact]
    public async Task Rewording_only_the_arabic_leaves_the_english_row_and_version_alone()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        await PublishSheetAsync(context, lessonId, Rows(2));
        var before = await ServedAsync(context, lessonId);

        var fixedTypo = Rows(2);
        fixedTypo[0].QuestionAr += " ؟";

        await PublishSheetAsync(context, lessonId, fixedTypo);
        var after = await ServedAsync(context, lessonId);

        Assert.Equal(before.Where(r => r.LangId == En), after.Where(r => r.LangId == En));
        Assert.NotEqual(before.Single(r => r.Row == 1 && r.LangId == Ar).Id, after.Single(r => r.Row == 1 && r.LangId == Ar).Id);

        var sets = await context.PublishedItemSets.AsNoTracking().Where(s => s.NodeId == lessonId && s.Role == NodeItemRole.Core).ToListAsync();
        Assert.Equal(1, sets.Single(s => s.LangId == En).Version);
        Assert.Equal(2, sets.Single(s => s.LangId == Ar).Version);

        // Each rendering stays on the version whose content it shows.
        var english = after.Single(r => r.Row == 1 && r.LangId == En);
        var arabic = after.Single(r => r.Row == 1 && r.LangId == Ar);
        Assert.Equal(english.ItemId, arabic.ItemId);
        Assert.NotEqual(english.ItemVersionId, arabic.ItemVersionId);
    }

    [Fact]
    public async Task The_recovery_pool_is_written_to_the_item_bank_and_its_old_table_under_the_same_ids()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        await PublishSheetAsync(context, lessonId, Rows(1));

        var recovery = await context.ItemLocalizations.AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.Role == NodeItemRole.Recovery && q.IsActive)
            .Include(q => q.Choices)
            .ToListAsync();

        Assert.Equal(2, recovery.Count);

        foreach (var row in recovery)
        {
            var copy = await context.RecoveryQuestions.AsNoTracking().Include(q => q.Choices).SingleAsync(q => q.Id == row.Id);
            Assert.Equal(row.Text, copy.Text);
            Assert.Equal(row.CorrectChoiceId, copy.CorrectChoiceId);
            Assert.Equal(row.Choices.OrderBy(c => c.OrderIndex).Select(c => (c.Id, c.Text)),
                copy.Choices.OrderBy(c => c.OrderIndex).Select(c => (c.Id, c.Text)));
        }

        // The main pool's readers never see them: the filter on Questions is the main pool.
        Assert.Equal(2, await context.Questions.CountAsync(q => q.LessonId == lessonId && q.IsActive));

        // And recovery items carry no learning-target mapping — nothing answers them for evidence.
        var recoveryItems = await context.NodeItemMappings.Where(m => m.NodeId == lessonId && m.Role == NodeItemRole.Recovery)
            .Select(m => m.ItemId).ToListAsync();
        Assert.False(await context.ItemTargetMappings.AnyAsync(m => recoveryItems.Contains(m.ItemId)));
    }

    [Fact]
    public async Task A_publish_prepared_against_an_older_version_is_refused_whole()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);
        await PublishSheetAsync(context, lessonId, Rows(1));

        var publisher = EngineTest.Publisher(context);
        var result = await publisher.PublishAsync(new ContentPublishRequest
        {
            LessonId = lessonId,
            Items = [],
            Covers = [new(NodeItemRole.Core, En)],
            Rules = ContentRuleSet.SingleLanguage,
            ExpectedVersions = new Dictionary<ContentSetKey, int> { [new(NodeItemRole.Core, En)] = 0 }
        });

        Assert.False(result.Succeeded);
        Assert.Equal(EngineErrors.ContentMoved, result.Error);
        Assert.Equal(1, await context.Questions.CountAsync(q => q.LessonId == lessonId && q.LangId == En && q.IsActive));
    }

    [Fact]
    public async Task The_studio_rules_ask_for_every_required_language_and_a_recovery_question()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        var problems = await EngineTest.Publisher(context).CheckAsync(
            lessonId,
            [
                new ContentDraftItem
                {
                    Role = NodeItemRole.Core,
                    Order = 1,
                    Renderings = [new ContentDraftRendering(En, "Two plus two?", ["4", "3", "4"], 0)]
                }
            ],
            [new(NodeItemRole.Core, En), new(NodeItemRole.Core, Ar), new(NodeItemRole.Recovery, En), new(NodeItemRole.Recovery, Ar)],
            ContentRuleSet.Studio);

        Assert.Contains(problems, p => p.Code == "languageMissing" && p.LangId == Ar);
        Assert.Contains(problems, p => p.Code == "recoveryMissing");
        Assert.Contains(problems, p => p.Code == "choicesNotDifferent" && p.LangId == En);
    }

    [Fact]
    public async Task The_one_language_path_still_publishes_one_language_at_a_time()
    {
        await using var context = _fixture.CreateContext();
        var lessonId = await EmptyLessonAsync(context);

        // Its rules are its own until cutover: no Arabic, no recovery pool, and it publishes.
        var result = await EngineTest.MainImport(context).PublishManualAsync(lessonId, En, new ManualQuestionSetRequest
        {
            Mode = ManualQuestionMode.Replace,
            Questions = [new ManualQuestionInput { Text = "Two plus two?", CorrectChoice = "4", WrongChoice1 = "3", WrongChoice2 = "5" }]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Equal(1, result.Version);
        Assert.False(await context.PublishedItemSets.AnyAsync(s => s.NodeId == lessonId && s.LangId == Ar));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private sealed record Served(int Row, Guid LangId, Guid Id, Guid ItemId, Guid ItemVersionId);

    private static async Task<List<Served>> ServedAsync(ApplicationDbContext context, Guid lessonId) =>
        (await context.Questions.AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .Select(q => new { q.RowNumber, q.LangId, q.Id, q.ItemVersion!.ItemId, q.ItemVersionId })
            .ToListAsync())
        .Select(q => new Served(q.RowNumber, q.LangId, q.Id, q.ItemId, q.ItemVersionId))
        .OrderBy(q => q.Row).ThenBy(q => q.LangId)
        .ToList();

    private static async Task<LessonSheetResult> PublishSheetAsync(ApplicationDbContext context, Guid lessonId, List<LessonSheetRow> rows)
    {
        var result = await EngineTest.Sheet(context).SaveAsync(lessonId, new SaveLessonSheetRequest { Rows = rows });
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        return result;
    }

    private static List<LessonSheetRow> Rows(int main) =>
    [
        .. Enumerable.Range(1, main).Select(i => new LessonSheetRow
        {
            RowNumber = i,
            QuestionEn = $"Question {i}", CorrectEn = $"right {i}", WrongEn1 = $"wrong {i}a", WrongEn2 = $"wrong {i}b",
            QuestionAr = $"سؤال {i}", CorrectAr = $"صح {i}", WrongAr1 = $"خطأ {i}أ", WrongAr2 = $"خطأ {i}ب"
        }),
        new LessonSheetRow
        {
            RowNumber = main + 1,
            IsRecovery = true,
            QuestionEn = "Recovery", CorrectEn = "yes", WrongEn1 = "no", WrongEn2 = "maybe",
            QuestionAr = "تعويضي", CorrectAr = "نعم", WrongAr1 = "لا", WrongAr2 = "ربما"
        }
    ];

    /// <summary>A lesson with nothing published and no item identity left over from the fixture.</summary>
    private static async Task<Guid> EmptyLessonAsync(ApplicationDbContext context)
    {
        var path = await TestData.CreateCurriculumPathAsync(context);

        var itemIds = await context.Questions.Where(q => q.LessonId == path.LessonId).Select(q => q.ItemVersion!.ItemId).ToListAsync();
        await context.Questions.Where(q => q.LessonId == path.LessonId).ExecuteDeleteAsync();
        await context.ItemVersions.Where(v => itemIds.Contains(v.ItemId)).ExecuteDeleteAsync();
        await context.NodeItemMappings.Where(m => itemIds.Contains(m.ItemId)).ExecuteDeleteAsync();
        await context.ItemTargetMappings.Where(m => itemIds.Contains(m.ItemId)).ExecuteDeleteAsync();
        await context.Items.Where(i => itemIds.Contains(i.Id)).ExecuteDeleteAsync();

        return path.LessonId;
    }
}
