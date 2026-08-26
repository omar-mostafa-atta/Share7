using Microsoft.EntityFrameworkCore;
using Share7.Application.Admin.Interfaces;
using Share7.Domain.Commerce;
using Share7.Domain.Constants;
using Share7.Domain.Economy;
using Share7.Domain.Games;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Domain.Progression;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// Seeds the catalogues the client reads on launch: currencies, the game it can start, the XP
/// ladder, what a pickup is worth, what a lesson pays, the shop, the quests and the boards.
/// <para>
/// Every write here is matched on a natural key first — a currency by <c>Key</c>, a board by
/// <c>BoardKey</c>, an objective by <c>Key</c>. A row that exists is left alone rather than updated,
/// because the values in it are prices and targets, and an operator who retuned one did not ask for
/// it back.
/// </para>
/// </summary>
internal sealed class PlatformCatalogueSeeder
{
    private readonly ApplicationDbContext _db;

    public PlatformCatalogueSeeder(ApplicationDbContext db) => _db = db;

    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    public async Task SeedAsync(ContentSeedReport report, CancellationToken ct)
    {
        var currencies = await CurrenciesAsync(report, ct);
        var runnerId = await GamesAsync(report, ct);

        await LevelsAsync(report, ct);
        await ValuationsAsync(currencies, runnerId, report, ct);
        await MetricBoundsAsync(report, ct);
        await RewardsAsync(currencies, report, ct);
        await ShopAsync(currencies, report, ct);
        await ObjectivesAsync(runnerId, report, ct);
        await BoardsAsync(runnerId, report, ct);

        await _db.SaveChangesAsync(ct);
    }

    // ── currencies ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>coins</c> and <c>gems</c>, next to the <c>xp</c> a migration already seeded.
    /// <para>
    /// <c>coins</c> matters more than it looks: the existing <c>SeedDefaultCoinValuation</c>
    /// migration resolves its row by currency key and silently inserts nothing when no currency has
    /// the key <c>coins</c> — which is every database that has not run this seeder. So the runner
    /// pays zero until this row exists.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, Guid>> CurrenciesAsync(ContentSeedReport report, CancellationToken ct)
    {
        var existing = await _db.Currencies.ToDictionaryAsync(c => c.Key, c => c.Id, StringComparer.Ordinal, ct);

        void Add(string key, string name, string description, bool spendable, bool hard, long? dailyCap)
        {
            if (existing.ContainsKey(key)) return;

            var id = SeedId.For("currency", key);
            _db.Currencies.Add(new Currency
            {
                Id = id,
                Key = key,
                Name = name,
                Description = description,
                Enabled = true,
                IsSpendable = spendable,
                IsHard = hard,
                DailyEarnCap = dailyCap,
                CreatedAtUtc = DateTime.UtcNow
            });

            existing[key] = id;
            report.Currencies++;
        }

        // The daily cap is the anti-farming ceiling, not a balance decision: a child playing all
        // afternoon should still hit it long after a script would.
        Add("coins", "Coins", "Soft currency earned by playing and spent in the shop.", true, false, 2000);
        Add("gems", "Gems", "Premium currency for bundles and limited offers.", true, true, null);

        return existing;
    }

    // ── games ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The runner, and only the runner.
    /// <para>
    /// The catalogue is the backend's answer to "what can this client start", so a row here for a
    /// game the client cannot load is not extra content — it is a menu entry that fails on tap. When
    /// a second mini-game ships, it gets a row.
    /// </para>
    /// </summary>
    private async Task<Guid> GamesAsync(ContentSeedReport report, CancellationToken ct)
    {
        const string key = "runner";

        var existing = await _db.Games.FirstOrDefaultAsync(g => g.GameKey == key, ct);
        if (existing is not null) return existing.Id;

        var id = SeedId.For("game", key);

        _db.Games.Add(new Game
        {
            Id = id,
            GameKey = key,
            MinPlayers = 1,
            MaxPlayers = 4,
            ReadyTimeoutSeconds = 20f,
            SupportsSinglePlayer = true,
            SupportsMultiplayer = true,
            UseLobby = true,
            UseMatchmaking = true,
            IsActive = true,
            Translations =
            [
                new GameTranslation
                {
                    GameId = id, LangId = En,
                    DisplayName = "Knowledge Runner",
                    Description = "Run, dodge and answer questions to finish a lesson."
                },
                new GameTranslation
                {
                    GameId = id, LangId = Ar,
                    DisplayName = "عدّاء المعرفة",
                    Description = "اجرِ وتفادَ العقبات وأجب عن الأسئلة لإنهاء الدرس."
                }
            ]
        });

        report.Games++;
        return id;
    }

