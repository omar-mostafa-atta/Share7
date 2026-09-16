using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Play.Models;
using Share7.Domain.Constants;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Play;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Rewards;
using Share7.Infrastructure.Commerce;
using Share7.Infrastructure.Economy;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// Fixtures for the play-context domain: modes, worlds, events and their prize tables.
/// <para>
/// Every service here is the real one over the shared context, for the same reason the run and
/// reward helpers are: what these tests are actually about is that a session's mode, its context and
/// its event all agree about what it was worth, and a stub anywhere in that chain would let them
/// agree on something nothing ships.
/// </para>
/// </summary>
public static class PlayTest
{
    public static PlaySelectionResolver Resolver(ApplicationDbContext context) =>
        new(context, new LevelService(context));

    public static GameModeService Modes(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));

    public static GameModeAdminService ModeAdmin(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));

    public static GameWorldService Worlds(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English), new LevelService(context));

    public static GameWorldAdminService WorldAdmin(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));

    public static PlayEventService Events(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English), new LevelService(context));

    public static PlayEventAdminService EventAdmin(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));

    public static PrizeClaimAdminService Claims(ApplicationDbContext context, Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));

    /// <summary>The prize observer, wired to the real reward engine it pays through.</summary>
    public static EventPrizeAwardService Awards(ApplicationDbContext context)
    {
        var wallet = new WalletService(context);

        return new EventPrizeAwardService(
            context,
            new RewardService(context, wallet, new LevelService(context), new EntitlementService(context)),
            NullLogger<EventPrizeAwardService>.Instance);
    }

    /// <summary>
    /// A mode, written straight to the table.
    /// <para>
    /// Deliberately not through the admin service: these tests are about what a mode <i>does</i> at
    /// run time, and authoring validation has its own suite. Defaults to the shape the seeder writes
    /// — playable alone or head to head, counting for everything.
    /// </para>
    /// </summary>
    public static async Task<GameMode> AddModeAsync(
        this ApplicationDbContext context,
        Guid gameId,
        string? modeKey = null,
        bool isDefault = false,
        PlayTopologies topologies = PlayTopologies.Solo | PlayTopologies.Versus,
        bool countsTowardMastery = true,
        bool settlesEconomy = true,
        bool countsTowardRanking = true,
        bool isActive = true,
        DateTime? availableFromUtc = null,
        DateTime? availableToUtc = null,
        Guid? entitlementProductId = null,
        int minGradeOrder = 0,
        Guid? economyProfileId = null,
        CancellationToken cancellationToken = default)
    {
        var mode = new GameMode
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            ModeKey = modeKey ?? $"mode_{Guid.NewGuid():N}"[..24],
            Topologies = topologies,
            MinPlayers = 1,
            MaxPlayers = 2,
            IsActive = isActive,
            IsDefault = isDefault,
            AvailableFromUtc = availableFromUtc,
            AvailableToUtc = availableToUtc,
            RequiresEntitlement = entitlementProductId is not null,
            EntitlementProductId = entitlementProductId,
            MinGradeOrder = minGradeOrder,
            CountsTowardMastery = countsTowardMastery,
            SettlesEconomy = settlesEconomy,
            CountsTowardRanking = countsTowardRanking,
            EconomyProfileId = economyProfileId,
            CreatedAtUtc = DateTime.UtcNow,
            Translations =
            [
                new GameModeTranslation { LangId = LanguageIds.English, Name = "Test mode", Description = "" },
                new GameModeTranslation { LangId = LanguageIds.Arabic, Name = "وضع", Description = "" }
            ]
        };

        context.GameModes.Add(mode);
        await context.SaveChangesAsync(cancellationToken);

        return mode;
    }

    /// <summary>An economy profile, for the tests about what a payout is scaled by.</summary>
    public static async Task<EconomyProfile> AddEconomyProfileAsync(
        this ApplicationDbContext context,
        int payoutPercent,
        bool paysRuleRewards = true,
        bool isDefault = false,
        CancellationToken cancellationToken = default)
    {
        var profile = new EconomyProfile
        {
            Id = Guid.NewGuid(),
            ProfileKey = $"p_{Guid.NewGuid():N}"[..16],
            Name = $"{payoutPercent}%",
            PayoutPercent = payoutPercent,
            PaysRuleRewards = paysRuleRewards,
            IsDefault = isDefault,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.EconomyProfiles.Add(profile);
        await context.SaveChangesAsync(cancellationToken);

        return profile;
    }

    /// <summary>A world policy row.</summary>
    public static async Task<GameWorld> AddWorldAsync(
        this ApplicationDbContext context,
        Guid gameId,
        string? worldKey = null,
        WorldUnlockKind unlockKind = WorldUnlockKind.Free,
        Guid? productId = null,
        int minLevel = 0,
        int minGradeOrder = 0,
        bool isDefault = false,
        CancellationToken cancellationToken = default)
    {
        var world = new GameWorld
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            WorldKey = worldKey ?? $"world_{Guid.NewGuid():N}"[..24],
            UnlockKind = unlockKind,
            ProductId = productId,
            MinLevel = minLevel,
            MinGradeOrder = minGradeOrder,
            IsActive = true,
            IsDefault = isDefault,
            CreatedAtUtc = DateTime.UtcNow,
            Translations =
            [
                new GameWorldTranslation { LangId = LanguageIds.English, Name = "Test world", Description = "" },
                new GameWorldTranslation { LangId = LanguageIds.Arabic, Name = "عالم", Description = "" }
            ]
        };

        context.GameWorlds.Add(world);
        await context.SaveChangesAsync(cancellationToken);

        return world;
    }

    /// <summary>
    /// The save request for an event, with everything a valid one needs and nothing else. Overrides
    /// go through the returned object, so each test names only what it is actually about.
    /// </summary>
    public static SavePlayEventRequest EventRequest(
        Guid gameId,
        Guid modeId,
        string metric,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        string? eventKey = null) => new()
    {
        EventKey = eventKey ?? $"ev_{Guid.NewGuid():N}"[..20],
        GameId = gameId,
        ModeId = modeId,
        Metric = metric,
        Aggregation = "sum",
        StartsAtUtc = startsAtUtc,
        EndsAtUtc = endsAtUtc,
        PrizeCohort = "all",
        IsActive = true,
        Translations =
        [
            new PlayEventTranslationRequest
            {
                LangId = LanguageIds.English, Name = "Test event", Description = "", Rules = "Score the most."
            },
            new PlayEventTranslationRequest
            {
                LangId = LanguageIds.Arabic, Name = "حدث", Description = "", Rules = "سجل الأكثر."
            }
        ]
    };

    /// <summary>One in-game prize tier paying a flat amount of one currency.</summary>
    public static SaveEventPrizeTierRequest CoinTier(
        int fromRank, int toRank, string currencyKey, long amount, int? quantity = null) => new()
    {
        FromRank = fromRank,
        ToRank = toRank,
        Kind = "in_game",
        Quantity = quantity,
        Grants = [new EventPrizeGrantRequest { Currency = currencyKey, Amount = amount }],
        Translations =
        [
            new EventPrizeTierTranslationRequest { LangId = LanguageIds.English, Title = $"{amount} coins" },
            new EventPrizeTierTranslationRequest { LangId = LanguageIds.Arabic, Title = $"{amount} عملة" }
        ]
    };

    /// <summary>One real-world prize tier — the kind a person has to fulfil.</summary>
    public static SaveEventPrizeTierRequest RealWorldTier(
        int fromRank, int toRank, string title, int? quantity = null) => new()
    {
        FromRank = fromRank,
        ToRank = toRank,
        Kind = "real_world",
        Quantity = quantity,
        DeclaredValueMinor = 50_000,
        ValueCurrencyCode = "EGP",
        Translations =
        [
            new EventPrizeTierTranslationRequest { LangId = LanguageIds.English, Title = title },
            new EventPrizeTierTranslationRequest { LangId = LanguageIds.Arabic, Title = title }
        ]
    };
}
