using Microsoft.EntityFrameworkCore;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Proves the harness itself works before anything relies on it: the scratch database migrates
/// from scratch, and it enforces constraints rather than quietly accepting anything — which is
/// the whole reason these tests use real SQL Server.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class InfrastructureSmokeTests
{
    private readonly SqlServerFixture _fixture;

    public InfrastructureSmokeTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Database_migrates_from_scratch()
    {
        await using var context = _fixture.CreateContext();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Seeded_lookup_data_is_present()
    {
        await using var context = _fixture.CreateContext();

        Assert.Equal(2, await context.Languages.CountAsync());
        Assert.Equal(14, await context.Grades.CountAsync());
    }

    [Fact]
    public async Task Unique_constraints_are_actually_enforced()
    {
        // If this ever passes silently, the tests have been pointed at a provider that does not
        // enforce indexes and every concurrency assertion below is worthless.
        await using var context = _fixture.CreateContext();

        var username = $"dupe_{Guid.NewGuid():N}";
        await TestData.CreateUserAsync(context, username);

        await using var second = _fixture.CreateContext();
        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => TestData.CreateUserAsync(second, username));
    }
}
