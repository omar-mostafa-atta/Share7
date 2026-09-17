using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Infrastructure.Persistence;
using Xunit;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// A throwaway SQL Server database, migrated from scratch once per test run and dropped at the
/// end.
/// <para>
/// **Deliberately not an in-memory provider.** Everything worth testing here is behaviour the
/// database owns rather than the C#: <c>UPDATE … SET Amount = Amount + @delta</c>, unique-index
/// violations under concurrency, transaction rollback, and cascade deletes. The in-memory
/// provider has no unique-constraint enforcement and no real transactions, so it would pass
/// every one of those tests while the production database failed them.
/// </para>
/// <para>
/// Server defaults to local SQL Express; override with <c>SHARE7_TEST_SQL_SERVER</c> for CI.
/// </para>
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private readonly string _database = $"Share7_Test_{Guid.NewGuid():N}";

    private static string Server =>
        Environment.GetEnvironmentVariable("SHARE7_TEST_SQL_SERVER") ?? @".\SQLEXPRESS";

    public string ConnectionString =>
        $"Data Source={Server};Initial Catalog={_database};Integrated Security=SSPI;TrustServerCertificate=True;";

    private static string MasterConnectionString =>
        $"Data Source={Server};Initial Catalog=master;Integrated Security=SSPI;TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();

        // Sessions left open by a failed test would block the drop, so evict them first.
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID('{_database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_database}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A fresh context per call. Tests that exercise concurrency need genuinely separate
    /// contexts — sharing one would serialise the very race being tested.
    /// </summary>
    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .EnableSensitiveDataLogging()
            .Options;

        return new ApplicationDbContext(options);
    }
}

/// <summary>
/// Shares one migrated database across every test class in the collection. Migrating from
/// scratch takes a few seconds, so doing it per class would dominate the run time.
/// </summary>
[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}
