using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Engine.Reads;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The Content Studio's workspace end to end, over the real container and database (plan Phase 3).
/// <para>
/// **The gate:** a full Draft → Review → Release → Rollback cycle, with what the game reads checked
/// at each step — plus the rules that make it safe: nobody approves their own work, scope limits
/// what a member changes, a draft that live content moved under cannot go out until it is brought
/// up to date, a release that cannot go out in full changes nothing, and practice never reaches
/// students.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class WorkspaceCycleTests : IDisposable
{
    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly SqlServerFixture _fixture;
    private readonly ServiceProvider _services;

    public WorkspaceCycleTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _services = StaffTestHost.Build(fixture);
    }

    public void Dispose() => _services.Dispose();

    // =====================================================================================
    // The gate
    // =====================================================================================

    [Fact]
    public async Task A_lesson_goes_from_draft_through_review_and_release_to_students_and_rolls_back()
    {
        var path = await PathAsync();
        var author = await MemberAsync(StudioRole.Author);
        var reviewer = await MemberAsync(StudioRole.Reviewer);
        var lead = await MemberAsync(StudioRole.Lead);

        // ── Draft ──────────────────────────────────────────────────────────────────────────
        var draft = Ok(await Drafts(author).CreateAsync(author.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = path.LessonId }));
        var live = Proposal<LessonContentProposal>(draft);
        var fixtureItem = Assert.Single(live.Items);

        // Before the release nothing a student reads has moved.
        var before = await QuestionsAsync(path.LessonId, En);

        draft = Ok(await Drafts(author).SaveAsync(author.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(new LessonContentProposal(
        [
            // The fixture's question, kept as it is in English and given its Arabic.
            fixtureItem with { Renderings = [.. fixtureItem.Renderings, new ContentDraftRendering(Ar, "رمز الحديد؟", ["Fe", "Ir", "F"], 0)] },
            Item(NodeItemRole.Core, 2, "Symbol for gold?", "رمز الذهب؟", "Au", "Ag", "Go"),
            Item(NodeItemRole.Recovery, 1, "Is iron a metal?", "هل الحديد فلز؟", "Yes", "No", "Sometimes")
        ])))));

        Assert.Empty(draft.Problems);
        Assert.Equal(Wire(before), Wire(await QuestionsAsync(path.LessonId, En)));

        // ── Review ─────────────────────────────────────────────────────────────────────────
        draft = Ok(await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        Assert.Equal(DraftStatus.InReview, draft.Summary.Status);

        // An Author does not review at all.
        var author_ = await Reviews(author).ApproveAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision));
        Assert.Equal(WorkspaceErrors.OutOfScope, author_.Error);

        var queue = await Reviews(reviewer).QueueAsync(reviewer.Member);
        Assert.Contains(queue, q => q.Draft.Id == draft.Summary.Id && q.CanReview);

        draft = Ok(await Reviews(reviewer).ApproveAsync(reviewer.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision, "Checked both languages.")));
        Assert.Equal(DraftStatus.Approved, draft.Summary.Status);

        // ── Release ────────────────────────────────────────────────────────────────────────
        var denied = await Releases(reviewer).CreateAsync(reviewer.Member, new CreateReleaseRequest("Iron", null, [draft.Summary.Id]));
        Assert.Equal(WorkspaceErrors.OutOfScope, denied.Error);

        var release = Ok(await Releases(lead).CreateAsync(lead.Member, new CreateReleaseRequest("Iron and gold", null, [draft.Summary.Id])));
        Assert.Empty(release.Blockers);

        release = Ok(await Releases(lead).PublishAsync(lead.Member, release.Summary.Id));
        Assert.Equal(ReleaseStatus.Published, release.Summary.Status);

        var english = await QuestionsAsync(path.LessonId, En);
        var arabic = await QuestionsAsync(path.LessonId, Ar);
        Assert.Equal(["Symbol for iron?", "Symbol for gold?"], english.Questions.Select(q => q.Text));
        Assert.Equal(["رمز الحديد؟", "رمز الذهب؟"], arabic.Questions.Select(q => q.Text));

        // The unchanged English question kept the id devices already cached.
        Assert.Equal(before.Questions[0].QuestionId, english.Questions[0].QuestionId);
        Assert.Equal(1, english.Version);

        await using (var check = Scope())
        {
            var db = check.Get<ApplicationDbContext>();
            Assert.Equal(DraftStatus.Released, (await db.Drafts.SingleAsync(d => d.Id == draft.Summary.Id)).Status);
            Assert.True(await db.ContentPublications.AnyAsync(p => p.NodeId == path.LessonId && p.ReleaseId == release.Summary.Id));
            Assert.Single(await Recovery(db, path.LessonId, En));
        }

        // ── Rollback ───────────────────────────────────────────────────────────────────────
        var noReason = await Releases(lead).RollbackAsync(lead.Member, release.Summary.Id, new RollbackReleaseRequest(""));
        Assert.Equal(WorkspaceErrors.ReleaseInvalid, noReason.Error);

        var rollback = Ok(await Releases(lead).RollbackAsync(lead.Member, release.Summary.Id, new RollbackReleaseRequest("Gold is next term's.")));
        Assert.Equal(release.Summary.Id, rollback.Summary.RollbackOfReleaseId);

        var restored = await QuestionsAsync(path.LessonId, En);
        Assert.Equal(["Symbol for iron?"], restored.Questions.Select(q => q.Text));

        // Versions never go down: the rollback is a new version whose content is the old one.
        Assert.Equal(2, restored.Version);
        Assert.Equal(before.Questions[0].QuestionId, restored.Questions[0].QuestionId);
        Assert.Empty((await QuestionsAsync(path.LessonId, Ar)).Questions);

        // Rolling back twice is not a thing.
        var again = await Releases(lead).RollbackAsync(lead.Member, release.Summary.Id, new RollbackReleaseRequest("Again."));
        Assert.Equal(WorkspaceErrors.ReleaseWrongStatus, again.Error);
    }

    [Fact]
    public async Task A_new_lesson_with_its_questions_is_one_draft_and_rolling_it_back_retires_it()
    {
        var path = await PathAsync();
        var author = await MemberAsync(StudioRole.Author);
        var lead = await MemberAsync(StudioRole.Lead);

        var draft = Ok(await Drafts(author).CreateAsync(author.Member, new CreateDraftRequest
        {
            Kind = DraftKind.NewNode,
            ParentNodeId = path.ChapterId,
            NodeKind = NodeKinds.Lesson,
            Proposal = Element(new NewNodeProposal(
                [new NodeTitle(En, "Alloys"), new NodeTitle(Ar, "السبائك")],
                null,
                [
                    Item(NodeItemRole.Core, 1, "Bronze is copper and…?", "البرونز نحاس و…؟", "Tin", "Iron", "Salt"),
                    Item(NodeItemRole.Recovery, 1, "Is steel an alloy?", "هل الصلب سبيكة؟", "Yes", "No", "Only when hot")
                ]))
        }));

        Assert.Empty(draft.Problems);
        var lessonId = draft.Summary.NodeId!.Value;

        draft = Ok(await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        draft = Ok(await Reviews(lead).ApproveAsync(lead.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));

        var release = Ok(await Releases(lead).CreateAsync(lead.Member, new CreateReleaseRequest("Alloys", null, [draft.Summary.Id])));
        release = Ok(await Releases(lead).PublishAsync(lead.Member, release.Summary.Id));

        // Live in both shapes, with its questions, under the id the draft reserved.
        await using (var check = Scope())
        {
            var reads = new NodeCurriculumReads(check.Get<ApplicationDbContext>());
            Assert.Contains(await reads.LessonsAsync(path.ChapterId, En, default), l => l.Id == lessonId && l.Name == "Alloys" && l.HasQuestions);
            Assert.True(await check.Get<ApplicationDbContext>().Lessons.AnyAsync(l => l.Id == lessonId));
        }

        Ok(await Releases(lead).RollbackAsync(lead.Member, release.Summary.Id, new RollbackReleaseRequest("Not this year.")));

        await using (var check = Scope())
        {
            var db = check.Get<ApplicationDbContext>();
            Assert.False(await db.Lessons.AnyAsync(l => l.Id == lessonId));
            Assert.True(await db.CurriculumNodes.AnyAsync(n => n.Id == lessonId && n.RetiredAtUtc != null));
        }
    }

    // =====================================================================================
    // What keeps it safe
    // =====================================================================================

    [Fact]
    public async Task A_draft_that_live_content_moved_under_must_be_brought_up_to_date_first()
    {
        var path = await PathAsync();
        var author = await MemberAsync(StudioRole.Author);

        var draft = Ok(await Drafts(author).CreateAsync(author.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = path.LessonId }));
        draft = Ok(await Drafts(author).SaveAsync(author.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(ValidContent()))));

        // Meanwhile, an old admin path publishes the same lesson directly.
        await using (var admin = Scope())
        {
            var sheet = new LessonSheetService(
                admin.Get<ApplicationDbContext>(),
                admin.Get<Share7.Application.Engine.Interfaces.ILessonContentReader>(),
                admin.Get<Share7.Application.Engine.Interfaces.ILessonContentPublisher>(),
                admin.Get<Share7.Application.Engine.Interfaces.IContentLanguages>());

            var saved = await sheet.SaveAsync(path.LessonId, new SaveLessonSheetRequest
            {
                Rows =
                [
                    new LessonSheetRow { RowNumber = 1, QuestionEn = "Changed underneath?", CorrectEn = "Yes", WrongEn1 = "No", WrongEn2 = "Maybe", QuestionAr = "تغير؟", CorrectAr = "نعم", WrongAr1 = "لا", WrongAr2 = "ربما" },
                    new LessonSheetRow { RowNumber = 2, IsRecovery = true, QuestionEn = "R?", CorrectEn = "a", WrongEn1 = "b", WrongEn2 = "c", QuestionAr = "ر؟", CorrectAr = "أ", WrongAr1 = "ب", WrongAr2 = "ت" }
                ]
            });
            Assert.True(saved.Succeeded, string.Join("; ", saved.Errors.Select(e => e.Message)));
        }

        var stale = await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision));
        Assert.Equal(WorkspaceErrors.DraftOutOfDate, stale.Error);

        draft = Ok(await Drafts(author).GetAsync(author.Member, draft.Summary.Id));
        Assert.True(draft.Summary.IsOutOfDate);

        // The author merges against what is live now, and the draft is current again.
        var liveNow = Ok(await Drafts(author).LiveNowAsync(author.Member, draft.Summary.Id));
        Assert.Contains("Changed underneath?", liveNow.GetRawText());

        draft = Ok(await Drafts(author).RebaseAsync(author.Member, draft.Summary.Id, new RebaseDraftRequest(draft.Summary.Revision, Element(ValidContent()))));
        Assert.False(draft.Summary.IsOutOfDate);

        Ok(await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
    }

    [Fact]
    public async Task Scope_limits_what_a_member_changes_but_not_what_they_see()
    {
        var path = await PathAsync();
        var elsewhere = await PathAsync();

        // An Arabic-only author for this lesson's subject.
        var arabicOnly = await MemberAsync(StudioRole.Author, nodes: [path.SubjectId], languages: [Ar]);

        var outside = await Drafts(arabicOnly).CreateAsync(arabicOnly.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = elsewhere.LessonId });
        Assert.Equal(WorkspaceErrors.OutOfScope, outside.Error);

        var draft = Ok(await Drafts(arabicOnly).CreateAsync(arabicOnly.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = path.LessonId }));
        var item = Assert.Single(Proposal<LessonContentProposal>(draft).Items);

        // Adding the Arabic is theirs to do…
        draft = Ok(await Drafts(arabicOnly).SaveAsync(arabicOnly.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(new LessonContentProposal(
            [item with { Renderings = [.. item.Renderings, new ContentDraftRendering(Ar, "رمز الحديد؟", ["Fe", "Ir", "F"], 0)] }])))));

        // …rewording the English is not.
        var english = await Drafts(arabicOnly).SaveAsync(arabicOnly.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(new LessonContentProposal(
            [item with { Renderings = [new ContentDraftRendering(En, "Iron's symbol?", ["Fe", "Ir", "F"], 0), new ContentDraftRendering(Ar, "رمز الحديد؟", ["Fe", "Ir", "F"], 0)] }]))));

        Assert.Equal(WorkspaceErrors.OutOfScope, english.Error);
        Assert.Equal("languages", english.Details!["reason"]);

        // Reading anything is open to everyone in the Studio.
        Ok(await _services.CreateAsyncScope().Get<IStudioCurriculumService>().LessonAsync(arabicOnly.Member, elsewhere.LessonId));
    }

    [Fact]
    public async Task Nobody_approves_a_draft_they_wrote_any_of()
    {
        var path = await PathAsync();
        var lead = await MemberAsync(StudioRole.Lead);

        var draft = Ok(await Drafts(lead).CreateAsync(lead.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = path.LessonId }));
        draft = Ok(await Drafts(lead).SaveAsync(lead.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(ValidContent()))));
        draft = Ok(await Drafts(lead).SubmitAsync(lead.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));

        // A Lead can review — but not this one, which they wrote.
        var own = await Reviews(lead).ApproveAsync(lead.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision));
        Assert.Equal(WorkspaceErrors.OwnWork, own.Error);
        Assert.False(draft.Can.Review);
    }

    [Fact]
    public async Task Practice_is_reviewed_like_anything_else_and_never_released()
    {
        var author = await MemberAsync(StudioRole.Author);
        var lead = await MemberAsync(StudioRole.Lead);

        var draft = Ok(await Drafts(author).CreateAsync(author.Member, new CreateDraftRequest
        {
            Kind = DraftKind.LessonContent,
            IsPractice = true,
            Proposal = Element(ValidContent())
        }));

        draft = Ok(await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        draft = Ok(await Reviews(lead).ApproveAsync(lead.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));

        var release = await Releases(lead).CreateAsync(lead.Member, new CreateReleaseRequest("Practice", null, [draft.Summary.Id]));
        Assert.Equal(WorkspaceErrors.PracticeNotReleasable, release.Error);
    }

    [Fact]
    public async Task A_release_that_cannot_go_out_whole_changes_nothing()
    {
        var path = await PathAsync();
        var author = await MemberAsync(StudioRole.Author);
        var lead = await MemberAsync(StudioRole.Lead);

        var draft = Ok(await Drafts(author).CreateAsync(author.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = path.LessonId }));
        draft = Ok(await Drafts(author).SaveAsync(author.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(ValidContent()))));
        draft = Ok(await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        draft = Ok(await Reviews(lead).ApproveAsync(lead.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));

        var release = Ok(await Releases(lead).CreateAsync(lead.Member, new CreateReleaseRequest("Iron", null, [draft.Summary.Id])));

        // Edited after approval: what was approved is not what is there, so it is not approved.
        draft = Ok(await Drafts(author).SaveAsync(author.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(ValidContent("Iron, again?")))));
        Assert.Equal(DraftStatus.Editing, draft.Summary.Status);

        var before = await QuestionsAsync(path.LessonId, En);
        var refused = await Releases(lead).PublishAsync(lead.Member, release.Summary.Id);

        Assert.Equal(WorkspaceErrors.ReleaseNotReady, refused.Error);
        Assert.Equal(Wire(before), Wire(await QuestionsAsync(path.LessonId, En)));

        var failed = Ok(await Releases(lead).GetAsync(lead.Member, release.Summary.Id));
        Assert.Equal(ReleaseStatus.Failed, failed.Summary.Status);
        Assert.Contains(failed.Blockers, b => b.DraftId == draft.Summary.Id && b.Reason == "notApproved");
    }

    [Fact]
    public async Task A_scheduled_release_publishes_itself_when_its_time_comes()
    {
        var path = await PathAsync();
        var author = await MemberAsync(StudioRole.Author);
        var lead = await MemberAsync(StudioRole.Lead);

        var draft = Ok(await Drafts(author).CreateAsync(author.Member, new CreateDraftRequest { Kind = DraftKind.LessonContent, NodeId = path.LessonId }));
        draft = Ok(await Drafts(author).SaveAsync(author.Member, draft.Summary.Id, new SaveDraftRequest(draft.Summary.Revision, Element(ValidContent()))));
        draft = Ok(await Drafts(author).SubmitAsync(author.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));
        draft = Ok(await Reviews(lead).ApproveAsync(lead.Member, draft.Summary.Id, new DraftActionRequest(draft.Summary.Revision)));

        var release = Ok(await Releases(lead).CreateAsync(lead.Member, new CreateReleaseRequest("Start of term", null, [draft.Summary.Id])));
        release = Ok(await Releases(lead).ScheduleAsync(lead.Member, release.Summary.Id, new ScheduleReleaseRequest(DateTime.UtcNow.AddDays(10))));
        Assert.Equal(ReleaseStatus.Scheduled, release.Summary.Status);

        await using (var clock = Scope())
        {
            // The start of term arrives.
            await clock.Get<ApplicationDbContext>().Releases.Where(r => r.Id == release.Summary.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ScheduledForUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        await using (var scheduler = Scope())
            Assert.Equal(1, await scheduler.Get<IReleaseService>().PublishDueAsync());

        Assert.Equal(["Iron?"], (await QuestionsAsync(path.LessonId, En)).Questions.Select(q => q.Text));
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private sealed record TestMember(StudioMember Member);

    private AsyncServiceScope Scope() => _services.CreateAsyncScope();

    // Each call is its own "request", as in the API.
    private IDraftService Drafts(TestMember member) => _services.Request(member.Member.UserId).Get<IDraftService>();
    private IReviewService Reviews(TestMember member) => _services.Request(member.Member.UserId).Get<IReviewService>();
    private IReleaseService Releases(TestMember member) => _services.Request(member.Member.UserId).Get<IReleaseService>();

    private async Task<CurriculumPathFixture> PathAsync()
    {
        await using var scope = Scope();
        return await TestData.CreateCurriculumPathAsync(scope.Get<ApplicationDbContext>());
    }

    private async Task<TestMember> MemberAsync(StudioRole role, IReadOnlyList<Guid>? nodes = null, IReadOnlyList<Guid>? languages = null)
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
            AllLanguages = languages is null,
            Status = StaffStatus.Active,
            CreatedAtUtc = now,
            ActivatedAtUtc = now,
            UpdatedAtUtc = now,
            ScopeNodes = (nodes ?? []).Select(n => new StaffScopeNode { UserId = userId, NodeId = n }).ToList(),
            ScopeLanguages = (languages ?? []).Select(l => new StaffScopeLanguage { UserId = userId, LanguageId = l }).ToList()
        });
        await db.SaveChangesAsync();

        var member = await scope.Get<IStudioMemberResolver>().ResolveAsync(userId);
        return new TestMember(member!);
    }

    private async Task<LessonQuestionsDto> QuestionsAsync(Guid lessonId, Guid langId)
    {
        await using var scope = Scope();
        return (await new NodeCurriculumReads(scope.Get<ApplicationDbContext>()).QuestionsAsync(lessonId, NodeItemRole.Core, langId, default))!;
    }

    private static Task<List<Domain.Curriculum.Question>> Recovery(ApplicationDbContext db, Guid lessonId, Guid langId) =>
        db.ItemLocalizations.Where(q => q.LessonId == lessonId && q.Role == NodeItemRole.Recovery && q.LangId == langId && q.IsActive).ToListAsync();

    private static LessonContentProposal ValidContent(string english = "Iron?") => new(
    [
        Item(NodeItemRole.Core, 1, english, "الحديد؟", "Fe", "Ir", "F"),
        Item(NodeItemRole.Recovery, 1, "Metal?", "فلز؟", "Yes", "No", "Maybe")
    ]);

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

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Json);

    /// <summary>What the game would receive, for comparing two reads.</summary>
    private static string Wire(LessonQuestionsDto questions) => JsonSerializer.Serialize(questions, Json);

    private static T Proposal<T>(DraftDto draft) => draft.Proposal.Deserialize<T>(Json)!;

    private static T Ok<T>(ServiceResult<T> result)
    {
        Assert.True(result.Succeeded, $"{result.Error?.Code}: {string.Join("; ", result.Errors)} {JsonSerializer.Serialize(result.Details)}");
        return result.Value!;
    }
}
