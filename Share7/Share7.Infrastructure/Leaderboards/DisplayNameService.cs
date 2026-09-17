using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Issues and reads public handles.
/// <para>
/// The one rule this type exists to enforce: **nothing a child typed and nothing they were
/// registered with ever reaches a board row.** Handles are generated, and the generator is not a
/// function of any personal field.
/// </para>
/// </summary>
public class DisplayNameService : IDisplayNameService
{
    /// <summary>
    /// Attempts before giving up on a unique handle. Three is generous against a keyspace of
    /// roughly nine million: needing a fourth means the table is far larger than the generator was
    /// sized for, which is a capacity problem to fix rather than a retry to absorb.
    /// </summary>
    private const int MaxHandleAttempts = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly LeaderboardOptions _options;

    public DisplayNameService(ApplicationDbContext dbContext, IOptions<LeaderboardOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<string> EnsureHandleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var handles = await EnsureHandlesAsync([userId], cancellationToken);
        return handles[userId];
    }

    public async Task<IReadOnlyDictionary<Guid, string>> EnsureHandlesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var wanted = userIds.Distinct().ToList();

        if (wanted.Count == 0)
            return new Dictionary<Guid, string>();

        var existing = await _dbContext.PlayerDisplayNames
            .AsNoTracking()
            .Where(n => wanted.Contains(n.UserId))
            .ToDictionaryAsync(n => n.UserId, n => n.Handle, cancellationToken);

        var missing = wanted.Where(id => !existing.ContainsKey(id)).ToList();

        foreach (var userId in missing)
            existing[userId] = await IssueAsync(userId, cancellationToken);

        return existing;
    }

    /// <summary>
    /// Mints one handle, retrying on the unique index.
    /// <para>
    /// Saved on its own rather than batched with the caller's work: two players registering at
    /// once can collide, and one collision must not roll back a projection batch that had nothing
    /// to do with it.
    /// </para>
    /// </summary>
    private async Task<string> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxHandleAttempts; attempt++)
        {
            var row = new PlayerDisplayName
            {
                UserId = userId,
                Handle = HandleGenerator.Next(),
                Source = DisplayNameSource.Generated,
                IsHidden = !_options.ListedByDefault,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.PlayerDisplayNames.Add(row);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return row.Handle;
            }
            // A handle cannot be issued to an account that no longer exists, and no number of
            // retries will change that. Rethrown immediately rather than swallowed into the loop,
            // which would burn every attempt and then report an exhausted keyspace — a diagnosis
            // that would send someone to widen the generator over a deleted user.
            catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
            {
                _dbContext.Entry(row).State = EntityState.Detached;
                throw;
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(row).State = EntityState.Detached;

                // Either the handle collided, or another request issued this player's handle while
                // we were working. The second case is a success in disguise, so look before
                // burning another attempt.
                var settled = await _dbContext.PlayerDisplayNames
                    .AsNoTracking()
                    .Where(n => n.UserId == userId)
                    .Select(n => n.Handle)
                    .FirstOrDefaultAsync(cancellationToken);

                if (settled is not null)
                    return settled;
            }
        }

        throw new InvalidOperationException(
            $"Could not issue a unique display handle for {userId} in {MaxHandleAttempts} attempts. " +
            "The handle keyspace is likely exhausted and the generator needs widening.");
    }

    /// <summary>
    /// A row pointing at a user that is not there. Distinct from the unique violation the retry loop
    /// exists for: that one is a collision worth another attempt, this one never is.
    /// </summary>
    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 547 };

    public async Task<bool> IsHiddenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.PlayerDisplayNames
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new { n.IsHidden, n.IsHiddenByGuardian })
            .FirstOrDefaultAsync(cancellationToken);

        // No row means no handle has been issued, which means the player has never been ranked.
        // Treat that as hidden: appearing on a board should follow from playing, not from asking.
        if (row is null)
            return true;

        return row.IsHidden || row.IsHiddenByGuardian;
    }

    public async Task<bool> SetHiddenAsync(
        Guid userId, bool isHidden, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.PlayerDisplayNames
            .FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken);

        if (row is null)
        {
            await EnsureHandleAsync(userId, cancellationToken);

            row = await _dbContext.PlayerDisplayNames
                .FirstAsync(n => n.UserId == userId, cancellationToken);
        }

        // A guardian's decision is not the child's to reverse. Reported rather than thrown: the
        // client needs to render "your guardian has turned this off", not an error.
        if (row.IsHiddenByGuardian && !isHidden)
            return false;

        row.IsHidden = isHidden;
        row.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Entries carry a snapshot of this flag, so the boards the player is already on have to be
        // told. Cheap: one indexed update per cycle they appear in.
        await _dbContext.LeaderboardEntries
            .Where(e => e.UserId == userId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(e => e.IsHidden, isHidden),
                cancellationToken);

        return true;
    }
}
