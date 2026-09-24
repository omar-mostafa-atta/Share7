namespace Share7.Domain.Play;

/// <summary>What one session is allowed to be worth, once the mode and the context have both had their say.</summary>
public readonly record struct PlaySettlementPolicy(bool AffectsMastery, bool SettlesEconomy, bool Ranks)
{
    /// <summary>A session that records nothing and pays nothing. What practice is worth, by design.</summary>
    public static readonly PlaySettlementPolicy Nothing = new(false, false, false);
}

/// <summary>
/// The intersection rule, in one place: <b>a session may do only what its mode and its context both
/// allow.</b>
/// <para>
/// Written as a pure function over the two, rather than as branches inside the run and attempt
/// services, because those two paths must never disagree about what a session was worth — and they
/// would, eventually, the first time one of them gained a condition the other did not. Everything
/// here is server-side: the client selects a mode and a context and is told the outcome.
/// </para>
/// <para>
/// Neither half can override the other, and that is the property worth protecting. A mode that
/// declares it never moves mastery cannot be made to by playing it on the curriculum; a practice
/// session cannot be made to pay by choosing a generous mode.
/// </para>
/// </summary>
public static class PlayAccounting
{
    public static PlaySettlementPolicy Decide(GameMode mode, PlayContextKind context)
    {
        if (mode is null) return PlaySettlementPolicy.Nothing;

        // Practice is worth nothing whatever the mode says. A child retrying the questions they got
        // wrong must not risk their record or their coins on it, because the moment practising can
        // cost something, the safe play is not to practise.
        if (context == PlayContextKind.Practice) return PlaySettlementPolicy.Nothing;

        // An assignment is curriculum work with a teacher's name on it, and settles as curriculum
        // does. It was worth nothing here only while it was refused at the boundary for want of a
        // class relation to resolve against; cohorts are that relation (§17.4).
        //
        // **What an assignment must not do is buy its evidence a stronger claim.** That is not
        // decided here at all — settlement is about gameplay. Strength follows the conditions
        // actually recorded, and the evidence contract decides whether it admits this context, so
        // homework cannot become exam-grade by being set as homework.
        bool onCurriculum =
            context is PlayContextKind.Curriculum or PlayContextKind.Assignment;

        bool affectsMastery = mode.CountsTowardMastery && onCurriculum;

        return new PlaySettlementPolicy(
            AffectsMastery: affectsMastery,
            SettlesEconomy: mode.SettlesEconomy,
            Ranks: mode.CountsTowardRanking);
    }
}
