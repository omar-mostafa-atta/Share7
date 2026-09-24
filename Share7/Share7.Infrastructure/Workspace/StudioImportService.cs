using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Interfaces;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Content;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Workspace;

/// <inheritdoc cref="IStudioImportService"/>
/// <remarks>
/// <para>
/// **The sheet's columns follow the content languages**, in their editor order: four per language
/// (question, correct answer, two wrong answers), then one recovery flag. For English and Arabic that
/// is exactly the nine columns the console's lesson sheet has always used, so an existing sheet still
/// imports. A header naming a language by code — "Question (EN)" — lets the columns come in any
/// language order; without one, the editor order is assumed.
/// </para>
/// <para>
/// A trial run saves nothing. Importing into a draft keeps each question's identity where the sheet
/// row lands on a question already there (same pool, same position), so re-importing a sheet with one
/// row fixed keeps every other question's id — as the old sheet path always did by row number.
/// </para>
/// </remarks>
public sealed class StudioImportService : IStudioImportService
{
    public const int MaxRows = 5000;

    private readonly ApplicationDbContext _db;
    private readonly IContentLanguages _languages;
    private readonly ILessonContentPublisher _publisher;
    private readonly ILessonContentReader _reader;
    private readonly DraftService _drafts;
    private readonly IAuditLog _audit;

    public StudioImportService(
        ApplicationDbContext db,
        IContentLanguages languages,
        ILessonContentPublisher publisher,
        ILessonContentReader reader,
        DraftService drafts,
        IAuditLog audit)
    {
        _db = db;
        _languages = languages;
        _publisher = publisher;
        _reader = reader;
        _drafts = drafts;
        _audit = audit;
    }

    // =====================================================================================
    // Trial run
    // =====================================================================================

    public async Task<ServiceResult<ImportTrialDto>> TrialAsync(Stream sheet, CancellationToken cancellationToken = default)
    {
        var read = await ReadAsync(sheet, cancellationToken);
        if (read.Fatal is { } fatal)
            return Invalid(fatal);

        return ServiceResult<ImportTrialDto>.Success(read.Trial!);
    }

    private sealed record SheetRead(ImportTrialDto? Trial, ImportProblemDto? Fatal, IReadOnlyDictionary<(NodeItemRole, int), int> RowOf);

    private async Task<SheetRead> ReadAsync(Stream sheet, CancellationToken cancellationToken)
    {
        var languages = (await _languages.GetAsync(cancellationToken)).Where(l => l.IsContentLanguage).ToList();
        var empty = new Dictionary<(NodeItemRole, int), int>();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(sheet);
        }
        catch (Exception)
        {
            return new(null, new ImportProblemDto(0, null, "notAWorkbook", "The file could not be read as an .xlsx workbook."), empty);
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null)
                return new(null, new ImportProblemDto(0, null, "noWorksheet", "The workbook has no worksheets."), empty);

            var rows = worksheet.RowsUsed().ToList();
            if (rows.Count == 0)
                return new(null, new ImportProblemDto(0, null, "empty", "The sheet is empty."), empty);

            // A header names the languages; without one, the editor's order is assumed.
            var header = rows[0];
            var order = LanguagesFromHeader(header, languages);
            var dataRows = order is null ? rows : rows.Skip(1).ToList();
            order ??= languages.Select(l => l.Id).ToList();

            if (dataRows.Count > MaxRows)
                return new(null, new ImportProblemDto(0, null, "tooManyRows", $"The sheet has {dataRows.Count} rows, above the {MaxRows} limit."), empty);

            var recoveryColumn = order.Count * 4 + 1;
            var problems = new List<ImportProblemDto>();
            var items = new List<ContentDraftItem>();
            var rowOf = new Dictionary<(NodeItemRole, int), int>();
            var mainCount = 0;
            var recoveryCount = 0;

