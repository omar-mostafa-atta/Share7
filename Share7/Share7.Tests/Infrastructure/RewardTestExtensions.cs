using Share7.Infrastructure.Objectives;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Share7.Application.Runs.Models;
using Share7.Infrastructure.Leaderboards;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Domain.Progress;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Evidence;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Play;
using Share7.Infrastructure.Progress;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Rewards;

namespace Share7.Tests.Infrastructure;

/// <summary>A currency and how much of it a rule pays.</summary>
public record GrantSpec(Guid CurrencyId, long Amount);

public static class RewardTestExtensions
{
    public static async Task<RewardRule> CreateRewardRuleAsync(
        this ApplicationDbContext context,
        RewardEventType eventType,
        GrantSpec[] grants,
        RewardRepeatPolicy repeatPolicy = RewardRepeatPolicy.Once,
        string? referenceKey = null,
        int? cooldownSeconds = null,
        int? dailyLimit = null,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rule = new RewardRule
        {
            Id = Guid.NewGuid(),
            Name = $"rule_{Guid.NewGuid():N}"[..16],
            EventType = eventType,
            ReferenceKey = referenceKey,
            RepeatPolicy = repeatPolicy,
            CooldownSeconds = cooldownSeconds,
            DailyLimit = dailyLimit,
            Enabled = enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var grant in grants)
        {
            rule.Grants.Add(new RewardRuleGrant
            {
                Id = Guid.NewGuid(),
                RewardRuleId = rule.Id,
                CurrencyId = grant.CurrencyId,
                Amount = grant.Amount
            });
        }

        context.RewardRules.Add(rule);
        await context.SaveChangesAsync(cancellationToken);
        return rule;
    }