    // ── progression ───────────────────────────────────────────────────────────

    /// <summary>
    /// Extends the XP ladder past the flat placeholder that shipped with the schema.
    /// <para>
    /// Levels 1–11 are 100 XP apart, which is a curve that stops meaning anything once a child has
    /// played for a week. From 12 the step widens, so the ladder keeps paying out for a term rather
    /// than a weekend. Existing rows are never rewritten — a level a player has already reached must
    /// not move underneath them.
    /// </para>
    /// </summary>
    private async Task LevelsAsync(ContentSeedReport report, CancellationToken ct)
    {
        var have = await _db.LevelThresholds.Select(l => l.Level).ToListAsync(ct);
        var known = have.ToHashSet();

        var cumulative = await _db.LevelThresholds
            .OrderByDescending(l => l.Level)
            .Select(l => l.CumulativeXp)
            .FirstOrDefaultAsync(ct);

        var top = known.Count == 0 ? 0 : have.Max();
        var now = DateTime.UtcNow;

        for (var level = 1; level <= 30; level++)
        {
            if (level <= top)
                continue;

            var step = level <= 11 ? 100 : 150 + (level - 12) * 50;
            cumulative = level == 1 ? 0 : cumulative + step;

            if (known.Contains(level)) continue;

            _db.LevelThresholds.Add(new LevelThreshold
            {
                Level = level,
                CumulativeXp = cumulative,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            report.LevelThresholds++;
        }
    }

    // ── what a signal is worth ────────────────────────────────────────────────

    private async Task ValuationsAsync(
        Dictionary<string, Guid> currencies, Guid runnerId, ContentSeedReport report, CancellationToken ct)
    {
        var existing = await _db.SignalValuations
            .Select(v => new { v.GameId, v.SignalKind, v.CurrencyId })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        void Add(string kind, string currencyKey, long unit, int maxPerRun, int? maxPerDay)
        {
            if (!currencies.TryGetValue(currencyKey, out var currencyId)) return;
            if (existing.Any(v => v.GameId == runnerId && v.SignalKind == kind && v.CurrencyId == currencyId)) return;

            _db.SignalValuations.Add(new SignalValuation
            {
                Id = SeedId.For("valuation", "runner", kind, currencyKey),
                GameId = runnerId,
                SignalKind = kind,
                CurrencyId = currencyId,
                UnitValue = unit,
                MaxPerRun = maxPerRun,
                MaxPerDay = maxPerDay,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            report.SignalValuations++;
        }

        // MaxPerRun is a forgery ceiling rather than a balance knob: generous enough that no honest
        // run is clipped, tight enough that a claim of ten thousand pays nothing extra.
        Add(SignalKinds.Coin, "coins", 1, 500, 2000);
        Add(SignalKinds.NearMiss, "coins", 2, 60, 400);
        Add(SignalKinds.DistanceM, "coins", 1, 300, 2000);
        Add(SignalKinds.CorrectAnswer, CurrencyKeys.Xp, 10, 60, null);
    }

    /// <summary>
    /// Per-metric ceilings for leaderboard submissions, so one forged result cannot own a board.
    /// </summary>
    private async Task MetricBoundsAsync(ContentSeedReport report, CancellationToken ct)
    {
        var existing = await _db.LeaderboardMetricBounds
            .Select(b => new { b.GameId, b.Metric })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        void Add(string metric, long? maxValue, int? maxPerDay, long? maxValuePerDay)
        {
            if (existing.Any(b => b.GameId == null && b.Metric == metric)) return;

            _db.LeaderboardMetricBounds.Add(new LeaderboardMetricBound
            {
                Id = SeedId.For("bound", metric),
                GameId = null,
                Metric = metric,
                MaxValue = maxValue,
                MaxResultsPerDay = maxPerDay,
                MaxValuePerDay = maxValuePerDay,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            report.MetricBounds++;
        }

        Add(LeaderboardMetrics.LessonsCompleted, 1, 300, 300);
        Add(LeaderboardMetrics.LessonsAced, 1, 300, 300);
        Add(LeaderboardMetrics.TotalLessonScore, 100, 300, 30000);
        Add(LeaderboardMetrics.LessonBestPercent, 100, 300, null);
        Add(LeaderboardMetrics.RunsCompleted, 1, 300, 300);
        Add(LeaderboardMetrics.RunsSettled, 1, 300, 300);
        Add(LeaderboardMetrics.RunSeconds, 3600, 300, 43200);
        Add(LeaderboardMetrics.BestRunSeconds, 3600, 300, null);
        Add(LeaderboardMetrics.PickupsCollected, 500, 300, 20000);
        Add(LeaderboardMetrics.CurrencyEarned, 2000, 300, 5000);
    }

    // ── what an event pays ────────────────────────────────────────────────────

    private async Task RewardsAsync(
        Dictionary<string, Guid> currencies, ContentSeedReport report, CancellationToken ct)
    {
        var existing = await _db.RewardRules.Select(r => r.Name).ToListAsync(ct);
        var now = DateTime.UtcNow;

        void Add(
            string name,
            RewardEventType eventType,
            RewardRepeatPolicy repeat,
            CurrencyTransactionType transaction,
            int? dailyLimit,
            params (string CurrencyKey, long Amount)[] grants)
        {
            if (existing.Contains(name, StringComparer.Ordinal)) return;

            var id = SeedId.For("reward", name);
            var rule = new RewardRule
            {
                Id = id,
                Name = name,
                EventType = eventType,
                ReferenceKey = null,
                RepeatPolicy = repeat,
                DailyLimit = dailyLimit,
                TransactionType = transaction,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            foreach (var (currencyKey, amount) in grants)
            {
                if (!currencies.TryGetValue(currencyKey, out var currencyId)) continue;

                rule.Grants.Add(new RewardRuleGrant
                {
                    Id = SeedId.For("reward-grant", name, currencyKey),
                    RewardRuleId = id,
                    CurrencyId = currencyId,
                    Amount = amount
                });
            }

            _db.RewardRules.Add(rule);
            report.RewardRules++;
        }

        // The daily limit is what stops a replayed lesson from being an XP faucet. It is deliberately
        // far above a real day's play — a child finishing a hundred lessons is not the case it guards.
        Add("Lesson attempted", RewardEventType.LessonAttempted, RewardRepeatPolicy.Once,
            CurrencyTransactionType.LessonReward, 200, (CurrencyKeys.Xp, 5));

        Add("Lesson completed", RewardEventType.LessonCompleted, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.LessonReward, 100, (CurrencyKeys.Xp, 20), ("coins", 10));

        Add("Lesson aced", RewardEventType.LessonAced, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.LessonReward, 100, (CurrencyKeys.Xp, 50), ("coins", 25));

        Add("Run settled", RewardEventType.RunSettled, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.GameReward, 200, (CurrencyKeys.Xp, 10));

        Add("Player levelled up", RewardEventType.PlayerLevelUp, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.AchievementReward, null, ("coins", 100));

        Add("Objective completed", RewardEventType.ObjectiveCompleted, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.DailyReward, null, (CurrencyKeys.Xp, 30), ("coins", 20));

        Add("Objective group completed", RewardEventType.ObjectiveGroupCompleted, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.AchievementReward, null, ("gems", 5));

        Add("Leaderboard settled", RewardEventType.LeaderboardSettled, RewardRepeatPolicy.EveryTime,
            CurrencyTransactionType.LeaderboardReward, null, ("gems", 10), ("coins", 200));
    }

    // ── shop ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Products for the cosmetics that actually exist in the client, and offers that sell them.
    /// <para>
    /// <b>The grant references are the client's real cosmetic ids</b> — <c>shirtdefault</c> and its
    /// four siblings, read off the <c>CosmeticDefinition</c> assets. The backend has no cosmetic
    /// catalogue by design, so nothing here validates them; a made-up reference would be stored
    /// happily and then fail to resolve to anything wearable, which is why the list is the five the
    /// wardrobe can render rather than an invented fifty.
    /// </para>
    /// </summary>
    private async Task ShopAsync(
        Dictionary<string, Guid> currencies, ContentSeedReport report, CancellationToken ct)
    {
        var cosmeticKind = await _db.ProductKinds
            .FirstOrDefaultAsync(k => k.Name == "Cosmetic", ct);

        if (cosmeticKind is null || !currencies.TryGetValue("coins", out var coins)) return;

        var haveProducts = await _db.Products.Select(p => p.Key).ToListAsync(ct);
        var haveOffers = await _db.Offers
            .SelectMany(o => o.Translations)
            .Where(t => t.LangId == En)
            .Select(t => t.Name)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var created = new Dictionary<string, Guid>(StringComparer.Ordinal);

        Guid Product(string key, string nameEn, string nameAr, string descEn, string descAr,
            params string[] cosmeticIds)
        {
            var id = SeedId.For("product", key);
            created[key] = id;

            if (haveProducts.Contains(key, StringComparer.Ordinal)) return id;

            var product = new Product
            {
                Id = id,
                Key = key,
                Active = true,
                ProductKindId = cosmeticKind.Id,
                Translations =
                [
                    new ProductTranslation { ProductId = id, LangId = En, Name = nameEn, Description = descEn },
                    new ProductTranslation { ProductId = id, LangId = Ar, Name = nameAr, Description = descAr }
                ]
            };

            foreach (var cosmeticId in cosmeticIds)
            {
                product.Grants.Add(new ProductGrant
                {
                    Id = SeedId.For("product-grant", key, cosmeticId),
                    ProductId = id,
                    Reference = cosmeticId,
                    Quantity = 1
                });
            }

            _db.Products.Add(product);
            report.Products++;
            return id;
        }

        var shirt = Product("cosmetic.shirt.default", "Classic Shirt", "قميص كلاسيكي",
            "A clean everyday shirt.", "قميص أنيق لكل يوم.", "shirtdefault");
        var jacket = Product("cosmetic.jacket.default", "School Jacket", "جاكيت مدرسي",
            "Warm enough for the morning queue.", "دافئ بما يكفي لطابور الصباح.", "jacketdefault");
        var pants = Product("cosmetic.pants.default", "Everyday Trousers", "بنطلون يومي",
            "Comfortable trousers that go with anything.", "بنطلون مريح يناسب كل الإطلالات.", "pantsdefault");
        var shoes = Product("cosmetic.shoes.default", "Running Shoes", "حذاء رياضي",
            "Made for the track.", "مصنوع للجري.", "shoesdefault");
        var bag = Product("cosmetic.bag.default", "Book Bag", "حقيبة كتب",
            "Carries the whole term.", "تتسع لكتب الفصل كله.", "bagdefault");

        var bundle = Product("bundle.starter.outfit", "Starter Outfit", "طقم البداية",
            "Every default piece, in one bundle.", "كل القطع الأساسية في حزمة واحدة.",
            "shirtdefault", "jacketdefault", "pantsdefault", "shoesdefault", "bagdefault");

        void Offer(string nameEn, string nameAr, string descEn, string descAr,
            long price, long? original, int sort, string? badge, DateTime? expires, params Guid[] productIds)
        {
            if (haveOffers.Contains(nameEn, StringComparer.Ordinal)) return;

            var id = SeedId.For("offer", nameEn);

            var offer = new Domain.Commerce.Offer
            {
                Id = id,
                Price = price,
                OriginalPrice = original,
                CurrencyId = coins,
                Availability = OfferAvailability.Available,
                PurchaseLimit = null,
                ExpiresAtUtc = expires,
                SortOrder = sort,
                BadgeKey = badge,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Translations =
                [
                    new OfferTranslation { OfferId = id, LangId = En, Name = nameEn, Description = descEn },
                    new OfferTranslation { OfferId = id, LangId = Ar, Name = nameAr, Description = descAr }
                ]
            };

            foreach (var productId in productIds)
                offer.Products.Add(new OfferProduct { OfferId = id, ProductId = productId });

            _db.Offers.Add(offer);
            report.Offers++;
        }

        Offer("Classic Shirt", "قميص كلاسيكي", "A clean everyday shirt.", "قميص أنيق لكل يوم.",
            250, null, 10, null, null, shirt);
        Offer("School Jacket", "جاكيت مدرسي", "Warm enough for the morning queue.", "دافئ بما يكفي لطابور الصباح.",
            350, null, 20, null, null, jacket);
        Offer("Everyday Trousers", "بنطلون يومي", "Comfortable trousers that go with anything.", "بنطلون مريح يناسب كل الإطلالات.",
            250, null, 30, null, null, pants);
        Offer("Running Shoes", "حذاء رياضي", "Made for the track.", "مصنوع للجري.",
            300, null, 40, null, null, shoes);
        Offer("Book Bag", "حقيبة كتب", "Carries the whole term.", "تتسع لكتب الفصل كله.",
            200, null, 50, null, null, bag);

        Offer("Starter Outfit", "طقم البداية", "Every default piece, in one bundle.", "كل القطع الأساسية في حزمة واحدة.",
            900, 1350, 1, "best_value", null, bundle);

        Offer("Back to School", "العودة إلى المدرسة", "Jacket and bag together, this fortnight only.",
            "الجاكيت والحقيبة معًا، لمدة أسبوعين فقط.",
            450, 550, 2, "limited", now.AddDays(14), jacket, bag);
    }

    // ── quests ────────────────────────────────────────────────────────────────

    private async Task ObjectivesAsync(Guid runnerId, ContentSeedReport report, CancellationToken ct)
    {
        var haveObjectives = await _db.Objectives.Select(o => o.Key).ToListAsync(ct);
        var haveGroups = await _db.ObjectiveGroups.Select(g => g.Key).ToListAsync(ct);
        var now = DateTime.UtcNow;

        Guid Group(string key, ObjectiveKind kind, GroupCompletionMode mode, int requiredCount,
            string nameEn, string nameAr, string descEn, string descAr, string icon, int sort)
        {
            var id = SeedId.For("objective-group", key);
            if (haveGroups.Contains(key, StringComparer.Ordinal)) return id;

            _db.ObjectiveGroups.Add(new ObjectiveGroup
            {
                Id = id,
                Key = key,
                Kind = kind,
                CompletionMode = mode,
                RequiredCount = requiredCount,
                SeasonKey = null,
                IconKey = icon,
                SortOrder = sort,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Translations =
                [
                    new ObjectiveGroupTranslation
                    {
                        Id = SeedId.For("objective-group-tr", key, "en"),
                        GroupId = id, LangId = En, Name = nameEn, Description = descEn
                    },
                    new ObjectiveGroupTranslation
                    {
                        Id = SeedId.For("objective-group-tr", key, "ar"),
                        GroupId = id, LangId = Ar, Name = nameAr, Description = descAr
                    }
                ]
            });

            report.ObjectiveGroups++;
            return id;
        }

        void Objective(string key, ObjectiveKind kind, string metric, long target,
            string nameEn, string nameAr, string descEn, string descAr,
            string icon, int sort, Guid? groupId = null, int step = 0, Guid? gameId = null)
        {
            if (haveObjectives.Contains(key, StringComparer.Ordinal)) return;

            var id = SeedId.For("objective", key);

            _db.Objectives.Add(new Domain.Objectives.Objective
            {
                Id = id,
                Key = key,
                Kind = kind,
                Metric = metric,
                Target = target,
                Aggregation = LeaderboardAggregation.Sum,
                GameId = gameId,
                IconKey = icon,
                GroupId = groupId,
                StepOrder = step,
                SortOrder = sort,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Translations =
                [
                    new ObjectiveTranslation
                    {
                        Id = SeedId.For("objective-tr", key, "en"),
                        ObjectiveId = id, LangId = En, Name = nameEn, Description = descEn
                    },
                    new ObjectiveTranslation
                    {
                        Id = SeedId.For("objective-tr", key, "ar"),
                        ObjectiveId = id, LangId = Ar, Name = nameAr, Description = descAr
                    }
                ]
            });

            report.Objectives++;
        }

        // Daily — four small asks that reset overnight, grouped so finishing all four pays again.
        var dailySet = Group("daily.all", ObjectiveKind.Daily, GroupCompletionMode.AllOf, 0,
            "Daily Set", "مهام اليوم",
            "Finish every daily quest.", "أنهِ كل مهام اليوم.", "quest_daily", 1);

        Objective("daily.lessons.3", ObjectiveKind.Daily, LeaderboardMetrics.LessonsCompleted, 3,
            "Finish 3 lessons", "أنهِ 3 دروس",
            "Complete three lessons today.", "أكمل ثلاثة دروس اليوم.", "quest_lesson", 10, dailySet);

        Objective("daily.aced.1", ObjectiveKind.Daily, LeaderboardMetrics.LessonsAced, 1,
            "Ace a lesson", "أتقن درسًا",
            "Finish a lesson with a perfect score.", "أنهِ درسًا بدرجة كاملة.", "quest_star", 20, dailySet);

        Objective("daily.runs.5", ObjectiveKind.Daily, LeaderboardMetrics.RunsCompleted, 5,
            "Complete 5 runs", "أكمل 5 جولات",
            "Finish five runs in Knowledge Runner.", "أنهِ خمس جولات في عدّاء المعرفة.",
            "quest_run", 30, dailySet, 0, runnerId);

        Objective("daily.pickups.100", ObjectiveKind.Daily, LeaderboardMetrics.PickupsCollected, 100,
            "Collect 100 coins", "اجمع 100 عملة",
            "Pick up a hundred coins while running.", "اجمع مئة عملة أثناء الجري.",
            "quest_coin", 40, dailySet, 0, runnerId);

        // Weekly — the same shapes at a size a week can actually hold.
        Objective("weekly.lessons.15", ObjectiveKind.Weekly, LeaderboardMetrics.LessonsCompleted, 15,
            "Finish 15 lessons", "أنهِ 15 درسًا",
            "Complete fifteen lessons this week.", "أكمل خمسة عشر درسًا هذا الأسبوع.", "quest_lesson", 10);

        Objective("weekly.aced.5", ObjectiveKind.Weekly, LeaderboardMetrics.LessonsAced, 5,
            "Ace 5 lessons", "أتقن 5 دروس",
            "Get a perfect score in five lessons.", "احصل على الدرجة الكاملة في خمسة دروس.", "quest_star", 20);

        Objective("weekly.currency.500", ObjectiveKind.Weekly, LeaderboardMetrics.CurrencyEarned, 500,
            "Earn 500 coins", "اكسب 500 عملة",
            "Earn five hundred coins this week.", "اكسب خمسمئة عملة هذا الأسبوع.", "quest_coin", 30);

        Objective("monthly.lessons.60", ObjectiveKind.Monthly, LeaderboardMetrics.LessonsCompleted, 60,
            "Finish 60 lessons", "أنهِ 60 درسًا",
            "Complete sixty lessons this month.", "أكمل ستين درسًا هذا الشهر.", "quest_lesson", 10);

        // Achievements — an ordered ladder, so the card shows one target at a time rather than three.
        var scholar = Group("achievement.scholar", ObjectiveKind.Achievement, GroupCompletionMode.Ordered, 0,
            "Scholar", "طالب مجتهد",
            "Work your way through the curriculum.", "تدرّج في المنهج خطوة بخطوة.", "badge_scholar", 1);

        Objective("achievement.lessons.10", ObjectiveKind.Achievement, LeaderboardMetrics.LessonsCompleted, 10,
            "Ten lessons", "عشرة دروس",
            "Complete ten lessons in total.", "أكمل عشرة دروس إجمالًا.", "badge_bronze", 10, scholar, 1);

        Objective("achievement.lessons.50", ObjectiveKind.Achievement, LeaderboardMetrics.LessonsCompleted, 50,
            "Fifty lessons", "خمسون درسًا",
            "Complete fifty lessons in total.", "أكمل خمسين درسًا إجمالًا.", "badge_silver", 20, scholar, 2);

        Objective("achievement.lessons.200", ObjectiveKind.Achievement, LeaderboardMetrics.LessonsCompleted, 200,
            "Two hundred lessons", "مئتا درس",
            "Complete two hundred lessons in total.", "أكمل مئتي درس إجمالًا.", "badge_gold", 30, scholar, 3);

        Objective("achievement.runs.100", ObjectiveKind.Achievement, LeaderboardMetrics.RunsCompleted, 100,
            "Marathon", "ماراثون",
            "Finish a hundred runs.", "أنهِ مئة جولة.", "badge_run", 40, null, 0, runnerId);
    }

    // ── boards ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authors the boards. Their cycles are not created here — <see cref="ILeaderboardRolloverService"/>
    /// opens the window covering now, and letting it do that is what keeps the seeded boards on the
    /// same rollover path as every board authored afterwards.
    /// </summary>
    private async Task BoardsAsync(Guid runnerId, ContentSeedReport report, CancellationToken ct)
    {
        var have = await _db.LeaderboardBoards.Select(b => b.BoardKey).ToListAsync(ct);
        var now = DateTime.UtcNow;

        void Add(string key, Guid? gameId, string metric, LeaderboardAggregation aggregation,
            LeaderboardPeriod period, string nameEn, string nameAr, string descEn, string descAr)
        {
            if (have.Contains(key, StringComparer.Ordinal)) return;

            var id = SeedId.For("board", key);

            _db.LeaderboardBoards.Add(new LeaderboardBoard
            {
                Id = id,
                BoardKey = key,
                GameId = gameId,
                Metric = metric,
                SortDirection = LeaderboardSortDirection.Desc,
                Aggregation = aggregation,
                Period = period,

                // All and Grade only. The other four cohorts on the enum have nothing in the schema
                // to resolve them from, and board authoring refuses them.
                SupportedCohorts = "All,Grade",
                VisibleRankLimit = 100,
                IsActive = true,
                GraceSeconds = 60,
                CreatedAtUtc = now,
                Translations =
                [
                    new LeaderboardBoardTranslation
                    {
                        Id = SeedId.For("board-tr", key, "en"),
                        BoardId = id, LangId = En, Name = nameEn, Description = descEn
                    },
                    new LeaderboardBoardTranslation
                    {
                        Id = SeedId.For("board-tr", key, "ar"),
                        BoardId = id, LangId = Ar, Name = nameAr, Description = descAr
                    }
                ]
            });

            report.LeaderboardBoards++;
        }

        Add("lessons.weekly", null, LeaderboardMetrics.LessonsCompleted,
            LeaderboardAggregation.Sum, LeaderboardPeriod.Weekly,
            "Lessons This Week", "دروس هذا الأسبوع",
            "Most lessons completed this week.", "الأكثر إنهاءً للدروس هذا الأسبوع.");

        Add("lessons.alltime", null, LeaderboardMetrics.LessonsCompleted,
            LeaderboardAggregation.Sum, LeaderboardPeriod.AllTime,
            "Lessons All Time", "الدروس على الإطلاق",
            "Most lessons completed ever.", "الأكثر إنهاءً للدروس على الإطلاق.");

        Add("lessons.aced.monthly", null, LeaderboardMetrics.LessonsAced,
            LeaderboardAggregation.Sum, LeaderboardPeriod.Monthly,
            "Perfect Scores", "الدرجات الكاملة",
            "Most lessons aced this month.", "الأكثر إتقانًا للدروس هذا الشهر.");

        Add("runner.pickups.weekly", runnerId, LeaderboardMetrics.PickupsCollected,
            LeaderboardAggregation.Sum, LeaderboardPeriod.Weekly,
            "Coin Collectors", "جامعو العملات",
            "Most coins picked up this week.", "الأكثر جمعًا للعملات هذا الأسبوع.");

        Add("runner.best.daily", runnerId, LeaderboardMetrics.BestRunSeconds,
            LeaderboardAggregation.Best, LeaderboardPeriod.Daily,
            "Longest Run Today", "أطول جولة اليوم",
            "Longest single run today.", "أطول جولة منفردة اليوم.");

        Add("currency.monthly", null, LeaderboardMetrics.CurrencyEarned,
            LeaderboardAggregation.Sum, LeaderboardPeriod.Monthly,
            "Top Earners", "الأكثر كسبًا",
            "Most coins earned this month.", "الأكثر كسبًا للعملات هذا الشهر.");
    }
}