            foreach (var row in dataRows)
            {
                var number = row.RowNumber();
                var flag = row.Cell(recoveryColumn).GetString().Trim();

                if (!TryReadFlag(flag, out var isRecovery))
                {
                    problems.Add(new(number, Letter(recoveryColumn), "recoveryFlag", $"Row {number}: \"{flag}\" is not yes or no."));
                    continue;
                }

                var renderings = new List<ContentDraftRendering>();

                for (var i = 0; i < order.Count; i++)
                {
                    var first = i * 4 + 1;
                    var text = row.Cell(first).GetString().Trim();
                    var correct = row.Cell(first + 1).GetString().Trim();
                    var wrong1 = row.Cell(first + 2).GetString().Trim();
                    var wrong2 = row.Cell(first + 3).GetString().Trim();

                    // A language left entirely blank on a row is simply not written in yet; the checks
                    // below say whether that is allowed.
                    if (text.Length == 0 && correct.Length == 0 && wrong1.Length == 0 && wrong2.Length == 0)
                        continue;

                    renderings.Add(new ContentDraftRendering(order[i], text, [correct, wrong1, wrong2], 0));
                }

                if (renderings.Count == 0)
                    continue;

                var role = isRecovery ? NodeItemRole.Recovery : NodeItemRole.Core;
                var position = isRecovery ? ++recoveryCount : ++mainCount;

                rowOf[(role, position)] = number;
                items.Add(new ContentDraftItem { Role = role, Order = position, Renderings = renderings });
            }

            // The same checks a draft is held to, placed back on the sheet's own rows and columns.
            var covers = order.SelectMany(l => new[] { new ContentSetKey(NodeItemRole.Core, l), new ContentSetKey(NodeItemRole.Recovery, l) }).ToList();
            var checks = await _publisher.CheckAsync(Guid.Empty, items, covers, ContentRuleSet.Studio, cancellationToken);

            foreach (var problem in checks)
            {
                var number = problem.Role is { } role && problem.Order is { } position && rowOf.TryGetValue((role, position), out var sheetRow) ? sheetRow : 0;
                problems.Add(new(number, Column(problem, order), problem.Code, number > 0 ? $"Row {number}: {problem.Message}" : problem.Message));
            }

