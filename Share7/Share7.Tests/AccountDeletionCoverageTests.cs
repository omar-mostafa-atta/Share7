using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Share7.Infrastructure.Users;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The guard the deletion contract asks for: adding a user-owned table without wiring it into
/// deletion has to fail a test, not reach production.
/// <para>
/// This does not enumerate known tables by hand — a hand-written list is exactly the thing that
/// goes stale. It walks the live EF model, finds everything keyed by <c>UserId</c>, and demands
/// each one be covered either by a cascading foreign key or by the explicit purge list.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AccountDeletionCoverageTests
{
    private readonly SqlServerFixture _fixture;

    public AccountDeletionCoverageTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public void Every_user_keyed_table_is_covered_by_deletion()
    {
        using var context = _fixture.CreateContext();

        var uncovered = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var userIdProperty = entityType.FindProperty("UserId");
            if (userIdProperty is null)
                continue;

            if (UserOwnedData.ManuallyPurged.Contains(entityType.ClrType))
                continue;

            if (CascadesFromUser(entityType, userIdProperty))
                continue;

            uncovered.Add(entityType.ClrType.Name);
        }

        Assert.True(
            uncovered.Count == 0,
            $"""
             These tables are keyed by UserId but nothing removes their rows when the account goes:
                 {string.Join(Environment.NewLine + "    ", uncovered)}

             Fix by either adding the type to UserOwnedData.ManuallyPurged, or giving it a
             foreign key to AspNetUsers with OnDelete(DeleteBehavior.Cascade) in its EF
             configuration — the second is preferable, since the database then enforces it.
             """);
    }

    [Fact]
    public void Manually_purged_types_are_all_still_mapped()
    {
        // Catches the opposite mistake: a table dropped from the model but left in the list,
        // which would make PurgeAsync throw at the worst possible moment — mid-deletion.
        using var context = _fixture.CreateContext();

        foreach (var clrType in UserOwnedData.ManuallyPurged)
        {
            var entityType = context.Model.FindEntityType(clrType);
            Assert.True(entityType is not null, $"{clrType.Name} is in ManuallyPurged but not in the EF model.");
            Assert.NotNull(entityType!.GetTableName());
            Assert.NotNull(entityType.FindProperty("UserId"));
        }
    }

    private static bool CascadesFromUser(IEntityType entityType, IProperty userIdProperty) =>
        entityType.GetForeignKeys().Any(fk =>
            fk.DeleteBehavior == DeleteBehavior.Cascade &&
            fk.Properties.Contains(userIdProperty) &&
            fk.PrincipalEntityType.GetTableName() == "AspNetUsers");
}
