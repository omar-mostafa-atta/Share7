using Share7.Domain.Content;
using Share7.Domain.Curriculum;

namespace Share7.Application.Engine.Models;

// ===========================================================================
// A lesson's content, as the engine sees it
//
// Language-generic: an item is one question, in one pool, at one position, with a rendering per
// language. The old shapes — the paired EN/AR sheet row, the one-language question list — are
// adapters over this, not the other way round, so a third content language is a row in
// Languages rather than a new set of columns.
// ===========================================================================

/// <summary>One pool of one lesson in one language: the unit the game caches and versions.</summary>
public readonly record struct ContentSetKey(NodeItemRole Role, Guid LangId);

/// <summary>What is live for a lesson: every served set's version, and every item currently served.</summary>
public sealed record LessonContentDto
{
    public required Guid LessonId { get; init; }

    /// <summary>Every (pool, language) that has ever been published, with its current version.</summary>
    public required IReadOnlyList<ContentSetDto> Sets { get; init; }

    /// <summary>The items served now, main pool first, each in position order.</summary>
    public required IReadOnlyList<ContentItemDto> Items { get; init; }

    public int VersionOf(NodeItemRole role, Guid langId) =>
        Sets.FirstOrDefault(s => s.Role == role && s.LangId == langId)?.Version ?? 0;
}

public sealed record ContentSetDto(
    NodeItemRole Role, Guid LangId, int Version, int ItemCount, DateTime UpdatedAtUtc);

public sealed record ContentItemDto
{
    public required Guid ItemId { get; init; }
    public required NodeItemRole Role { get; init; }

    /// <summary>1-based position within the pool. The order the game serves the pool in.</summary>
    public required int Order { get; init; }

    /// <summary>One per language the item is served in. A language missing here is not served.</summary>
    public required IReadOnlyList<ContentRenderingDto> Renderings { get; init; }

    public ContentRenderingDto? In(Guid langId) => Renderings.FirstOrDefault(r => r.LangId == langId);
}

/// <param name="QuestionId">The live row's id — what the game names in an attempt.</param>
/// <param name="Choices">In stored order; the game shuffles before assigning lanes.</param>
/// <param name="CorrectIndex">Which of <paramref name="Choices"/> is right.</param>
public sealed record ContentRenderingDto(
    Guid LangId,
    Guid QuestionId,
    Guid ItemVersionId,
    string Text,
    IReadOnlyList<ContentChoiceDto> Choices,
    int CorrectIndex);

public sealed record ContentChoiceDto(Guid Id, string Text);

// ---------------------------------------------------------------------------
// The desired state a publish (or a Studio draft) proposes
// ---------------------------------------------------------------------------

/// <summary>
/// One item as it should be after a publish.
/// <para>
/// <see cref="ItemId"/> names an item the lesson already has — which is how an unchanged question
/// keeps its identity, and its question id, across publishes. Leave it null for a new question.
/// </para>
/// </summary>
public sealed record ContentDraftItem
{
    public Guid? ItemId { get; init; }

    /// <summary>
    /// For a new item: the lineage key an old admin path would have used
    /// (<c>lesson/{id}/core/{row}</c>), so a question deleted and re-added at the same row is
    /// recognised as the same item, as it always was. Ignored when <see cref="ItemId"/> is set.
    /// </summary>
    public string? SourceKeyHint { get; init; }

    public NodeItemRole Role { get; init; } = NodeItemRole.Core;

    /// <summary>1-based position within the pool.</summary>
    public int Order { get; init; }

    public IReadOnlyList<ContentDraftRendering> Renderings { get; init; } = [];

    public ContentDraftRendering? In(Guid langId) => Renderings.FirstOrDefault(r => r.LangId == langId);
}

/// <param name="Choices">Exactly three today: the game has three lanes.</param>
public sealed record ContentDraftRendering(
    Guid LangId,
    string Text,
    IReadOnlyList<string> Choices,
    int CorrectIndex);

