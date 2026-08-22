using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Idempotency for multiplayer operations: one place that decides whether a request has already been
/// answered, and one place that writes the answer down.
/// <para>
/// **Only successful operations are recorded.** A refusal leaves the key unspent, so a client that
/// retries the same key after the blocking condition clears — a full session that empties, a session
/// that finishes starting — gets a fresh evaluation instead of its own stale "no" replayed forever.
/// This is the <c>PurchaseIdempotencyOnCompletedOnly</c> lesson applied before it costs anything.
/// </para>
/// </summary>
public sealed class MultiplayerRequestLogStore
{
    private readonly ApplicationDbContext _dbContext;

    public MultiplayerRequestLogStore(ApplicationDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// A caller's key, or a generated one. A generated key still lets the operation run — it simply
    /// cannot protect a retry, because it is new every time.
    /// </summary>
    public static string ResolveKey(string? requestId)
    {
        var trimmed = (requestId ?? string.Empty).Trim();
        return trimmed.Length == 0 ? $"srv_{Guid.NewGuid():N}" : trimmed;
    }

    /// <summary>
    /// The stored response for this key, or null if the operation has not completed before.
    /// <para>
    /// Matched on <paramref name="operation"/> as well as the key. Reusing one key across two
    /// different operations is a client bug, and replaying a <c>join</c> body in answer to a
    /// <c>close</c> would turn that bug into something far harder to diagnose than simply letting
    /// the second operation run.
    /// </para>
    /// </summary>
    public async Task<T?> TryReplayAsync<T>(
        Guid userId,
        string requestId,
        string operation,
        CancellationToken cancellationToken = default) where T : class
    {
        var logged = await _dbContext.MultiplayerRequestLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.UserId == userId && l.RequestId == requestId && l.Operation == operation,
                cancellationToken);

        if (logged is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(logged.ResponseJson, MultiplayerMappings.CurriculumPathJson);
        }
        catch (JsonException)
        {
            // A body this deployment can no longer parse — the DTO changed shape since it was
            // written. Falling through to re-run the operation is safe for everything this store
            // guards: create collides on the transport name, join on the membership index, and the
            // rest are idempotent by construction.
            return null;
        }
    }

    /// <summary>
    /// Writes the answer down. Call this **only after the operation succeeded**.
    /// <para>
    /// A key already spent on a different operation collides on the primary key. That is swallowed:
    /// the work is already committed and the caller is entitled to their response, so failing here
    /// would turn a client-side key-reuse bug into a lost result.
    /// </para>
    /// </summary>
    public async Task RecordAsync<T>(
        Guid userId,
        string requestId,
        string operation,
        Guid? sessionId,
        T response,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        _dbContext.MultiplayerRequestLogs.Add(new MultiplayerRequestLog
        {
            UserId = userId,
            RequestId = requestId,
            Operation = operation,
            SessionId = sessionId,
            ResponseJson = JsonSerializer.Serialize(response, MultiplayerMappings.CurriculumPathJson),
            StatusCode = statusCode,
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            foreach (var entry in _dbContext.ChangeTracker.Entries<MultiplayerRequestLog>().ToList())
                entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}

/// <summary>The <c>Operation</c> tokens, so a replay lookup and a write cannot disagree by typo.</summary>
internal static class MultiplayerOperations
{
    public const string Create = "create";
    public const string Join = "join";
    public const string Leave = "leave";
    public const string Start = "start";
    public const string Close = "close";
    public const string Matchmake = "matchmake";
    public const string HostTransfer = "host-transfer";
}
