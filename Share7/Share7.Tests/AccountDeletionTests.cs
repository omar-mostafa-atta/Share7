using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Economy;
using Share7.Domain.Commerce;
using Share7.Domain.Entities;
using Share7.Domain.Progress;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Users;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class AccountDeletionTests
{
    private readonly SqlServerFixture _fixture;

    public AccountDeletionTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Deleting_an_account_removes_every_trace_of_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await SeedUserWithDataAsync(context);

        var rewardTransactionId = await context.RewardTransactions
            .Where(t => t.UserId == userId)
            .Select(t => t.Id)
            .SingleAsync();

        var productId = await context.Entitlements
            .Where(e => e.UserId == userId)
            .Select(e => e.ProductId)
            .SingleAsync();

        var service = CreateService(context);
        var result = await service.DeleteOwnAccountAsync(userId);

        Assert.True(result.Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await check.RefreshTokens.AnyAsync(r => r.UserId == userId));
        Assert.False(await check.StudentProfiles.AnyAsync(p => p.UserId == userId));
        Assert.False(await check.UserLessonProgress.AnyAsync(p => p.UserId == userId));
        Assert.False(await check.UserQuestionProgress.AnyAsync(p => p.UserId == userId));
        Assert.False(await check.UserNodeUnlocks.AnyAsync(u => u.UserId == userId));

        // Cascades rather than being swept by hand, so worth asserting separately — this is the
        // path that breaks silently if the FK is ever changed to Restrict.
        Assert.False(await check.UserCurrencyBalances.AnyAsync(b => b.UserId == userId));
        Assert.False(await check.CurrencyLedgerEntries.AnyAsync(e => e.UserId == userId));
        Assert.False(await check.RewardTransactions.AnyAsync(t => t.UserId == userId));

        // Reward lines carry no UserId, so the coverage test cannot see them at all — they only go
        // if the cascade runs two levels, from the account through the transaction. Asserted here
        // because nothing else would notice it breaking.
        Assert.False(await check.RewardTransactionLines.AnyAsync(l => l.RewardTransactionId == rewardTransactionId));
        Assert.False(await check.Entitlements.AnyAsync(e => e.UserId == userId));

        // But the catalogue is not the account's to take with it. Deleting a buyer must not remove
        // the product other accounts still own — the FK is Restrict precisely to stop that.
        Assert.True(await check.Products.AnyAsync(p => p.Id == productId));
        Assert.True(await check.ProductGrants.AnyAsync(g => g.ProductId == productId));
    }

    [Fact]
    public async Task Deletion_revokes_every_session()
    {
        await using var context = _fixture.CreateContext();
        var userId = await SeedUserWithDataAsync(context);

        context.RefreshTokens.AddRange(
            NewToken(userId),
            NewToken(userId),
            NewToken(userId));
        await context.SaveChangesAsync();

        Assert.Equal(4, await context.RefreshTokens.CountAsync(r => r.UserId == userId));

        await CreateService(context).DeleteOwnAccountAsync(userId);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.RefreshTokens.CountAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task Deletion_is_idempotent()
    {
        await using var context = _fixture.CreateContext();
        var userId = await SeedUserWithDataAsync(context);

        var service = CreateService(context);

        var first = await service.DeleteOwnAccountAsync(userId);
        var second = await service.DeleteOwnAccountAsync(userId);
        var third = await service.DeleteOwnAccountAsync(userId);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(third.Succeeded);
    }

    [Fact]
    public async Task Deleting_an_account_that_never_existed_succeeds()
    {
        await using var context = _fixture.CreateContext();

        var result = await CreateService(context).DeleteOwnAccountAsync(Guid.NewGuid());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Deletion_leaves_other_accounts_untouched()
    {
        await using var context = _fixture.CreateContext();
        var victim = await SeedUserWithDataAsync(context);
        var bystander = await SeedUserWithDataAsync(context);

        await CreateService(context).DeleteOwnAccountAsync(victim);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.Users.AnyAsync(u => u.Id == bystander));
        Assert.True(await check.RefreshTokens.AnyAsync(r => r.UserId == bystander));
        Assert.True(await check.StudentProfiles.AnyAsync(p => p.UserId == bystander));
        Assert.True(await check.UserLessonProgress.AnyAsync(p => p.UserId == bystander));
        Assert.True(await check.UserCurrencyBalances.AnyAsync(b => b.UserId == bystander));
    }

    [Fact]
    public async Task Deletion_leaves_no_orphaned_rows_anywhere()
    {
        await using var context = _fixture.CreateContext();
        var userId = await SeedUserWithDataAsync(context);

        await CreateService(context).DeleteOwnAccountAsync(userId);

        await using var check = _fixture.CreateContext();
        foreach (var clrType in UserOwnedData.ManuallyPurged)
        {
            var entityType = check.Model.FindEntityType(clrType)!;
            var table = entityType.GetTableName();

            var orphans = await check.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM [{table}] t LEFT JOIN [AspNetUsers] u ON u.[Id] = t.[UserId] WHERE u.[Id] IS NULL")
                .SingleAsync();

            Assert.True(orphans == 0, $"{table} still holds {orphans} row(s) with no owning account.");
        }
    }

    private AccountDeletionService CreateService(ApplicationDbContext context) =>
        new(IdentityTestHost.CreateUserManager(context), context);

    private static RefreshToken NewToken(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Token = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(7)
    };

    /// <summary>An account with at least one row in every table deletion is meant to clear.</summary>
    private static async Task<Guid> SeedUserWithDataAsync(ApplicationDbContext context)
    {
        var userId = await TestData.CreateUserAsync(context);

        // Progress rows are foreign-keyed down to Lessons and across to Games, so the whole path
        // has to exist before any of them will insert.
        var path = await TestData.CreateCurriculumPathAsync(context);
        var gradeId = path.GradeId;
        var lessonId = path.LessonId;
        var gameId = path.GameId;

        context.RefreshTokens.Add(NewToken(userId));

        context.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = "Test Student",
            Age = 12,
            PhoneNumber = "0100000000",
            GradeId = gradeId,
            CreatedAt = DateTime.UtcNow
        });

        context.UserLessonProgress.Add(new UserLessonProgress
        {
            UserId = userId,
            GameId = gameId,
            LessonId = lessonId,
            CompletionState = CompletionState.Completed,
            CorrectCount = 4,
            TotalCount = 5,
            Percent = 80,
            Attempts = 1,
            QuestionsVersion = 1,
            LastAttemptAt = DateTime.UtcNow
        });

        context.UserQuestionProgress.Add(new UserQuestionProgress
        {
            UserId = userId,
            GameId = gameId,
            LessonId = lessonId,
            QuestionId = path.QuestionId,
            IsCorrect = true,
            Attempts = 1,
            LastAttemptAt = DateTime.UtcNow
        });

        context.UserNodeUnlocks.Add(new UserNodeUnlock
        {
            UserId = userId,
            GameId = gameId,
            NodeType = CurriculumNodeType.Lesson,
            NodeId = lessonId,
            UnlockedAt = DateTime.UtcNow
        });

        var currency = await context.Currencies.FirstOrDefaultAsync();
        if (currency is null)
        {
            currency = new Currency
            {
                Id = Guid.NewGuid(),
                Key = "coins",
                Name = "Coins",
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Currencies.Add(currency);
        }

        context.UserCurrencyBalances.Add(new UserCurrencyBalance
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CurrencyId = currency.Id,
            Amount = 100,
            UpdatedAtUtc = DateTime.UtcNow
        });

        // Reward history. Scoped to this seed's own lesson so the rule cannot match, and pay out
        // inside, an unrelated test in the collection.
        var rule = new RewardRule
        {
            Id = Guid.NewGuid(),
            Name = "deletion fixture",
            EventType = RewardEventType.LessonCompleted,
            ReferenceKey = lessonId.ToString(),
            RepeatPolicy = RewardRepeatPolicy.Once,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        rule.Grants.Add(new RewardRuleGrant
        {
            Id = Guid.NewGuid(),
            RewardRuleId = rule.Id,
            CurrencyId = currency.Id,
            Amount = 10
        });

        var rewardTransaction = new RewardTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RewardRuleId = rule.Id,
            EventType = RewardEventType.LessonCompleted,
            SourceType = LedgerSourceType.ProgressAttempt,
            SourceId = lessonId.ToString(),
            IdempotencyKey = $"LESSON_COMPLETED:{gameId}:{lessonId}",
            SubmissionKey = $"{gameId}:{lessonId}:n1",
            CreatedAtUtc = DateTime.UtcNow
        };

        rewardTransaction.Lines.Add(new RewardTransactionLine
        {
            RewardTransactionId = rewardTransaction.Id,
            CurrencyId = currency.Id,
            Amount = 10,
            BalanceAfter = 100,
            LedgerEntryId = 0
        });

        context.RewardRules.Add(rule);
        context.RewardTransactions.Add(rewardTransaction);

        // Commerce ownership. The product itself is catalogue data and must survive the account —
        // only the entitlement pointing at it goes.
        var productKind = new ProductKind
        {
            Id = Guid.NewGuid(),
            Name = $"Kind{Guid.NewGuid():N}"[..12]
        };

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Key = $"p_{Guid.NewGuid():N}"[..20],
            ProductKindId = productKind.Id
        };

        product.Grants.Add(new ProductGrant
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Reference = "cosmetic_deletion_fixture",
            Quantity = 1
        });

        context.ProductKinds.Add(productKind);
        context.Products.Add(product);

        context.Entitlements.Add(new Entitlement
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = product.Id,
            GrantedAtUtc = DateTime.UtcNow,
            Source = EntitlementSource.Purchase,
            SourceId = "txn-fixture"
        });

        context.CurrencyLedgerEntries.Add(new CurrencyLedgerEntry
        {
            UserId = userId,
            CurrencyId = currency.Id,
            Amount = 10,
            BalanceAfter = 100,
            TransactionType = CurrencyTransactionType.LessonReward,
            SourceType = LedgerSourceType.ProgressAttempt,
            SourceId = lessonId.ToString(),
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }
}
