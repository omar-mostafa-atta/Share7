using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Application.Evidence.Interfaces;
using Share7.Application.Evidence.Models;
using Share7.Domain.Assessment;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

// Aliased because this file's own namespace is Share7.Infrastructure.Assessment, so an unqualified
// `Assessment` binds to the namespace rather than to the entity.
using AssessmentEntity = Share7.Domain.Assessment.Assessment;

namespace Share7.Infrastructure.Assessment;

/// <inheritdoc cref="IAssessmentService"/>
public class AssessmentService : IAssessmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IItemSelector _selector;
    private readonly IEvidenceRecorder _evidence;
    private readonly ILogger<AssessmentService> _logger;

    public AssessmentService(
        ApplicationDbContext dbContext,
        IItemSelector selector,
        IEvidenceRecorder evidence,
        ILogger<AssessmentService> logger)
    {
        _dbContext = dbContext;
        _selector = selector;
        _evidence = evidence;
        _logger = logger;
    }

    // ------------------------------------------------------------------------ starting

    public async Task<AdministrationDto> StartAsync(
        Guid learnerId, StartAdministrationRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await _dbContext.AssessmentAdministrations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.LearnerId == learnerId && a.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            // A replayed start finds its own sitting rather than opening a second one. Two
            // administrations of the same paper is not a duplicate row — it is a second set of
            // conditions and a second score, and the learner sat once.
            if (replay is not null) return await DescribeAsync(replay, cancellationToken);
        }

        var form = await ResolveFormAsync(request, cancellationToken)
            ?? throw new InvalidOperationException("No sealed form is available for this assessment.");

        var now = DateTime.UtcNow;

        // Sealed on first sitting. After this the item list is frozen, because the promise a form
        // makes is that this exact paper can be reproduced — and a paper that changed after
        // somebody sat it cannot be.
        if (form.SealedAtUtc is null)
        {
            var tracked = await _dbContext.AssessmentForms.FirstAsync(f => f.Id == form.Id, cancellationToken);
            tracked.SealedAtUtc = now;
        }

        var limit = form.TimeLimitMs ?? form.Blueprint?.TimeLimitMs;

        var administration = new AssessmentAdministration
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            LearnerId = learnerId,
            Purpose = request.Purpose,
            State = AdministrationState.InProgress,
            DeliveryMode = request.DeliveryMode,

            // Declared by whoever opened the sitting, and **fixed from here**. A client that could
            // change these mid-paper would be choosing its own evidence strength.
            RetryPermitted = request.RetryPermitted || (form.Blueprint?.RetryPermitted ?? false),
            WasAided = request.WasAided,
            TimeLimitMs = limit,

            LangId = request.LangId ?? LanguageIds.English,
            StartedAtUtc = now,

            // Server-side, from the server's clock. A deadline the client computed is a deadline
            // the client can move.
            ExpiresAtUtc = limit is { } ms ? now.AddMilliseconds(ms) : null,

            IdempotencyKey = request.IdempotencyKey
        };

        _dbContext.AssessmentAdministrations.Add(administration);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(administration, cancellationToken);
    }

    private async Task<AssessmentForm?> ResolveFormAsync(
        StartAdministrationRequest request, CancellationToken cancellationToken)
    {
        var query = _dbContext.AssessmentForms.AsNoTracking().Include(f => f.Blueprint);

        if (request.FormId is { } formId)
            return await query.FirstOrDefaultAsync(f => f.Id == formId, cancellationToken);

        if (request.AssessmentId is { } assessmentId)
        {
            return await query
                .Where(f => f.AssessmentId == assessmentId)
                .OrderByDescending(f => f.FormNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    // -------------------------------------------------------------------------- reading

    public async Task<AdministrationDto?> GetAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken = default)
    {
        var administration = await LoadAsync(administrationId, learnerId, cancellationToken);
        return administration is null ? null : await DescribeAsync(administration, cancellationToken);
    }

    public async Task<IReadOnlyList<AdministrationItemDto>> GetItemsAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken = default)
    {
        var administration = await LoadAsync(administrationId, learnerId, cancellationToken);
        if (administration is null) return [];

        var answered = await AnsweredPositionsAsync(administration, cancellationToken);

        var rows = await _dbContext.AssessmentFormItems
            .AsNoTracking()
            .Where(i => i.FormId == administration.FormId)
            .OrderBy(i => i.Position)
            .Select(i => new
            {
                i.Position,
                i.ItemVersionId,
                i.Points,
                Question = i.ItemVersion!.Localizations
                    .Where(q => q.LangId == administration.LangId && q.IsActive)
                    .Select(q => new { q.Id, q.Text })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var questionIds = rows.Where(r => r.Question != null).Select(r => r.Question!.Id).ToList();

        // Correctness is deliberately not selected. The client renders what the learner must
        // choose between and nothing that would tell them which to pick.
        var choices = (await _dbContext.QuestionChoices
                .AsNoTracking()
                .Where(c => questionIds.Contains(c.QuestionId))
                .Select(c => new { c.Id, c.QuestionId, c.Text, c.OrderIndex })
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.OrderIndex).ToList());

        return
        [
            .. rows.Select(r => new AdministrationItemDto
            {
                Position = r.Position,
                ItemVersionId = r.ItemVersionId,
                QuestionId = r.Question?.Id,
                Stem = r.Question?.Text,
                Points = r.Points,
                IsAnswered = answered.Contains(r.Position),
                Choices = r.Question is null || !choices.TryGetValue(r.Question.Id, out var list)
                    ? []
                    : [.. list.Select(c => new AdministrationChoiceDto
                    {
                        ChoiceId = c.Id,
                        Text = c.Text,
                        Order = c.OrderIndex
                    })]
            })
        ];
    }

    // ------------------------------------------------------------------------ answering

    public async Task<AdministrationAnswerResult> AnswerAsync(
        Guid administrationId, Guid learnerId, AdministrationAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        var administration = await LoadAsync(administrationId, learnerId, cancellationToken)
            ?? throw new InvalidOperationException("No such sitting.");

        var answered = await AnsweredPositionsAsync(administration, cancellationToken);
        var total = await _dbContext.AssessmentFormItems
            .CountAsync(i => i.FormId == administration.FormId, cancellationToken);

        AdministrationAnswerResult Reject(string reason) => new()
        {
            Position = request.Position,
            IsCorrect = false,
            CorrectChoiceId = null,
            Accepted = false,
            Rejection = reason,
            AnsweredCount = answered.Count,
            ItemCount = total
        };

        if (administration.State != AdministrationState.InProgress)
            return Reject("closed");

        // Enforced against the server's clock. Expiring the sitting here rather than only at
        // completion is what makes the time limit a real condition rather than a suggestion the
        // client renders.
        if (administration.ExpiresAtUtc is { } deadline && DateTime.UtcNow > deadline)
        {
            administration.State = AdministrationState.Expired;
            administration.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Reject("expired");
        }

        // Re-answering is refused unless the sitting declared retries. Silently accepting the
        // second answer would let a learner's evidence be collected under conditions nobody
        // declared, which is exactly what the conditions record exists to prevent.
        if (answered.Contains(request.Position) && !administration.RetryPermitted)
            return Reject("already_answered");

        var item = await _dbContext.AssessmentFormItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.FormId == administration.FormId && i.Position == request.Position,
                cancellationToken);

        if (item is null) return Reject("no_such_position");

        var question = await _dbContext.Questions
            .AsNoTracking()
            .Where(q => q.ItemVersionId == item.ItemVersionId
                        && q.LangId == administration.LangId
                        && q.IsActive)
            .Select(q => new { q.Id, q.CorrectChoiceId })
            .FirstOrDefaultAsync(cancellationToken);

        if (question is null) return Reject("no_rendering_in_language");

        // Server-graded against the stored key, always. The client has no field in which to assert
        // this, by the rule that already governs every other score in the platform.
        var belongs = request.ChoiceId is null
            || await _dbContext.QuestionChoices.AnyAsync(
                c => c.Id == request.ChoiceId && c.QuestionId == question.Id, cancellationToken);

        var isCorrect = request.ChoiceId is { } choice && choice == question.CorrectChoiceId;

        await _evidence.RecordAsync(
            new EvidenceRecordingContext
            {
                LearnerId = learnerId,
                LangId = administration.LangId,
                PlayContext = PlayContextKind.Curriculum,
                NodeId = null,
                OrgId = administration.OrgId,
                DeliveryMode = administration.DeliveryMode,
                WasAided = administration.WasAided,
                ReceivedAtUtc = DateTime.UtcNow,

                // The sitting's own contract, not the lesson one. Its conditions were fixed before
                // the paper was served, which is the basis the stronger claim rests on.
                InteractionKind = InteractionKinds.AssessmentResponse,
                IdempotencyKey = $"adm/{administration.Id:D}/{request.Position}"
            },
            [
                new EvidenceAnswer
                {
                    ItemLocalizationId = question.Id,
                    ChoiceId = belongs ? request.ChoiceId : null,
                    IsCorrect = isCorrect,
                    WasUnrecognised = !belongs,
                    ElapsedMs = request.ElapsedMs,
                    HintsUsed = request.HintsUsed,
                    TimeLimitMs = administration.TimeLimitMs,
                    RetryPermitted = administration.RetryPermitted
                }
            ],
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AdministrationAnswerResult
        {
            Position = request.Position,
            IsCorrect = isCorrect,
            CorrectChoiceId = question.CorrectChoiceId,
            Accepted = true,
            AnsweredCount = answered.Count + (answered.Contains(request.Position) ? 0 : 1),
            ItemCount = total
        };
    }

    // ------------------------------------------------------------------------ completing

    public async Task<AdministrationDto?> CompleteAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken = default)
    {
        var administration = await LoadAsync(administrationId, learnerId, cancellationToken);
        if (administration is null) return null;

        if (administration.State == AdministrationState.InProgress)
        {
            administration.State = AdministrationState.Submitted;
            administration.CompletedAtUtc = DateTime.UtcNow;
        }

        var items = await _dbContext.AssessmentFormItems
            .AsNoTracking()
            .Where(i => i.FormId == administration.FormId)
            .Select(i => new { i.Position, i.ItemVersionId, i.Points })
            .ToListAsync(cancellationToken);

        var correct = await _dbContext.LearnerResponses
            .AsNoTracking()
            .Where(r => r.LearnerId == learnerId
                        && r.IdempotencyKey != null
                        && r.IdempotencyKey.StartsWith($"adm/{administration.Id:D}/")
                        && r.IsCorrect)
            .Select(r => r.ItemVersionId)
            .ToListAsync(cancellationToken);

        var earned = items.Where(i => correct.Contains(i.ItemVersionId)).Sum(i => i.Points);

        administration.PointsEarned = earned;
        administration.PointsAvailable = items.Sum(i => i.Points);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Unreached positions are left alone rather than recorded as wrong. A learner who ran out
        // of time did not answer those questions incorrectly, and the projector already treats an
        // absent response as NoResponse — which is the distinction between measuring knowledge and
        // measuring speed.
        _logger.LogInformation(
            "Sitting {Administration} closed: {Earned} of {Available} points over {Items} positions.",
            administration.Id, earned, administration.PointsAvailable, items.Count);

        return await DescribeAsync(administration, cancellationToken);
    }

    // -------------------------------------------------------------------- form generation

    public async Task<FormGenerationReport> GenerateFormAsync(
        Guid assessmentId, Guid blueprintId, CancellationToken cancellationToken = default)
    {
        var assessment = await _dbContext.Set<AssessmentEntity>()
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new InvalidOperationException("No such assessment.");

        var blueprint = await _dbContext.AssessmentBlueprints
            .AsNoTracking()
            .Include(b => b.Areas).ThenInclude(a => a.Lines).ThenInclude(l => l.Target)
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken)
            ?? throw new InvalidOperationException("No such blueprint.");

        var nextNumber = await _dbContext.AssessmentForms
            .Where(f => f.AssessmentId == assessmentId)
            .Select(f => (int?)f.FormNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var now = DateTime.UtcNow;

        var form = new AssessmentForm
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessmentId,
            FormNumber = nextNumber + 1,
            Label = $"{assessment.Name} form {nextNumber + 1}",
            BlueprintId = blueprintId,
            TimeLimitMs = blueprint.TimeLimitMs,
            CreatedAtUtc = now
        };

        _dbContext.AssessmentForms.Add(form);

        var position = 0;
        var unfilled = new List<UnfilledLine>();
        var used = new HashSet<Guid>();

        foreach (var line in blueprint.Areas.OrderBy(a => a.Order).SelectMany(a => a.Lines))
        {
            var wanted = line.ItemCount > 0 ? line.ItemCount : 1;

            // The newest live version of each item mapped to this target. A retired version is a
            // question somebody replaced, and serving it would be serving a paper out of a bin.
            var candidates = await _dbContext.ItemTargetMappings
                .AsNoTracking()
                .Where(m => m.TargetId == line.TargetId)
                .Where(m => m.Item!.RetiredAtUtc == null)
                .Select(m => m.Item!.Versions
                    .Where(v => v.RetiredAtUtc == null)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => v.Id)
                    .FirstOrDefault())
                .Where(id => id != Guid.Empty)
                .Take(wanted * 4)
                .ToListAsync(cancellationToken);

            var drawn = candidates.Where(id => used.Add(id)).Take(wanted).ToList();

            foreach (var versionId in drawn)
            {
                _dbContext.AssessmentFormItems.Add(new AssessmentFormItem
                {
                    Id = Guid.NewGuid(),
                    FormId = form.Id,
                    Position = ++position,
                    ItemVersionId = versionId,
                    Points = 1.0m,
                    BlueprintLineId = line.Id
                });
            }

            // **Reported, never silently skipped.** A paper short on geometry because no geometry
            // items exist is not the paper the blueprint describes, and shipping it as though it
            // were is how an assessment quietly stops measuring what it claims to.
            if (drawn.Count < wanted)
            {
                unfilled.Add(new UnfilledLine
                {
                    LineId = line.Id,
                    TargetId = line.TargetId,
                    Statement = line.Target?.TargetKey ?? line.TargetId.ToString("D"),
                    Requested = wanted,
                    Available = drawn.Count
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FormGenerationReport
        {
            FormId = form.Id,
            FormNumber = form.FormNumber,
            ItemsPlaced = position,
            Unfilled = unfilled
        };
    }

    // --------------------------------------------------------------------------- helpers

    private Task<AssessmentAdministration?> LoadAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken) =>
        _dbContext.AssessmentAdministrations
            .FirstOrDefaultAsync(
                a => a.Id == administrationId && a.LearnerId == learnerId, cancellationToken);

    /// <summary>
    /// Which positions carry an answer, read from the evidence log rather than from a column on
    /// the administration. **The log is the truth**; a counter beside it would be a second copy of
    /// a fact that can already be asked for, free to drift and impossible to reconcile.
    /// </summary>
    private async Task<HashSet<int>> AnsweredPositionsAsync(
        AssessmentAdministration administration, CancellationToken cancellationToken)
    {
        var prefix = $"adm/{administration.Id:D}/";

        var keys = await _dbContext.LearnerResponses
            .AsNoTracking()
            .Where(r => r.LearnerId == administration.LearnerId
                        && r.IdempotencyKey != null
                        && r.IdempotencyKey.StartsWith(prefix))
            .Select(r => r.IdempotencyKey!)
            .ToListAsync(cancellationToken);

        return
        [
            .. keys
                .Select(k => int.TryParse(k[prefix.Length..], out var position) ? position : -1)
                .Where(p => p > 0)
        ];
    }

    private async Task<AdministrationDto> DescribeAsync(
        AssessmentAdministration administration, CancellationToken cancellationToken)
    {
        var form = await _dbContext.AssessmentForms
            .AsNoTracking()
            .Where(f => f.Id == administration.FormId)
            .Select(f => new
            {
                Name = f.Assessment!.Name,
                ItemCount = f.Items.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        var answered = await AnsweredPositionsAsync(administration, cancellationToken);

        return new AdministrationDto
        {
            AdministrationId = administration.Id,
            FormId = administration.FormId,
            AssessmentName = form?.Name ?? string.Empty,
            Purpose = administration.Purpose,
            State = administration.State,
            ItemCount = form?.ItemCount ?? 0,
            AnsweredCount = answered.Count,
            RetryPermitted = administration.RetryPermitted,
            WasAided = administration.WasAided,
            DeliveryMode = administration.DeliveryMode,
            TimeLimitMs = administration.TimeLimitMs,
            StartedAtUtc = administration.StartedAtUtc,
            ExpiresAtUtc = administration.ExpiresAtUtc,
            CompletedAtUtc = administration.CompletedAtUtc,
            PointsEarned = administration.PointsEarned,
            PointsAvailable = administration.PointsAvailable,
            ExpectedStrength = await ExpectedStrengthAsync(administration, cancellationToken)
        };
    }

    /// <summary>
    /// What this sitting's answers will be worth, computed from its declared conditions before a
    /// single question is served.
    /// <para>
    /// Surfaced so that somebody setting up a sitting with retries enabled finds out it will never
    /// produce exam-grade evidence **while there is still time to change it**, rather than
    /// discovering it in a coverage report three months later. The same contract and the same
    /// function the projector uses, so the preview cannot disagree with the outcome.
    /// </para>
    /// </summary>
    private async Task<EvidenceStrength> ExpectedStrengthAsync(
        AssessmentAdministration administration, CancellationToken cancellationToken)
    {
        var contract = await _dbContext.EvidenceContractVersions
            .AsNoTracking()
            .Where(v => v.ContractId == EvidenceContractIds.PlatformAssessmentAdministration)
            .Where(v => v.PublishedAtUtc != null && v.RetiredAtUtc == null)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (contract is null) return EvidenceStrength.None;

        // The same function the projector applies to the stored response, so a preview shown to
        // whoever is setting the sitting up cannot disagree with what the evidence turns out to be.
        return contract.StrengthFor(
            isFirstEncounter: true,
            hintsUsed: 0,
            administration.RetryPermitted,
            administration.DeliveryMode,
            administration.WasAided);
    }
}
