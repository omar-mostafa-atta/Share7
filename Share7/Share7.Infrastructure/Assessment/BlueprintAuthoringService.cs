using Microsoft.EntityFrameworkCore;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Domain.Assessment;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Assessment;

/// <inheritdoc cref="IBlueprintAuthoringService"/>
public class BlueprintAuthoringService : IBlueprintAuthoringService
{
    private readonly ApplicationDbContext _dbContext;

    public BlueprintAuthoringService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // --------------------------------------------------------------------------- reading

    public async Task<IReadOnlyList<BlueprintDto>> ListBlueprintsAsync(
        Guid langId, CancellationToken cancellationToken = default)
    {
        var ids = await _dbContext.AssessmentBlueprints
            .AsNoTracking()
            .Where(b => b.RetiredAtUtc == null)
            .OrderBy(b => b.BlueprintKey)
            .ThenBy(b => b.VersionNumber)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var list = new List<BlueprintDto>();

        foreach (var id in ids)
        {
            if (await GetBlueprintAsync(id, langId, cancellationToken) is { } dto) list.Add(dto);
        }

        return list;
    }

    public async Task<BlueprintDto?> GetBlueprintAsync(
        Guid blueprintId, Guid langId, CancellationToken cancellationToken = default)
    {
        var blueprint = await _dbContext.AssessmentBlueprints
            .AsNoTracking()
            .Include(b => b.Areas).ThenInclude(a => a.Lines)
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);

        if (blueprint is null) return null;

        var frameworkName = await _dbContext.CompetencyFrameworks
            .AsNoTracking()
            .Where(f => f.Id == blueprint.FrameworkId)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var targetIds = blueprint.Areas.SelectMany(a => a.Lines).Select(l => l.TargetId).Distinct().ToList();

