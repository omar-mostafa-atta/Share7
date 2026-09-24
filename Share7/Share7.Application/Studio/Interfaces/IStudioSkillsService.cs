using Share7.Application.Common.Models;
using Share7.Application.Workspace.Models;
using Share7.Domain.Competency;

namespace Share7.Application.Studio.Interfaces;

/// <summary>
/// Skills, as the content team works on them: import the official outcomes, edit what came in,
/// have a specialist confirm each claim, and map the questions that measure it.
/// <para>
/// **Imported, then edited** (decided 24 Sep 2026). The outcomes are somebody else's document and
/// the Studio does not pretend to author them: a filled template comes in as a framework nobody has
/// reviewed yet, and the team's work is turning those lines into claims that can be true or false
/// about a child. An import never overwrites an edit — a second import of the same sheet updates
/// only the lines nobody has touched, and says which ones it left alone.
/// </para>
/// <para>
/// Everything here is a measurement act rather than a change to what children see, so it takes
/// effect at once with an audit row, exactly like anchoring an item. Nothing on this surface is
/// released, and nothing on it can change a question a child is being asked.
/// </para>
/// </summary>
public interface IStudioSkillsService
{
    /// <summary>Every framework, with how much of it has been reviewed.</summary>
    Task<IReadOnlyList<FrameworkDto>> FrameworksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a framework for a curriculum: one for the whole thing, all subjects in it (decided
    /// 24 Sep 2026), because a ministry revises a curriculum rather than a subject.
    /// </summary>
    Task<ServiceResult<FrameworkDto>> CreateFrameworkAsync(
        StudioMember member, CreateFrameworkRequest request, CancellationToken cancellationToken = default);

    /// <summary>The blank sheet a team fills in, with its columns and one worked row per language.</summary>
    Task<byte[]> TemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a filled template into a framework. Nothing is written until every row has been read:
    /// a sheet with one bad row imports nothing and says which row, because half an import is worse
    /// than none when the thing being imported is a vocabulary.
    /// </summary>
    Task<ServiceResult<SkillImportReport>> ImportAsync(
        StudioMember member, Guid frameworkId, Stream sheet, bool dryRun, CancellationToken cancellationToken = default);

    /// <summary>The outcomes in a framework, searched by their words, with what each one is carrying.</summary>
    Task<IReadOnlyList<SkillDto>> SkillsAsync(
        Guid frameworkId, Guid langId, string? search = null, string? reviewState = null,
        int take = 100, int skip = 0, CancellationToken cancellationToken = default);

    Task<ServiceResult<SkillDto>> SkillAsync(Guid targetId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>Rewords a claim, or sets its kind and band. The import will not touch it again.</summary>
    Task<ServiceResult<SkillDto>> EditSkillAsync(
        StudioMember member, Guid targetId, EditSkillRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// A subject specialist confirming the statement is a real, measurable claim — or taking that
    /// back. This is the review loop for skills; they are not drafted and not released.
    /// </summary>
    Task<ServiceResult<SkillDto>> SetReviewStateAsync(
        StudioMember member, Guid targetId, TargetReviewState state, CancellationToken cancellationToken = default);

    /// <summary>What one question is measuring now, and everything it could be measuring.</summary>
    Task<ServiceResult<ItemSkillsDto>> ItemSkillsAsync(
        Guid itemId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets what a question measures. Exactly one of the mappings is the primary one — reporting
    /// needs a single answer to "what is this question for", and two answers is none.
    /// </summary>
    Task<ServiceResult<ItemSkillsDto>> MapItemAsync(
        StudioMember member, Guid itemId, MapItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// How far one subject is from being mapped — the Phase 5 gate, as a number the team can watch
    /// go up.
    /// </summary>
    Task<ServiceResult<SubjectMappingDto>> SubjectProgressAsync(
        Guid subjectNodeId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The questions under one subject and what each one says it measures, worst first: the ones
    /// mapped to nothing, then the ones still on the stand-in their lesson was minted with, then
    /// the ones that are done. Worst-first is what makes the list finishable.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<QuestionToMapDto>>> QuestionsToMapAsync(
        Guid subjectNodeId, Guid langId, string? state = null, int take = 50, int skip = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stand-ins under one node, with how much evidence each is carrying — so a team works on
    /// the claims that matter rather than alphabetically.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<StandInDto>>> StandInsAsync(
        Guid nodeId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints one real skill and moves the named stand-ins' questions onto it.
    /// <para>
    /// **This is the recompute line paying for itself.** Questions point at skills through a mapping
    /// table rather than owning them, and what a child knows is derived from their answers rather
    /// than stored as a verdict — so re-pointing a thousand questions is one UPDATE, and every
    /// answer a child has ever given is re-read against the real claim it was always about. The
    /// stand-ins are deprecated, never deleted: a measurement taken against one last month was a
    /// real measurement and its row still has to resolve.
    /// </para>
    /// </summary>
    Task<ServiceResult<PromotionReportDto>> PromoteAsync(
        StudioMember member, PromoteRequest request, CancellationToken cancellationToken = default);
}

public sealed record FrameworkDto
{
    public required Guid Id { get; init; }
    public required string FrameworkKey { get; init; }
    public required string Name { get; init; }
    public required string VersionLabel { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public required int Skills { get; init; }
    public required int Reviewed { get; init; }

    /// <summary>The bootstrap framework of lesson placeholders, which nobody authors into.</summary>
    public required bool IsPlaceholders { get; init; }
}

public sealed record CreateFrameworkRequest(string FrameworkKey, string Name, string VersionLabel);

/// <summary>One outcome, as the Studio shows it.</summary>
public sealed record SkillDto
{
    public required Guid Id { get; init; }
    public required Guid FrameworkId { get; init; }
    public required string TargetKey { get; init; }

    /// <summary>The claim in every language it has been written in, keyed by language id.</summary>
    public required IReadOnlyDictionary<Guid, string> Statements { get; init; }

    public required string TargetKindKey { get; init; }
    public required TargetReviewState ReviewState { get; init; }
    public int? DifficultyBand { get; init; }

    /// <summary>Questions mapped to it. Zero means it cannot be measured yet.</summary>
    public required int Questions { get; init; }

    /// <summary>Lesson placeholders it has replaced.</summary>
    public required int Replaced { get; init; }

    /// <summary>True once somebody has edited it here, so a re-import will leave it alone.</summary>
    public required bool IsEdited { get; init; }
}

public sealed record EditSkillRequest(
    IReadOnlyDictionary<Guid, string>? Statements,
    string? TargetKindKey,
    int? DifficultyBand);

/// <summary>What an import did, and what it deliberately did not do.</summary>
public sealed record SkillImportReport
{
    public required int RowsRead { get; init; }
    public required int Added { get; init; }
    public required int Updated { get; init; }

    /// <summary>Rows that match a skill somebody has since edited here. Left exactly as they are.</summary>
    public required int LeftAlone { get; init; }

    /// <summary>Rows whose parent code names an outcome that is not in the sheet or the framework.</summary>
    public required IReadOnlyList<SkillImportProblem> Problems { get; init; }

    /// <summary>True when nothing was written because <c>dryRun</c> was asked for, or a row was bad.</summary>
    public required bool WroteNothing { get; init; }
}

public sealed record SkillImportProblem(int Row, string Code, string Message);

public sealed record ItemSkillsDto
{
    public required Guid ItemId { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyList<ItemSkillDto> Mapped { get; init; }
}

public sealed record ItemSkillDto(Guid TargetId, string Statement, decimal Emphasis, bool IsPrimary, bool IsPlaceholder);

public sealed record MapItemRequest(IReadOnlyList<ItemSkillLine> Skills);

public sealed record ItemSkillLine(Guid TargetId, decimal Emphasis, bool IsPrimary);

/// <summary>
/// The Phase 5 gate for one subject: every live question under it mapped to a real skill rather
/// than to the placeholder its lesson was minted with.
/// </summary>
public sealed record SubjectMappingDto
{
    public required Guid SubjectNodeId { get; init; }
    public required string SubjectTitle { get; init; }
    public required int Lessons { get; init; }
    public required int Questions { get; init; }

    /// <summary>Questions whose primary mapping is a real, authored skill.</summary>
    public required int MappedToSkills { get; init; }

    /// <summary>Questions still on the placeholder their lesson was minted with.</summary>
    public required int OnPlaceholders { get; init; }

    /// <summary>Questions mapped to nothing at all. These cannot be measured by anybody.</summary>
    public required int Unmapped { get; init; }

    public bool IsComplete => Questions > 0 && MappedToSkills == Questions;
}

/// <summary>One question on the mapping board, and what it says it measures now.</summary>
public sealed record QuestionToMapDto
{
    public required Guid ItemId { get; init; }
    public required string Text { get; init; }
    public required Guid LessonId { get; init; }
    public required string LessonTitle { get; init; }
    public required string Role { get; init; }
    public required int Order { get; init; }

    /// <summary>What it is mainly about now. Null when it is mapped to nothing at all.</summary>
    public Guid? PrimaryTargetId { get; init; }
    public string? PrimaryStatement { get; init; }

    /// <summary>True when the thing it is mapped to is the stand-in its lesson was minted with.</summary>
    public required bool OnStandIn { get; init; }

    /// <summary>Anything else it touches, beyond the one it is mainly about.</summary>
    public required int AlsoTouches { get; init; }
}

/// <summary>A stand-in, and what promoting it would move.</summary>
public sealed record StandInDto
{
    public required Guid TargetId { get; init; }
    public required string Statement { get; init; }
    public Guid? NodeId { get; init; }
    public string? NodeTitle { get; init; }

    /// <summary>Questions mapped to it. What a promotion would move.</summary>
    public required int Questions { get; init; }

    /// <summary>Answers already taken against it. What a promotion would re-read.</summary>
    public required int Answers { get; init; }

    /// <summary>True once a real skill has replaced it. Kept in the list so the work stays visible.</summary>
    public required bool IsReplaced { get; init; }
}

/// <summary>What a team submits: the real claim, and the stand-ins it replaces.</summary>
public sealed record PromoteRequest
{
    public required string TargetKey { get; init; }

    /// <summary>The claim, in every language the platform serves. Both are required.</summary>
    public required IReadOnlyDictionary<Guid, string> Statements { get; init; }

    public string TargetKindKey { get; init; } = TargetKinds.Skill;
    public int? DifficultyBand { get; init; }

    /// <summary>The stand-ins whose questions move onto the new skill. At least one.</summary>
    public required IReadOnlyList<Guid> ReplacesTargetIds { get; init; }

    /// <summary>Confirm it straight away. Only a reviewer or a Lead may.</summary>
    public bool MarkReviewed { get; init; }

    /// <summary>
    /// An outcome that is already imported — the ordinary case once the official document is in.
    /// The stand-ins' questions move onto it and nothing is minted, because this operation has no
    /// business rewording somebody else's curriculum.
    /// </summary>
    public Guid? UseExistingTargetId { get; init; }
}

/// <summary>What a promotion actually moved. Every number is checkable in the database after.</summary>
public sealed record PromotionReportDto
{
    public required Guid TargetId { get; init; }
    public required string TargetKey { get; init; }
    public required int StandInsReplaced { get; init; }
    public required int QuestionsMoved { get; init; }
    public required int LessonsMoved { get; init; }

    /// <summary>Answers re-read against the real claim they were always about.</summary>
    public required int AnswersRebuilt { get; init; }

    /// <summary>Human decisions to ignore an answer, carried across. These are never re-derived.</summary>
    public required int ExclusionsKept { get; init; }
}
