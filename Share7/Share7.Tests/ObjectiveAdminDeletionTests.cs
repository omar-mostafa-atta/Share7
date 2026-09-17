using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Objectives;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Deleting an authored objective.
/// <para>
/// Retiring is the operation an operator almost always wants, and delete exists for the objective
/// authored by mistake. What these tests fix is the boundary between the two: a delete that would
/// take somebody's finished quest with it has to be refused and has to say what it would cost,
/// while an objective nobody has touched goes quietly.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ObjectiveAdminDeletionTests
{
    private readonly SqlServerFixture _fixture;

    public ObjectiveAdminDeletionTests(SqlServerFixture fixture) => _fixture = fixture;

    private static ObjectiveAdminService Admin(ApplicationDbContext context) => new(context);

    [Fact]
    public async Task An_objective_nobody_has_played_deletes_without_a_force()
    {
        await using var context = _fixture.CreateContext();
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 3);

        var result = await Admin(context).DeleteAsync(objective.Id, force: false);

        Assert.True(result.Succeeded);
        Assert.False(await context.Objectives.AnyAsync(o => o.Id == objective.Id));

        // The translations go with it — they cascade from Objectives rather than being orphaned.
        Assert.False(await context.ObjectiveTranslations.AnyAsync(t => t.ObjectiveId == objective.Id));
    }

    [Fact]
    public async Task A_played_objective_is_refused_and_reports_what_would_be_destroyed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 1);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var result = await Admin(context).DeleteAsync(objective.Id, force: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);

        // The refusal carries the breakdown, not just a sentence: the admin console renders it into
        // the second confirm, so a caller cannot reach force=true without being shown the cost.
        Assert.NotNull(result.Value);
        Assert.True(result.Value!.HasProgress);
        Assert.Equal(1, result.Value.ProgressRows);
        Assert.Equal(1, result.Value.Students);
        Assert.Equal(1, result.Value.Completed);

        // And nothing was deleted on the way to being refused.
        Assert.True(await context.Objectives.AnyAsync(o => o.Id == objective.Id));
    }

    [Fact]
    public async Task Force_deletes_the_objective_and_every_counter_against_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 1);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var result = await Admin(context).DeleteAsync(objective.Id, force: true);

        Assert.True(result.Succeeded);
        Assert.False(await context.Objectives.AnyAsync(o => o.Id == objective.Id));

        // Progress cascades from the objective. If it did not, these rows would outlive the row
        // that gives them meaning.
        Assert.False(await context.UserObjectiveProgress.AnyAsync(p => p.ObjectiveId == objective.Id));
    }

    [Fact]
    public async Task Deleting_an_objective_that_is_not_there_is_a_not_found()
    {
        await using var context = _fixture.CreateContext();

        var result = await Admin(context).DeleteAsync(Guid.NewGuid(), force: true);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Retiring_leaves_the_row_and_its_progress_intact()
    {
        // The alternative the refusal points at. Worth pinning next to the delete tests: if these
        // two ever behaved the same, the advice on the console footer would be wrong.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var objective = await context.CreateObjectiveAsync(LeaderboardMetrics.RunsSettled, target: 1);

        await context.AddResultAsync(userId, LeaderboardMetrics.RunsSettled, 1);
        await ObjectiveTestExtensions.CreateProjector(context).ProjectForUserAsync(userId);

        var stored = await context.Objectives.FirstAsync(o => o.Id == objective.Id);
        stored.IsActive = false;
        await context.SaveChangesAsync();

        Assert.True(await context.Objectives.AnyAsync(o => o.Id == objective.Id && !o.IsActive));
        Assert.True(await context.UserObjectiveProgress.AnyAsync(p => p.ObjectiveId == objective.Id));
    }
}
