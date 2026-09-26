using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Application.Users.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Structure;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The accounting in authentication, authorization and accounting: every change to curriculum,
/// questions and accounts leaves a row naming who made it, and nothing can take that row back.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AuditTrailTests
{
    private readonly SqlServerFixture _fixture;

    public AuditTrailTests(SqlServerFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------- accounts

    [Fact]
    public async Task Creating_an_account_records_who_did_it_and_the_role_but_not_the_username()
    {
        await using var context = _fixture.CreateContext();
        var actor = new TestAuditActor(Guid.NewGuid(), [Roles.SuperAdmin]);
        var username = $"staff_{Guid.NewGuid():N}"[..20];

        var created = await (await UserAdminAsync(context, actor)).CreateUserAsync(
            new CreateAdminUserRequest { Username = username, Password = "Content#2026", Role = Roles.Admin },
            actorIsSuperAdmin: true);

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        await using var check = _fixture.CreateContext();
        var row = await check.AuditEvents.SingleAsync(e => e.TargetId == created.Value!.UserId.ToString());

        Assert.Equal(AuditActions.AccountCreated, row.Action);
        Assert.Equal(AuditAreas.Accounts, row.Area);
        Assert.Equal(actor.UserId, row.ActorUserId);
        Assert.Equal(Roles.SuperAdmin, row.ActorRoles);
        Assert.Equal(actor.IpAddress, row.IpAddress);
        Assert.Equal(actor.CorrelationId, row.CorrelationId);
        Assert.Contains(Roles.Admin, row.DataJson);

        // Ids only: the row has to be able to outlive the account without carrying anything that
        // describes the person.
        Assert.DoesNotContain(username, row.Summary);
        Assert.DoesNotContain(username, row.DataJson ?? string.Empty);
    }

    [Fact]
    public async Task A_refused_account_creation_leaves_no_record_claiming_it_happened()
    {
        await using var context = _fixture.CreateContext();
        var actor = new TestAuditActor(Guid.NewGuid(), [Roles.Admin]);

        // An Admin may not mint a SuperAdmin — refused before anything is written. (Since 2026-09-26
        // an Admin may create another Admin, so that is no longer the refusal to test with.)
        var refused = await (await UserAdminAsync(context, actor)).CreateUserAsync(
            new CreateAdminUserRequest { Username = $"sup_{Guid.NewGuid():N}"[..20], Password = "Content#2026", Role = Roles.SuperAdmin },
            actorIsSuperAdmin: false);

        Assert.False(refused.Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.False(await check.AuditEvents.AnyAsync(e => e.ActorUserId == actor.UserId));
    }

    [Fact]
    public async Task Deleting_an_account_is_recorded_and_the_record_survives_the_account()
    {
        await using var context = _fixture.CreateContext();
        var actor = new TestAuditActor(Guid.NewGuid(), [Roles.Admin]);
        var service = await UserAdminAsync(context, actor);

        var created = await service.CreateUserAsync(
            new CreateAdminUserRequest { Username = $"gone_{Guid.NewGuid():N}"[..20], Password = "Content#2026", Role = Roles.Student },
            actorIsSuperAdmin: false);
        var userId = created.Value!.UserId;

        var deleted = await service.DeleteUserAsync(userId, actor.UserId, actorIsSuperAdmin: false);
        Assert.True(deleted.Succeeded, string.Join("; ", deleted.Errors));

        await using var check = _fixture.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.Id == userId));

        var actions = await check.AuditEvents
            .Where(e => e.TargetId == userId.ToString())
            .OrderBy(e => e.Sequence)
            .Select(e => e.Action)
            .ToListAsync();

        Assert.Equal([AuditActions.AccountCreated, AuditActions.AccountDeleted], actions);
    }

    // ------------------------------------------------------------- content

    [Fact]
    public async Task Publishing_a_lesson_records_the_publish_in_the_same_commit()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        var actor = new TestAuditActor(Guid.NewGuid(), [Roles.ContentTeam]);

        var sheet = EngineTest.Sheet(context, TestAudit.For(context, actor));
        var result = await sheet.SaveAsync(path.LessonId, new SaveLessonSheetRequest
        {
            Rows =
            [
                Row(1, isRecovery: false),
                Row(2, isRecovery: true)
            ]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));

        await using var check = _fixture.CreateContext();
        var row = await check.AuditEvents.SingleAsync(e =>
            e.Action == AuditActions.QuestionsPublished && e.TargetId == path.LessonId.ToString());

        Assert.Equal(actor.UserId, row.ActorUserId);
        Assert.Equal("lesson", row.TargetType);
        Assert.Contains("\"path\":\"lesson-sheet\"", row.DataJson);
        Assert.Contains($"\"to\":{result.MainVersion}", row.DataJson);
    }

    [Fact]
    public async Task A_refused_publish_leaves_no_record()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);

        // No recovery row: refused whole, so nothing was published and nothing may claim it was.
        var sheet = EngineTest.Sheet(context);
        var result = await sheet.SaveAsync(path.LessonId, new SaveLessonSheetRequest { Rows = [Row(1, isRecovery: false)] });

        Assert.False(result.Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.False(await check.AuditEvents.AnyAsync(e => e.TargetId == path.LessonId.ToString()));
    }

    [Fact]
    public async Task A_forced_delete_retires_and_records_what_it_hid()
    {
        await using var context = _fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(context);
        await context.AddQuestionsAsync(path.LessonId, 3);

        var actor = new TestAuditActor(Guid.NewGuid(), [Roles.Admin]);
        var service = new CurriculumAdminService(
            context,
            new StubLanguageService(LanguageIds.English),
            EngineTest.Structure(context, TestAudit.For(context, actor)),
            new TestCurrentUser(actor.UserId));

        var deleted = await service.DeleteLessonAsync(path.LessonId, force: true);
        Assert.True(deleted.Succeeded, string.Join("; ", deleted.Errors));

        await using var check = _fixture.CreateContext();

        // Deleting retires: the record says so, and nothing it names has gone anywhere.
        var row = await check.AuditEvents.SingleAsync(e =>
            e.Action == AuditActions.CurriculumNodeRetired && e.TargetId == path.LessonId.ToString());

        Assert.Equal(actor.UserId, row.ActorUserId);
        Assert.True(await check.Lessons.IgnoreQueryFilters().AnyAsync(l => l.Id == path.LessonId && l.RetiredAtUtc != null));
        Assert.False(await check.Lessons.AnyAsync(l => l.Id == path.LessonId));
        Assert.Equal(4, await check.Questions.CountAsync(q => q.LessonId == path.LessonId));
    }

    // ------------------------------------------------------------- append-only

    [Fact]
    public async Task An_audit_row_can_be_neither_edited_nor_removed()
    {
        await using var context = _fixture.CreateContext();
        var marker = Guid.NewGuid().ToString();

        TestAudit.For(context).Record(new AuditEntry(
            AuditActions.CurriculumNodeCreated, AuditAreas.Curriculum, "Test row.", "test", marker));
        await context.SaveChangesAsync();

        await using var attacker = _fixture.CreateContext();

        // The database refuses both, whoever asks — this is not a rule the application keeps, it is
        // one the table enforces.
        await Assert.ThrowsAsync<SqlException>(() => attacker.AuditEvents
            .Where(e => e.TargetId == marker)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Summary, "Rewritten.")));

        await Assert.ThrowsAsync<SqlException>(() => attacker.AuditEvents
            .Where(e => e.TargetId == marker)
            .ExecuteDeleteAsync());

        await using var check = _fixture.CreateContext();
        var row = await check.AuditEvents.SingleAsync(e => e.TargetId == marker);
        Assert.Equal("Test row.", row.Summary);
    }

    [Fact]
    public async Task Rows_are_numbered_in_the_order_they_were_written()
    {
        await using var context = _fixture.CreateContext();
        var marker = Guid.NewGuid().ToString();
        var audit = TestAudit.For(context);

        for (var i = 0; i < 3; i++)
        {
            audit.Record(new AuditEntry(
                AuditActions.CurriculumNodeCreated, AuditAreas.Curriculum, $"Row {i}.", "test", marker));
            await context.SaveChangesAsync();
        }

        await using var check = _fixture.CreateContext();
        var summaries = await check.AuditEvents
            .Where(e => e.TargetId == marker)
            .OrderBy(e => e.Sequence)
            .Select(e => e.Summary)
            .ToListAsync();

        Assert.Equal(["Row 0.", "Row 1.", "Row 2."], summaries);
    }

    // ------------------------------------------------------------- helpers

    private static async Task<UserAdminService> UserAdminAsync(ApplicationDbContext context, IAuditActor actor)
    {
        await IdentityTestHost.EnsureRolesAsync(context);

        return new UserAdminService(
            IdentityTestHost.CreateUserManager(context),
            context,
            new WalletService(context),
            new LevelService(context),
            ObjectiveTestExtensions.CreateObjectiveService(context),
            TestAudit.For(context, actor));
    }

    private static LessonSheetRow Row(int rowNumber, bool isRecovery) => new()
    {
        RowNumber = rowNumber,
        IsRecovery = isRecovery,
        QuestionEn = $"Question {rowNumber}?",
        CorrectEn = "Right",
        WrongEn1 = "Wrong one",
        WrongEn2 = "Wrong two",
        QuestionAr = $"سؤال {rowNumber}؟",
        CorrectAr = "صحيح",
        WrongAr1 = "خطأ أول",
        WrongAr2 = "خطأ ثان"
    };
}
