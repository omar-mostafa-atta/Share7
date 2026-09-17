namespace Share7.Domain.Rewards;

/// <summary>
/// How often one rule is allowed to pay the same user.
/// <para>
/// Deliberately only two values. Cooldown and daily limit are **not** policies here — they are
/// independent optional constraints on <see cref="EveryTime"/>
/// (<see cref="RewardRule.CooldownSeconds"/>, <see cref="RewardRule.DailyLimit"/>). Modelling them
/// as enum members would make them mutually exclusive, and "at most 5 a day, no more than one
/// every 10 minutes" is a perfectly ordinary rule.
/// </para>
/// </summary>
public enum RewardRepeatPolicy
{
    /// <summary>
    /// Pays once per user, per rule, per reference — forever. The reference is the lesson within a
    /// game, so a student who completes lesson X in the runner is still paid the first time they
    /// complete it in a different game; progress is tracked per game and rewards follow it.
    /// </summary>
    Once = 0,

    /// <summary>
    /// Pays on every qualifying event, subject to <see cref="RewardRule.CooldownSeconds"/> and
    /// <see cref="RewardRule.DailyLimit"/> when set. Without either, replaying one lesson is an
    /// unbounded coin faucet — the admin validation warns about this but does not forbid it.
    /// </summary>
    EveryTime
}
