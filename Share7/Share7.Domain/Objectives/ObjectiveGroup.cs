namespace Share7.Domain.Objectives;

/// <summary>
/// How many of a group's members have to finish before the group does.
/// <para>
/// **This one field is the whole missions feature.** A complex mission, a "finish 5 of this week's
/// 8", and a season pass are the same structure with a different value here — which is why there is
/// no separate mission table and no separate season table.
/// </para>
/// </summary>
public enum GroupCompletionMode
{
    /// <summary>Every member. The ordinary multi-part mission.</summary>
    AllOf = 0,

    /// <summary>Any one member. A "do any of these three" choice.</summary>
    AnyOf = 1,

    /// <summary>
    /// Members in <c>StepOrder</c>, each locked until the one before it is done. A step that has
    /// not been reached does not count and does not accrue — otherwise a child could finish step
    /// three by accident before seeing step one, which is not a chain.
    /// </summary>
    Ordered = 2,

    /// <summary>Any <c>RequiredCount</c> of them. "Finish 5 of this week's 8 quests."</summary>
    NOf = 3
}

/// <summary>
/// An ordered or unordered set of objectives that pays when enough of it is done.
/// <para>
/// Missions, weekly challenge sets and season passes are all this. The members are ordinary
/// <see cref="Objective"/> rows — the group adds a completion rule over them and a reward of its
/// own, and nothing about being in a group changes how a member counts.
/// </para>
/// </summary>
public class ObjectiveGroup
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable public token — <c>mission.first_week</c>, <c>season.2026.autumn</c>. The reward rule
    /// that pays for the group keys on this, so it is immutable once published.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Which cycle the group counts in. Usually matches its members'; a <c>Seasonal</c> group over
    /// daily members is legitimate and means "over the season, finish N dailies".
    /// </summary>
    public ObjectiveKind Kind { get; set; } = ObjectiveKind.Seasonal;

    public GroupCompletionMode CompletionMode { get; set; } = GroupCompletionMode.AllOf;

    /// <summary>
    /// How many members are needed under <see cref="GroupCompletionMode.NOf"/>. Ignored otherwise,
    /// and refused at authoring when it exceeds the member count — a group that can never complete
    /// is dead configuration nobody sees fail.
    /// </summary>
    public int RequiredCount { get; set; }

    /// <summary>
    /// The season this group belongs to, for <see cref="ObjectiveKind.Seasonal"/>. Becomes the
    /// cycle key, so ending a season is retiring the group rather than a rollover.
    /// </summary>
    public string? SeasonKey { get; set; }

    public DateTime? AvailableFromUtc { get; set; }
    public DateTime? AvailableToUtc { get; set; }

    /// <summary>A token the client maps to its own art. Never a URL.</summary>
    public string? IconKey { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ObjectiveGroupTranslation> Translations { get; set; } =
        new List<ObjectiveGroupTranslation>();
}

/// <summary>A group's name and description in one language. Same rule as everything localized.</summary>
public class ObjectiveGroupTranslation
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }
    public ObjectiveGroup? Group { get; set; }

    public Guid LangId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>
/// One player's standing on one group in one cycle. The same shape as
/// <see cref="UserObjectiveProgress"/>, and separate from it because a group's completion is a fact
/// about its members rather than a counter of its own.
/// </summary>
public class UserObjectiveGroupProgress
{
    public Guid UserId { get; set; }

    public Guid GroupId { get; set; }
    public ObjectiveGroup? Group { get; set; }

    public string CycleKey { get; set; } = string.Empty;

    /// <summary>How many members are finished. Derived on every fold, stored for cheap reads.</summary>
    public int CompletedCount { get; set; }

    public ObjectiveState State { get; set; } = ObjectiveState.InProgress;

    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? ClaimableUntilUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
