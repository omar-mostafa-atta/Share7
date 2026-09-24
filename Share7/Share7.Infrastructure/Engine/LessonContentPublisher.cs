using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Content.Interfaces;
using Share7.Application.Engine;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Audit;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Domain.Structure;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine;

/// <inheritdoc cref="ILessonContentPublisher"/>
public sealed class LessonContentPublisher : ILessonContentPublisher
{
    private readonly ApplicationDbContext _db;
    private readonly IContentLanguages _languages;
    private readonly IItemIdentityMinter _minter;
    private readonly IAuditLog _audit;

    public LessonContentPublisher(
        ApplicationDbContext db, IContentLanguages languages, IItemIdentityMinter minter, IAuditLog audit)
    {
        _db = db;
        _languages = languages;
        _minter = minter;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ContentProblem>> CheckAsync(
        Guid lessonId,
        IReadOnlyList<ContentDraftItem> items,
        IReadOnlyList<ContentSetKey> covers,
        ContentRuleSet rules,
        CancellationToken cancellationToken = default)
    {
        var languages = await _languages.GetAsync(cancellationToken);
        var known = await KnownItemIdsAsync(lessonId, cancellationToken);
        return LessonContentRules.Check(Normalise(items), covers.Distinct().ToList(), rules, languages, known);
    }

    public async Task<ServiceResult<ContentPublishOutcome>> PublishAsync(
        ContentPublishRequest request, CancellationToken cancellationToken = default)
    {
        var lessonId = request.LessonId;

        var lessonIsLive = await _db.CurriculumNodes.AnyAsync(
            n => n.Id == lessonId && n.KindKey == NodeKinds.Lesson && n.RetiredAtUtc == null, cancellationToken);

        if (!lessonIsLive)
            return ServiceResult<ContentPublishOutcome>.Failure(
                EngineErrors.NodeNotFound, ServiceErrorKind.NotFound, "Lesson not found.");

        var covers = request.Covers.Distinct().ToList();
        if (covers.Count == 0)
            return Invalid([new("nothingCovered", "The publish names no pool and language to replace.")]);

        var languages = await _languages.GetAsync(cancellationToken);
        var items = Normalise(request.Items);

        await using var own = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Serialises publishes of one lesson. Without it two saves that both read version N both
        // write N+1, and whichever commits second silently replaces the first's questions — the
        // overwrite the whole draft layer exists to prevent. Held until the transaction ends.
        await LockLessonAsync(lessonId, cancellationToken);

        var current = await LessonContentReader.LoadActiveRowsAsync(_db, lessonId, cancellationToken);
        var sets = await _db.PublishedItemSets
            .Where(s => s.NodeId == lessonId)
            .ToListAsync(cancellationToken);

        if (request.ExpectedVersions is { Count: > 0 } expected)
        {
            var moved = expected
                .Select(e => new
                {
                    role = e.Key.Role,
                    langId = e.Key.LangId,
                    expected = e.Value,
                    found = sets.FirstOrDefault(s => s.Role == e.Key.Role && s.LangId == e.Key.LangId)?.Version ?? 0
                })
                .Where(e => e.expected != e.found)
                .ToList();

            if (moved.Count > 0)
            {
                return ServiceResult<ContentPublishOutcome>.Failure(
                    EngineErrors.ContentMoved,
                    ServiceErrorKind.Conflict,
                    "The lesson's live questions changed after this was prepared.",
                    new Dictionary<string, object?> { ["sets"] = moved });
            }
        }

        var known = await KnownItemIdsAsync(lessonId, cancellationToken);
        var problems = LessonContentRules.Check(items, covers, request.Rules, languages, known);
        if (problems.Count > 0)
            return Invalid(problems);

        var now = DateTime.UtcNow;
        var plan = Plan(items, covers, current);

        // ── versions: a set moves only if what the game receives for it changed ──────────
        var outcomes = new List<ContentSetOutcome>();
        var newVersions = new Dictionary<ContentSetKey, int>();

        foreach (var key in covers)
        {
            var before = Signature(current.Where(r => r.Role == key.Role && r.LangId == key.LangId)
                .Select(r => new Served(r.Id, r.RowNumber, r.Text, r.CorrectChoiceId, r.Choices)));

            var after = Signature(plan.Kept.Where(k => k.Row.Role == key.Role && k.Row.LangId == key.LangId)
                    .Select(k => new Served(k.Row.Id, k.RowNumber, k.Row.Text, k.Row.CorrectChoiceId, k.Row.Choices))
                .Concat(plan.New.Where(n => n.Role == key.Role && n.LangId == key.LangId)
                    .Select(n => n.Served)));

            var set = sets.FirstOrDefault(s => s.Role == key.Role && s.LangId == key.LangId);
            var previous = set?.Version ?? 0;
            var changed = !before.SequenceEqual(after);

            var version = changed ? previous + 1 : previous;
            newVersions[key] = version;
            outcomes.Add(new ContentSetOutcome(key.Role, key.LangId, previous, version, after.Count, changed));
        }

        // ── write ────────────────────────────────────────────────────────────────────────
        await RetireRowsAsync(plan.Retired, now, cancellationToken);
        await RepositionAsync(plan.Kept.Where(k => k.RowNumber != k.Row.RowNumber).ToList(), cancellationToken);

        var itemIds = await MintItemsAsync(lessonId, plan, now, cancellationToken);
        var versionIds = await MintVersionsAsync(plan, itemIds, now, cancellationToken);

        foreach (var row in plan.New)
            AddRendering(lessonId, row, itemIds[row.ItemKey], versionIds[row.ItemKey], newVersions[new(row.Role, row.LangId)], now);

        await RetireOrphanedVersionsAsync(plan, now, cancellationToken);
        await UpdateMappingsAsync(lessonId, plan, itemIds, now, cancellationToken);

        var legacyMain = await _db.LessonQuestionSets.Where(s => s.LessonId == lessonId).ToListAsync(cancellationToken);
        var legacyRecovery = await _db.LessonRecoveryQuestionSets.Where(s => s.LessonId == lessonId).ToListAsync(cancellationToken);

        foreach (var outcome in outcomes.Where(o => o.Changed))
            RecordSet(lessonId, outcome, sets, legacyMain, legacyRecovery, request, now);

        _audit.Record(new AuditEntry(
            AuditActions.QuestionsPublished,
            AuditAreas.Questions,
            Summarise(outcomes, languages),
            "lesson",
            lessonId.ToString(),
            new
            {
                path = request.AuditPath,
                rules = request.Rules.ToString(),
                source = request.Source.ToString(),
                releaseId = request.ReleaseId,
                sets = outcomes.Select(o => new
                {
                    pool = o.Role == NodeItemRole.Recovery ? "recovery" : "main",
                    langId = o.LangId,
                    from = o.PreviousVersion,
                    to = o.Version,
                    count = o.ItemCount
                }),
                newRows = plan.New.Count,
                keptRows = plan.Kept.Count,
                retiredRows = plan.Retired.Count
            }));

        await _db.SaveChangesAsync(cancellationToken);

        if (own is not null)
            await own.CommitAsync(cancellationToken);

        return ServiceResult<ContentPublishOutcome>.Success(new ContentPublishOutcome
        {
            LessonId = lessonId,
            Sets = outcomes,
            NewRows = plan.New.Count,
            KeptRows = plan.Kept.Count,
            RetiredRows = plan.Retired.Count
        });
    }

    // =====================================================================================
    // Planning — pure: what stays, what goes, what is written new
    // =====================================================================================

    private sealed record KeptRow(LessonContentReader.ActiveRow Row, int RowNumber);

    /// <param name="ItemKey">The existing item id, or a fresh key standing in for an item not yet minted.</param>
    private sealed record NewRow(
        Guid ItemKey,
        bool ItemIsNew,
        string? SourceKeyHint,
        NodeItemRole Role,
        Guid LangId,
        int RowNumber,
        Guid QuestionId,
        string Text,
        IReadOnlyList<ContentChoiceDto> Choices,
        Guid CorrectChoiceId)
    {
        public Served Served => new(QuestionId, RowNumber, Text, CorrectChoiceId, Choices);
    }

    private sealed record PublishPlan(
        List<KeptRow> Kept,
        List<NewRow> New,
        List<LessonContentReader.ActiveRow> Retired);

    private static PublishPlan Plan(
        IReadOnlyList<ContentDraftItem> items,
        IReadOnlyList<ContentSetKey> covers,
        List<LessonContentReader.ActiveRow> current)
    {
        var covered = covers.ToHashSet();
        var coveredRoles = covers.Select(c => c.Role).ToHashSet();

        var kept = new List<KeptRow>();
        var added = new List<NewRow>();
        var accounted = new HashSet<Guid>();

        foreach (var item in items.Where(i => coveredRoles.Contains(i.Role)))
        {
            var langs = covers.Where(c => c.Role == item.Role).Select(c => c.LangId).ToList();

            var existing = item.ItemId is { } id
                ? current.Where(r => r.ItemId == id && r.Role == item.Role).ToList()
                : [];

            var itemKey = item.ItemId ?? Guid.NewGuid();

            // **Judged language by language.** A rendering whose own text, answers and key did not
            // change is kept — same question id, same choice ids — even when another language of the
            // same question was reworded: fixing an Arabic typo must not hand every English reader a
            // new question id and a new version to download. Each rendering stays on the item
            // version whose content it shows; the reworded ones move to one new version, together.
            foreach (var langId in langs)
            {
                var served = existing.FirstOrDefault(r => r.LangId == langId);

                if (item.ItemId is not null && served is not null && Same(item.In(langId), served))
                {
                    kept.Add(new KeptRow(served, item.Order));
                    accounted.Add(served.Id);
                    continue;
                }

                // Changed, new, or no longer written in this language: whatever is served is left
                // unaccounted for, and so retired below; a new rendering is written when there is one.
                if (item.In(langId) is not { } rendering) continue;

                var choices = rendering.Choices.Select(text => new ContentChoiceDto(Guid.NewGuid(), text)).ToList();

                added.Add(new NewRow(
                    itemKey,
                    ItemIsNew: item.ItemId is null,
                    item.SourceKeyHint,
                    item.Role,
                    langId,
                    item.Order,
                    Guid.NewGuid(),
                    rendering.Text,
                    choices,
                    choices[rendering.CorrectIndex].Id));
            }
        }

        // Whatever the covered sets serve today and the desired state did not keep.
        var retired = current
            .Where(r => covered.Contains(new ContentSetKey(r.Role, r.LangId)) && !accounted.Contains(r.Id))
            .ToList();

        return new PublishPlan(kept, added, retired);
    }

    /// <summary>
    /// Whether a desired rendering is exactly what is served now: same question text, same answers in
    /// the same order, same one marked correct. Anything else is a change the game must re-download.
    /// </summary>
    private static bool Same(ContentDraftRendering? desired, LessonContentReader.ActiveRow? served)
    {
        if (desired is null || served is null)
            return desired is null && served is null;

        return desired.Text == served.Text
               && desired.CorrectIndex == served.CorrectIndex
               && desired.Choices.SequenceEqual(served.Choices.Select(c => c.Text), StringComparer.Ordinal);
    }

    /// <summary>One entry of what the game receives for a set, in the order it receives them.</summary>
    private sealed record Served(
        Guid Id, int RowNumber, string Text, Guid CorrectChoiceId, IReadOnlyList<ContentChoiceDto> Choices);

    /// <summary>
    /// What the game receives for a set, reduced to comparable strings. Row numbers only decide the
    /// order and are not themselves sent, so renumbering alone changes nothing.
    /// </summary>
    private static List<string> Signature(IEnumerable<Served> served) =>
        served
            .OrderBy(s => s.RowNumber)
            .ThenBy(s => s.Id)
            .Select(s => $"{s.Id:N}|{s.Text}|{s.CorrectChoiceId:N}|{string.Join("|", s.Choices.Select(c => $"{c.Id:N}:{c.Text}"))}")
            .ToList();

    private static List<ContentDraftItem> Normalise(IReadOnlyList<ContentDraftItem> items) =>
        items.Select(i => i with
        {
            Renderings = (i.Renderings ?? [])
                .Select(r => r with
                {
                    Text = (r.Text ?? string.Empty).Trim(),
                    Choices = (r.Choices ?? []).Select(c => (c ?? string.Empty).Trim()).ToList()
                })
                .ToList()
        }).ToList();

    // =====================================================================================
    // Writing
    // =====================================================================================

    private async Task LockLessonAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var resource = $"share7.lesson-content/{lessonId:N}";

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                                         @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, 'Another publish of this lesson did not finish in time.', 1;
            """, cancellationToken);
    }

    /// <summary>Retired, never deleted: progress and evidence name these rows. Both tables of the recovery pool.</summary>
    private async Task RetireRowsAsync(List<LessonContentReader.ActiveRow> rows, DateTime now, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        var ids = rows.Select(r => r.Id).ToList();

        await _db.Set<Question>().IgnoreQueryFilters()
            .Where(q => ids.Contains(q.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        var recoveryIds = rows.Where(r => r.Role == NodeItemRole.Recovery).Select(r => r.Id).ToList();
        if (recoveryIds.Count > 0)
        {
            await _db.RecoveryQuestions
                .Where(q => recoveryIds.Contains(q.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                    cancellationToken);
        }
    }

    /// <summary>
    /// A kept row that moved position. Its content is untouched — only the order it is served in —
    /// so it keeps its id and is updated in place.
    /// </summary>
    private async Task RepositionAsync(List<KeptRow> moved, CancellationToken cancellationToken)
    {
        foreach (var kept in moved)
        {
            await _db.Set<Question>().IgnoreQueryFilters()
                .Where(q => q.Id == kept.Row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.RowNumber, kept.RowNumber), cancellationToken);

            if (kept.Row.Role == NodeItemRole.Recovery)
            {
                await _db.RecoveryQuestions
                    .Where(q => q.Id == kept.Row.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(q => q.RowNumber, kept.RowNumber), cancellationToken);
            }
        }
    }

    /// <summary>Creates the items the plan introduces. Returns the real id for every item key in the plan.</summary>
    private async Task<Dictionary<Guid, Guid>> MintItemsAsync(
        Guid lessonId, PublishPlan plan, DateTime now, CancellationToken cancellationToken)
    {
        var ids = plan.New.Where(n => !n.ItemIsNew).Select(n => n.ItemKey).Distinct().ToDictionary(k => k, k => k);

        var fresh = plan.New.Where(n => n.ItemIsNew).GroupBy(n => n.ItemKey).Select(g => g.First()).ToList();
        if (fresh.Count == 0) return ids;

        var hints = fresh.Where(n => n.SourceKeyHint is not null).Select(n => n.SourceKeyHint!).Distinct().ToList();
        var taken = hints.Count == 0
            ? []
            : (await _db.Items.Where(i => hints.Contains(i.SourceKey)).Select(i => i.SourceKey).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

        foreach (var row in fresh)
        {
            var id = Guid.NewGuid();

            // The hint keeps the old admin paths' lineage (lesson + pool + row number). Anything else
            // — every Studio question — gets a key that can never collide with one.
            var sourceKey = row.SourceKeyHint is { } hint && taken.Add(hint)
                ? hint
                : $"lesson/{lessonId:D}/{row.Role.ToString().ToLowerInvariant()}/item-{id:N}";

            _db.Items.Add(new Item
            {
                Id = id,
                ItemBankId = Domain.Constants.ContentIds.PlatformCurriculumBank,
                SourceKey = sourceKey,
                IsAnchor = false,
                CreatedAtUtc = now
            });

            ids[row.ItemKey] = id;
        }

        return ids;
    }

    /// <summary>One new, immutable version per changed or new item, shared by all its new renderings.</summary>
    private async Task<Dictionary<Guid, Guid>> MintVersionsAsync(
        PublishPlan plan, Dictionary<Guid, Guid> itemIds, DateTime now, CancellationToken cancellationToken)
    {
        var keys = plan.New.Select(n => n.ItemKey).Distinct().ToList();
        var existingIds = plan.New.Where(n => !n.ItemIsNew).Select(n => n.ItemKey).Distinct().ToList();

        var highest = existingIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.ItemVersions
                .Where(v => existingIds.Contains(v.ItemId))
                .GroupBy(v => v.ItemId)
                .Select(g => new { g.Key, Max = g.Max(v => v.VersionNumber) })
                .ToDictionaryAsync(x => x.Key, x => x.Max, cancellationToken);

        var versionIds = new Dictionary<Guid, Guid>();

        foreach (var key in keys)
        {
            var itemId = itemIds[key];
            var version = new ItemVersion
            {
                Id = Guid.NewGuid(),
                ItemId = itemId,
                VersionNumber = highest.GetValueOrDefault(itemId) + 1,
                ItemKindKey = ItemKinds.SingleChoice,

                // False, always, until a surface exists to ask the author at publish time. A wrong
                // true silently pools statistics across a rewritten question; a wrong false only
                // restarts them. Only one of those is recoverable (§10.1).
                PsychometricContinuity = false,
                CreatedAtUtc = now
            };

            _db.ItemVersions.Add(version);
            versionIds[key] = version.Id;
        }

        return versionIds;
    }

    private void AddRendering(Guid lessonId, NewRow row, Guid itemId, Guid itemVersionId, int setVersion, DateTime now)
    {
        var question = new Question
        {
            Id = row.QuestionId,
            ItemVersionId = itemVersionId,
            LessonId = lessonId,
            Role = row.Role,
            LangId = row.LangId,
            Text = row.Text,
            CorrectChoiceId = row.CorrectChoiceId,
            Version = setVersion,
            IsActive = true,
            RowNumber = row.RowNumber,
            CreatedAt = now,
            Choices = row.Choices
                .Select((c, index) => new QuestionChoice { Id = c.Id, QuestionId = row.QuestionId, Text = c.Text, OrderIndex = index })
                .ToList()
        };

        _db.Questions.Add(question);

        // The recovery pool's compatibility copy: the same row, under the same ids, in the table the
        // readers not yet moved still read.
        if (row.Role == NodeItemRole.Recovery)
        {
            _db.RecoveryQuestions.Add(new RecoveryQuestion
            {
                Id = row.QuestionId,
                LessonId = lessonId,
                LangId = row.LangId,
                Text = row.Text,
                CorrectChoiceId = row.CorrectChoiceId,
                Version = setVersion,
                IsActive = true,
                RowNumber = row.RowNumber,
                CreatedAt = now,
                Choices = row.Choices
                    .Select((c, index) => new RecoveryQuestionChoice { Id = c.Id, RecoveryQuestionId = row.QuestionId, Text = c.Text, OrderIndex = index })
                    .ToList()
            });
        }
    }

    /// <summary>
    /// A version with nothing left serving it is marked retired. Never deleted — responses name it —
    /// and never retired while any rendering (another language on the one-language paths) still
    /// points at it.
    /// </summary>
    private async Task RetireOrphanedVersionsAsync(PublishPlan plan, DateTime now, CancellationToken cancellationToken)
    {
        var stillServing = plan.Kept.Select(k => k.Row.ItemVersionId).ToHashSet();
        var candidates = plan.Retired.Select(r => r.ItemVersionId).Where(id => !stillServing.Contains(id)).Distinct().ToList();

        if (candidates.Count == 0) return;

        // Other lessons' rows are not in the plan; a version shared outside this lesson stays live.
        var elsewhere = await _db.ItemLocalizations
            .Where(q => q.IsActive && candidates.Contains(q.ItemVersionId))
            .Select(q => q.ItemVersionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var retire = candidates.Except(elsewhere).ToList();
        if (retire.Count == 0) return;

        await _db.ItemVersions
            .Where(v => retire.Contains(v.Id) && v.RetiredAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.RetiredAtUtc, now), cancellationToken);
    }

    /// <summary>
    /// Keeps "which items this lesson uses, in which pool" in step: a mapping for anything newly
    /// served, reopened if it had been closed; closed (never deleted) for an item the lesson stopped
    /// serving in that pool; and the item itself marked retired when nothing serves it any more.
    /// Main-pool items are pointed at the lesson's placeholder learning target, as every importer
    /// has always done — recovery items never are.
    /// </summary>
    private async Task UpdateMappingsAsync(
        Guid lessonId, PublishPlan plan, Dictionary<Guid, Guid> itemIds, DateTime now, CancellationToken cancellationToken)
    {
        var served = plan.New.Select(n => (ItemId: itemIds[n.ItemKey], n.Role))
            .Concat(plan.Kept.Select(k => (k.Row.ItemId, k.Row.Role)))
            .ToHashSet();

        var touched = served.Select(s => s.ItemId)
            .Concat(plan.Retired.Select(r => r.ItemId))
            .Distinct()
            .ToList();

        if (touched.Count == 0) return;

        var mappings = await _db.NodeItemMappings
            .Where(m => m.NodeId == lessonId && touched.Contains(m.ItemId))
            .ToListAsync(cancellationToken);

        var alreadyMain = mappings.Where(m => m.Role == NodeItemRole.Core).Select(m => m.ItemId).ToHashSet();

        foreach (var (itemId, role) in served)
        {
            var mapping = mappings.FirstOrDefault(m => m.ItemId == itemId && m.Role == role);
            var order = plan.New.FirstOrDefault(n => itemIds[n.ItemKey] == itemId && n.Role == role)?.RowNumber
                        ?? plan.Kept.First(k => k.Row.ItemId == itemId && k.Row.Role == role).RowNumber;

            if (mapping is null)
            {
                _db.NodeItemMappings.Add(new NodeItemMapping
                {
                    Id = Guid.NewGuid(),
                    CurriculumVersionId = Domain.Constants.EducationIds.EgyptianNationalAsMigrated,
                    NodeId = lessonId,
                    ItemId = itemId,
                    Role = role,
                    Order = order,
                    CreatedAtUtc = now
                });
            }
            else
            {
                mapping.RemovedAtUtc = null;
                mapping.Order = order;
            }
        }

        // Anything the lesson still serves in another pool or language keeps its mapping there.
        var stillActive = (await _db.ItemLocalizations
                .Where(q => q.LessonId == lessonId && q.IsActive && touched.Contains(q.ItemVersion!.ItemId))
                .Select(q => new { q.ItemVersion!.ItemId, q.Role })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(x => (x.ItemId, x.Role))
            .ToHashSet();

        foreach (var mapping in mappings.Where(m => m.RemovedAtUtc == null))
        {
            if (!served.Contains((mapping.ItemId, mapping.Role)) && !stillActive.Contains((mapping.ItemId, mapping.Role)))
                mapping.RemovedAtUtc = now;
        }

        var items = await _db.Items.Where(i => touched.Contains(i.Id)).ToListAsync(cancellationToken);
        var anyServing = served.Select(s => s.ItemId).Concat(stillActive.Select(s => s.ItemId)).ToHashSet();

        foreach (var item in items)
            item.RetiredAtUtc = anyServing.Contains(item.Id) ? null : item.RetiredAtUtc ?? now;

        // Only items entering the main pool: one already there was mapped when it first arrived.
        foreach (var row in plan.New.Where(n => n.Role == NodeItemRole.Core).DistinctBy(n => n.ItemKey))
        {
            var itemId = itemIds[row.ItemKey];
            if (row.ItemIsNew || !alreadyMain.Contains(itemId))
                await _minter.EnsureLessonTargetAsync(lessonId, itemId, row.ItemIsNew, now, cancellationToken);
        }
    }

    /// <summary>The set's new version, its history row, and both compatibility copies of each.</summary>
    private void RecordSet(
        Guid lessonId,
        ContentSetOutcome outcome,
        List<PublishedItemSet> sets,
        List<LessonQuestionSet> legacyMain,
        List<LessonRecoveryQuestionSet> legacyRecovery,
        ContentPublishRequest request,
        DateTime now)
    {
        var set = sets.FirstOrDefault(s => s.Role == outcome.Role && s.LangId == outcome.LangId);
        if (set is null)
        {
            set = new PublishedItemSet { NodeId = lessonId, Role = outcome.Role, LangId = outcome.LangId };
            _db.PublishedItemSets.Add(set);
            sets.Add(set);
        }

        set.Version = outcome.Version;
        set.ItemCount = outcome.ItemCount;
        set.UpdatedAtUtc = now;
        set.UpdatedByUserId = request.ActorUserId;

        var fileName = request.FileName.Length > 260 ? request.FileName[..260] : request.FileName;

        _db.ContentPublications.Add(new ContentPublication
        {
            Id = Guid.NewGuid(),
            NodeId = lessonId,
            Role = outcome.Role,
            LangId = outcome.LangId,
            Version = outcome.Version,
            Source = request.Source,
            FileName = fileName,
            ItemCount = outcome.ItemCount,
            PublishedByUserId = request.ActorUserId,
            PublishedAtUtc = now,
            ReleaseId = request.ReleaseId
        });

        if (outcome.Role == NodeItemRole.Core)
        {
            var legacy = legacyMain.FirstOrDefault(s => s.LangId == outcome.LangId);

            if (legacy is null)
                _db.LessonQuestionSets.Add(new LessonQuestionSet { LessonId = lessonId, LangId = outcome.LangId, Version = outcome.Version });
            else
                legacy.Version = outcome.Version;

            _db.LessonQuestionUploads.Add(new LessonQuestionUpload
            {
                Id = Guid.NewGuid(),
                LessonId = lessonId,
                LangId = outcome.LangId,
                Version = outcome.Version,
                FileName = fileName,
                Source = request.Source,
                QuestionCount = outcome.ItemCount,
                UploadedByUserId = request.ActorUserId,
                UploadedAt = now
            });
        }
        else if (outcome.Role == NodeItemRole.Recovery)
        {
            var legacy = legacyRecovery.FirstOrDefault(s => s.LangId == outcome.LangId);

            if (legacy is null)
                _db.LessonRecoveryQuestionSets.Add(new LessonRecoveryQuestionSet { LessonId = lessonId, LangId = outcome.LangId, Version = outcome.Version });
            else
                legacy.Version = outcome.Version;

            _db.LessonRecoveryQuestionUploads.Add(new LessonRecoveryQuestionUpload
            {
                Id = Guid.NewGuid(),
                LessonId = lessonId,
                LangId = outcome.LangId,
                Version = outcome.Version,
                FileName = fileName,
                Source = request.Source,
                QuestionCount = outcome.ItemCount,
                UploadedByUserId = request.ActorUserId,
                UploadedAt = now
            });
        }
    }

    private async Task<HashSet<Guid>> KnownItemIdsAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var mapped = await _db.NodeItemMappings
            .Where(m => m.NodeId == lessonId)
            .Select(m => m.ItemId)
            .ToListAsync(cancellationToken);

        var rendered = await _db.ItemLocalizations
            .Where(q => q.LessonId == lessonId)
            .Select(q => q.ItemVersion!.ItemId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. mapped, .. rendered];
    }

    private static ServiceResult<ContentPublishOutcome> Invalid(IReadOnlyList<ContentProblem> problems) =>
        ServiceResult<ContentPublishOutcome>.Failure(
            EngineErrors.ContentInvalid,
            ServiceErrorKind.Validation,
            problems.Count == 1 ? problems[0].Message : $"{problems.Count} problems stop this from being published.",
            new Dictionary<string, object?> { ["problems"] = problems });

    private static string Summarise(List<ContentSetOutcome> outcomes, IReadOnlyList<ContentLanguage> languages)
    {
        var changed = outcomes.Where(o => o.Changed).ToList();
        if (changed.Count == 0)
            return "Published a lesson's questions; nothing the game receives changed.";

        var parts = changed.Select(o =>
        {
            var code = languages.FirstOrDefault(l => l.Id == o.LangId)?.Code.ToUpperInvariant() ?? "?";
            var pool = o.Role == NodeItemRole.Recovery ? "recovery" : "main";
            return $"{pool} {code} v{o.PreviousVersion}→v{o.Version} ({o.ItemCount})";
        });

        return $"Published a lesson's questions: {string.Join(", ", parts)}.";
    }
}
