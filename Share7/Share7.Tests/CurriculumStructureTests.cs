using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Engine;
using Share7.Application.Engine.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Progress;
using Share7.Infrastructure.Engine.Reads;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The one writer of the curriculum's structure (Content Studio plan, Phase 2, step A1), over the
/// real database.
/// <para>
/// Three things are pinned: the node and its typed compatibility row always agree; nothing is ever
/// deleted — retiring hides, restoring brings back exactly what was hidden; and no student is left
/// stranded by a change to the tree they are working through.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CurriculumStructureTests
{
    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    private readonly SqlServerFixture _fixture;

    public CurriculumStructureTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- create, rename, move, reorder ------------------------------------------------------

    [Fact]
    public async Task A_new_lesson_is_written_to_the_node_and_its_typed_copy_in_one_go()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);

        var created = Ok(await EngineTest.Structure(context).CreateAsync(Lesson(path.ChapterId, "Density"), Actor()));

        await using var check = _fixture.CreateContext();
        var node = await check.CurriculumNodes.Include(n => n.Translations).SingleAsync(n => n.Id == created.Node.Id);
        var typed = await check.Lessons.Include(l => l.Translations).SingleAsync(l => l.Id == created.Node.Id);

        Assert.Equal(path.ChapterId, node.ParentNodeId);
        Assert.Equal(path.ChapterId, typed.ChapterId);
        Assert.Equal(node.Order, typed.Order);
        Assert.EndsWith($"/{path.ChapterId:D}/{node.Id:D}", node.Path);
        Assert.Equal("Density", node.Translations.Single(t => t.LangId == En).Title);
        Assert.Equal("Density", typed.Translations.Single(t => t.LangId == En).Name);
        Assert.True(await check.AuditEvents.AnyAsync(e => e.Action == AuditActions.CurriculumNodeCreated && e.TargetId == node.Id.ToString()));
    }

    [Fact]
    public async Task A_taken_position_is_refused_unless_the_caller_asks_to_make_room()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var structure = EngineTest.Structure(context);

        var fixtureOrder = (await context.Lessons.SingleAsync(l => l.Id == path.LessonId)).Order;

        var refused = await structure.CreateAsync(Lesson(path.ChapterId, "Pressure") with { Position = fixtureOrder }, Actor());
        Assert.Equal(EngineErrors.NodePositionTaken, refused.Error);

        var placed = Ok(await structure.CreateAsync(
            Lesson(path.ChapterId, "Pressure") with { Position = fixtureOrder, ShiftSiblings = true }, Actor()));

        await using var check = _fixture.CreateContext();
        Assert.Equal(fixtureOrder, (await check.Lessons.SingleAsync(l => l.Id == placed.Node.Id)).Order);
        Assert.Equal(fixtureOrder + 1, (await check.Lessons.SingleAsync(l => l.Id == path.LessonId)).Order);
        Assert.Equal(fixtureOrder + 1, (await check.CurriculumNodes.SingleAsync(n => n.Id == path.LessonId)).Order);
    }

    [Fact]
    public async Task A_rename_made_against_an_old_revision_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var structure = EngineTest.Structure(context);

        var created = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "Mass"), Actor()));
        var renamed = Ok(await structure.RenameAsync(created.Node.Id, Titles("Mass and weight"), created.Node.Revision, Actor()));

        Assert.Equal(created.Node.Revision + 1, renamed.Node.Revision);
        Assert.Equal("Mass and weight", (await context.LessonTranslations.SingleAsync(t => t.LessonId == created.Node.Id && t.LangId == En)).Name);

        // Somebody else's rename, made against the revision that has since moved.
        var stale = await structure.RenameAsync(created.Node.Id, Titles("Weight"), created.Node.Revision, Actor());
        Assert.Equal(EngineErrors.NodeMoved, stale.Error);
        Assert.Equal(ServiceErrorKind.Conflict, stale.ErrorKind);
    }

    [Fact]
    public async Task Moving_a_lesson_rewrites_its_place_in_both_shapes()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var structure = EngineTest.Structure(context);

        var chapter = Ok(await structure.CreateAsync(Chapter(path.SubjectId, "Waves"), Actor()));
        var moved = Ok(await structure.MoveAsync(path.LessonId, chapter.Node.Id, null, null, Actor()));

        await using var check = _fixture.CreateContext();
        Assert.Equal(chapter.Node.Id, (await check.Lessons.SingleAsync(l => l.Id == path.LessonId)).ChapterId);
        Assert.Equal($"{chapter.Node.Path}/{path.LessonId:D}", (await check.CurriculumNodes.SingleAsync(n => n.Id == path.LessonId)).Path);
        Assert.Equal(1, moved.Node.Order);
    }

    [Fact]
    public async Task A_reorder_numbers_the_children_one_to_n_in_both_shapes()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var structure = EngineTest.Structure(context);

        var b = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "B"), Actor())).Node.Id;
        var c = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "C"), Actor())).Node.Id;

        var incomplete = await structure.ReorderAsync(path.ChapterId, [c, b], null, Actor());
        Assert.Equal(EngineErrors.NodeOrderInvalid, incomplete.Error);

        Ok(await structure.ReorderAsync(path.ChapterId, [c, path.LessonId, b], null, Actor()));

        await using var check = _fixture.CreateContext();
        var typed = await check.Lessons.Where(l => l.ChapterId == path.ChapterId).OrderBy(l => l.Order).Select(l => l.Id).ToListAsync();
        var nodes = await check.CurriculumNodes.Where(n => n.ParentNodeId == path.ChapterId).OrderBy(n => n.Order).Select(n => n.Id).ToListAsync();

        Assert.Equal([c, path.LessonId, b], typed);
        Assert.Equal(typed, nodes);
        Assert.Equal([1, 2, 3], await check.Lessons.Where(l => l.ChapterId == path.ChapterId).OrderBy(l => l.Order).Select(l => l.Order).ToListAsync());
    }

    [Fact]
    public async Task Grades_are_fixed()
    {
        await using var context = _fixture.CreateContext();
        var gradeId = await context.Grades.Select(g => g.Id).FirstAsync();
        await TestData.CreateCurriculumPathAsync(context);

        var structure = EngineTest.Structure(context);
        Assert.Equal(EngineErrors.NodeNotEditable, (await structure.RenameAsync(gradeId, Titles("G"), null, Actor())).Error);
        Assert.Equal(EngineErrors.NodeNotEditable, (await structure.RetireAsync(gradeId, null, Actor())).Error);
    }

    // ---- retire and restore -----------------------------------------------------------------

    [Fact]
    public async Task Retiring_a_chapter_hides_it_and_its_lessons_from_every_game_read_and_restoring_brings_back_exactly_those()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var structure = EngineTest.Structure(context);

        var second = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "Second"), Actor())).Node.Id;

        // Retired first, on its own: it must stay retired when the chapter comes back.
        Ok(await structure.RetireAsync(second, null, Actor()));
        Ok(await structure.RetireAsync(path.ChapterId, null, Actor()));

        foreach (var reads in Readers(context))
        {
            Assert.Empty(await reads.ChaptersAsync(path.SubjectId, En, default));
            Assert.Empty(await reads.LessonsAsync(path.ChapterId, En, default));
            Assert.False(await reads.LessonExistsAsync(path.LessonId, default));
            Assert.Null(await reads.QuestionsAsync(path.LessonId, Domain.Content.NodeItemRole.Core, En, default));
        }

        // Nothing was deleted: the lesson's question is still there, retired with it.
        Assert.True(await context.Questions.AnyAsync(q => q.LessonId == path.LessonId && q.IsActive));

        Ok(await structure.RestoreAsync(path.ChapterId, Actor()));

        foreach (var reads in Readers(context))
        {
            Assert.Single(await reads.ChaptersAsync(path.SubjectId, En, default));
            Assert.Equal([path.LessonId], (await reads.LessonsAsync(path.ChapterId, En, default)).Select(l => l.Id));
        }

        Assert.True(await context.Lessons.IgnoreQueryFilters().AnyAsync(l => l.Id == second && l.RetiredAtUtc != null));
    }

    [Fact]
    public async Task A_restored_lesson_whose_place_was_taken_goes_to_the_end()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var structure = EngineTest.Structure(context);

        var order = (await context.Lessons.SingleAsync(l => l.Id == path.LessonId)).Order;
        Ok(await structure.RetireAsync(path.LessonId, null, Actor()));

        var replacement = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "Replacement") with { Position = order }, Actor()));
        Assert.Equal(order, replacement.Node.Order);

        var restored = Ok(await structure.RestoreAsync(path.LessonId, Actor()));
        Assert.Equal(order + 1, restored.Node.Order);
        Assert.Equal(order + 1, (await context.Lessons.AsNoTracking().SingleAsync(l => l.Id == path.LessonId)).Order);
    }

    // ---- nobody stranded --------------------------------------------------------------------

    [Fact]
    public async Task Retiring_the_lesson_a_student_is_on_opens_the_next_one_for_them()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var userId = await TestData.CreateUserAsync(context);
        var structure = EngineTest.Structure(context);

        var next = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "Next"), Actor())).Node.Id;
        await GrantAsync(context, userId, path.GameId, CurriculumNodeType.Lesson, path.LessonId);

        Ok(await structure.RetireAsync(path.LessonId, null, Actor()));
        await EngineTest.Repairs(context).RunPendingAsync();

        Assert.True(await HoldsAsync(context, userId, path.GameId, next));
    }

    [Fact]
    public async Task A_lesson_moved_ahead_of_where_a_student_reached_is_opened_behind_them()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var userId = await TestData.CreateUserAsync(context);
        var structure = EngineTest.Structure(context);

        var second = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "Second"), Actor())).Node.Id;
        var third = Ok(await structure.CreateAsync(Lesson(path.ChapterId, "Third"), Actor())).Node.Id;

        // The student reached the second lesson.
        await GrantAsync(context, userId, path.GameId, CurriculumNodeType.Lesson, path.LessonId);
        await GrantAsync(context, userId, path.GameId, CurriculumNodeType.Lesson, second);

        // The third lesson now comes first: it would sit locked before what they already reached.
        Ok(await structure.ReorderAsync(path.ChapterId, [third, path.LessonId, second], null, Actor()));
        await EngineTest.Repairs(context).RunPendingAsync();

        Assert.True(await HoldsAsync(context, userId, path.GameId, third));
        Assert.True(await context.UnlockRepairJobs.AllAsync(j => j.CompletedAtUtc != null));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static EngineActor Actor() => new(Guid.NewGuid());

    private static IReadOnlyList<NodeTitle> Titles(string english) => [new(En, english), new(Ar, english + " (ع)")];

    private static CreateNodeCommand Lesson(Guid chapterId, string english) =>
        new() { ParentId = chapterId, Kind = NodeKinds.Lesson, Titles = Titles(english) };

    private static CreateNodeCommand Chapter(Guid subjectId, string english) =>
        new() { ParentId = subjectId, Kind = NodeKinds.Chapter, Titles = Titles(english) };

    private static IEnumerable<ICurriculumReads> Readers(ApplicationDbContext context) =>
        [new TypedCurriculumReads(context), new NodeCurriculumReads(context)];

    private static T Ok<T>(ServiceResult<T> result)
    {
        Assert.True(result.Succeeded, $"{result.Error?.Code}: {string.Join("; ", result.Errors)}");
        return result.Value!;
    }

    private static async Task GrantAsync(ApplicationDbContext context, Guid userId, Guid gameId, CurriculumNodeType type, Guid nodeId)
    {
        context.UserNodeUnlocks.Add(new UserNodeUnlock { UserId = userId, GameId = gameId, NodeType = type, NodeId = nodeId, UnlockedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private static Task<bool> HoldsAsync(ApplicationDbContext context, Guid userId, Guid gameId, Guid lessonId) =>
        context.UserNodeUnlocks.AnyAsync(u =>
            u.UserId == userId && u.GameId == gameId && u.NodeType == CurriculumNodeType.Lesson && u.NodeId == lessonId);
}
