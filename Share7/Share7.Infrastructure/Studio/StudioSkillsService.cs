using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Competency.Interfaces;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Studio.Interfaces;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Competency;
using Share7.Domain.Content;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Workspace;

namespace Share7.Infrastructure.Studio;

/// <inheritdoc cref="IStudioSkillsService"/>
public sealed class StudioSkillsService : IStudioSkillsService
{
    private readonly ApplicationDbContext _db;
    private readonly IContentLanguages _languages;
    private readonly ITargetAuthoringService _authoring;
    private readonly StudioScope _scope;
    private readonly IAuditLog _audit;

    public StudioSkillsService(
        ApplicationDbContext db,
        IContentLanguages languages,
        ITargetAuthoringService authoring,
        StudioScope scope,
        IAuditLog audit)
    {
        _db = db;
        _languages = languages;
        _authoring = authoring;
        _scope = scope;
        _audit = audit;
    }

    // =====================================================================================
    // Frameworks
    // =====================================================================================

    public async Task<IReadOnlyList<FrameworkDto>> FrameworksAsync(CancellationToken cancellationToken = default)
    {
        var frameworks = await _db.CompetencyFrameworks.AsNoTracking()
            .Where(f => f.RetiredAtUtc == null)
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.FrameworkKey, f.Name, f.VersionLabel, f.PublishedAtUtc })
            .ToListAsync(cancellationToken);

        var ids = frameworks.Select(f => f.Id).ToList();

        // Counted whole, placeholders included. The bootstrap framework holds nothing else, and a
        // stand-in set reporting nought is the one number on this board that would be a lie.
        var counts = await _db.LearningTargets.AsNoTracking()
            .Where(t => ids.Contains(t.FrameworkId) && t.RetiredAtUtc == null)
            .GroupBy(t => t.FrameworkId)
            .Select(g => new
            {
                FrameworkId = g.Key,
                Skills = g.Count(),
                Reviewed = g.Count(t => t.ReviewState == TargetReviewState.Reviewed)
            })
            .ToListAsync(cancellationToken);

        var placeholders = await _db.LearningTargets.AsNoTracking()
            .Where(t => ids.Contains(t.FrameworkId) && t.IsPlaceholder)
            .Select(t => t.FrameworkId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return frameworks.Select(f =>
        {
            var count = counts.FirstOrDefault(c => c.FrameworkId == f.Id);
            return new FrameworkDto
            {
                Id = f.Id,
                FrameworkKey = f.FrameworkKey,
                Name = f.Name,
                VersionLabel = f.VersionLabel,
                PublishedAtUtc = f.PublishedAtUtc,
                Skills = count?.Skills ?? 0,
                Reviewed = count?.Reviewed ?? 0,
                IsPlaceholders = placeholders.Contains(f.Id)
            };
        }).ToList();
    }

    public async Task<ServiceResult<FrameworkDto>> CreateFrameworkAsync(
        StudioMember member, CreateFrameworkRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return Refused<FrameworkDto>("Only a Lead starts a framework.");

        var key = (request.FrameworkKey ?? string.Empty).Trim().ToLowerInvariant();
        var name = (request.Name ?? string.Empty).Trim();

        if (key.Length is < 3 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            return Invalid<FrameworkDto>("frameworkKey", "A framework's key is letters, digits, dots, dashes and underscores — 'eg.national' for instance.");
        if (name.Length is < 2 or > 200)
            return Invalid<FrameworkDto>("name", "Give the framework the name the curriculum is known by.");

        if (await _db.CompetencyFrameworks.AnyAsync(f => f.FrameworkKey == key, cancellationToken))
            return Invalid<FrameworkDto>("frameworkKey", "A framework already has that key.");

        var framework = new CompetencyFramework
        {
            Id = Guid.NewGuid(),
            FrameworkKey = key,
            Name = name,
            VersionLabel = (request.VersionLabel ?? string.Empty).Trim() is { Length: > 0 } v ? v : "1",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CompetencyFrameworks.Add(framework);

        _audit.Record(new AuditEntry(
            AuditActions.FrameworkCreated, AuditAreas.Workspace,
            $"Started a framework: {framework.Name}.",
            "framework", framework.Id.ToString(),
            new { key = framework.FrameworkKey, version = framework.VersionLabel }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<FrameworkDto>.Success(new FrameworkDto
        {
            Id = framework.Id,
            FrameworkKey = framework.FrameworkKey,
            Name = framework.Name,
            VersionLabel = framework.VersionLabel,
            PublishedAtUtc = null,
            Skills = 0,
            Reviewed = 0,
            IsPlaceholders = false
        });
    }

    // =====================================================================================
    // The template, and reading a filled one
    // =====================================================================================

    /// <summary>The columns before the per-language ones. Order is the sheet's contract.</summary>
    private static readonly string[] FixedColumns = ["Code", "Kind", "Parent code", "Band"];

    public async Task<byte[]> TemplateAsync(CancellationToken cancellationToken = default)
    {
        var languages = (await _languages.GetAsync(cancellationToken)).Where(l => l.IsContentLanguage).ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Outcomes");

        for (var i = 0; i < FixedColumns.Length; i++)
            sheet.Cell(1, i + 1).Value = FixedColumns[i];

        for (var i = 0; i < languages.Count; i++)
            sheet.Cell(1, FixedColumns.Length + i + 1).Value = $"Statement ({languages[i].Code})";

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        // Two worked rows, and the second is a child of the first on purpose: the parent code is
        // the one column people leave empty, and an example that uses it teaches what it is for.
        Fill(sheet, 2, "M4.NPV.1", TargetKinds.Concept, null, 2, languages,
            ar: "يقرأ ويكتب الأعداد حتى ١٠٠٠٠ بالأرقام والكلمات.",
            en: "Can read and write numbers up to 10,000 in figures and words.");

        Fill(sheet, 3, "M4.NPV.1.a", TargetKinds.Skill, "M4.NPV.1", 1, languages,
            ar: "يحدد قيمة المنزلة لأي رقم في عدد من أربعة أرقام.",
            en: "Can say the place value of any digit in a four-digit number.");

        var notes = workbook.AddWorksheet("How to fill this in");
        var lines = new[]
        {
            "One row per learning outcome. Nothing else on the sheet is read.",
            "",
            "Code — the outcome's own code in the official document. It has to be unique in this",
            "framework, and it is what a second import matches on, so do not renumber it later.",
            "",
            "Kind — one of: skill, concept, procedure. A skill is something a child can DO.",
            "",
            "Parent code — the code of the outcome this one is part of, if any. Leave it empty for a",
            "top-level outcome. The parent has to be somewhere on this sheet or already imported.",
            "",
            "Band — how hard it is nominally, 1 to 9, or leave it empty. This is the author's",
            "judgement for building exams; it is never how many children got it wrong.",
            "",
            "Statement — the outcome itself, phrased as a claim that can be true or false about one",
            "child: \"Can order fractions with unlike denominators.\" Fill in every language column;",
            "an outcome with no Arabic statement is an outcome half the children cannot be shown.",
            "",
            "Importing the same sheet twice is safe. Rows are matched on Code: new ones are added,",
            "changed ones are updated, and any outcome somebody has since edited in the Studio is",
            "left exactly as it is and reported back to you."
        };

        for (var i = 0; i < lines.Length; i++)
            notes.Cell(i + 1, 1).Value = lines[i];

        notes.Column(1).Width = 95;
        sheet.Columns().AdjustToContents();

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();

        static void Fill(
            IXLWorksheet sheet, int row, string code, string kind, string? parent, int band,
            IReadOnlyList<ContentLanguage> languages, string ar, string en)
        {
            sheet.Cell(row, 1).Value = code;
            sheet.Cell(row, 2).Value = kind;
            sheet.Cell(row, 3).Value = parent ?? string.Empty;
            sheet.Cell(row, 4).Value = band;

            for (var i = 0; i < languages.Count; i++)
                sheet.Cell(row, FixedColumns.Length + i + 1).Value =
                    languages[i].Code.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? ar : en;
        }
    }

    public async Task<ServiceResult<SkillImportReport>> ImportAsync(
        StudioMember member, Guid frameworkId, Stream sheet, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Author))
            return Refused<SkillImportReport>("Only a content-team member imports outcomes.");

        var framework = await _db.CompetencyFrameworks.FirstOrDefaultAsync(f => f.Id == frameworkId, cancellationToken);
        if (framework is null) return Missing<SkillImportReport>("There is no framework with that id.");

        var languages = (await _languages.GetAsync(cancellationToken)).Where(l => l.IsContentLanguage).ToList();
        var problems = new List<SkillImportProblem>();

        List<SheetRow> rows;
        try
        {
            rows = Read(sheet, languages, problems);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Invalid<SkillImportReport>("file", "That file could not be read as a sheet.");
        }

        if (rows.Count == 0 && problems.Count == 0)
            problems.Add(new SkillImportProblem(0, "empty", "The sheet has no rows under the headings."));

        // A code appearing twice is the one mistake that silently halves an import: the second row
        // would overwrite the first and the count would still look right.
        foreach (var group in rows.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add(new SkillImportProblem(
                group.Last().Row, "duplicateCode", $"The code '{group.Key}' is on more than one row."));

        var existing = await _db.LearningTargets
            .Where(t => t.FrameworkId == frameworkId)
            .ToListAsync(cancellationToken);

        var byKey = existing.ToDictionary(t => t.TargetKey, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(r => r.ParentCode is { Length: > 0 }))
        {
            var onSheet = rows.Any(r => string.Equals(r.Code, row.ParentCode, StringComparison.OrdinalIgnoreCase));
            if (!onSheet && !byKey.ContainsKey(row.ParentCode!))
                problems.Add(new SkillImportProblem(
                    row.Row, "parentMissing", $"'{row.ParentCode}' is not on this sheet and is not in the framework."));
        }

        // An outcome cannot be part of itself, and a chain of them cannot close into a ring. Both
        // read as ordinary typing mistakes on a sheet and both would be written as edges nobody
        // could roll a mastery figure up through — the loop would never end.
        foreach (var row in CircularRows(rows))
            problems.Add(new SkillImportProblem(
                row.Row,
                string.Equals(row.Code, row.ParentCode, StringComparison.OrdinalIgnoreCase) ? "parentIsSelf" : "parentLoops",
                string.Equals(row.Code, row.ParentCode, StringComparison.OrdinalIgnoreCase)
                    ? $"'{row.Code}' is listed as part of itself."
                    : $"'{row.Code}' is part of '{row.ParentCode}', which is part of '{row.Code}' again."));

        // Nothing is written unless every row is good. A vocabulary half imported is a vocabulary
        // nobody can trust, and the team would have no way of telling which half.
        if (problems.Count > 0 || dryRun)
        {
            return ServiceResult<SkillImportReport>.Success(new SkillImportReport
            {
                RowsRead = rows.Count,
                // All three or none. A sheet with a problem in it writes nothing at all, so
                // reporting rows it "would leave alone" beside two zeroes reads as a partial
                // import that was never going to happen.
                Added = problems.Count > 0 ? 0 : rows.Count(r => !byKey.ContainsKey(r.Code)),
                Updated = problems.Count > 0 ? 0 : rows.Count(r => byKey.TryGetValue(r.Code, out var t) && t.EditedAtUtc is null),
                LeftAlone = problems.Count > 0 ? 0 : rows.Count(r => byKey.TryGetValue(r.Code, out var t) && t.EditedAtUtc is not null),
                Problems = problems,
                WroteNothing = true
            });
        }

        var now = DateTime.UtcNow;
        var added = 0;
        var updated = 0;
        var leftAlone = 0;

        foreach (var row in rows)
        {
            if (byKey.TryGetValue(row.Code, out var target))
            {
                // Somebody has reworded this here. The official document changing does not give an
                // import the right to undo a specialist's correction without anybody being told.
                if (target.EditedAtUtc is not null)
                {
                    leftAlone++;
                    continue;
                }

                target.TargetKindKey = row.Kind;
                target.DifficultyBand = row.Band;
                await WriteStatementsAsync(target.Id, row.Statements, cancellationToken);
                updated++;
            }
            else
            {
                target = new LearningTarget
                {
                    Id = Guid.NewGuid(),
                    FrameworkId = frameworkId,
                    TargetKey = row.Code,
                    TargetKindKey = row.Kind,
                    IsPlaceholder = false,
                    ReviewState = TargetReviewState.Unreviewed,
                    DifficultyBand = row.Band,
                    CreatedAtUtc = now
                };

                _db.LearningTargets.Add(target);
                byKey[row.Code] = target;

                foreach (var (langId, statement) in row.Statements)
                    _db.LearningTargetTranslations.Add(new LearningTargetTranslation
                    {
                        TargetId = target.Id,
                        LangId = langId,
                        Statement = statement
                    });

                added++;
            }
        }

        // Parents second: a row's parent may have been minted by this same import, a few rows down.
        var edges = await _db.LearningTargetEdges
            .Where(e => e.EdgeKind == LearningTargetEdgeKind.ComponentOf)
            .ToListAsync(cancellationToken);

        foreach (var row in rows.Where(r => r.ParentCode is { Length: > 0 }))
        {
            if (!byKey.TryGetValue(row.Code, out var child) || !byKey.TryGetValue(row.ParentCode!, out var parent))
                continue;

            if (edges.Any(e => e.FromTargetId == child.Id && e.ToTargetId == parent.Id))
                continue;

            _db.LearningTargetEdges.Add(new LearningTargetEdge
            {
                Id = Guid.NewGuid(),
                FromTargetId = child.Id,
                ToTargetId = parent.Id,
                EdgeKind = LearningTargetEdgeKind.ComponentOf,
                Weight = 1.0m,
                CreatedAtUtc = now
            });
        }

        _audit.Record(new AuditEntry(
            AuditActions.SkillsImported, AuditAreas.Workspace,
            $"Imported {rows.Count} outcomes into {framework.Name}.",
            "framework", framework.Id.ToString(),
            new { rows = rows.Count, added, updated, leftAlone }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<SkillImportReport>.Success(new SkillImportReport
        {
            RowsRead = rows.Count,
            Added = added,
            Updated = updated,
            LeftAlone = leftAlone,
            Problems = [],
            WroteNothing = false
        });
    }

    /// <summary>
    /// Rows whose parent chain, followed within this sheet, comes back to the row itself. A row
    /// pointing at a parent the sheet does not carry simply stops — that is the framework's own
    /// tree, which cannot contain a loop because this check is what keeps loops out of it.
    /// </summary>
    private static IEnumerable<SheetRow> CircularRows(IReadOnlyList<SheetRow> rows)
    {
        var byCode = new Dictionary<string, SheetRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            byCode.TryAdd(row.Code, row);

        foreach (var row in rows.Where(r => r.ParentCode is { Length: > 0 }))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Code };
            var at = row.ParentCode;

            while (at is { Length: > 0 })
            {
                if (!seen.Add(at))
                {
                    yield return row;
                    break;
                }

                at = byCode.TryGetValue(at, out var next) ? next.ParentCode : null;
            }
        }
    }

    private sealed record SheetRow(
        int Row, string Code, string Kind, string? ParentCode, int? Band, IReadOnlyDictionary<Guid, string> Statements);

    private static List<SheetRow> Read(
        Stream sheet, IReadOnlyList<ContentLanguage> languages, List<SkillImportProblem> problems)
    {
        using var workbook = new XLWorkbook(sheet);
        var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Outcomes", StringComparison.OrdinalIgnoreCase))
                        ?? workbook.Worksheets.First();

        // The language columns are found by their heading rather than by position, so a team that
        // deletes a column they do not use still gets an import that does what they meant.
        var headings = new Dictionary<int, Guid>();
        var used = worksheet.RangeUsed();
        if (used is null) return [];

        for (var column = 1; column <= used.ColumnCount(); column++)
        {
            var heading = worksheet.Cell(1, column).GetString().Trim();
            var language = languages.FirstOrDefault(l =>
                heading.Contains($"({l.Code})", StringComparison.OrdinalIgnoreCase)
                || heading.Equals(l.Code, StringComparison.OrdinalIgnoreCase));

            if (language is not null) headings[column] = language.Id;
        }

        var rows = new List<SheetRow>();

        for (var number = 2; number <= used.RowCount(); number++)
        {
            var code = worksheet.Cell(number, 1).GetString().Trim();
            if (code.Length == 0) continue;

            var kind = worksheet.Cell(number, 2).GetString().Trim().ToLowerInvariant();
            if (kind.Length == 0) kind = TargetKinds.Skill;

            if (kind is not (TargetKinds.Skill or TargetKinds.Concept or TargetKinds.Procedure))
            {
                problems.Add(new SkillImportProblem(number, "kind", $"'{kind}' is not a kind. Use skill, concept or procedure."));
                continue;
            }

            var parent = worksheet.Cell(number, 3).GetString().Trim();
            var bandText = worksheet.Cell(number, 4).GetString().Trim();
            int? band = null;

            if (bandText.Length > 0)
            {
                if (!int.TryParse(bandText, out var parsed) || parsed is < 1 or > 9)
                {
                    problems.Add(new SkillImportProblem(number, "band", "A band is a whole number from 1 to 9, or empty."));
                    continue;
                }

                band = parsed;
            }

            var statements = new Dictionary<Guid, string>();
            foreach (var (column, langId) in headings)
            {
                var statement = worksheet.Cell(number, column).GetString().Trim();
                if (statement.Length > 0) statements[langId] = statement;
            }

            var missing = languages.Where(l => l.RequiredToPublish && !statements.ContainsKey(l.Id)).ToList();
            if (missing.Count > 0)
            {
                problems.Add(new SkillImportProblem(
                    number, "statement",
                    $"'{code}' has no statement in {string.Join(", ", missing.Select(l => l.Name))}."));
                continue;
            }

            if (statements.Count == 0)
            {
                problems.Add(new SkillImportProblem(number, "statement", $"'{code}' has no statement in any language."));
                continue;
            }

            rows.Add(new SheetRow(number, code, kind, parent.Length == 0 ? null : parent, band, statements));
        }

        return rows;
    }

    private async Task WriteStatementsAsync(
        Guid targetId, IReadOnlyDictionary<Guid, string> statements, CancellationToken cancellationToken)
    {
        var current = await _db.LearningTargetTranslations
            .Where(t => t.TargetId == targetId)
            .ToListAsync(cancellationToken);

        foreach (var (langId, statement) in statements)
        {
            var row = current.FirstOrDefault(t => t.LangId == langId);
            if (row is null)
                _db.LearningTargetTranslations.Add(new LearningTargetTranslation
                {
                    TargetId = targetId,
                    LangId = langId,
                    Statement = statement
                });
            else
                row.Statement = statement;
        }
    }

    // =====================================================================================
    // Reading and editing one skill
    // =====================================================================================

    public async Task<IReadOnlyList<SkillDto>> SkillsAsync(
        Guid frameworkId, Guid langId, string? search = null, string? reviewState = null,
        int take = 100, int skip = 0, CancellationToken cancellationToken = default)
    {
        var query = _db.LearningTargets.AsNoTracking()
            .Where(t => t.FrameworkId == frameworkId && t.RetiredAtUtc == null && !t.IsPlaceholder);

        if (Enum.TryParse<TargetReviewState>(reviewState, true, out var state))
            query = query.Where(t => t.ReviewState == state);

        if (search is { Length: > 1 })
        {
            var words = search.Trim();
            query = query.Where(t =>
                t.TargetKey.Contains(words)
                || t.Translations.Any(x => x.Statement.Contains(words)));
        }

        var targets = await query
            .OrderBy(t => t.TargetKey)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 500))
            .Select(t => new
            {
                t.Id,
                t.FrameworkId,
                t.TargetKey,
                t.TargetKindKey,
                t.ReviewState,
                t.DifficultyBand,
                t.EditedAtUtc,
                Statements = t.Translations.Select(x => new { x.LangId, x.Statement }).ToList()
            })
            .ToListAsync(cancellationToken);

        return await DecorateAsync(targets.Select(t => (
            t.Id, t.FrameworkId, t.TargetKey, t.TargetKindKey, t.ReviewState, t.DifficultyBand, t.EditedAtUtc,
            (IReadOnlyDictionary<Guid, string>)t.Statements.ToDictionary(x => x.LangId, x => x.Statement))).ToList(),
            cancellationToken);
    }

    public async Task<ServiceResult<SkillDto>> SkillAsync(Guid targetId, Guid langId, CancellationToken cancellationToken = default)
    {
        var one = await _db.LearningTargets.AsNoTracking()
            .Where(t => t.Id == targetId)
            .Select(t => new
            {
                t.Id,
                t.FrameworkId,
                t.TargetKey,
                t.TargetKindKey,
                t.ReviewState,
                t.DifficultyBand,
                t.EditedAtUtc,
                Statements = t.Translations.Select(x => new { x.LangId, x.Statement }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (one is null) return Missing<SkillDto>("There is no skill with that id.");

        var decorated = await DecorateAsync(
            [(one.Id, one.FrameworkId, one.TargetKey, one.TargetKindKey, one.ReviewState, one.DifficultyBand, one.EditedAtUtc,
              (IReadOnlyDictionary<Guid, string>)one.Statements.ToDictionary(x => x.LangId, x => x.Statement))],
            cancellationToken);

        return ServiceResult<SkillDto>.Success(decorated[0]);
    }

    private async Task<IReadOnlyList<SkillDto>> DecorateAsync(
        IReadOnlyList<(Guid Id, Guid FrameworkId, string Key, string Kind, TargetReviewState State, int? Band, DateTime? Edited,
            IReadOnlyDictionary<Guid, string> Statements)> targets,
        CancellationToken cancellationToken)
    {
        var ids = targets.Select(t => t.Id).ToList();

        var questions = await _db.ItemTargetMappings.AsNoTracking()
            .Where(m => ids.Contains(m.TargetId))
            .GroupBy(m => m.TargetId)
            .Select(g => new { TargetId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var replaced = await _db.LearningTargetEdges.AsNoTracking()
            .Where(e => e.EdgeKind == LearningTargetEdgeKind.SupersededBy && ids.Contains(e.ToTargetId))
            .GroupBy(e => e.ToTargetId)
            .Select(g => new { TargetId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return targets.Select(t => new SkillDto
        {
            Id = t.Id,
            FrameworkId = t.FrameworkId,
            TargetKey = t.Key,
            Statements = t.Statements,
            TargetKindKey = t.Kind,
            ReviewState = t.State,
            DifficultyBand = t.Band,
            Questions = questions.FirstOrDefault(q => q.TargetId == t.Id)?.Count ?? 0,
            Replaced = replaced.FirstOrDefault(r => r.TargetId == t.Id)?.Count ?? 0,
            IsEdited = t.Edited is not null
        }).ToList();
    }

    public async Task<ServiceResult<SkillDto>> EditSkillAsync(
        StudioMember member, Guid targetId, EditSkillRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Author))
            return Refused<SkillDto>("Only a content-team member edits a skill.");

        var target = await _db.LearningTargets.FirstOrDefaultAsync(t => t.Id == targetId, cancellationToken);
        if (target is null) return Missing<SkillDto>("There is no skill with that id.");
        if (target.IsPlaceholder) return Invalid<SkillDto>("placeholder", "That is a lesson standing in for a skill. Promote it instead of editing it.");

        if (request.Statements is { Count: > 0 } statements)
        {
            var outside = member.LanguagesOutside(statements.Keys);
            if (outside.Count > 0)
                return DraftService.OutOfScope<SkillDto>("languages", outside);

            foreach (var (_, statement) in statements)
                if (statement.Trim().Length is < 3 or > 500)
                    return Invalid<SkillDto>("statement", "A claim is between 3 and 500 characters.");

            await WriteStatementsAsync(targetId, statements.ToDictionary(s => s.Key, s => s.Value.Trim()), cancellationToken);
        }

        if (request.TargetKindKey is { Length: > 0 } kind)
        {
            if (kind is not (TargetKinds.Skill or TargetKinds.Concept or TargetKinds.Procedure))
                return Invalid<SkillDto>("targetKindKey", "Use skill, concept or procedure.");
            target.TargetKindKey = kind;
        }

        if (request.DifficultyBand is { } band)
        {
            if (band is < 1 or > 9) return Invalid<SkillDto>("difficultyBand", "A band is a whole number from 1 to 9.");
            target.DifficultyBand = band;
        }

        // The stamp is the whole point: it is what a later import reads to know to leave this alone.
        target.EditedAtUtc = DateTime.UtcNow;
        target.EditedByUserId = member.UserId;

        _audit.Record(new AuditEntry(
            AuditActions.SkillEdited, AuditAreas.Workspace,
            $"Edited a skill: {target.TargetKey}.",
            "skill", target.Id.ToString(),
            new { targetKey = target.TargetKey, kind = target.TargetKindKey, band = target.DifficultyBand }));

        await _db.SaveChangesAsync(cancellationToken);
        return await SkillAsync(targetId, Guid.Empty, cancellationToken);
    }

    public async Task<ServiceResult<SkillDto>> SetReviewStateAsync(
        StudioMember member, Guid targetId, TargetReviewState state, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Reviewer))
            return Refused<SkillDto>("Only a reviewer or a Lead confirms a skill.");

        var target = await _db.LearningTargets.FirstOrDefaultAsync(t => t.Id == targetId, cancellationToken);
        if (target is null) return Missing<SkillDto>("There is no skill with that id.");

        // A claim nobody has written cannot be confirmed. The check is here rather than on the
        // screen because the screen is not the only thing that can call this.
        var statements = await _db.LearningTargetTranslations.CountAsync(t => t.TargetId == targetId, cancellationToken);
        if (state == TargetReviewState.Reviewed && statements == 0)
            return Invalid<SkillDto>("statement", "It has no statement yet, so there is nothing to confirm.");

        target.ReviewState = state;

        _audit.Record(new AuditEntry(
            AuditActions.SkillReviewed, AuditAreas.Workspace,
            state switch
            {
                TargetReviewState.Reviewed => $"Confirmed a skill: {target.TargetKey}.",
                TargetReviewState.Deprecated => $"Stopped using a skill: {target.TargetKey}.",
                _ => $"Sent a skill back to be looked at again: {target.TargetKey}."
            },
            "skill", target.Id.ToString(),
            new { targetKey = target.TargetKey, state = state.ToString() }));

        await _db.SaveChangesAsync(cancellationToken);
        return await SkillAsync(targetId, Guid.Empty, cancellationToken);
    }

    // =====================================================================================
    // What a question measures
    // =====================================================================================

    public async Task<ServiceResult<ItemSkillsDto>> ItemSkillsAsync(
        Guid itemId, Guid langId, CancellationToken cancellationToken = default)
    {
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null) return Missing<ItemSkillsDto>("There is no question with that id.");

        // No scope check here on purpose: scope limits what a member may change, never what they
        // may see. Everyone in the Studio reads all of the curriculum.
        return ServiceResult<ItemSkillsDto>.Success(await ReadItemAsync(itemId, langId, cancellationToken));
    }

    private async Task<ItemSkillsDto> ReadItemAsync(Guid itemId, Guid langId, CancellationToken cancellationToken)
    {
        // A Question row is one language's rendering of one frozen item version, so the question's
        // words are reached through the version rather than held on the item.
        var text = await _db.Questions.AsNoTracking()
            .Where(q => q.ItemVersion!.ItemId == itemId && (langId == Guid.Empty || q.LangId == langId))
            .OrderByDescending(q => q.ItemVersion!.VersionNumber)
            .Select(q => q.Text)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var mapped = await _db.ItemTargetMappings.AsNoTracking()
            .Where(m => m.ItemId == itemId)
            .Select(m => new
            {
                m.TargetId,
                m.Emphasis,
                m.IsPrimary,
                IsPlaceholder = m.Target!.IsPlaceholder,
                Statement = m.Target!.Translations
                    .Where(t => langId == Guid.Empty || t.LangId == langId)
                    .Select(t => t.Statement)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new ItemSkillsDto
        {
            ItemId = itemId,
            Text = text,
            Mapped = mapped
                .OrderByDescending(m => m.IsPrimary)
                .Select(m => new ItemSkillDto(m.TargetId, m.Statement ?? string.Empty, m.Emphasis, m.IsPrimary, m.IsPlaceholder))
                .ToList()
        };
    }

    public async Task<ServiceResult<ItemSkillsDto>> MapItemAsync(
        StudioMember member, Guid itemId, MapItemRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Author))
            return Refused<ItemSkillsDto>("Only a content-team member maps a question.");

        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null) return Missing<ItemSkillsDto>("There is no question with that id.");

        // What a question measures is read back as proficiency for every child who has ever
        // answered it, so saying it is a change to the lesson the question sits in — and a member
        // changes the lessons they are responsible for. Drafts have been held to this from the
        // start; this act happens immediately, and was held to nothing but the role.
        if (!await _scope.CoversItemAsync(member, itemId, cancellationToken))
            return Refused<ItemSkillsDto>("This question is outside the part of the curriculum you work in.");

        var lines = request.Skills ?? [];

        if (lines.Count == 0)
            return Invalid<ItemSkillsDto>("skills", "A question has to measure at least one thing, or it cannot be measured at all.");
        if (lines.Select(l => l.TargetId).Distinct().Count() != lines.Count)
            return Invalid<ItemSkillsDto>("skills", "The same skill is on the list twice.");
        if (lines.Count(l => l.IsPrimary) != 1)
            return Invalid<ItemSkillsDto>("isPrimary", "Exactly one of them is what the question is mainly about.");
        if (lines.Any(l => l.Emphasis is <= 0 or > 1))
            return Invalid<ItemSkillsDto>("emphasis", "How much of the question is about a skill is more than 0 and at most 1.");

        var targetIds = lines.Select(l => l.TargetId).ToList();
        var found = await _db.LearningTargets.AsNoTracking()
            .Where(t => targetIds.Contains(t.Id) && t.RetiredAtUtc == null)
            .Select(t => new { t.Id, t.ReviewState, t.TargetKey })
            .ToListAsync(cancellationToken);

        if (found.Count != targetIds.Count)
            return Invalid<ItemSkillsDto>("skills", "One of those skills no longer exists.");

        if (found.Any(t => t.ReviewState == TargetReviewState.Deprecated))
            return Invalid<ItemSkillsDto>("skills", "One of those skills has been stopped. Map the question to the one that replaced it.");

        var current = await _db.ItemTargetMappings.Where(m => m.ItemId == itemId).ToListAsync(cancellationToken);
        _db.ItemTargetMappings.RemoveRange(current);

        var now = DateTime.UtcNow;
        foreach (var line in lines)
            _db.ItemTargetMappings.Add(new ItemTargetMapping
            {
                Id = Guid.NewGuid(),
                ItemId = itemId,
                TargetId = line.TargetId,
                Emphasis = line.Emphasis,
                IsPrimary = line.IsPrimary,
                CreatedAtUtc = now
            });

        _audit.Record(new AuditEntry(
            AuditActions.QuestionMapped, AuditAreas.Workspace,
            $"Said what a question measures: {found.First(t => t.Id == lines.First(l => l.IsPrimary).TargetId).TargetKey}.",
            "item", itemId.ToString(),
            new { skills = lines.Count, primary = lines.First(l => l.IsPrimary).TargetId, was = current.Count }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<ItemSkillsDto>.Success(await ReadItemAsync(itemId, Guid.Empty, cancellationToken));
    }

    // =====================================================================================
    // How far one subject has got — the Phase 5 gate as a number
    // =====================================================================================

    public async Task<ServiceResult<SubjectMappingDto>> SubjectProgressAsync(
        Guid subjectNodeId, Guid langId, CancellationToken cancellationToken = default)
    {
        var subject = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == subjectNodeId)
            .Select(n => new { n.Id, n.Path, n.KindKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (subject is null) return Missing<SubjectMappingDto>("There is no subject with that id.");

        var title = await _db.CurriculumNodeTranslations.AsNoTracking()
            .Where(t => t.NodeId == subjectNodeId && (langId == Guid.Empty || t.LangId == langId))
            .Select(t => t.Title)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var lessons = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Path.StartsWith(subject.Path + "/") && n.IsPlayable && n.RetiredAtUtc == null)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        if (lessons.Count == 0)
            return ServiceResult<SubjectMappingDto>.Success(new SubjectMappingDto
            {
                SubjectNodeId = subjectNodeId,
                SubjectTitle = title,
                Lessons = 0,
                Questions = 0,
                MappedToSkills = 0,
                OnPlaceholders = 0,
                Unmapped = 0
            });

        // Live items only. A question in somebody's draft is not yet something a child can be
        // measured on, and counting it would make the gate look closer than it is.
        var itemIds = await _db.NodeItemMappings.AsNoTracking()
            .Where(m => lessons.Contains(m.NodeId) && m.RemovedAtUtc == null)
            .Select(m => m.ItemId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var primaries = await _db.ItemTargetMappings.AsNoTracking()
            .Where(m => itemIds.Contains(m.ItemId) && m.IsPrimary)
            .Select(m => new { m.ItemId, IsPlaceholder = m.Target!.IsPlaceholder })
            .ToListAsync(cancellationToken);

        var onSkills = primaries.Count(p => !p.IsPlaceholder);
        var onPlaceholders = primaries.Count(p => p.IsPlaceholder);

        return ServiceResult<SubjectMappingDto>.Success(new SubjectMappingDto
        {
            SubjectNodeId = subjectNodeId,
            SubjectTitle = title,
            Lessons = lessons.Count,
            Questions = itemIds.Count,
            MappedToSkills = onSkills,
            OnPlaceholders = onPlaceholders,
            Unmapped = itemIds.Count - primaries.Select(p => p.ItemId).Distinct().Count()
        });
    }

    // =====================================================================================
    // Mapping a subject, question by question
    // =====================================================================================

    public async Task<ServiceResult<IReadOnlyList<QuestionToMapDto>>> QuestionsToMapAsync(
        Guid subjectNodeId, Guid langId, string? state = null, int take = 50, int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var subject = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == subjectNodeId)
            .Select(n => new { n.Id, n.Path })
            .FirstOrDefaultAsync(cancellationToken);

        if (subject is null)
            return Missing<IReadOnlyList<QuestionToMapDto>>("There is no subject with that id.");

        var lessons = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Path.StartsWith(subject.Path + "/") && n.IsPlayable && n.RetiredAtUtc == null)
            .Select(n => new { n.Id, n.Order })
            .ToListAsync(cancellationToken);

        var lessonIds = lessons.Select(l => l.Id).ToList();
        if (lessonIds.Count == 0)
            return ServiceResult<IReadOnlyList<QuestionToMapDto>>.Success([]);

        var titles = await _db.CurriculumNodeTranslations.AsNoTracking()
            .Where(t => lessonIds.Contains(t.NodeId))
            .Select(t => new { t.NodeId, t.LangId, t.Title })
            .ToListAsync(cancellationToken);

        var placed = await _db.NodeItemMappings.AsNoTracking()
            .Where(m => lessonIds.Contains(m.NodeId) && m.RemovedAtUtc == null)
            .Select(m => new { m.ItemId, m.NodeId, m.Role, m.Order })
            .ToListAsync(cancellationToken);

        var itemIds = placed.Select(p => p.ItemId).Distinct().ToList();

        var mappings = await _db.ItemTargetMappings.AsNoTracking()
            .Where(m => itemIds.Contains(m.ItemId))
            .Select(m => new
            {
                m.ItemId,
                m.TargetId,
                m.IsPrimary,
                IsPlaceholder = m.Target!.IsPlaceholder,
                Statement = m.Target!.Translations
                    .Where(t => langId == Guid.Empty || t.LangId == langId)
                    .Select(t => t.Statement)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var words = await _db.Questions.AsNoTracking()
            .Where(q => itemIds.Contains(q.ItemVersion!.ItemId) && (langId == Guid.Empty || q.LangId == langId))
            .Select(q => new { ItemId = q.ItemVersion!.ItemId, q.ItemVersion!.VersionNumber, q.Text })
            .ToListAsync(cancellationToken);

        var rows = placed.Select(one =>
        {
            var mine = mappings.Where(m => m.ItemId == one.ItemId).ToList();
            var primary = mine.FirstOrDefault(m => m.IsPrimary) ?? mine.FirstOrDefault();

            return new QuestionToMapDto
            {
                ItemId = one.ItemId,
                Text = words.Where(w => w.ItemId == one.ItemId)
                           .OrderByDescending(w => w.VersionNumber)
                           .Select(w => w.Text)
                           .FirstOrDefault() ?? string.Empty,
                LessonId = one.NodeId,
                LessonTitle = titles.FirstOrDefault(t => t.NodeId == one.NodeId && (langId == Guid.Empty || t.LangId == langId))?.Title
                              ?? titles.FirstOrDefault(t => t.NodeId == one.NodeId)?.Title
                              ?? string.Empty,
                Role = one.Role.ToString(),
                Order = one.Order,
                PrimaryTargetId = primary?.TargetId,
                PrimaryStatement = primary?.Statement,
                OnStandIn = primary?.IsPlaceholder ?? false,
                AlsoTouches = Math.Max(0, mine.Count - 1)
            };
        });

        // Worst first: mapped to nothing, then still on a stand-in, then done. A list that opens
        // on the work left is a list somebody can finish; alphabetical order is a list nobody starts.
        rows = state switch
        {
            "unmapped" => rows.Where(r => r.PrimaryTargetId is null),
            "standIn" => rows.Where(r => r.OnStandIn),
            "mapped" => rows.Where(r => r.PrimaryTargetId is not null && !r.OnStandIn),
            _ => rows
        };

        var ordered = rows
            .OrderBy(r => r.PrimaryTargetId is null ? 0 : r.OnStandIn ? 1 : 2)
            .ThenBy(r => r.LessonTitle)
            .ThenBy(r => r.Order)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 200))
            .ToList();

        return ServiceResult<IReadOnlyList<QuestionToMapDto>>.Success(ordered);
    }

    // =====================================================================================
    // Stand-ins, and replacing them
    // =====================================================================================

    public async Task<ServiceResult<IReadOnlyList<StandInDto>>> StandInsAsync(
        Guid nodeId, Guid langId, CancellationToken cancellationToken = default)
    {
        var node = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new { n.Id, n.Path, n.IsPlayable })
            .FirstOrDefaultAsync(cancellationToken);

        if (node is null) return Missing<IReadOnlyList<StandInDto>>("There is no place with that id.");

        // A stand-in was minted per lesson, so the ones under a subject are the ones its lessons
        // are mapped to. Reached through the node mappings rather than by name.
        var lessons = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => (n.Id == nodeId || n.Path.StartsWith(node.Path + "/")) && n.IsPlayable && n.RetiredAtUtc == null)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        if (lessons.Count == 0) return ServiceResult<IReadOnlyList<StandInDto>>.Success([]);

        var rows = await _db.NodeTargetMappings.AsNoTracking()
            .Where(m => lessons.Contains(m.NodeId) && m.Target!.IsPlaceholder)
            .Select(m => new
            {
                m.TargetId,
                m.NodeId,
                Statement = m.Target!.Translations
                    .Where(t => langId == Guid.Empty || t.LangId == langId)
                    .Select(t => t.Statement)
                    .FirstOrDefault(),
                Questions = _db.ItemTargetMappings.Count(i => i.TargetId == m.TargetId),
                Answers = _db.Observations.Count(o => o.TargetId == m.TargetId),
                IsReplaced = _db.LearningTargetEdges.Any(
                    e => e.FromTargetId == m.TargetId && e.EdgeKind == LearningTargetEdgeKind.SupersededBy)
            })
            .ToListAsync(cancellationToken);

        var titles = await _db.CurriculumNodeTranslations.AsNoTracking()
            .Where(t => lessons.Contains(t.NodeId))
            .Select(t => new { t.NodeId, t.LangId, t.Title })
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<StandInDto>>.Success(rows
            .GroupBy(r => r.TargetId)
            .Select(g => g.First())
            .OrderByDescending(r => r.Answers)
            .ThenByDescending(r => r.Questions)
            .Select(r => new StandInDto
            {
                TargetId = r.TargetId,
                Statement = r.Statement ?? string.Empty,
                NodeId = r.NodeId,
                NodeTitle = titles.FirstOrDefault(t => t.NodeId == r.NodeId && (langId == Guid.Empty || t.LangId == langId))?.Title
                            ?? titles.FirstOrDefault(t => t.NodeId == r.NodeId)?.Title,
                Questions = r.Questions,
                Answers = r.Answers,
                IsReplaced = r.IsReplaced
            })
            .ToList());
    }

    public async Task<ServiceResult<PromotionReportDto>> PromoteAsync(
        StudioMember member, PromoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Author))
            return Refused<PromotionReportDto>("Only a content-team member replaces a stand-in.");

        if (request.MarkReviewed && !member.IsAtLeast(StudioRole.Reviewer))
            return Refused<PromotionReportDto>("Only a reviewer or a Lead confirms a skill.");

        if (request.ReplacesTargetIds is not { Count: > 0 })
            return Invalid<PromotionReportDto>("replacesTargetIds", "Name at least one stand-in to replace.");

        // Replacing a stand-in moves every question mapped to it and re-reads every answer ever
        // given to those questions. Those questions are in lessons, and the lessons have owners.
        if (!await _scope.CoversTargetsAsync(member, request.ReplacesTargetIds, cancellationToken))
            return Refused<PromotionReportDto>(
                "One of those stand-ins belongs to a part of the curriculum you do not work in.");

        var minting = request.UseExistingTargetId is null;

        if (minting)
        {
            if (request.TargetKey.Trim().Length is < 2 or > 100)
                return Invalid<PromotionReportDto>("targetKey", "Give the skill a short code from the official document.");

            var required = (await _languages.GetAsync(cancellationToken))
                .Where(l => l.IsContentLanguage && l.RequiredToPublish)
                .ToList();

            var missing = required.Where(l => !request.Statements.ContainsKey(l.Id)
                                              || request.Statements[l.Id].Trim().Length < 3).ToList();

            // A claim with no Arabic is a claim half the children cannot be shown.
            if (missing.Count > 0)
                return Invalid<PromotionReportDto>(
                    "statements", $"Write the claim in {string.Join(" and ", missing.Select(l => l.Name))} as well.");

            var outside = member.LanguagesOutside(request.Statements.Keys);
            if (outside.Count > 0) return DraftService.OutOfScope<PromotionReportDto>("languages", outside);
        }

        TargetPromotionReport report;
        try
        {
            report = await _authoring.PromoteAsync(new PromoteTargetRequest
            {
                TargetKey = request.TargetKey.Trim(),
                Statements = request.Statements.ToDictionary(s => s.Key, s => s.Value.Trim()),
                TargetKindKey = request.TargetKindKey,
                DifficultyBand = request.DifficultyBand,
                ReplacesTargetIds = request.ReplacesTargetIds,
                MarkReviewed = request.MarkReviewed,
                IntoFrameworkId = null,
                UseExistingTargetId = request.UseExistingTargetId
            }, cancellationToken);
        }
        catch (InvalidOperationException refusal)
        {
            return Invalid<PromotionReportDto>("replacesTargetIds", refusal.Message);
        }

        _audit.Record(new AuditEntry(
            AuditActions.SkillPromoted, AuditAreas.Workspace,
            $"Replaced {report.PlaceholdersSuperseded} stand-ins with a real skill: {report.TargetKey}.",
            "skill", report.TargetId.ToString(),
            new
            {
                targetKey = report.TargetKey,
                standIns = report.PlaceholdersSuperseded,
                questions = report.ItemMappingsMoved,
                answersRebuilt = report.ObservationsRebuilt,
                minted = minting
            }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<PromotionReportDto>.Success(new PromotionReportDto
        {
            TargetId = report.TargetId,
            TargetKey = report.TargetKey,
            StandInsReplaced = report.PlaceholdersSuperseded,
            QuestionsMoved = report.ItemMappingsMoved,
            LessonsMoved = report.NodeMappingsMoved,
            AnswersRebuilt = report.ObservationsRebuilt,
            ExclusionsKept = report.ExclusionsPreserved
        });
    }

    // =====================================================================================
    // Refusals, in the Studio's own words
    // =====================================================================================

    private static ServiceResult<T> Refused<T>(string message) =>
        ServiceResult<T>.Failure(WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, message);

    private static ServiceResult<T> Missing<T>(string message) =>
        ServiceResult<T>.Failure(WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, message);

    private static ServiceResult<T> Invalid<T>(string field, string message) =>
        ServiceResult<T>.Failure(WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation, message,
            new Dictionary<string, object?> { ["field"] = field });
}