/// <summary>
/// Which rules a publish is held to.
/// <para>
/// **The old admin paths keep their own rules until cutover** (decided 22 Sep 2026): the
/// one-language paths can publish one language at a time and need no recovery pool, and the lesson
/// sheet insists on a recovery question. The Studio holds every lesson to the full rules. All
/// three share the per-question rules (three different answers, the length limits).
/// </para>
/// </summary>
public enum ContentRuleSet
{
    /// <summary>One language, one pool, as the per-language upload and hand entry always allowed.</summary>
    SingleLanguage = 0,

    /// <summary>The paired English/Arabic sheet: both languages per row, and a recovery question.</summary>
    LessonSheet = 1,

    /// <summary>
    /// Every language marked required-to-publish filled in for every question, a recovery question
    /// whenever there are main questions, and the per-question rules everywhere.
    /// </summary>
    Studio = 2,

    /// <summary>
    /// Putting back content that was live before — a rollback. Only the per-question rules: what is
    /// being restored was published once already, under whatever rules applied then, and refusing
    /// to put it back would leave the release that replaced it impossible to undo.
    /// </summary>
    Restore = 3
}

public sealed record ContentPublishRequest
{
    public required Guid LessonId { get; init; }

    /// <summary>The desired state of the sets this publish covers. Anything outside them is ignored.</summary>
    public required IReadOnlyList<ContentDraftItem> Items { get; init; }

    /// <summary>
    /// The (pool, language) sets this publish replaces. A set not listed is left exactly as it is —
    /// publishing English does not touch Arabic.
    /// </summary>
    public required IReadOnlyList<ContentSetKey> Covers { get; init; }

    public required ContentRuleSet Rules { get; init; }

    public QuestionSetSource Source { get; init; } = QuestionSetSource.ManualEntry;

    public string FileName { get; init; } = string.Empty;

    public Guid? ActorUserId { get; init; }

    /// <summary>The Studio release doing this publish, when one is.</summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>
    /// The versions the caller last saw. When given, a set that has moved since refuses the whole
    /// publish — how a release notices that live content changed after its draft was reviewed.
    /// </summary>
    public IReadOnlyDictionary<ContentSetKey, int>? ExpectedVersions { get; init; }

    /// <summary>Which door the publish came through, for the audit record.</summary>
    public string AuditPath { get; init; } = "engine";
}

public sealed record ContentPublishOutcome
{
    public required Guid LessonId { get; init; }

    public required IReadOnlyList<ContentSetOutcome> Sets { get; init; }

    /// <summary>Renderings written new this publish.</summary>
    public int NewRows { get; init; }

    /// <summary>Renderings carried into the new version unchanged — same id, nothing re-downloaded for them.</summary>
    public int KeptRows { get; init; }

    /// <summary>Renderings taken out of service (retired, never deleted).</summary>
    public int RetiredRows { get; init; }

    public bool ChangedAnything => Sets.Any(s => s.Changed);

    public ContentSetOutcome? For(NodeItemRole role, Guid langId) =>
        Sets.FirstOrDefault(s => s.Role == role && s.LangId == langId);
}

/// <param name="Changed">Whether what the game receives changed — and so whether the version moved.</param>
public sealed record ContentSetOutcome(
    NodeItemRole Role, Guid LangId, int PreviousVersion, int Version, int ItemCount, bool Changed);

/// <summary>
/// One reason content cannot be published, precise enough to put next to the field.
/// </summary>
/// <param name="Code">
/// Stable: <c>questionEmpty</c>, <c>questionTooLong</c>, <c>choiceEmpty</c>, <c>choiceTooLong</c>,
/// <c>choicesNotDifferent</c>, <c>choiceCount</c>, <c>correctIndex</c>, <c>languageMissing</c>,
/// <c>recoveryMissing</c>, <c>tooManyQuestions</c>, <c>duplicatePosition</c>, <c>unknownItem</c>,
/// <c>duplicateItem</c>, <c>unknownLanguage</c>.
/// </param>
/// <param name="Message">English, for the admin paths and logs. The Studio translates <paramref name="Code"/>.</param>
/// <param name="Field"><c>question</c>, <c>choice1</c>…<c>choice3</c>, or null for the whole item.</param>
public sealed record ContentProblem(
    string Code,
    string Message,
    NodeItemRole? Role = null,
    int? Order = null,
    Guid? LangId = null,
    string? Field = null);
