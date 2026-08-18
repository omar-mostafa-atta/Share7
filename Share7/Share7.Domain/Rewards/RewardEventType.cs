namespace Share7.Domain.Rewards;

/// <summary>
/// A gameplay outcome the backend is willing to pay for.
/// <para>
/// **Every value here must have a producer.** Unlike
/// <see cref="Economy.CurrencyTransactionType"/>, which deliberately declares types no code emits
/// yet, an event type nothing raises is dead configuration: an admin creates a rule against it,
/// sees no error, and waits for a reward that can never fire. Adding a value means adding the
/// code that raises it in the same change.
/// </para>
/// <para>
/// All of these are derived from a **server-validated** progress attempt. There is no event the
/// client can assert directly — that is the whole point of the authority model.
/// </para>
/// </summary>
public enum RewardEventType
{
    Unknown = 0,

    /// <summary>
    /// A lesson was played to the end, at any score. Fires on every attempt, so a rule against it
    /// almost always wants a cooldown or a daily limit.
    /// </summary>
    LessonAttempted,

    /// <summary>The attempt landed at or above the pass mark — <c>Completed</c> or <c>Aced</c>.</summary>
    LessonCompleted,

    /// <summary>
    /// The attempt was a clean sweep. Raised **alongside** <see cref="LessonCompleted"/>, not
    /// instead of it: an aced lesson fires both, so "10 coins to pass, 5 gems to ace" is two
    /// independent rules rather than one rule with branching.
    /// </summary>
    LessonAced
}