            return new(new ImportTrialDto(order, items, problems, mainCount, recoveryCount), null, rowOf);
        }
    }

    /// <summary>Languages named in a header row ("Question (EN)"), in column order — or null when there is no header.</summary>
    private static List<Guid>? LanguagesFromHeader(IXLRow header, IReadOnlyList<ContentLanguage> languages)
    {
        var found = new List<Guid>();

        for (var column = 1; ; column += 4)
        {
            var label = header.Cell(column).GetString();
            var open = label.LastIndexOf('(');
            var close = label.LastIndexOf(')');
            if (open < 0 || close <= open) break;

            var code = label[(open + 1)..close].Trim();
            var language = languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
            if (language is null) break;

            found.Add(language.Id);
        }

        return found.Count == 0 ? null : found;
    }

    private static string? Column(ContentProblem problem, IReadOnlyList<Guid> order)
    {
        var index = problem.LangId is { } lang ? order.ToList().IndexOf(lang) : -1;
        if (index < 0) return null;

        var offset = problem.Field switch
        {
            "question" => 1,
            "choice1" => 2,
            "choice2" => 3,
            "choice3" => 4,
            _ => 1
        };

        return Letter(index * 4 + offset);
    }

    private static string Letter(int column)
    {
        var letters = string.Empty;
        while (column > 0)
        {
            var rem = (column - 1) % 26;
            letters = (char)('A' + rem) + letters;
            column = (column - 1) / 26;
        }
        return letters;
    }

    /// <summary>Permissive, as the lesson sheet has always been: blank means a main question.</summary>
    private static bool TryReadFlag(string raw, out bool value)
    {
        value = false;
        switch (raw.ToLowerInvariant())
        {
            case "":
            case "0":
            case "n":
            case "no":
            case "false":
            case "main":
            case "لا":
                return true;
            case "1":
            case "y":
            case "yes":
            case "true":
            case "recovery":
            case "نعم":
                value = true;
                return true;
            default:
                return false;
        }
    }

    // =====================================================================================
    // Into a draft
    // =====================================================================================

    public async Task<ServiceResult<DraftDto>> ImportToDraftAsync(
        StudioMember member, Guid draftId, int revision, Stream sheet, string fileName, CancellationToken cancellationToken = default)
    {
        var draft = await _db.Drafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null) return DraftService.DraftMissing<DraftDto>();

        if (draft.Kind is not (DraftKind.LessonContent or DraftKind.NewNode)
            || draft.Kind == DraftKind.NewNode && draft.NodeKind != NodeKinds.Lesson)
            return ServiceResult<DraftDto>.Failure(WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation,
                "Only a lesson's questions can be imported.", new Dictionary<string, object?> { ["reason"] = "kind" });

        var read = await ReadAsync(sheet, cancellationToken);
        if (read.Fatal is { } fatal)
            return ServiceResult<DraftDto>.Failure(WorkspaceErrors.ImportInvalid, ServiceErrorKind.Validation, fatal.Message,
                new Dictionary<string, object?> { ["problems"] = new[] { fatal } });

        // Keep the identity of every question the sheet lands on: same pool, same position.
        var current = draft.Kind == DraftKind.LessonContent
            ? WorkspaceJson.Read<LessonContentProposal>(draft.ProposedJson)?.Items ?? []
            : WorkspaceJson.Read<NewNodeProposal>(draft.ProposedJson)?.Items ?? [];

        var items = read.Trial!.Items
            .Select(item => item with
            {
                ItemId = current.FirstOrDefault(c => c.Role == item.Role && c.Order == item.Order)?.ItemId,

                // Languages the sheet does not carry keep what the draft had for them.
                Renderings = item.Renderings
                    .Concat((current.FirstOrDefault(c => c.Role == item.Role && c.Order == item.Order)?.Renderings ?? [])
                        .Where(r => !read.Trial.Languages.Contains(r.LangId)))
                    .ToList()
            })
            .ToList();

        var proposal = draft.Kind == DraftKind.LessonContent
            ? JsonSerializer.SerializeToElement(new LessonContentProposal(items), WorkspaceJson.Options)
            : JsonSerializer.SerializeToElement(
                (WorkspaceJson.Read<NewNodeProposal>(draft.ProposedJson) ?? new([], null, null)) with { Items = items },
                WorkspaceJson.Options);

        var saved = await _drafts.SaveAsync(member, draftId, new SaveDraftRequest(revision, proposal), cancellationToken);

        if (saved.Succeeded)
        {
            _audit.Record(new AuditEntry(
                AuditActions.DraftImported,
                AuditAreas.Workspace,
                $"Imported a sheet into a draft: {read.Trial.MainCount} main and {read.Trial.RecoveryCount} recovery question(s).",
                "draft",
                draftId.ToString(),
                new { fileName = fileName.Length > 200 ? fileName[..200] : fileName, main = read.Trial.MainCount, recovery = read.Trial.RecoveryCount, problems = read.Trial.Problems.Count }));
            await _db.SaveChangesAsync(cancellationToken);
        }

        return saved;
    }

    // =====================================================================================
    // Template and export
    // =====================================================================================

    public async Task<byte[]> TemplateAsync(IReadOnlyList<string>? languageCodes, CancellationToken cancellationToken = default)
    {
        var languages = await SheetLanguagesAsync(languageCodes, cancellationToken);

        // One filled row, and it is a recovery row on purpose: an author's first move is to overwrite
        // it, and a template whose only example is a main question teaches the upload to fail the
        // recovery check.
        var example = new ContentDraftItem
        {
            Role = NodeItemRole.Recovery,
            Order = 1,
            Renderings = languages.Select(l => new ContentDraftRendering(l.Id, l.Code == "ar" ? "ما ناتج 2 + 3؟" : "What is 2 + 3?", ["5", "4", "6"], 0)).ToList()
        };

        return Write(languages, [example]);
    }

    public async Task<ServiceResult<byte[]>> ExportAsync(StudioMember member, Guid lessonId, bool fromDraft, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ContentDraftItem> items;

        if (fromDraft)
        {
            var draft = await _db.Drafts.AsNoTracking()
                .FirstOrDefaultAsync(d => d.NodeId == lessonId && d.Kind == DraftKind.LessonContent && d.IsOpen && !d.IsPractice, cancellationToken);
            if (draft is null) return DraftService.DraftMissing<byte[]>();

            items = WorkspaceJson.Read<LessonContentProposal>(draft.ProposedJson)?.Items ?? [];
        }
        else
        {
            var live = await _reader.ReadAsync(lessonId, cancellationToken);
            if (live is null) return DraftService.NodeMissing<byte[]>();

            items = live.Items.Select(DraftKinds.ToDraftItem).ToList();
        }

        var languages = await SheetLanguagesAsync(null, cancellationToken);
        return ServiceResult<byte[]>.Success(Write(languages, items));
    }

    private async Task<IReadOnlyList<ContentLanguage>> SheetLanguagesAsync(IReadOnlyList<string>? codes, CancellationToken cancellationToken)
    {
        var content = (await _languages.GetAsync(cancellationToken)).Where(l => l.IsContentLanguage).ToList();
        if (codes is not { Count: > 0 }) return content;

        var chosen = codes
            .Select(code => content.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
            .OfType<ContentLanguage>()
            .Distinct()
            .ToList();

        return chosen.Count == 0 ? content : chosen;
    }

    private static byte[] Write(IReadOnlyList<ContentLanguage> languages, IReadOnlyList<ContentDraftItem> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Questions");

        var column = 1;
        foreach (var language in languages)
        {
            var code = language.Code.ToUpperInvariant();
            foreach (var label in new[] { $"Question ({code})", $"Correct answer ({code})", $"Wrong answer ({code})", $"Wrong answer ({code})" })
            {
                var cell = sheet.Cell(1, column++);
                cell.Value = label;
                cell.Style.Font.Bold = true;
            }
        }

        var flagColumn = column;
        sheet.Cell(1, flagColumn).Value = "Recovery? (yes/no)";
        sheet.Cell(1, flagColumn).Style.Font.Bold = true;

        var rowNumber = 2;
        foreach (var item in items.OrderBy(i => i.Role).ThenBy(i => i.Order))
        {
            for (var i = 0; i < languages.Count; i++)
            {
                var rendering = item.In(languages[i].Id);
                if (rendering is null) continue;

                var first = i * 4 + 1;
                var correct = rendering.CorrectIndex >= 0 && rendering.CorrectIndex < rendering.Choices.Count ? rendering.Choices[rendering.CorrectIndex] : string.Empty;
                var wrong = rendering.Choices.Where((_, index) => index != rendering.CorrectIndex).ToList();

                sheet.Cell(rowNumber, first).Value = rendering.Text;
                sheet.Cell(rowNumber, first + 1).Value = correct;
                sheet.Cell(rowNumber, first + 2).Value = wrong.ElementAtOrDefault(0) ?? string.Empty;
                sheet.Cell(rowNumber, first + 3).Value = wrong.ElementAtOrDefault(1) ?? string.Empty;
            }

            sheet.Cell(rowNumber, flagColumn).Value = item.Role == NodeItemRole.Recovery ? "yes" : "no";
            rowNumber++;
        }

        // Each language's columns read in its own direction.
        for (var i = 0; i < languages.Count; i++)
        {
            if (languages[i].Direction != Domain.LookUps.LanguageDirections.RightToLeft) continue;
            sheet.Range(1, i * 4 + 1, Math.Max(rowNumber - 1, 1), i * 4 + 4).Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.RightToLeft;
        }

        sheet.Columns(1, flagColumn).AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static ServiceResult<ImportTrialDto> Invalid(ImportProblemDto problem) =>
        ServiceResult<ImportTrialDto>.Failure(WorkspaceErrors.ImportInvalid, ServiceErrorKind.Validation, problem.Message,
            new Dictionary<string, object?> { ["problems"] = new[] { problem } });
}