        var targets = await _dbContext.LearningTargets
            .AsNoTracking()
            .Where(t => targetIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.IsPlaceholder,
                Statement = t.Translations
                    .Where(x => x.LangId == langId)
                    .Select(x => x.Statement)
                    .FirstOrDefault(),

                // How many questions in the bank could actually serve this line. Zero is the
                // orphan a syllabus change produces, and catching it is half the reason a
                // blueprint is a separate object from a form.
                Items = _dbContext.ItemTargetMappings.Count(
                    m => m.TargetId == t.Id && m.Item!.RetiredAtUtc == null)
            })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var areaTotal = blueprint.Areas.Sum(a => a.Weight);
        if (areaTotal <= 0) areaTotal = Math.Max(1, blueprint.Areas.Count);

        var areas = blueprint.Areas
            .OrderBy(a => a.Order)
            .Select(a => new BlueprintAreaDto
            {
                AreaId = a.Id,
                AreaKey = a.AreaKey,
                Label = a.Label,
                Weight = a.Weight,
                NormalisedWeight = Math.Round(a.Weight / areaTotal, 4),
                Order = a.Order,
                Lines =
                [
                    .. a.Lines.Select(l =>
                    {
                        targets.TryGetValue(l.TargetId, out var target);

                        return new BlueprintLineDto
                        {
                            LineId = l.Id,
                            TargetId = l.TargetId,
                            Statement = target?.Statement ?? string.Empty,
                            IsPlaceholder = target?.IsPlaceholder ?? false,
                            Weight = l.Weight,
                            ItemCount = l.ItemCount,
                            DifficultyBandLow = l.DifficultyBandLow,
                            DifficultyBandHigh = l.DifficultyBandHigh,
                            AvailableItems = target?.Items ?? 0
                        };
                    })
                    .OrderByDescending(l => l.Weight)
                    .ThenBy(l => l.Statement)
                ]
            })
            .ToList();

        return new BlueprintDto
        {
            BlueprintId = blueprint.Id,
            BlueprintKey = blueprint.BlueprintKey,
            VersionNumber = blueprint.VersionNumber,
            Name = blueprint.Name,
            FrameworkId = blueprint.FrameworkId,
            FrameworkName = frameworkName,
            SourceNote = blueprint.SourceNote,
            IsPublished = blueprint.PublishedAtUtc != null,
            MinCoverageRatio = blueprint.MinCoverageRatio,
            MinAreaCoverageRatio = blueprint.MinAreaCoverageRatio,
            MinObservationsOverall = blueprint.MinObservationsOverall,
            MinObservationsPerArea = blueprint.MinObservationsPerArea,
            MaxMedianEvidenceAgeDays = blueprint.MaxMedianEvidenceAgeDays,
            Areas = areas,
            PlaceholderLines = areas.SelectMany(a => a.Lines).Count(l => l.IsPlaceholder),
            UnservableLines = areas.SelectMany(a => a.Lines).Count(l => l.AvailableItems == 0)
        };
    }

    // --------------------------------------------------------------------------- writing

    public async Task<AuthoringReport> SaveBlueprintAsync(
        SaveBlueprintRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Areas.Count == 0)
            throw new InvalidOperationException("A blueprint with no areas describes no examination.");

        var now = DateTime.UtcNow;

        var latest = await _dbContext.AssessmentBlueprints
            .Include(b => b.Areas).ThenInclude(a => a.Lines)
            .Where(b => b.BlueprintKey == request.BlueprintKey)
            .OrderByDescending(b => b.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        AssessmentBlueprint blueprint;

        if (latest is null)
        {
            blueprint = New(request, versionNumber: 1, now);
            _dbContext.AssessmentBlueprints.Add(blueprint);
        }
        else if (latest.PublishedAtUtc is null)
        {
            // A draft is still an argument somebody is having. Edit it in place.
            blueprint = latest;
            Apply(blueprint, request);
            _dbContext.AssessmentBlueprintAreas.RemoveRange(latest.Areas);
        }
        else
        {
            // **A published blueprint is never edited.** Every coverage figure ever computed named
            // this version, and changing what it says would retroactively change what those figures
            // meant. The write becomes the next version instead.
            blueprint = New(request, latest.VersionNumber + 1, now);
            _dbContext.AssessmentBlueprints.Add(blueprint);
        }

        var lines = 0;

        foreach (var (area, index) in request.Areas.Select((a, i) => (a, i)))
        {
            var areaRow = new AssessmentBlueprintArea
            {
                Id = Guid.NewGuid(),
                BlueprintId = blueprint.Id,
                AreaKey = area.AreaKey,
                Label = area.Label,
                Weight = area.Weight,
                Order = area.Order == 0 ? index : area.Order
            };

            _dbContext.AssessmentBlueprintAreas.Add(areaRow);

            foreach (var line in area.Lines.DistinctBy(l => l.TargetId))
            {
                _dbContext.AssessmentBlueprintLines.Add(new AssessmentBlueprintLine
                {
                    Id = Guid.NewGuid(),
                    AreaId = areaRow.Id,
                    TargetId = line.TargetId,
                    Weight = line.Weight,
                    ItemCount = line.ItemCount,
                    DifficultyBandLow = line.DifficultyBandLow,
                    DifficultyBandHigh = line.DifficultyBandHigh,
                    CreatedAtUtc = now
                });

                lines++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var placeholderLines = await _dbContext.AssessmentBlueprintLines
            .CountAsync(
                l => l.Area!.BlueprintId == blueprint.Id && l.Target!.IsPlaceholder,
                cancellationToken);

        return new AuthoringReport
        {
            Id = blueprint.Id,
            Key = blueprint.BlueprintKey,
            Areas = request.Areas.Count,
            Lines = lines,
            PlaceholderLines = placeholderLines,
            Warning = placeholderLines > 0
                ? $"{placeholderLines} of {lines} lines name a lesson placeholder rather than an "
                  + "authored competency. This blueprint cannot be published and its coverage "
                  + "figures will read zero for those lines until real targets replace them."
                : null
        };
    }

    private static AssessmentBlueprint New(SaveBlueprintRequest request, int versionNumber, DateTime now) =>
        Apply(new AssessmentBlueprint
        {
            Id = Guid.NewGuid(),
            BlueprintKey = request.BlueprintKey,
            VersionNumber = versionNumber,
            CreatedAtUtc = now
        }, request);

    private static AssessmentBlueprint Apply(AssessmentBlueprint blueprint, SaveBlueprintRequest request)
    {
        blueprint.Name = request.Name;
        blueprint.FrameworkId = request.FrameworkId;
        blueprint.SourceNote = request.SourceNote;
        blueprint.TimeLimitMs = request.TimeLimitMs;
        blueprint.RetryPermitted = request.RetryPermitted;
        blueprint.MinCoverageRatio = request.MinCoverageRatio;
        blueprint.MinAreaCoverageRatio = request.MinAreaCoverageRatio;
        blueprint.MinObservationsOverall = request.MinObservationsOverall;
        blueprint.MinObservationsPerArea = request.MinObservationsPerArea;
        blueprint.MaxMedianEvidenceAgeDays = request.MaxMedianEvidenceAgeDays;

        return blueprint;
    }

    public async Task<bool> PublishBlueprintAsync(
        Guid blueprintId, CancellationToken cancellationToken = default)
    {
        var blueprint = await _dbContext.AssessmentBlueprints
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);

        if (blueprint is null) return false;
        if (blueprint.PublishedAtUtc is not null) return true;

        // **The gate that keeps §20.5 honest.** A blueprint over lesson placeholders would produce
        // a coverage figure that looks like an exam readiness claim and is a completion percentage
        // in disguise. Refused at publish rather than warned about, because a published blueprint
        // is what the learner-facing projection reads.
        var placeholders = await _dbContext.AssessmentBlueprintLines
            .CountAsync(l => l.Area!.BlueprintId == blueprintId && l.Target!.IsPlaceholder, cancellationToken);

        if (placeholders > 0)
        {
            throw new InvalidOperationException(
                $"This blueprint has {placeholders} line(s) naming a lesson placeholder. A placeholder "
                + "cannot say what an examination covers, so publishing would put a completion "
                + "percentage behind an exam-readiness claim. Author real targets for those lessons "
                + "first.");
        }

        if (!await _dbContext.AssessmentBlueprintLines
                .AnyAsync(l => l.Area!.BlueprintId == blueprintId, cancellationToken))
        {
            throw new InvalidOperationException("A blueprint with no lines covers nothing.");
        }

        blueprint.PublishedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---------------------------------------------------------------- exam specifications

    public async Task<AuthoringReport> SaveExamSpecificationAsync(
        SaveExamSpecificationRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var specification = await _dbContext.ExamSpecifications
            .FirstOrDefaultAsync(s => s.SpecificationKey == request.SpecificationKey, cancellationToken);

        if (specification is null)
        {
            specification = new ExamSpecification
            {
                Id = Guid.NewGuid(),
                SpecificationKey = request.SpecificationKey,
                CreatedAtUtc = now
            };

            _dbContext.ExamSpecifications.Add(specification);
        }

        specification.Name = request.Name;

        // Defaults to Share7's own authority rather than to null. An unattributed examination is
        // one whose report says nothing about who is making the claim, and the claim is the part
        // a parent will weigh.
        specification.AuthorityId = request.AuthorityId ?? AssessmentIds.Share7Authority;
        specification.CountryCode = request.CountryCode;
        specification.SubjectLabel = request.SubjectLabel;

        var version = await _dbContext.ExamSpecificationVersions
            .FirstOrDefaultAsync(
                v => v.ExamSpecificationId == specification.Id && v.VersionLabel == request.VersionLabel,
                cancellationToken);

        if (version is null)
        {
            version = new ExamSpecificationVersion
            {
                Id = Guid.NewGuid(),
                ExamSpecificationId = specification.Id,
                VersionLabel = request.VersionLabel,
                CreatedAtUtc = now
            };

            _dbContext.ExamSpecificationVersions.Add(version);
        }
        else if (version.PublishedAtUtc is not null)
        {
            throw new InvalidOperationException(
                "This sitting's specification is published. Coverage figures and reported outcomes "
                + "both name it, so it revises by publishing a new version rather than by editing.");
        }

        version.BlueprintId = request.BlueprintId;
        version.SittingDate = request.SittingDate;
        version.MaxScore = request.MaxScore;
        version.PassingScore = request.PassingScore;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthoringReport { Id = version.Id, Key = specification.SpecificationKey };
    }

    public async Task<bool> PublishExamVersionAsync(
        Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.ExamSpecificationVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null) return false;

        var blueprint = await _dbContext.AssessmentBlueprints
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == version.BlueprintId, cancellationToken);

        if (blueprint?.PublishedAtUtc is null)
        {
            throw new InvalidOperationException(
                "An examination cannot be published against an unpublished blueprint — the thing it "
                + "says the paper covers would still be editable underneath it.");
        }

        version.PublishedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ------------------------------------------------------------------- worked example

    public async Task<AuthoringReport> GenerateBenchmarkAsync(
        GenerateBenchmarkRequest request, Guid langId, CancellationToken cancellationToken = default)
    {
        var subject = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.Id == request.SubjectNodeId)
            .Select(n => new
            {
                n.Id,
                n.Path,
                Title = _dbContext.CurriculumNodeTranslations
                    .Where(t => t.NodeId == n.Id && t.LangId == langId)
                    .Select(t => t.Title)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No such node.");

        var prefix = subject.Path + "/";

        // Chapters become areas and the targets their lessons teach become lines. That is the only
        // structure the platform actually has; inventing a weighting a real syllabus would have
        // published is precisely what this must not do, so every area carries equal weight and the
        // source note says so.
        var chapters = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.Path.StartsWith(prefix) && n.KindKey == "chapter" && n.RetiredAtUtc == null)
            .OrderBy(n => n.Order)
            .Select(n => new
            {
                n.Id,
                n.Path,
                n.Order,
                Title = _dbContext.CurriculumNodeTranslations
                    .Where(t => t.NodeId == n.Id && t.LangId == langId)
                    .Select(t => t.Title)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        if (chapters.Count == 0)
            throw new InvalidOperationException("That node has no chapters beneath it to build areas from.");

        var areas = new List<SaveBlueprintAreaRequest>();

        foreach (var (chapter, index) in chapters.Select((c, i) => (c, i)))
        {
            var chapterPrefix = chapter.Path + "/";

            var targetIds = await _dbContext.NodeTargetMappings
                .AsNoTracking()
                .Where(m => _dbContext.CurriculumNodes.Any(
                    n => n.Id == m.NodeId && n.Path.StartsWith(chapterPrefix) && n.RetiredAtUtc == null))
                .Where(m => m.Target!.RetiredAtUtc == null)
                .Select(m => m.TargetId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (targetIds.Count == 0) continue;

            areas.Add(new SaveBlueprintAreaRequest
            {
                AreaKey = $"area{index + 1:D2}",
                Label = chapter.Title ?? $"Area {index + 1}",
                Weight = 1.0m,
                Order = index,
                Lines =
                [
                    .. targetIds.Select(id => new SaveBlueprintLineRequest
                    {
                        TargetId = id,
                        Weight = 1.0m,
                        ItemCount = 2
                    })
                ]
            });
        }

        if (areas.Count == 0)
            throw new InvalidOperationException("No lesson beneath that node maps to a learning target.");

        // Keyed by the node so regenerating a subject revises its own benchmark rather than
        // accumulating a new one each time. 49 characters by construction, well inside the column.
        var key = $"share7.benchmark.{subject.Id:N}";
        var label = request.VersionLabel ?? DateTime.UtcNow.Year.ToString();

        var report = await SaveBlueprintAsync(new SaveBlueprintRequest
        {
            BlueprintKey = key,
            Name = $"Share7 benchmark — {subject.Title}",
            FrameworkId = AssessmentIds.Share7CoreFramework,

            // **The most important field on the row.** Anybody reading a coverage number built
            // from this has to know it was derived from Share7's own content with equal weights,
            // not taken from a syllabus an examining body published.
            SourceNote =
                $"Derived structurally by Share7 from its own \"{subject.Title}\" content on "
                + $"{DateTime.UtcNow:yyyy-MM-dd}: one area per chapter, equal weights, two items per "
                + "target. It is a benchmark over what this platform teaches, not a published "
                + "examination syllabus, and its weights are not an examining body's.",

            TimeLimitMs = null,
            RetryPermitted = false,
            Areas = areas
        }, cancellationToken);

        var specification = await SaveExamSpecificationAsync(new SaveExamSpecificationRequest
        {
            SpecificationKey = key,
            Name = $"Share7 benchmark — {subject.Title}",
            AuthorityId = AssessmentIds.Share7Authority,
            SubjectLabel = subject.Title,
            VersionLabel = label,
            BlueprintId = report.Id
        }, cancellationToken);

        return report with
        {
            Id = specification.Id,
            Key = key,
            Warning = report.Warning
        };
    }
}
