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
    LessonAced,

    /// <summary>
    /// A leaderboard cycle closed and this player's final rank landed in a prize band. Raised by
    /// the settlement job, once per <c>(cycle, cohort, player)</c>.
    /// <para>
    /// Scoped by <c>ReferenceKey</c> as <c>{boardKey}:{band}</c> — <c>…:1</c>, <c>…:2</c>,
    /// <c>…:top10</c> — so the entire prize structure is authored in the existing admin UI as
    /// data, and changing what third place pays needs no deploy.
    /// </para>
    /// <para>
    /// Unlike the lesson events, this one is **not** derived from a client submission at all: it
    /// comes from a rank the server computed from results the server graded. There is no point in
    /// the chain at which a player asserted anything.
    /// </para>
    /// </summary>
    LeaderboardSettled,

    /// <summary>
    /// The player's XP balance crossed a <c>LevelThreshold</c>. Raised **once per level crossed**,
    /// so a grant large enough to skip two levels fires twice and both rules pay.
    /// <para>
    /// Scoped by <c>ReferenceKey</c> as the level reached — <c>"5"</c>, <c>"10"</c> — so the whole
    /// level-reward table is authored in the existing admin UI and changing what level 10 pays
    /// needs no deploy. A rule with a null reference pays on *every* level-up, which is how a
    /// flat "20 coins per level" is expressed.
    /// </para>
    /// <para>
    /// Like the rest of this enum it is derived from a server-side fact: the level comes from a
    /// balance that only the reward engine and the purchase path can move, and the client neither
    /// computes nor reports it.
    /// </para>
    /// <para>
    /// **A rule on this event must not grant XP.** Doing so is a payout that causes the event that
    /// triggers it. Authoring refuses it, and evaluation pays level-ups in a single pass that
    /// cannot re-enter — see <c>RewardService</c>.
    /// </para>
    /// </summary>
    PlayerLevelUp,

    /// <summary>
    /// A run of a mini-game settled. Raised by <c>RunService</c> at any outcome, on a run the server
    /// itself opened — never on a result for a run that was never started.
    /// <para>
    /// **For the <i>fixed</i> half of a run's payout only**: completed a run, first run of the day, a
    /// perfect run. What the run's pickups were worth is not expressible as a rule, because it varies
    /// with what was collected — that comes from <c>PickupValuation</c>, and is granted through the
    /// same wallet inside the same transaction.
    /// </para>
    /// <para>
    /// Scoped by <c>ReferenceKey</c> = the **game id**, not a lesson id. A rule with a null reference
    /// pays for a run of any game, which is the normal case.
    /// </para>
    /// </summary>
    RunSettled,

    /// <summary>
    /// A player claimed a completed objective — a daily quest, a weekly quest, an achievement.
    /// Raised by the claim path, never by the projector: completing is bookkeeping, and only a
    /// deliberate claim moves currency.
    /// <para>
    /// Scoped by <c>ReferenceKey</c> = the objective's key, so what each quest pays is authored in
    /// the existing admin UI and retuning a reward needs no deploy. A rule with a null reference
    /// pays on *every* objective, which is how a flat "any quest is worth 5 coins" is expressed.
    /// </para>
    /// <para>
    /// One claim is one payout whatever the repeat policy says: the key is the objective and its
    /// cycle, so a retried claim finds it already spent and replays rather than paying twice, while
    /// next week's cycle of the same quest is a genuinely different key.
    /// </para>
    /// </summary>
    ObjectiveCompleted,

    /// <summary>
    /// A player claimed a completed objective **group** — a mission's capstone, a season's finish.
    /// Raised by the group claim path, on the same terms as a single objective.
    /// <para>
    /// Scoped by <c>ReferenceKey</c> = the group's key. A group's reward is separate from its
    /// members' — finishing each step pays its own rule, and completing the set pays this one.
    /// </para>
    /// </summary>
    ObjectiveGroupCompleted
}
