using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Studio.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Competency;
using Share7.Domain.Staff;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The outcomes sheet is typed by hand from a ministry document, so the importer is the only thing
/// standing between a typing mistake and the vocabulary everything else measures against.
/// <para>
/// Two mistakes it used to accept: an outcome listed as part of itself, and a pair listed as part
/// of each other. Both were written straight through as <c>ComponentOf</c> edges. Nothing walks
/// those edges today, which is exactly why it was worth closing now — the first thing that rolls a
/// mastery figure up through them would not come back.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SkillImportLoopTests : StaffTestBase
{
    public SkillImportLoopTests(SqlServerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_outcome_cannot_be_part_of_itself()
    {
        var report = await DryRunAsync(("A.1", "A.1"));

        Assert.Contains(report.Problems, p => p.Code == "parentIsSelf");
        Assert.True(report.WroteNothing);
    }

    [Fact]
    public async Task A_pair_of_outcomes_cannot_be_part_of_each_other()
    {
        var report = await DryRunAsync(("A.1", "A.2"), ("A.2", "A.1"));

        Assert.Equal(2, report.Problems.Count(p => p.Code == "parentLoops"));
        Assert.True(report.WroteNothing);
    }

    [Fact]
    public async Task A_sheet_with_a_problem_reports_nothing_it_would_have_done()
    {
        // All three counts or none. Reporting rows it "would leave alone" beside two zeroes read as
        // a partial import, when in fact a sheet with any problem in it writes nothing at all.
        var report = await DryRunAsync(("A.1", "A.1"), ("A.2", null));

        Assert.NotEmpty(report.Problems);
        Assert.Equal(0, report.Added);
        Assert.Equal(0, report.Updated);
        Assert.Equal(0, report.LeftAlone);
    }

    [Fact]
    public async Task An_ordinary_parent_chain_is_not_a_loop()
    {
        var report = await DryRunAsync(("A.1", null), ("A.1.a", "A.1"), ("A.1.a.i", "A.1.a"));

        Assert.Empty(report.Problems);
        Assert.Equal(3, report.Added);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private async Task<SkillImportReport> DryRunAsync(params (string Code, string? Parent)[] rows)
    {
        await using var scope = AsSuperAdmin();
        var db = scope.Get<ApplicationDbContext>();
        await IdentityTestHost.EnsureRolesAsync(db);

        var languages = await db.Languages.AsNoTracking().Where(l => l.IsContentLanguage).OrderBy(l => l.SortOrder).ToListAsync();
        Assert.NotEmpty(languages);

        var framework = new CompetencyFramework
        {
            Id = Guid.NewGuid(),
            FrameworkKey = $"loops-{Guid.NewGuid():N}"[..24],
            Name = "Loop check",
            VersionLabel = "2026",
            CreatedAtUtc = DateTime.UtcNow
        };

        db.CompetencyFrameworks.Add(framework);
        await db.SaveChangesAsync();

        using var sheet = Sheet(rows, languages.Select(l => l.Code).ToList());

        var member = new StudioMember(Guid.NewGuid(), StudioRole.Lead, AllNodes: true, [], AllLanguages: true, new HashSet<Guid>());
        var result = await scope.Get<IStudioSkillsService>().ImportAsync(member, framework.Id, sheet, dryRun: true);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Value!;
    }

    private static MemoryStream Sheet(IReadOnlyList<(string Code, string? Parent)> rows, IReadOnlyList<string> languageCodes)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Outcomes");

        var headings = new List<string> { "Code", "Kind", "Parent code", "Band" };
        headings.AddRange(languageCodes.Select(code => $"Statement ({code})"));

        for (var i = 0; i < headings.Count; i++)
            sheet.Cell(1, i + 1).Value = headings[i];

        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Cell(r + 2, 1).Value = rows[r].Code;
            sheet.Cell(r + 2, 2).Value = TargetKinds.Skill;
            sheet.Cell(r + 2, 3).Value = rows[r].Parent ?? string.Empty;
            sheet.Cell(r + 2, 4).Value = 1;

            for (var i = 0; i < languageCodes.Count; i++)
                sheet.Cell(r + 2, 5 + i).Value = $"Can do {rows[r].Code}.";
        }

        var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        buffer.Position = 0;
        return buffer;
    }
}
