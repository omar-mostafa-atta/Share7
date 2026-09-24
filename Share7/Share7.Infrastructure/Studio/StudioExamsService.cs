using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Studio.Interfaces;
using Share7.Application.Workspace;
using Share7.Application.Workspace.Models;
using Share7.Domain.Audit;
using Share7.Domain.Staff;
using Share7.Infrastructure.Workspace;

namespace Share7.Infrastructure.Studio;

/// <inheritdoc cref="IStudioExamsService"/>
public sealed class StudioExamsService : IStudioExamsService
{
    private readonly IBlueprintAuthoringService _blueprints;
    private readonly StudioScope _scope;
    private readonly IAuditLog _audit;

    public StudioExamsService(IBlueprintAuthoringService blueprints, StudioScope scope, IAuditLog audit)
    {
        _blueprints = blueprints;
        _scope = scope;
        _audit = audit;
    }

    public Task<IReadOnlyList<BlueprintDto>> BlueprintsAsync(Guid langId, CancellationToken cancellationToken = default) =>
        _blueprints.ListBlueprintsAsync(langId, cancellationToken);

    public async Task<ServiceResult<BlueprintDto>> BlueprintAsync(
        Guid blueprintId, Guid langId, CancellationToken cancellationToken = default)
    {
        var one = await _blueprints.GetBlueprintAsync(blueprintId, langId, cancellationToken);

        return one is null
            ? ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no blueprint with that id.")
            : ServiceResult<BlueprintDto>.Success(one);
    }

    public async Task<ServiceResult<BenchmarkReportDto>> GenerateBenchmarkAsync(
        StudioMember member, Guid subjectNodeId, string? versionLabel, Guid langId,
        CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return ServiceResult<BenchmarkReportDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "Only a Lead builds a benchmark.");

        // The paper takes its name and its source note from the subject's title, read in one
        // language. Without one it is written as "Share7 benchmark — " and saved that way, and a
        // paper with no name in the permanent record is worse than no paper.
        if (langId == Guid.Empty)
            return ServiceResult<BenchmarkReportDto>.Failure(
                WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation,
                "Say which language to read the subject's name in.",
                new Dictionary<string, object?> { ["field"] = "langId" });

        // A benchmark is written over one subject, and writing it is a change to that subject's
        // record whatever else it is. A Lead builds papers for the curriculum they are responsible
        // for, the same as everywhere else in the Studio.
        if (!await _scope.CoversNodeAsync(member, subjectNodeId, cancellationToken))
            return ServiceResult<BenchmarkReportDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden,
                "That subject is outside the part of the curriculum you work in.",
                new Dictionary<string, object?> { ["reason"] = "node" });

        AuthoringReport report;
        try
        {
            report = await _blueprints.GenerateBenchmarkAsync(
                new GenerateBenchmarkRequest { SubjectNodeId = subjectNodeId, VersionLabel = versionLabel },
                langId, cancellationToken);
        }
        catch (InvalidOperationException refusal)
        {
            return ServiceResult<BenchmarkReportDto>.Failure(
                WorkspaceErrors.DraftInvalid, ServiceErrorKind.Validation, refusal.Message);
        }

        _audit.Record(new AuditEntry(
            AuditActions.BenchmarkBuilt, AuditAreas.Workspace,
            $"Built a benchmark paper over a subject: {report.Key}.",
            "blueprint", report.Id.ToString(),
            new { areas = report.Areas, lines = report.Lines, onStandIns = report.PlaceholderLines }));

        // **`report.Id` is the exam specification's, not the blueprint's.** The authoring service
        // returns the specification because that is what it wrote last; a caller that sends the
        // team to it gets a blueprint that does not exist. Found by its key, which is the one thing
        // both share and which is keyed to the subject.
        var blueprintId = (await _blueprints.ListBlueprintsAsync(langId, cancellationToken))
            .Where(b => b.BlueprintKey == report.Key)
            .OrderByDescending(b => b.VersionNumber)
            .Select(b => b.BlueprintId)
            .FirstOrDefault();

        return ServiceResult<BenchmarkReportDto>.Success(new BenchmarkReportDto
        {
            BlueprintId = blueprintId,
            BlueprintKey = report.Key,
            Areas = report.Areas,
            Lines = report.Lines,
            LinesOnStandIns = report.PlaceholderLines,
            Warning = report.Warning
        });
    }

    public async Task<ServiceResult<BlueprintDto>> PublishAsync(
        StudioMember member, Guid blueprintId, Guid langId, CancellationToken cancellationToken = default)
    {
        if (!member.IsAtLeast(StudioRole.Lead))
            return ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden, "Only a Lead freezes a blueprint.");

        var before = await _blueprints.GetBlueprintAsync(blueprintId, langId, cancellationToken);
        if (before is null)
            return ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no blueprint with that id.");

        if (before.IsPublished)
            return ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.DraftWrongStatus, ServiceErrorKind.Conflict,
                "It is already published. A change makes the next version rather than editing this one.");

        // Freezing is what makes a paper usable, so it is scoped to what the paper measures: every
        // place in the curriculum the skills on its lines are taught. A paper spanning more than a
        // member's part of the tree is a paper for somebody whose part is all of it.
        var measured = before.Areas.SelectMany(a => a.Lines).Select(l => l.TargetId).Distinct().ToList();

        if (!await _scope.CoversTargetsAsync(member, measured, cancellationToken))
            return ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.OutOfScope, ServiceErrorKind.Forbidden,
                "This paper measures parts of the curriculum you do not work in.",
                new Dictionary<string, object?> { ["reason"] = "node" });

        // The refusal that matters. A blueprint whose lines name lesson stand-ins measures whether
        // a child sat through lessons, and freezing it would make that reportable as proficiency.
        if (before.PlaceholderLines > 0)
            return ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.DraftHasProblems, ServiceErrorKind.Validation,
                $"{before.PlaceholderLines} of its lines still name a stand-in rather than a real skill. "
                + "Replace those stand-ins first, or this paper measures attendance.",
                new Dictionary<string, object?> { ["placeholderLines"] = before.PlaceholderLines });

        if (!await _blueprints.PublishBlueprintAsync(blueprintId, cancellationToken))
            return ServiceResult<BlueprintDto>.Failure(
                WorkspaceErrors.NodeNotFound, ServiceErrorKind.NotFound, "There is no blueprint with that id.");

        _audit.Record(new AuditEntry(
            AuditActions.BlueprintPublished, AuditAreas.Workspace,
            $"Froze a blueprint: {before.Name}.",
            "blueprint", blueprintId.ToString(),
            new { key = before.BlueprintKey, version = before.VersionNumber, unservableLines = before.UnservableLines }));

        return await BlueprintAsync(blueprintId, langId, cancellationToken);
    }
}
