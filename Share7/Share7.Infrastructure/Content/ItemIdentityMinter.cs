using Microsoft.EntityFrameworkCore;
using Share7.Application.Content.Interfaces;
using Share7.Domain.Competency;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Structure;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Content;

/// <inheritdoc cref="IItemIdentityMinter"/>
public class ItemIdentityMinter : IItemIdentityMinter
{
    private readonly ApplicationDbContext _dbContext;

    /// <summary>
    /// Within one publish the same sheet row is resolved twice, once per language, and the two
    /// calls must return the same version. The change tracker cannot answer that on its own —
    /// a query does not see rows that have been added but not saved — so the unit of work keeps
    /// its own map. Scoped alongside the DbContext, so it lives exactly as long as one.
    /// </summary>
    private readonly Dictionary<string, Item> _itemsBySourceKey = [];
    private readonly Dictionary<(Guid ItemId, int Version), ItemVersion> _versions = [];
    private readonly Dictionary<Guid, LearningTarget> _targetsByLesson = [];

    public ItemIdentityMinter(ApplicationDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// The item's lineage key. <c>(LessonId, Role, RowNumber)</c> is the identity that already
    /// existed in the data unpromoted — the importer has always written the English and Arabic
    /// rows of one sheet row with the same row number, which is what makes the migration exact
    /// rather than a guess.
    /// </summary>
    public static string SourceKeyFor(Guid lessonId, NodeItemRole role, int rowNumber) =>
        $"lesson/{lessonId:D}/{role.ToString().ToLowerInvariant()}/{rowNumber}";

    public async Task<ItemVersion> ResolveForLessonRowAsync(
        Guid lessonId,
        int rowNumber,
        int versionNumber,
        NodeItemRole role,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var (item, isNew) = await ResolveItemAsync(lessonId, rowNumber, role, nowUtc, cancellationToken);
        var version = await ResolveVersionAsync(item, versionNumber, isNew, nowUtc, cancellationToken);

        // An item minted a moment ago cannot already have mappings, so the existence checks are
        // skipped for it. That is not a micro-optimisation: bulk seeding publishes thousands of
        // rows in one unit of work, and six round trips per row turns a seed into a coffee break.
        await EnsureNodeMappingAsync(lessonId, item, role, rowNumber, isNew, nowUtc, cancellationToken);
        await EnsureTargetMappingAsync(lessonId, item, isNew, nowUtc, cancellationToken);

        return version;
    }

    private async Task<(Item Item, bool IsNew)> ResolveItemAsync(
        Guid lessonId, int rowNumber, NodeItemRole role, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var sourceKey = SourceKeyFor(lessonId, role, rowNumber);

        if (_itemsBySourceKey.TryGetValue(sourceKey, out var cached))
            return (cached, false);

        // **This lookup is the lineage link.** Republishing a lesson used to mint a stranger:
        // new GUIDs, no relationship to the rows they replaced, and an item's entire history
        // severed at every content edit. Finding the existing item by its source key is what
        // turns a republish into a new version of the same question.
        var existing = await _dbContext.Items
            .FirstOrDefaultAsync(i => i.SourceKey == sourceKey, cancellationToken);

        if (existing is not null)
        {
            _itemsBySourceKey[sourceKey] = existing;
            return (existing, false);
        }

        var item = new Item
        {
            Id = Guid.NewGuid(),
            ItemBankId = ContentIds.PlatformCurriculumBank,
            SourceKey = sourceKey,
            IsAnchor = false,
            CreatedAtUtc = nowUtc
        };

        _dbContext.Items.Add(item);
        _itemsBySourceKey[sourceKey] = item;
        return (item, true);
    }

    private async Task<ItemVersion> ResolveVersionAsync(
        Item item, int versionNumber, bool itemIsNew, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (_versions.TryGetValue((item.Id, versionNumber), out var cached))
            return cached;

        if (!itemIsNew)
        {
            var existing = await _dbContext.ItemVersions
                .FirstOrDefaultAsync(
                    v => v.ItemId == item.Id && v.VersionNumber == versionNumber, cancellationToken);

            if (existing is not null)
            {
                _versions[(item.Id, versionNumber)] = existing;
                return existing;
            }

            // Every earlier version is retired, not edited. A response against one stays readable
            // forever; what changes is which version is current.
            await _dbContext.ItemVersions
                .Where(v => v.ItemId == item.Id && v.VersionNumber < versionNumber && v.RetiredAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.RetiredAtUtc, nowUtc), cancellationToken);
        }

        var version = new ItemVersion
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            Item = item,
            VersionNumber = versionNumber,
            ItemKindKey = ItemKinds.SingleChoice,

            // False, always, until an admin surface exists to ask the question at publish time.
            // A wrong `true` silently corrupts every measurement built on the item; a wrong
            // `false` merely restarts its statistics. Only one of those is recoverable. §10.1.
            PsychometricContinuity = false,

            CreatedAtUtc = nowUtc
        };

