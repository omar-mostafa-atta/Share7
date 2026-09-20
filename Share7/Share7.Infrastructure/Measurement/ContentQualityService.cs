using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Measurement.Interfaces;
using Share7.Application.Measurement.Models;
using Share7.Domain.Competency;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;
using Share7.Domain.Measurement;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Measurement;

/// <inheritdoc cref="IContentQualityService"/>
public class ContentQualityService : IContentQualityService
{
    private readonly ApplicationDbContext _dbContext;

    public ContentQualityService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// Above this share correct, an item is not telling us much: nearly everybody gets it. Not a
    /// fault on its own — a lesson needs a gentle opener — but worth surfacing when it is the whole
    /// set.
    /// </summary>
    private const double TooEasyAbove = 0.95;

    /// <summary>
    /// Below this, the commonest explanation is not a hard question. It is a wrong answer key.
    /// </summary>
    private const double TooHardBelow = 0.20;

    /// <summary>A choice picked by fewer than this share is doing no work at all.</summary>
    private const double DeadDistractorBelow = 0.02;

    /// <summary>
    /// Faster than this and the stem cannot have been read. Deliberately generous — children are
    /// quick, and calling a fast right answer cheating is a worse error than missing one.
    /// </summary>
    private const int TooFastMs = 1200;

    public async Task<ContentQualitySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Items.CountAsync(cancellationToken);
        var anchors = await _dbContext.Items.CountAsync(i => i.IsAnchor, cancellationToken);

        var unmapped = await _dbContext.Items
            .CountAsync(i => !_dbContext.ItemTargetMappings.Any(m => m.ItemId == i.Id), cancellationToken);

        var responses = await _dbContext.LearnerResponses.CountAsync(cancellationToken);
        var observations = await _dbContext.Observations.CountAsync(cancellationToken);
        var excluded = await _dbContext.Observations
            .CountAsync(o => o.ExcludedAtUtc != null, cancellationToken);

        var byStrength = await _dbContext.Observations
            .GroupBy(o => o.Strength)
            .Select(g => new { Strength = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Strength, x => x.Count, cancellationToken);

        // What the projector has not folded yet. Shown rather than hidden: a growing backlog is the
        // single most useful signal that the measurement layer has fallen behind reality.
        //
        // Counted by anti-join rather than from the checkpoint watermark, because the watermark
        // answers a different question. The per-learner path projects on the learner's own read
        // and deliberately does not move the global cursor, so a watermark-based count reports
        // work as outstanding that has already been done — a backlog that never clears, which is
        // the one thing a backlog number must never do.
        var pending = await _dbContext.LearnerResponses
            .CountAsync(r => !_dbContext.Observations.Any(o => o.LearnerResponseId == r.Id),
                cancellationToken);

        var stats = await _dbContext.ItemStatistics
            .AsNoTracking()
            .Where(s => s.Population == ItemStatisticsPopulations.Global)
            .Select(s => new { s.ItemId, s.NTotal, s.NFirstEncounter, s.NFirstEncounterCorrect })
            .ToListAsync(cancellationToken);

        var withResponses = stats.Where(s => s.NTotal > 0).Select(s => s.ItemId).Distinct().Count();
        var aboveFloor = stats
            .Where(s => s.NFirstEncounter >= ItemStatisticsPopulations.MinimumForReporting)
            .Select(s => s.ItemId).Distinct().Count();

        var placeholders = await _dbContext.LearningTargets
            .CountAsync(t => t.IsPlaceholder && t.RetiredAtUtc == null, cancellationToken);
        var authored = await _dbContext.LearningTargets
            .CountAsync(t => !t.IsPlaceholder && t.RetiredAtUtc == null, cancellationToken);

        return new ContentQualitySummaryDto
        {
            Items = items,
            ItemsWithResponses = withResponses,
            ItemsAboveReportingFloor = aboveFloor,
            ItemsUnmapped = unmapped,
            AnchorItems = anchors,

            Responses = responses,
            Observations = observations,
            ObservationsExcluded = excluded,
            PendingResponses = pending,

            ObservationsByStrength = byStrength,
            FlagCounts = await FlagCountsAsync(cancellationToken),

            PlaceholderTargets = placeholders,
            AuthoredTargets = authored,
            ReportingFloor = ItemStatisticsPopulations.MinimumForReporting,
            CurriculumProjection = await CurriculumProjectionAsync(cancellationToken)
        };
    }

