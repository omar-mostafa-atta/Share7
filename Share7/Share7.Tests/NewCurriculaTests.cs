using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Share7.Application.Common.Models;
using Share7.Application.Engine;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Engine.Reads;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Curricula declared in the Studio — authored now, played later — over the real database.
/// <para>
/// **The one thing every test here is really about: nothing written under a declared curriculum can
/// reach the game.** Not its nodes (no legacy typed row is ever written for them, and no read the
/// game makes can see one), and not its questions (they are never rows of the game's own
/// <c>Questions</c> table). Everything else — the draft, the review, the release, the item bank —
/// works exactly as it does for the Egyptian tree.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class NewCurriculaTests : IDisposable
{
    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly SqlServerFixture _fixture;
    private readonly ServiceProvider _services;

    public NewCurriculaTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _services = StaffTestHost.Build(fixture);
    }

    public void Dispose() => _services.Dispose();

    // ---- declaring one ----------------------------------------------------------------------

    [Fact]
    public async Task A_lead_scoped_everywhere_declares_a_curriculum_of_any_shape()
    {
        var lead = await MemberAsync(StudioRole.Lead);

        var created = Ok(await Curricula(lead).CreateAsync(lead, Request(Unique("British IGCSE"), ("Subject", "المادة"), ("Paper", "الورقة"), ("Topic", "الموضوع"))));

        Assert.False(created.IsServed);
        Assert.False(created.LevelsLocked);
        Assert.True(created.CanManage);
        Assert.NotNull(created.RootNodeId);
        Assert.Equal(["level1", "level2", "level3"], created.Levels.Select(l => l.Key));
        Assert.Equal([false, false, true], created.Levels.Select(l => l.IsPlayable));
        Assert.Equal("الموضوع", created.Levels[2].Names.Single(n => n.LangId == Ar).Title);

        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();

        var root = await db.CurriculumNodes.SingleAsync(n => n.Id == created.RootNodeId);
        Assert.Equal(NodeKinds.CurriculumRoot, root.KindKey);
        Assert.Null(root.ParentNodeId);
        Assert.Equal($"/{root.Id:D}", root.Path);
        Assert.Null(root.LegacySource);

        var version = await db.CurriculumVersions.SingleAsync(v => v.Id == created.VersionId);
        Assert.True(version.IsAuthoritative);

        Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == AuditActions.CurriculumCreated && e.TargetId == created.Id.ToString()));
    }

    [Fact]
    public async Task Only_a_lead_scoped_everywhere_may_declare_one()
    {
        var reviewer = await MemberAsync(StudioRole.Reviewer);
        var path = await PathAsync();
        var partLead = await MemberAsync(StudioRole.Lead, nodes: [path.GradeId]);

        Assert.Equal(WorkspaceErrors.OutOfScope, (await Curricula(reviewer).CreateAsync(reviewer, Request(Unique("A"), ("Unit", "الوحدة")))).Error);
        Assert.Equal(WorkspaceErrors.OutOfScope, (await Curricula(partLead).CreateAsync(partLead, Request(Unique("B"), ("Unit", "الوحدة")))).Error);
    }

    [Fact]
    public async Task A_curriculum_needs_a_name_and_named_levels_in_every_required_language()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var curricula = Curricula(lead);

        var noLevels = await curricula.CreateAsync(lead, new CreateCurriculumRequest([new(En, Unique("X")), new(Ar, "س")], []));
        Assert.Equal(WorkspaceErrors.CurriculumInvalid, noLevels.Error);

        var noArabic = await curricula.CreateAsync(lead, new CreateCurriculumRequest(
            [new(En, Unique("Y")), new(Ar, "ص")], [new CurriculumLevelRequest([new(En, "Unit")])]));
        Assert.Equal(WorkspaceErrors.CurriculumInvalid, noArabic.Error);

        var name = Unique("Taken");
        Ok(await curricula.CreateAsync(lead, Request(name, ("Unit", "الوحدة"))));
        Assert.Equal(WorkspaceErrors.CurriculumInvalid, (await curricula.CreateAsync(lead, Request(name, ("Unit", "الوحدة")))).Error);
    }

    [Fact]
    public async Task The_curriculum_the_game_serves_is_listed_first_and_cannot_be_changed_from_the_Studio()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        Ok(await Curricula(lead).CreateAsync(lead, Request(Unique("Listed"), ("Unit", "الوحدة"))));

        var list = await Curricula(lead).ListAsync(lead);
        var served = list[0];

        Assert.True(served.IsServed);
        Assert.Equal(EducationIds.EgyptianNationalCurriculum, served.Id);
        Assert.Null(served.RootNodeId);
        Assert.Contains(list, c => !c.IsServed);

        var refused = await Curricula(lead).UpdateAsync(lead, served.Id, new UpdateCurriculumRequest([new(En, "Mine"), new(Ar, "لي")], null));
        Assert.Equal(WorkspaceErrors.CurriculumServed, refused.Error);
    }

    // ---- the guarantee ----------------------------------------------------------------------

    [Fact]
    public async Task A_declared_curriculum_is_built_through_review_and_release_and_never_reaches_the_game()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var author = await MemberAsync(StudioRole.Author);
        var reviewer = await MemberAsync(StudioRole.Reviewer);

        // Its top level is even called "Grade" — and is still not a grade.
        var curriculum = Ok(await Curricula(lead).CreateAsync(lead, Request(Unique("Azhar track"), ("Grade", "الصف"), ("Topic", "الموضوع"))));
        var rootId = curriculum.RootNodeId!.Value;

        var typedBefore = await TypedCountsAsync();

        // A top-level node, proposed, reviewed and released like any other change.
        var top = await ReleasedNewNodeAsync(author, reviewer, lead, rootId, "level1", "Year one", "السنة الأولى", items: null);

        // A playable node under it, questions and all, in one draft.
        var topic = await ReleasedNewNodeAsync(author, reviewer, lead, top, "level2", "Fractions", "الكسور",
        [
            Item(NodeItemRole.Core, 1, "Half of 8?", "نصف ٨؟", "4", "2", "6"),
            Item(NodeItemRole.Recovery, 1, "Half of 2?", "نصف ٢؟", "1", "0", "2")
        ]);

        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();

        // Nodes: in the node table at the declared levels, and nowhere in the legacy one.
        var nodes = await db.CurriculumNodes.Where(n => n.Id == top || n.Id == topic).ToListAsync();
        Assert.Equal("level1", nodes.Single(n => n.Id == top).KindKey);
        Assert.True(nodes.Single(n => n.Id == topic).IsPlayable);
        Assert.All(nodes, n => Assert.Null(n.LegacySource));
        Assert.Equal(typedBefore, await TypedCountsAsync());
        Assert.False(await db.Lessons.IgnoreQueryFilters().AnyAsync(l => l.Id == topic));

        // The game's reads, typed and node, see none of it.
        var nodeGrades = await new NodeCurriculumReads(db).GradesAsync(En, default);
        var typedGrades = await new TypedCurriculumReads(db).GradesAsync(En, default);
        Assert.DoesNotContain(nodeGrades, g => g.Id == top || g.Id == rootId);
        Assert.DoesNotContain(typedGrades, g => g.Id == top || g.Id == rootId);
        Assert.Null(await new NodeCurriculumReads(db).QuestionsAsync(topic, NodeItemRole.Core, En, default));

        // Questions: never a row of the game's Questions table, and no legacy set row either.
        Assert.False(await db.ItemLocalizations.AnyAsync(q => q.LessonId == topic));
        Assert.False(await db.LessonQuestionSets.AnyAsync(s => s.LessonId == topic));
        Assert.Equal(2, await db.NodeItemRenderings.CountAsync(r => r.NodeId == topic && r.IsActive && r.Role == NodeItemRole.Core));
        Assert.Equal(2, await db.NodeItemRenderings.CountAsync(r => r.NodeId == topic && r.IsActive && r.Role == NodeItemRole.Recovery));

        // …but everything the item bank knows about them is shared, and reads back in the Studio.
        Assert.Equal(1, (await db.PublishedItemSets.SingleAsync(s => s.NodeId == topic && s.Role == NodeItemRole.Core && s.LangId == En)).Version);
        Assert.True(await db.NodeItemMappings.AnyAsync(m => m.NodeId == topic && m.CurriculumVersionId == curriculum.VersionId));

        var live = await new LessonContentReader(db).ReadAsync(topic);
        Assert.NotNull(live);
        Assert.Equal(["Half of 8?"], live!.Items.Where(i => i.Role == NodeItemRole.Core).Select(i => i.Renderings.Single(r => r.LangId == En).Text));
    }

    [Fact]
    public async Task A_second_publish_keeps_unchanged_questions_and_retires_removed_ones_in_their_own_table()
    {
        var (_, _, topic) = await TreeAsync();

        await using (var scope = Scope())
        {
            var db = scope.Get<ApplicationDbContext>();
            var publisher = EngineTest.Publisher(db);
            var covers = CoversBoth();

            Ok(await publisher.PublishAsync(Publish(topic, covers,
                Item(NodeItemRole.Core, 1, "Two plus two?", "اثنان زائد اثنان؟", "4", "3", "5"),
                Item(NodeItemRole.Core, 2, "Three plus one?", "ثلاثة زائد واحد؟", "4", "2", "5"),
                Item(NodeItemRole.Recovery, 1, "One plus one?", "واحد زائد واحد؟", "2", "1", "3"))));
        }

        Guid keptId;
        await using (var scope = Scope())
        {
            var db = scope.Get<ApplicationDbContext>();
            var live = (await new LessonContentReader(db).ReadAsync(topic))!;
            var first = live.Items.Single(i => i.Role == NodeItemRole.Core && i.Order == 1);
            var second = live.Items.Single(i => i.Role == NodeItemRole.Recovery);
            keptId = first.Renderings.Single(r => r.LangId == En).QuestionId;

            Ok(await EngineTest.Publisher(db).PublishAsync(Publish(topic, CoversBoth(),
                Keep(first), Keep(second))));
        }

        await using var check = Scope();
        var rows = await check.Get<ApplicationDbContext>().NodeItemRenderings.Where(r => r.NodeId == topic && r.Role == NodeItemRole.Core).ToListAsync();

        Assert.Contains(rows, r => r.Id == keptId && r.IsActive);
        Assert.Equal(2, rows.Count(r => r.IsActive));
        Assert.Equal(2, rows.Count(r => !r.IsActive && r.DeactivatedAtUtc is not null));
    }

    // ---- shape ------------------------------------------------------------------------------

    [Fact]
    public async Task Levels_follow_the_declared_shape_and_nothing_moves_between_curricula()
    {
        var (_, top, topic) = await TreeAsync();
        var path = await PathAsync();

        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();
        var structure = EngineTest.Structure(db);

        // Not a level this curriculum has, not under this parent, and not somewhere else's.
        Assert.Equal(EngineErrors.NodeNotEditable, (await structure.CreateAsync(New(top, NodeKinds.Lesson, "Stray"), Actor())).Error);
        Assert.Equal(EngineErrors.NodeWrongParent, (await structure.CreateAsync(New(topic, "level2", "Deeper"), Actor())).Error);
        Assert.Equal(EngineErrors.NodeWrongParent, (await structure.MoveAsync(topic, path.ChapterId, null, null, Actor())).Error);
        Assert.Equal(EngineErrors.NodeWrongParent, (await structure.MoveAsync(path.LessonId, top, null, null, Actor())).Error);

        // Its own root is its name, not a place: it is not renamed, moved or retired as a node.
        var rootId = (await db.CurriculumNodes.SingleAsync(n => n.Id == top)).ParentNodeId!.Value;
        Assert.Equal(EngineErrors.NodeNotEditable, (await structure.RetireAsync(rootId, null, Actor())).Error);
    }

    [Fact]
    public async Task Retiring_and_restoring_in_a_declared_curriculum_touch_no_legacy_row_and_queue_no_repair()
    {
        var (_, top, topic) = await TreeAsync();

        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();
        var structure = EngineTest.Structure(db);
        var repairsBefore = await db.UnlockRepairJobs.CountAsync();

        var retired = Ok(await structure.RetireAsync(top, null, Actor()));
        Assert.False(retired.UnlocksQueued);
        Assert.Contains(topic, retired.AlsoChanged);

        var restored = Ok(await structure.RestoreAsync(top, Actor()));
        Assert.False(restored.UnlocksQueued);

        Assert.Equal(repairsBefore, await db.UnlockRepairJobs.CountAsync());
        Assert.Null((await db.CurriculumNodes.SingleAsync(n => n.Id == topic)).RetiredAtUtc);
    }

    [Fact]
    public async Task Levels_above_what_is_built_keep_their_places_and_the_ones_below_stay_free()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var name = Unique("Lockable");
        var curriculum = Ok(await Curricula(lead).CreateAsync(lead, Request(name, ("Unit", "الوحدة"), ("Lesson", "الدرس"))));

        // Nothing there yet: a level can be added anywhere.
        curriculum = Ok(await Curricula(lead).UpdateAsync(lead, curriculum.Id, new UpdateCurriculumRequest(
            Titles(name), [Level("Book", "الكتاب"), Level("Unit", "الوحدة"), Level("Lesson", "الدرس")])));
        Assert.Equal(3, curriculum.Levels.Count);
        Assert.Equal(0, curriculum.FixedLevels);
        Assert.True(curriculum.Levels[2].IsPlayable);

        Guid book;
        await using (var scope = Scope())
        {
            book = Ok(await EngineTest.Structure(scope.Get<ApplicationDbContext>())
                .CreateAsync(New(curriculum.RootNodeId!.Value, "level1", "Book one"), Actor())).Node.Id;
        }

        // Something sits at the top level: it keeps its place, and everything under it is still free.
        curriculum = Ok(await Curricula(lead).GetAsync(lead, curriculum.Id));
        Assert.Equal(1, curriculum.FixedLevels);
        Assert.False(curriculum.LevelsLocked);

        curriculum = Ok(await Curricula(lead).UpdateAsync(lead, curriculum.Id, new UpdateCurriculumRequest(
            Titles(name), [Level("Volume", "المجلد"), Level("Part", "الجزء"), Level("Unit", "الوحدة"), Level("Lesson", "الدرس")])));
        Assert.Equal(["level1", "level2", "level3", "level4"], curriculum.Levels.Select(l => l.Key));
        Assert.Equal("Volume", curriculum.Levels[0].Names.Single(n => n.LangId == En).Title);
        Assert.Equal([false, false, false, true], curriculum.Levels.Select(l => l.IsPlayable));

        await using (var scope = Scope())
        {
            // The node that was there still stands on a level that exists — the same row, renamed.
            var db = scope.Get<ApplicationDbContext>();
            var kindId = (await db.CurriculumNodes.SingleAsync(n => n.Id == book)).NodeKindId;
            Assert.Equal("level1", (await db.CurriculumNodeKinds.SingleAsync(k => k.Id == kindId)).KindKey);
        }

        // What sits at the top level is never made the played one, and never loses its level.
        var cut = await Curricula(lead).UpdateAsync(lead, curriculum.Id, new UpdateCurriculumRequest(Titles(name), [Level("Volume", "المجلد")]));
        Assert.Equal(WorkspaceErrors.CurriculumLocked, cut.Error);

        // Once something sits at the played level, nothing can be added or taken out — only renamed.
        await using (var scope = Scope())
        {
            var structure = EngineTest.Structure(scope.Get<ApplicationDbContext>());
            var part = Ok(await structure.CreateAsync(New(book, "level2", "Part one"), Actor())).Node.Id;
            var unit = Ok(await structure.CreateAsync(New(part, "level3", "Unit one"), Actor())).Node.Id;
            Ok(await structure.CreateAsync(New(unit, "level4", "Lesson one"), Actor()));
        }

        curriculum = Ok(await Curricula(lead).GetAsync(lead, curriculum.Id));
        Assert.Equal(4, curriculum.FixedLevels);
        Assert.True(curriculum.LevelsLocked);

        var added = await Curricula(lead).UpdateAsync(lead, curriculum.Id, new UpdateCurriculumRequest(
            Titles(name), [Level("Volume", "المجلد"), Level("Part", "الجزء"), Level("Unit", "الوحدة"), Level("Lesson", "الدرس"), Level("Step", "الخطوة")]));
        Assert.Equal(WorkspaceErrors.CurriculumLocked, added.Error);

        var renamed = Ok(await Curricula(lead).UpdateAsync(lead, curriculum.Id, new UpdateCurriculumRequest(
            Titles(name), [Level("Volume", "المجلد"), Level("Part", "الجزء"), Level("Unit", "الوحدة"), Level("Topic", "الموضوع")])));
        Assert.Equal("Topic", renamed.Levels[3].Names.Single(n => n.LangId == En).Title);
    }

    [Fact]
    public async Task A_new_node_proposed_in_an_open_draft_fixes_its_level_too()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var name = Unique("Proposed");
        var curriculum = Ok(await Curricula(lead).CreateAsync(lead, Request(name, ("KG1", "KG1"), ("KG2", "KG2"), ("Primary 1", "Primary 1"))));

        // What the first person to use the board did: the grades typed in as its levels, and one
        // proposed at the top. The levels under it can still be put right.
        Ok(await Drafts(lead).CreateAsync(lead, new CreateDraftRequest { Kind = DraftKind.NewNode, ParentNodeId = curriculum.RootNodeId, NodeKind = "level1" }));

        curriculum = Ok(await Curricula(lead).GetAsync(lead, curriculum.Id));
        Assert.Equal(1, curriculum.FixedLevels);

        curriculum = Ok(await Curricula(lead).UpdateAsync(lead, curriculum.Id, new UpdateCurriculumRequest(Titles(name),
            [Level("Grade", "صف"), Level("Term", "فصل دراسي"), Level("Subject", "مادة"), Level("Chapter", "وحدة"), Level("Lesson", "درس")])));

        Assert.Equal(["Grade", "Term", "Subject", "Chapter", "Lesson"], curriculum.Levels.Select(l => l.Names.Single(n => n.LangId == En).Title));
        Assert.True(curriculum.Levels[^1].IsPlayable);
    }

    // ---- a Lead needs nobody else -----------------------------------------------------------

    [Fact]
    public async Task A_lead_adds_a_grade_and_releases_it_in_one_step()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var author = await MemberAsync(StudioRole.Author);
        var curriculum = Ok(await Curricula(lead).CreateAsync(lead, Request(Unique("American"), ("Grade", "صف"), ("Subject", "مادة"), ("Lesson", "درس"))));
        var rootId = curriculum.RootNodeId!.Value;

        var draft = Ok(await Drafts(lead).CreateAsync(lead, new CreateDraftRequest { Kind = DraftKind.NewNode, ParentNodeId = rootId, NodeKind = "level1" }));
        draft = Ok(await Drafts(lead).SaveAsync(lead, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision,
            JsonSerializer.SerializeToElement(new NewNodeProposal([new(En, "Grade 1"), new(Ar, "الصف الأول")], null, null), Json))));
        Assert.True(draft.Can.ReleaseNow);

        var released = Ok(await Releases(lead).ReleaseNowAsync(lead, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        Assert.Equal(DraftStatus.Released, released.Summary.Status);
        Assert.False(released.Can.ReleaseNow);

        await using (var scope = Scope())
        {
            var db = scope.Get<ApplicationDbContext>();
            var grade = await db.CurriculumNodes.SingleAsync(n => n.Id == draft.Summary.NodeId);
            Assert.Equal(rootId, grade.ParentNodeId);
            Assert.Equal("level1", grade.KindKey);
            Assert.Equal(ReleaseStatus.Published, (await db.Releases.SingleAsync(r => r.Id == released.Summary.ReleaseId)).Status);

            var actions = await db.AuditEvents.Where(a => a.TargetId == draft.Summary.Id.ToString()).Select(a => a.Action).ToListAsync();
            Assert.Contains(AuditActions.DraftSelfApproved, actions);
        }

        // An author's work still goes to somebody else.
        var theirs = Ok(await Drafts(author).CreateAsync(author, new CreateDraftRequest { Kind = DraftKind.NewNode, ParentNodeId = rootId, NodeKind = "level1" }));
        Assert.False(theirs.Can.ReleaseNow);
        var refused = await Releases(author).ReleaseNowAsync(author, theirs.Summary.Id, new DraftActionRequest(theirs.Summary.Revision));
        Assert.Equal(WorkspaceErrors.OutOfScope, refused.Error);
    }

    [Fact]
    public async Task A_one_step_release_that_is_refused_leaves_the_draft_free_to_fix()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var curriculum = Ok(await Curricula(lead).CreateAsync(lead, Request(Unique("Refused"), ("Grade", "صف"), ("Lesson", "درس"))));

        // No names yet: it has problems, and nothing is approved, built or published.
        var draft = Ok(await Drafts(lead).CreateAsync(lead, new CreateDraftRequest { Kind = DraftKind.NewNode, ParentNodeId = curriculum.RootNodeId, NodeKind = "level1" }));
        var refused = await Releases(lead).ReleaseNowAsync(lead, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision));
        Assert.Equal(WorkspaceErrors.DraftHasProblems, refused.Error);

        // Out of date with what the Lead was reading: refused before anything happens.
        var stale = await Releases(lead).ReleaseNowAsync(lead, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision + 5));
        Assert.Equal(WorkspaceErrors.DraftRevisionMoved, stale.Error);

        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();
        Assert.Equal(DraftStatus.Editing, (await db.Drafts.SingleAsync(d => d.Id == draft.Summary.Id)).Status);
        Assert.False(await db.ReleaseEntries.AnyAsync(e => e.DraftId == draft.Summary.Id));
    }

    [Fact]
    public async Task The_Egyptian_grades_are_still_fixed()
    {
        var path = await PathAsync();
        var lead = await MemberAsync(StudioRole.Lead);

        var refused = await Drafts(lead).CreateAsync(lead, new CreateDraftRequest { Kind = DraftKind.Rename, NodeId = path.GradeId });
        Assert.Equal(WorkspaceErrors.DraftInvalid, refused.Error);
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>A declared curriculum with one released top node and one playable node under it.</summary>
    private async Task<(StudioCurriculumDto Curriculum, Guid Top, Guid Topic)> TreeAsync()
    {
        var lead = await MemberAsync(StudioRole.Lead);
        var curriculum = Ok(await Curricula(lead).CreateAsync(lead, Request(Unique("Tree"), ("Unit", "الوحدة"), ("Topic", "الموضوع"))));

        await using var scope = Scope();
        var structure = EngineTest.Structure(scope.Get<ApplicationDbContext>());
        var top = Ok(await structure.CreateAsync(New(curriculum.RootNodeId!.Value, "level1", "Unit one"), Actor())).Node.Id;
        var topic = Ok(await structure.CreateAsync(New(top, "level2", "Topic one"), Actor())).Node.Id;

        return (curriculum, top, topic);
    }

    private async Task<Guid> ReleasedNewNodeAsync(
        StudioMember author, StudioMember reviewer, StudioMember lead,
        Guid parentId, string level, string english, string arabic, IReadOnlyList<ContentDraftItem>? items)
    {
        var draft = Ok(await Drafts(author).CreateAsync(author, new CreateDraftRequest { Kind = DraftKind.NewNode, ParentNodeId = parentId, NodeKind = level }));
        draft = Ok(await Drafts(author).SaveAsync(author, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision,
            JsonSerializer.SerializeToElement(new NewNodeProposal([new(En, english), new(Ar, arabic)], null, items), Json))));

        Assert.Empty(draft.Problems);

        draft = Ok(await Drafts(author).SubmitAsync(author, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        draft = Ok(await Reviews(reviewer).ApproveAsync(reviewer, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));

        var release = Ok(await Releases(lead).CreateAsync(lead, new CreateReleaseRequest(english, null, [draft.Summary.Id])));
        Assert.Empty(release.Blockers);
        release = Ok(await Releases(lead).PublishAsync(lead, release.Summary.Id));
        Assert.Equal(ReleaseStatus.Published, release.Summary.Status);

        return draft.Summary.NodeId!.Value;
    }

    private async Task<(int Grades, int Terms, int Subjects, int Chapters, int Lessons, int Questions)> TypedCountsAsync()
    {
        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();
        return (
            await db.Grades.IgnoreQueryFilters().CountAsync(),
            await db.Terms.IgnoreQueryFilters().CountAsync(),
            await db.Subjects.IgnoreQueryFilters().CountAsync(),
            await db.Chapters.IgnoreQueryFilters().CountAsync(),
            await db.Lessons.IgnoreQueryFilters().CountAsync(),
            await db.ItemLocalizations.CountAsync());
    }

    private static IReadOnlyList<ContentSetKey> CoversBoth() =>
    [
        new(NodeItemRole.Core, En), new(NodeItemRole.Core, Ar),
        new(NodeItemRole.Recovery, En), new(NodeItemRole.Recovery, Ar)
    ];

    private static ContentPublishRequest Publish(Guid nodeId, IReadOnlyList<ContentSetKey> covers, params ContentDraftItem[] items) => new()
    {
        LessonId = nodeId,
        Items = items,
        Covers = covers,
        Rules = ContentRuleSet.Studio,
        Source = Share7.Domain.Curriculum.QuestionSetSource.Release,
        ActorUserId = null,
        ReleaseId = null,
        AuditPath = "test"
    };

    /// <summary>An item exactly as it is served now, for a publish that should leave it alone.</summary>
    private static ContentDraftItem Keep(ContentItemDto item) => new()
    {
        ItemId = item.ItemId,
        Role = item.Role,
        Order = item.Order,
        Renderings = item.Renderings.Select(r => new ContentDraftRendering(r.LangId, r.Text, r.Choices.Select(c => c.Text).ToList(), r.CorrectIndex)).ToList()
    };

    private static CreateCurriculumRequest Request(string name, params (string En, string Ar)[] levels) =>
        new(Titles(name), levels.Select(l => Level(l.En, l.Ar)).ToList());

    private static IReadOnlyList<NodeTitle> Titles(string name) => [new(En, name), new(Ar, name + " ع")];

    private static CurriculumLevelRequest Level(string english, string arabic) => new([new(En, english), new(Ar, arabic)]);

    private static CreateNodeCommand New(Guid parentId, string kind, string name) => new()
    {
        ParentId = parentId,
        Kind = kind,
        Titles = [new(En, name), new(Ar, name + " ع")]
    };

    private static ContentDraftItem Item(NodeItemRole role, int order, string english, string arabic, string correct, string wrong1, string wrong2) => new()
    {
        Role = role,
        Order = order,
        Renderings =
        [
            new ContentDraftRendering(En, english, [correct, wrong1, wrong2], 0),
            new ContentDraftRendering(Ar, arabic, [correct, wrong1, wrong2], 0)
        ]
    };

    /// <summary>Curriculum names are unique across the whole database the suite shares.</summary>
    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..Math.Min(name.Length + 9, 60)];

    private static EngineActor Actor() => new(null);

    private AsyncServiceScope Scope() => _services.CreateAsyncScope();

    private IStudioCurriculaService Curricula(StudioMember member) => _services.Request(member.UserId).Get<IStudioCurriculaService>();
    private IDraftService Drafts(StudioMember member) => _services.Request(member.UserId).Get<IDraftService>();
    private IReviewService Reviews(StudioMember member) => _services.Request(member.UserId).Get<IReviewService>();
    private IReleaseService Releases(StudioMember member) => _services.Request(member.UserId).Get<IReleaseService>();

    private async Task<CurriculumPathFixture> PathAsync()
    {
        await using var scope = Scope();
        return await TestData.CreateCurriculumPathAsync(scope.Get<ApplicationDbContext>());
    }

    private async Task<StudioMember> MemberAsync(StudioRole role, IReadOnlyList<Guid>? nodes = null)
    {
        await using var scope = Scope();
        var db = scope.Get<ApplicationDbContext>();
        var userId = await TestData.CreateUserAsync(db);
        var now = DateTime.UtcNow;

        db.StaffProfiles.Add(new StaffProfile
        {
            UserId = userId,
            FullName = $"{role} {userId:N}"[..20],
            StudioRole = role,
            AllNodes = nodes is null,
            AllLanguages = true,
            Status = StaffStatus.Active,
            CreatedAtUtc = now,
            ActivatedAtUtc = now,
            UpdatedAtUtc = now,
            ScopeNodes = (nodes ?? []).Select(n => new StaffScopeNode { UserId = userId, NodeId = n }).ToList()
        });
        await db.SaveChangesAsync();

        return (await scope.Get<IStudioMemberResolver>().ResolveAsync(userId))!;
    }

    private static T Ok<T>(ServiceResult<T> result)
    {
        Assert.True(result.Succeeded, $"{result.Error?.Code}: {string.Join("; ", result.Errors)} {JsonSerializer.Serialize(result.Details)}");
        return result.Value!;
    }
}
