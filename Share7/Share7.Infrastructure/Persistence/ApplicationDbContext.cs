using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Commerce;
using Share7.Domain.Curriculum;
using Share7.Domain.Economy;
using Share7.Domain.Entities;
using Share7.Domain.Equipment;
using Share7.Domain.Games;
using Share7.Domain.LookUps;
using Share7.Domain.Multiplayer;
using Share7.Domain.Progress;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();

    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionChoice> QuestionChoices => Set<QuestionChoice>();
    public DbSet<LessonQuestionUpload> LessonQuestionUploads => Set<LessonQuestionUpload>();

    // The secondary per-lesson pool. Structurally a clone of the four tables above, kept apart so
    // the two pools carry independent versions and one can be re-uploaded without disturbing the
    // other. Trigger logic (when the game shows these) is the client's, not the backend's.
    public DbSet<RecoveryQuestion> RecoveryQuestions => Set<RecoveryQuestion>();
    public DbSet<RecoveryQuestionChoice> RecoveryQuestionChoices => Set<RecoveryQuestionChoice>();
    public DbSet<LessonRecoveryQuestionUpload> LessonRecoveryQuestionUploads => Set<LessonRecoveryQuestionUpload>();

    // The tree carries no text of its own — every node's name lives in its translations.
    public DbSet<GradeTranslation> GradeTranslations => Set<GradeTranslation>();
    public DbSet<TermTranslation> TermTranslations => Set<TermTranslation>();
    public DbSet<SubjectTranslation> SubjectTranslations => Set<SubjectTranslation>();
    public DbSet<ChapterTranslation> ChapterTranslations => Set<ChapterTranslation>();
    public DbSet<LessonTranslation> LessonTranslations => Set<LessonTranslation>();

    /// <summary>Per-language question versions — what <c>Lessons.QuestionsVersion</c> used to be.</summary>
    public DbSet<LessonQuestionSet> LessonQuestionSets => Set<LessonQuestionSet>();

    /// <summary>Per-language recovery-question versions — the same protocol, its own counter.</summary>
    public DbSet<LessonRecoveryQuestionSet> LessonRecoveryQuestionSets => Set<LessonRecoveryQuestionSet>();

    // Avatar outfits. One row per user — the two lists are JSON columns rather than child tables
    // because they are only ever read and written whole.
    public DbSet<UserEquipment> Equipments => Set<UserEquipment>();

    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameTranslation> GameTranslations => Set<GameTranslation>();

    // Progress is per (user, game). Nothing above lesson level is stored — chapter, subject and
    // term progress are GROUP BY queries over UserLessonProgress.
    public DbSet<UserQuestionProgress> UserQuestionProgress => Set<UserQuestionProgress>();
    public DbSet<UserLessonProgress> UserLessonProgress => Set<UserLessonProgress>();
    public DbSet<UserNodeUnlock> UserNodeUnlocks => Set<UserNodeUnlock>();

    // Economy. Virtual currency only — nothing here represents real money. UserCurrencyBalances
    // is the authoritative wallet and the fast projection; CurrencyLedgerEntries is the
    // append-only truth the balances can be rebuilt from.
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<UserCurrencyBalance> UserCurrencyBalances => Set<UserCurrencyBalance>();
    public DbSet<CurrencyLedgerEntry> CurrencyLedgerEntries => Set<CurrencyLedgerEntry>();

    // Rewards. Configuration (what an outcome is worth) is kept apart from the economy (what a
    // balance is) so that a later achievements or events module can pay through the same wallet
    // without either domain knowing about the other.
    public DbSet<RewardRule> RewardRules => Set<RewardRule>();
    public DbSet<RewardRuleGrant> RewardRuleGrants => Set<RewardRuleGrant>();
    public DbSet<RewardTransaction> RewardTransactions => Set<RewardTransaction>();
    public DbSet<RewardTransactionLine> RewardTransactionLines => Set<RewardTransactionLine>();

    // Commerce. Products are what is sold and Entitlements are who owns them; price and
    // availability belong to Offers, which is why neither of these tables carries one. Ownership
    // outlives the shop — an entitlement stays resolvable after its product is delisted.
    // Shop text is per language, like the curriculum tree: the product and kind rows carry no
    // display name at all, so one product has one id in every language and an entitlement survives
    // a language switch. ProductKind.Name is the exception — it is the client's `kind` token, not
    // a label, so it stays untranslated on the parent row.
    public DbSet<ProductKind> ProductKinds => Set<ProductKind>();
    public DbSet<ProductKindTranslation> ProductKindTranslations => Set<ProductKindTranslation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<ProductGrant> ProductGrants => Set<ProductGrant>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    // Offers are what a product costs; products are what it hands over. One offer can sell several
    // products, so the link is its own table. PurchaseTransactions is append-only and records
    // refusals as well as successes — it is both the audit trail and the idempotency record.
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferTranslation> OfferTranslations => Set<OfferTranslation>();
    public DbSet<OfferProduct> OfferProducts => Set<OfferProduct>();
    public DbSet<PurchaseTransaction> PurchaseTransactions => Set<PurchaseTransaction>();

    // Multiplayer lobbies. Photon Fusion owns the realtime connection; these tables own everything
    // it cannot arbitrate — who is allowed in, how many fit, who is host, and whether the session
    // still exists. Capacity and single-membership are enforced by filtered unique indexes rather
    // than by service-layer checks, so they hold under genuine concurrency.
    public DbSet<MultiplayerSession> MultiplayerSessions => Set<MultiplayerSession>();
    public DbSet<MultiplayerSessionPlayer> MultiplayerSessionPlayers => Set<MultiplayerSessionPlayer>();

    /// <summary>Idempotency keys for multiplayer operations. **Successes only** — see the entity.</summary>
    public DbSet<MultiplayerRequestLog> MultiplayerRequestLogs => Set<MultiplayerRequestLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