        _dbContext.ItemVersions.Add(version);
        _versions[(item.Id, versionNumber)] = version;
        return version;
    }

    /// <summary>
    /// Records that this item is used at this node, in this role. The node id is the lesson id —
    /// the projection preserves legacy GUIDs, so no lookup is needed and none can go stale.
    /// </summary>
    private async Task EnsureNodeMappingAsync(
        Guid lessonId, Item item, NodeItemRole role, int order, bool itemIsNew, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var exists = !itemIsNew
            && (_dbContext.NodeItemMappings.Local.Any(
                    m => m.NodeId == lessonId && m.ItemId == item.Id && m.Role == role)
                || await _dbContext.NodeItemMappings.AnyAsync(
                    m => m.NodeId == lessonId && m.ItemId == item.Id && m.Role == role, cancellationToken));

        if (exists) return;

        _dbContext.NodeItemMappings.Add(new NodeItemMapping
        {
            Id = Guid.NewGuid(),
            CurriculumVersionId = EducationIds.EgyptianNationalAsMigrated,
            NodeId = lessonId,
            ItemId = item.Id,
            Role = role,
            Order = order,
            CreatedAtUtc = nowUtc
        });
    }

    /// <summary>
    /// Points the item at the lesson's placeholder target, creating the target if this lesson has
    /// never had one.
    /// <para>
    /// **A question mapped to nothing is a question that can never be measured**, so this is not
    /// optional and not deferred to an authoring pass that may never come. The target is a
    /// placeholder and says so: it claims no more than "the items in this lesson", is excluded from
    /// every aggregation and every exam projection, and is replaced wholesale when real targets are
    /// authored — at which point this mapping is an UPDATE and the historical observations are
    /// regenerated from the immutable responses (§20.5).
    /// </para>
    /// </summary>
    private async Task EnsureTargetMappingAsync(
        Guid lessonId, Item item, bool itemIsNew, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var target = await ResolveLessonTargetAsync(lessonId, nowUtc, cancellationToken);

        var exists = !itemIsNew
            && (_dbContext.ItemTargetMappings.Local.Any(
                    m => m.ItemId == item.Id && m.TargetId == target.Id)
                || await _dbContext.ItemTargetMappings.AnyAsync(
                    m => m.ItemId == item.Id && m.TargetId == target.Id, cancellationToken));

        if (exists) return;

        var hasPrimary = !itemIsNew
            && (_dbContext.ItemTargetMappings.Local.Any(m => m.ItemId == item.Id && m.IsPrimary)
                || await _dbContext.ItemTargetMappings.AnyAsync(
                    m => m.ItemId == item.Id && m.IsPrimary, cancellationToken));

        _dbContext.ItemTargetMappings.Add(new ItemTargetMapping
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            TargetId = target.Id,
            Emphasis = 1.0m,
            IsPrimary = !hasPrimary,
            CreatedAtUtc = nowUtc
        });
    }

    private async Task<LearningTarget> ResolveLessonTargetAsync(
        Guid lessonId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (_targetsByLesson.TryGetValue(lessonId, out var cached))
            return cached;

        var targetKey = PlaceholderTargetKeyFor(lessonId);

        var existing = await _dbContext.LearningTargets.FirstOrDefaultAsync(
            t => t.FrameworkId == EducationIds.PlaceholderFramework && t.TargetKey == targetKey,
            cancellationToken);

        if (existing is not null)
        {
            await EnsureNodeMappingAsync(lessonId, existing.Id, nowUtc, cancellationToken);
            _targetsByLesson[lessonId] = existing;
            return existing;
        }

        var target = new LearningTarget
        {
            Id = Guid.NewGuid(),
            FrameworkId = EducationIds.PlaceholderFramework,
            TargetKey = targetKey,
            TargetKindKey = TargetKinds.LessonPlaceholder,
            IsPlaceholder = true,
            ReviewState = TargetReviewState.Unreviewed,
            CreatedAtUtc = nowUtc
        };

        _dbContext.LearningTargets.Add(target);

        // The statement is the lesson's own name, in each language the lesson has one. That is
        // honest about what the target is: it is the lesson, typed as a target, and the wording
        // should not pretend a specialist wrote it.
        var titles = await _dbContext.LessonTranslations
            .Where(t => t.LessonId == lessonId)
            .Select(t => new { t.LangId, t.Name })
            .ToListAsync(cancellationToken);

        foreach (var title in titles)
        {
            _dbContext.LearningTargetTranslations.Add(new LearningTargetTranslation
            {
                TargetId = target.Id,
                LangId = title.LangId,
                Statement = title.Name
            });
        }

        await EnsureNodeMappingAsync(lessonId, target.Id, nowUtc, cancellationToken);

        _targetsByLesson[lessonId] = target;
        return target;
    }

    /// <summary>
    /// Records that this lesson teaches this target — "what does this lesson actually teach",
    /// machine-readable.
    /// <para>
    /// **Written here because this is the only moment both facts are known.** The Phase 1 backfill
    /// wrote one of these per migrated lesson and then nothing wrote another, so every lesson
    /// created since would have had a placeholder target and no statement anywhere that the lesson
    /// taught it. Nothing read the table until coverage reporting did, which is exactly how a gap
    /// like this survives: it costs nothing until the first reader arrives, and then it costs a
    /// silently empty report.
    /// </para>
    /// </summary>
    private async Task EnsureNodeMappingAsync(
        Guid lessonId, Guid targetId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var exists =
            _dbContext.NodeTargetMappings.Local.Any(m => m.NodeId == lessonId && m.TargetId == targetId)
            || await _dbContext.NodeTargetMappings.AnyAsync(
                m => m.NodeId == lessonId && m.TargetId == targetId, cancellationToken);

        if (exists) return;

        _dbContext.NodeTargetMappings.Add(new NodeTargetMapping
        {
            Id = Guid.NewGuid(),
            CurriculumVersionId = EducationIds.EgyptianNationalAsMigrated,

            // The lesson's own id. Node ids are preserved from the legacy tree, so this resolves
            // whether the reader walks the typed tables or the node projection.
            NodeId = lessonId,
            TargetId = targetId,
            CreatedAtUtc = nowUtc
        });
    }

    /// <summary>
    /// The placeholder target key for a lesson. Shared with the migration's bootstrap so the two
    /// cannot mint two targets for one lesson.
    /// </summary>
    public static string PlaceholderTargetKeyFor(Guid lessonId) => $"lesson/{lessonId:D}";
}
