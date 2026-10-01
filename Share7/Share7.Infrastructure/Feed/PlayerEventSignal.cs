using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Share7.Domain.Feed;

namespace Share7.Infrastructure.Feed;

/// <summary>
/// Wakes a waiting feed read the moment an event for its player commits on this server instance.
/// <para>
/// **An accelerator, never the source of truth.** A read that misses a wake-up — the event committed
/// on another instance, or the wake landed between two reads — still finds the event on its next
/// database check (<c>EventFallbackPollSeconds</c>). What this saves is the wait, not correctness.
/// </para>
/// <para>
/// Arm before reading, wait after: a reader takes its wake-up handle *before* querying, so an event
/// committing between the query and the wait still wakes it.
/// </para>
/// </summary>
public sealed class PlayerEventSignal
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiters = new();

    /// <summary>A task that completes when an event for <paramref name="recipient"/> commits.</summary>
    public Task Arm(Guid recipient) =>
        _waiters.GetOrAdd(recipient, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public void Notify(IEnumerable<Guid> recipients)
    {
        foreach (var recipient in recipients.Distinct())
        {
            if (_waiters.TryRemove(recipient, out var waiter))
                waiter.TrySetResult();
        }
    }
}

/// <summary>
/// Which recipients a context has written events for and not yet seen committed. Shared by the two
/// interceptors below, which EF requires to be separate types.
/// </summary>
public sealed class PlayerEventCommitTracker
{
    private readonly ConditionalWeakTable<DbContext, List<Guid>> _saving = new();
    private readonly ConditionalWeakTable<DbContext, List<Guid>> _uncommitted = new();
    private readonly PlayerEventSignal _signal;

    public PlayerEventCommitTracker(PlayerEventSignal signal) => _signal = signal;

    internal void Saving(DbContext context)
    {
        var recipients = context.ChangeTracker.Entries<PlayerEvent>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.RecipientUserId)
            .ToList();

        if (recipients.Count > 0)
            _saving.AddOrUpdate(context, recipients);
    }

    internal void Saved(DbContext context)
    {
        if (!_saving.TryGetValue(context, out var recipients))
            return;

        _saving.Remove(context);

        // No explicit transaction: SaveChanges committed its own, so the rows are visible now.
        if (context.Database.CurrentTransaction is null)
        {
            _signal.Notify(recipients);
            return;
        }

        _uncommitted.GetOrCreateValue(context).AddRange(recipients);
    }

    internal void SaveFailed(DbContext context) => _saving.Remove(context);

    internal void Committed(DbContext? context)
    {
        if (context is null || !_uncommitted.TryGetValue(context, out var recipients))
            return;

        _uncommitted.Remove(context);
        _signal.Notify(recipients);
    }

    internal void RolledBack(DbContext? context)
    {
        if (context is not null)
            _uncommitted.Remove(context);
    }
}

public sealed class PlayerEventSaveInterceptor : SaveChangesInterceptor
{
    private readonly PlayerEventCommitTracker _tracker;

    public PlayerEventSaveInterceptor(PlayerEventCommitTracker tracker) => _tracker = tracker;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _tracker.Saving(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context) _tracker.Saving(context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _tracker.Saved(context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context) _tracker.Saved(context);
        return base.SavedChanges(eventData, result);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _tracker.SaveFailed(context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context) _tracker.SaveFailed(context);
        base.SaveChangesFailed(eventData);
    }
}

public sealed class PlayerEventTransactionInterceptor : DbTransactionInterceptor
{
    private readonly PlayerEventCommitTracker _tracker;

    public PlayerEventTransactionInterceptor(PlayerEventCommitTracker tracker) => _tracker = tracker;

    public override Task TransactionCommittedAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _tracker.Committed(eventData.Context);
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData)
    {
        _tracker.Committed(eventData.Context);
        base.TransactionCommitted(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _tracker.RolledBack(eventData.Context);
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData)
    {
        _tracker.RolledBack(eventData.Context);
        base.TransactionRolledBack(transaction, eventData);
    }
}