    /// <summary>
    /// How the derived node tree compares with the typed tables it is built from.
    /// <para>
    /// The projection is invisible in the authoring UI — an admin adds a chapter through the old
    /// tree editor and the node table follows silently, in the same transaction. Silent is the
    /// right behaviour until it stops being true, and then nothing in the console would say so.
    /// This is that "and then".
    /// </para>
    /// </summary>
    private async Task<CurriculumProjectionStatusDto> CurriculumProjectionAsync(
        CancellationToken cancellationToken)
    {
        var version = await _dbContext.CurriculumVersions
            .AsNoTracking()
            .Where(v => v.Id == EducationIds.EgyptianNationalAsMigrated)
            .Select(v => new { v.VersionLabel, v.IsAuthoritative })
            .FirstOrDefaultAsync(cancellationToken);

        var byKind = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.RetiredAtUtc == null)
            .GroupBy(n => n.KindKey)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Kind, x => x.Count, cancellationToken);

        var retired = await _dbContext.CurriculumNodes
            .CountAsync(n => n.RetiredAtUtc != null, cancellationToken);

        // Counted from the source of truth, so the two numbers disagreeing is the alarm.
        var legacy = await _dbContext.Grades.CountAsync(cancellationToken)
            + await _dbContext.Terms.CountAsync(cancellationToken)
            + await _dbContext.Subjects.CountAsync(cancellationToken)
            + await _dbContext.Chapters.CountAsync(cancellationToken)
            + await _dbContext.Lessons.CountAsync(cancellationToken);

        var live = byKind.Values.Sum();

        return new CurriculumProjectionStatusDto
        {
            LiveNodes = live,
            RetiredNodes = retired,
            LegacyRows = legacy,
            VersionLabel = version?.VersionLabel ?? "none",
            IsAuthoritative = version?.IsAuthoritative ?? false,
            NodesByKind = byKind,
            Missing = Math.Max(0, legacy - live)
        };
    }

    /// <summary>
    /// Everything the flag rules need about one item, in one shape. Loaded for the whole page at
    /// once rather than per row — a quality surface that issues a query per item is a quality
    /// surface nobody opens twice.
    /// </summary>
    private sealed record Row(
        Guid ItemId, Guid ItemVersionId, int VersionNumber, string SourceKey, bool IsAnchor,
        int NTotal, int NFirstEncounter, int NFirstEncounterCorrect, long SumElapsedMs, int NElapsed,
        string? ChoiceFrequency, Guid? NodeId, bool IsMapped);

    public async Task<IReadOnlyList<ItemQualityDto>> GetItemsAsync(
        Guid langId,
        string? flag = null,
        Guid? nodeId = null,
        int take = 50,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(nodeId, cancellationToken);
        var built = await BuildAsync(rows, langId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(flag))
            built = [.. built.Where(i => i.Flags.Contains(flag))];

        // Worst first: the item a distractor is beating, then the extremes, then everything with
        // enough data to judge. An admin opening this should land on the thing worth fixing.
        return
        [
            .. built
                .OrderByDescending(i => i.Flags.Contains(ItemQualityFlags.DistractorBeatsKey))
                .ThenByDescending(i => i.Flags.Contains(ItemQualityFlags.TooHard))
                .ThenByDescending(i => i.Flags.Contains(ItemQualityFlags.Unmapped))
                .ThenByDescending(i => i.Flags.Count)
                .ThenByDescending(i => i.NFirstEncounter)
                .Skip(skip)
                .Take(take)
        ];
    }

    public async Task<ItemQualityDto?> GetItemAsync(
        Guid itemId, Guid langId, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(nodeId: null, cancellationToken, itemId);
        var built = await BuildAsync(rows, langId, cancellationToken);
        return built.FirstOrDefault();
    }

    public async Task<bool> SetAnchorAsync(
        Guid itemId, bool isAnchor, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null) return false;

        item.IsAnchor = isAnchor;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ExcludeItemObservationsAsync(
        Guid itemVersionId,
        ObservationExclusionReason reason,
        Guid? excludedByUserId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // An UPDATE, never a DELETE, and never against LearnerResponses. The responses are what
        // happened; the observations are what we concluded, and only the conclusion was wrong.
        var affected = await _dbContext.Observations
            .Where(o => o.ItemVersionId == itemVersionId && o.ExcludedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(o => o.ExcludedAtUtc, now)
                    .SetProperty(o => o.ExclusionReason, reason)
                    .SetProperty(o => o.ExcludedByUserId, excludedByUserId)
                    .SetProperty(o => o.ExclusionNote, note),
                cancellationToken);

        return affected;
    }

    // ---- loading and flagging ----------------------------------------------------------------

    private async Task<List<Row>> LoadRowsAsync(
        Guid? nodeId, CancellationToken cancellationToken, Guid? itemId = null)
    {
        var query = _dbContext.ItemVersions.AsNoTracking().Where(v => v.RetiredAtUtc == null);

        if (itemId is { } id)
            query = query.Where(v => v.ItemId == id);

        if (nodeId is { } node)
        {
            query = query.Where(v => _dbContext.NodeItemMappings
                .Any(m => m.ItemId == v.ItemId && m.NodeId == node));
        }

        return await query
            .Select(v => new Row(
                v.ItemId,
                v.Id,
                v.VersionNumber,
                v.Item!.SourceKey,
                v.Item.IsAnchor,
                _dbContext.ItemStatistics
                    .Where(s => s.ItemVersionId == v.Id && s.Population == ItemStatisticsPopulations.Global)
                    .Select(s => s.NTotal).FirstOrDefault(),
                _dbContext.ItemStatistics
                    .Where(s => s.ItemVersionId == v.Id && s.Population == ItemStatisticsPopulations.Global)
                    .Select(s => s.NFirstEncounter).FirstOrDefault(),
                _dbContext.ItemStatistics
                    .Where(s => s.ItemVersionId == v.Id && s.Population == ItemStatisticsPopulations.Global)
                    .Select(s => s.NFirstEncounterCorrect).FirstOrDefault(),
                _dbContext.ItemStatistics
                    .Where(s => s.ItemVersionId == v.Id && s.Population == ItemStatisticsPopulations.Global)
                    .Select(s => s.SumElapsedMs).FirstOrDefault(),
                _dbContext.ItemStatistics
                    .Where(s => s.ItemVersionId == v.Id && s.Population == ItemStatisticsPopulations.Global)
                    .Select(s => s.NElapsed).FirstOrDefault(),
                _dbContext.ItemStatistics
                    .Where(s => s.ItemVersionId == v.Id && s.Population == ItemStatisticsPopulations.Global)
                    .Select(s => s.ChoiceFrequency).FirstOrDefault(),
                _dbContext.NodeItemMappings
                    .Where(m => m.ItemId == v.ItemId)
                    .Select(m => (Guid?)m.NodeId).FirstOrDefault(),
                _dbContext.ItemTargetMappings.Any(m => m.ItemId == v.ItemId)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The question as one language renders it, named so it can cross a method boundary.</summary>
    private sealed record Localized(string Text, Guid CorrectChoiceId, List<LocalizedChoice> Choices);

    private sealed record LocalizedChoice(Guid Id, string Text);

    private async Task<List<ItemQualityDto>> BuildAsync(
        List<Row> rows, Guid langId, CancellationToken cancellationToken)
    {
        var versionIds = rows.Select(r => r.ItemVersionId).ToList();
        var nodeIds = rows.Where(r => r.NodeId != null).Select(r => r.NodeId!.Value).Distinct().ToList();

        // The stem and the choices, in the language asked for. A quality surface that cannot show
        // the question is a table of numbers nobody can act on.
        var localizations = await _dbContext.Questions
            .AsNoTracking()
            .Where(q => versionIds.Contains(q.ItemVersionId) && q.LangId == langId)
            .Select(q => new
            {
                q.ItemVersionId,
                q.Text,
                q.CorrectChoiceId,
                Choices = q.Choices.OrderBy(c => c.OrderIndex)
                    .Select(c => new { c.Id, c.Text }).ToList()
            })
            .ToListAsync(cancellationToken);

        var byVersion = localizations
            .GroupBy(l => l.ItemVersionId)
            .ToDictionary(
                g => g.Key,
                g => new Localized(
                    g.First().Text,
                    g.First().CorrectChoiceId,
                    [.. g.First().Choices.Select(c => new LocalizedChoice(c.Id, c.Text))]));

        var titles = await _dbContext.CurriculumNodeTranslations
            .AsNoTracking()
            .Where(t => nodeIds.Contains(t.NodeId) && t.LangId == langId)
            .ToDictionaryAsync(t => t.NodeId, t => t.Title, cancellationToken);

        // The corpus mean, so "too fast" means fast for this content rather than fast in the
        // abstract. A platform whose questions are all quick should not flag all of them.
        var timed = rows.Where(r => r.NElapsed > 0).ToList();
        var corpusMean = timed.Count > 0
            ? timed.Sum(r => (double)r.SumElapsedMs) / timed.Sum(r => r.NElapsed)
            : 0d;

        return [.. rows.Select(r => Build(r, byVersion, titles, corpusMean))];
    }

    private static ItemQualityDto Build(
        Row row,
        IReadOnlyDictionary<Guid, Localized> localizations,
        IReadOnlyDictionary<Guid, string> titles,
        double corpusMean)
    {
        localizations.TryGetValue(row.ItemVersionId, out var local);

        var counts = Counts(row.ChoiceFrequency);
        var totalPicks = counts.Values.Sum();

        var choices = (local?.Choices ?? [])
            .Select(c => new ChoiceShareDto
            {
                ChoiceId = c.Id,
                Text = c.Text,
                IsCorrect = local is not null && c.Id == local.CorrectChoiceId,
                Count = counts.GetValueOrDefault(c.Id.ToString("D")),
                Share = totalPicks > 0
                    ? (double)counts.GetValueOrDefault(c.Id.ToString("D")) / totalPicks
                    : 0d
            })
            .ToList();

        var facility = row.NFirstEncounter >= ItemStatisticsPopulations.MinimumForReporting
            ? (double?)row.NFirstEncounterCorrect / row.NFirstEncounter
            : null;

        var mean = row.NElapsed > 0 ? (double?)row.SumElapsedMs / row.NElapsed : null;

        return new ItemQualityDto
        {
            ItemId = row.ItemId,
            ItemVersionId = row.ItemVersionId,
            VersionNumber = row.VersionNumber,
            SourceKey = row.SourceKey,
            IsAnchor = row.IsAnchor,
            Stem = local?.Text,
            NodeId = row.NodeId,
            NodeTitle = row.NodeId is { } node ? titles.GetValueOrDefault(node) : null,
            NTotal = row.NTotal,
            NFirstEncounter = row.NFirstEncounter,
            Facility = facility,
            MeanElapsedMs = mean,
            Choices = choices,
            Flags = Flags(row, choices, facility, mean, corpusMean)
        };
    }

    /// <summary>
    /// What is worth an admin's attention about this item.
    /// <para>
    /// Every rule here is gated on having enough data, except <c>Unmapped</c>, which is a fact
    /// about the content rather than about the answers. **Flagging an item from six responses
    /// would be the same fabrication the mastery gate exists to prevent**, applied to questions
    /// instead of to children.
    /// </para>
    /// </summary>
    private static List<string> Flags(
        Row row, List<ChoiceShareDto> choices, double? facility, double? mean, double corpusMean)
    {
        var flags = new List<string>();

        if (!row.IsMapped) flags.Add(ItemQualityFlags.Unmapped);

        if (row.NFirstEncounter < ItemStatisticsPopulations.MinimumForReporting)
        {
            flags.Add(ItemQualityFlags.InsufficientData);
            return flags;
        }

        if (facility is { } f)
        {
            if (f >= TooEasyAbove) flags.Add(ItemQualityFlags.TooEasy);
            if (f <= TooHardBelow) flags.Add(ItemQualityFlags.TooHard);
        }

        var key = choices.FirstOrDefault(c => c.IsCorrect);
        var topWrong = choices.Where(c => !c.IsCorrect).OrderByDescending(c => c.Count).FirstOrDefault();

        // The single strongest signal of a wrong answer key there is: more children chose one
        // particular wrong answer than chose the one marked right.
        if (key is not null && topWrong is not null && topWrong.Count > key.Count)
            flags.Add(ItemQualityFlags.DistractorBeatsKey);

        if (choices.Count > 0 && choices.Any(c => !c.IsCorrect && c.Share < DeadDistractorBelow))
            flags.Add(ItemQualityFlags.DeadDistractor);

        if (mean is { } m && m < TooFastMs && corpusMean > 0 && m < corpusMean / 2)
            flags.Add(ItemQualityFlags.AnsweredTooFast);

        return flags;
    }

    private static Dictionary<string, int> Counts(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];

    /// <summary>
    /// How many items carry each flag. Computed over the whole bank rather than the current page,
    /// because a count that changes when you paginate is a count nobody can act on.
    /// </summary>
    private async Task<Dictionary<string, int>> FlagCountsAsync(CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(nodeId: null, cancellationToken);
        var built = await BuildAsync(rows, Domain.Constants.LanguageIds.English, cancellationToken);

        return built
            .SelectMany(i => i.Flags)
            .GroupBy(f => f)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
