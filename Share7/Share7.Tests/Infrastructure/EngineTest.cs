using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Audit.Interfaces;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Engine.Reads;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Persistence.Migrations;
using Share7.Infrastructure.Progress;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// The education engine's services, built over a test context the way the container builds them.
/// <para>
/// **Which curriculum reads the tests use is a switch**, <c>SHARE7_TEST_READ_MODEL</c> =
/// <c>Legacy</c> (the default), <c>Shadow</c> or <c>Generic</c>. Running the whole suite with
/// <c>Generic</c> puts every progress, unlock and matchmaking test on the node tree; with
/// <c>Shadow</c> every read is answered from the typed tables and checked against the node tree,
/// and <see cref="ShadowTally"/> holds what disagreed.
/// </para>
/// </summary>
public static class EngineTest
{
    public static CurriculumReadModel ReadModel { get; } =
        Enum.TryParse<CurriculumReadModel>(Environment.GetEnvironmentVariable("SHARE7_TEST_READ_MODEL"), ignoreCase: true, out var model)
            ? model
            : CurriculumReadModel.Legacy;

    /// <summary>Every shadow comparison the suite made, across every test.</summary>
    public static CurriculumReadTally ShadowTally { get; } = new();

    /// <summary>
    /// In Shadow, with <c>SHARE7_TEST_SHADOW_REPORT</c> naming a file, the tally is written there
    /// when the test process ends: one line per read, compared / differed / failed, and the first
    /// difference. Nothing differing across the whole suite is the goal.
    /// </summary>
    static EngineTest()
    {
        var report = Environment.GetEnvironmentVariable("SHARE7_TEST_SHADOW_REPORT");
        if (ReadModel != CurriculumReadModel.Shadow || string.IsNullOrWhiteSpace(report))
            return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var lines = ShadowTally.Pending()
                .Select(r => $"{r.Read}\tcompared={r.Compared}\tdiffered={r.Differed}\tfailed={r.Failed}\t{r.Sample}");
            File.WriteAllLines(report, lines);
        };
    }

    public static ICurriculumReads Reads(ApplicationDbContext context) => ReadModel switch
    {
        CurriculumReadModel.Generic => new NodeCurriculumReads(context),
        CurriculumReadModel.Shadow => new ShadowCurriculumReads(
            new TypedCurriculumReads(context), new NodeCurriculumReads(context), ShadowTally, 1.0,
            NullLogger<ShadowCurriculumReads>.Instance),
        _ => new TypedCurriculumReads(context)
    };

    public static UnlockService Unlocks(ApplicationDbContext context) => new(context, Reads(context));

    public static LessonContentPublisher Publisher(ApplicationDbContext context, IAuditLog? audit = null) =>
        new(context, new ContentLanguages(context), new ItemIdentityMinter(context), audit ?? TestAudit.For(context));

    public static LessonSheetService Sheet(ApplicationDbContext context, IAuditLog? audit = null) =>
        new(context, new LessonContentReader(context), Publisher(context, audit), new ContentLanguages(context));

    public static QuestionImportService MainImport(ApplicationDbContext context, IAuditLog? audit = null) =>
        new(context, Publisher(context, audit));

    public static RecoveryQuestionImportService RecoveryImport(ApplicationDbContext context, IAuditLog? audit = null) =>
        new(context, Publisher(context, audit));

    public static CurriculumStructureService Structure(ApplicationDbContext context, IAuditLog? audit = null) =>
        new(context, new ContentLanguages(context), audit ?? TestAudit.For(context), new UnlockRepairSignal());

    public static UnlockRepairRunner Repairs(ApplicationDbContext context) =>
        new(context, Unlocks(context), NullLogger<UnlockRepairRunner>.Instance);

    /// <summary>
    /// What the EngineAuthoritative migration does to content written the old way: nodes for typed
    /// rows, the recovery pool into the item bank, served versions into their generic table. Tests
    /// that write typed rows directly run it, exactly as the seeder does.
    /// </summary>
    public static Task BackfillAsync(ApplicationDbContext context, CancellationToken cancellationToken = default) =>
        context.Database.ExecuteSqlRawAsync(EngineBackfill.Sql, cancellationToken);
}

/// <summary>A signed-in user with fixed details, for services that stamp who made a change.</summary>
public sealed class TestCurrentUser : Share7.Application.Common.Interfaces.ICurrentUserService
{
    public TestCurrentUser(Guid? userId, Guid? langId = null)
    {
        UserId = userId;
        PreferredLanguageId = langId ?? Share7.Domain.Constants.LanguageIds.English;
    }

    public Guid? UserId { get; }
    public string? Email => null;
    public bool IsAuthenticated => UserId is not null;
    public Guid? PreferredLanguageId { get; }
}