    /// <summary>
    /// Runs the reward engine the way <c>ProgressService</c> does — inside a transaction it does
    /// not own — so the ambient-transaction composition is under test rather than bypassed.
    /// </summary>
    public static async Task<IReadOnlyList<RewardDto>> EvaluateRewardsAsync(
        this ApplicationDbContext context,
        ProgressRewardContext rewardContext,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var rewards = await new RewardService(context, new WalletService(context), new LevelService(context), new Share7.Infrastructure.Commerce.EntitlementService(context))
            .EvaluateProgressAttemptAsync(rewardContext, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return rewards;
    }

    public static ProgressRewardContext Attempt(
        Guid userId,
        CurriculumPathFixture path,
        CompletionState state = CompletionState.Aced,
        int attemptNumber = 1,
        string? requestId = null) => new()
    {
        UserId = userId,
        GameId = path.GameId,
        LessonId = path.LessonId,
        AttemptNumber = attemptNumber,
        Percent = state == CompletionState.Aced ? 100 : state == CompletionState.Completed ? 60 : 10,
        CompletionState = state,
        RequestId = requestId
    };

    /// <summary>
    /// The real service graph, wired by hand. No mocks: the point of these tests is that progress,
    /// unlocks, rewards and the wallet all compose over **one** DbContext and therefore one
    /// transaction — a substitute for any of them would hide exactly that.
    /// </summary>
    /// <summary>
    /// <paramref name="langId"/> is the content language the caller is treated as reading in —
    /// what a real access token's claim would carry. It defaults to English so every existing call
    /// site is unchanged, and is passed explicitly by the tests that care what happens when a
    /// learner switches language mid-course.
    /// </summary>
    public static ProgressService CreateProgressService(
        ApplicationDbContext context, Guid userId, Guid? langId = null)
    {
        var wallet = new WalletService(context);

        // The real recorder, not a stub: it writes into the same DbContext and therefore the same
        // transaction as the attempt, and that is precisely the property worth testing — a result
        // that survived a rolled-back attempt would be a rank for gameplay that never happened.
        return new ProgressService(
            context,
            new LanguageService(context, new StubCurrentUser(userId, langId)),
            new UnlockService(context),
            new RewardService(context, wallet, new LevelService(context), new Share7.Infrastructure.Commerce.EntitlementService(context)),
            wallet,
            new GameResultRecorder(
                context,
                new PlausibilityGuard(context),
                NullLogger<GameResultRecorder>.Instance),
            new LevelService(context),
            // The real pricer: an attempt's variable XP is granted through it, inside the attempt's
            // own transaction, and a stub would hide the only part of that worth testing.
            new SignalPricer(context, new EarnCeilingService(context), Options.Create(new RunOptions())),
            new ObjectiveProjector(context, NullLogger<ObjectiveProjector>.Instance),
            // The real resolver: whether an attempt moves mastery at all is its decision, and a stub
            // would let every test pass under a policy nothing ships.
            new PlaySelectionResolver(context, new LevelService(context)),
            // The real recorder, over the same context. The evidence log is the one thing every
            // attempt writes regardless of what it settles for, so a stub here would let a test
            // pass while the attempt recorded nothing at all.
            new EvidenceRecorder(context, NullLogger<EvidenceRecorder>.Instance));
    }

    /// <summary>
    /// Opens a lesson directly. The seeding path picks the first lesson of the grade by
    /// <c>Order</c>, which is whichever fixture happened to be created first — so tests that need
    /// a specific lesson playable grant it themselves.
    /// </summary>
    public static async Task UnlockLessonAsync(
        this ApplicationDbContext context,
        Guid userId,
        CurriculumPathFixture path,
        CancellationToken cancellationToken = default)
    {
        context.UserNodeUnlocks.Add(new UserNodeUnlock
        {
            UserId = userId,
            GameId = path.GameId,
            NodeType = CurriculumNodeType.Lesson,
            NodeId = path.LessonId,
            UnlockedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds questions to a lesson and returns their correct choice ids, so a test can submit a
    /// chosen fraction of them and land on an exact percentage.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> AddQuestionsAsync(
        this ApplicationDbContext context,
        Guid lessonId,
        int count,
        CancellationToken cancellationToken = default)
    {
        var correctChoiceIds = new List<Guid>();

        // The real minter, not a stand-in. A question with no item identity cannot be measured, so
        // the fixture goes through the same path the importer does rather than inventing ids that
        // would let a test pass over content production could never produce.
        var minter = new ItemIdentityMinter(context);
        var now = DateTime.UtcNow;

        // Row numbers continue past whatever the lesson already has. They are half of an item's
        // lineage key, so reusing row 1 on a lesson that already has one would make two unrelated
        // questions the same item — which the importer never does (it retires a set before
        // inserting the next) and which would quietly double every attempt ordinal.
        var firstRow = await context.Questions
            .Where(q => q.LessonId == lessonId)
            .Select(q => (int?)q.RowNumber)
            .MaxAsync(cancellationToken) ?? 0;

        for (var i = 0; i < count; i++)
        {
            var rowNumber = firstRow + i + 1;

            var itemVersion = await minter.ResolveForLessonRowAsync(
                lessonId, rowNumber, 1, NodeItemRole.Core, now, cancellationToken);

            var question = new Question
            {
                Id = Guid.NewGuid(),
                ItemVersionId = itemVersion.Id,
                ItemVersion = itemVersion,
                LessonId = lessonId,
                LangId = LanguageIds.English,
                Text = $"Question {i}",
                Version = 1,
                RowNumber = rowNumber,
                CreatedAt = now
            };

            // Three real choices, like the importer writes — grading now checks that a submitted
            // choice actually belongs to its question, so a question with no choices is ungradeable.
            question.Choices = Enumerable.Range(0, 3)
                .Select(index => new QuestionChoice
                {
                    Id = Guid.NewGuid(),
                    QuestionId = question.Id,
                    Text = $"Choice {index}",
                    OrderIndex = index
                })
                .ToList();

            question.CorrectChoiceId = question.Choices.First().Id;

            context.Questions.Add(question);
            correctChoiceIds.Add(question.CorrectChoiceId);
        }

        await context.SaveChangesAsync(cancellationToken);
        return correctChoiceIds;
    }

    public static Task<List<RewardTransaction>> RewardTransactionsOfAsync(
        this ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.RewardTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    private sealed class StubCurrentUser : ICurrentUserService
    {
        public StubCurrentUser(Guid userId, Guid? langId = null)
        {
            UserId = userId;
            PreferredLanguageId = langId ?? LanguageIds.English;
        }

        public Guid? UserId { get; }
        public string? Email => null;
        public bool IsAuthenticated => true;
        public Guid? PreferredLanguageId { get; }
    }
}
