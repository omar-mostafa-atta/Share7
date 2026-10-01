using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// Commits a competing write at an exact point inside another operation — immediately before a
/// chosen command of that operation reaches the database.
/// <para>
/// **Races are proven by interleaving, not by hoping.** Firing two requests at once and asserting on
/// the outcome passes or fails with the thread scheduler. This puts the competing commit precisely in
/// the window between an operation's reads and its writes, on every run — the window a real host
/// heartbeat or a real join lands in once every few thousand requests in production.
/// </para>
/// <para>
/// The competing write must run on its own context (its own connection), or it would join the very
/// transaction whose isolation it is testing.
/// </para>
/// </summary>
public sealed class InterleavingInterceptor : DbCommandInterceptor
{
    private readonly Func<Task> _competingWrite;
    private readonly Func<DbCommand, bool> _trigger;
    private int _fired;

    public InterleavingInterceptor(Func<Task> competingWrite, Func<DbCommand, bool>? trigger = null)
    {
        _competingWrite = competingWrite;
        _trigger = trigger ?? (command => IsAction(command.CommandText));
    }

    /// <summary>Whether the competing write actually ran. A race test that never raced proves nothing.</summary>
    public bool Fired => Volatile.Read(ref _fired) == 1;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await MaybeFireAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await MaybeFireAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await MaybeFireAsync(command);
        return result;
    }

    private async Task MaybeFireAsync(DbCommand command)
    {
        if (!_trigger(command))
            return;

        // Once only. The competing write itself is on another context, so it never re-enters here,
        // but a second trigger in the same operation would stop being "one race" and become noise.
        if (Interlocked.Exchange(ref _fired, 1) == 1)
            return;

        await _competingWrite();
    }

    /// <summary>
    /// The moment an operation stops reading and starts acting: its first write, or its first lock
    /// taken to write under.
    /// <para>
    /// **The lock matters as much as the write.** A competing write fired *after* the operation holds
    /// the session row's update lock would wait for that lock while the operation waits for the
    /// competing write — a deadlock that exists only in this harness, since in production the second
    /// writer would simply queue behind the first. Firing before the lock tests the real window.
    /// </para>
    /// </summary>
    public static bool IsAction(string sql) =>
        IsWrite(sql) || sql.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase);

    /// <summary>A statement that changes a row.</summary>
    public static bool IsWrite(string sql) =>
        sql.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
        || sql.Contains("INSERT ", StringComparison.OrdinalIgnoreCase)
        || sql.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)
        || sql.Contains("MERGE ", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether any parameter of the command carries <paramref name="value"/>.</summary>
    public static bool HasParameter(DbCommand command, Guid value) =>
        command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid g && g == value);
}

public static class InterleavingContexts
{
    /// <summary>A context on the fixture's database with <paramref name="interceptor"/> installed.</summary>
    public static ApplicationDbContext CreateInterleavedContext(
        this SqlServerFixture fixture, InterleavingInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(fixture.ConnectionString, sql => sql.CommandTimeout(30))
            .AddInterceptors(interceptor)
            .EnableSensitiveDataLogging()
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// What a host heartbeat does to the session row: advances the clock, and with it the row version.
    /// Written as the statement itself rather than through the service so the race is exactly one write.
    /// </summary>
    public static async Task SimulateHeartbeatCommitAsync(this SqlServerFixture fixture, Guid sessionId)
    {
        await using var context = fixture.CreateContext();

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE [MultiplayerSessions] SET [LastHeartbeatAtUtc] = SYSUTCDATETIME() WHERE [Id] = {0}",
            sessionId);
    }
}
