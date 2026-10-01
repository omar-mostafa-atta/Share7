using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Names roster seats. See <see cref="IRosterNameResolver"/> and <see cref="MultiplayerOptions.RosterNames"/>.
/// <para>
/// **Handles come from the leaderboards' own issuer**, so a child has one public name across the whole
/// platform — the one on the board is the one on the seat — and nothing here can mint a second.
/// </para>
/// </summary>
public sealed class RosterNameResolver : IRosterNameResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDisplayNameService _displayNames;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<RosterNameResolver> _logger;

    public RosterNameResolver(
        ApplicationDbContext dbContext,
        IDisplayNameService displayNames,
        IOptions<MultiplayerOptions> options,
        ILogger<RosterNameResolver> logger)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        if (_options.RosterNames == RosterNameSource.ProfileName)
            return await _dbContext.StudentProfiles
                .AsNoTracking()
                .Where(p => ids.Contains(p.UserId) && p.FullName != string.Empty)
                .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        try
        {
            return await _displayNames.EnsureHandlesAsync(ids, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
        {
            // **A seat without a name is a cosmetic gap; a session nobody can read is an outage.**
            // Issuing a missing handle writes, and a write can fail — an exhausted keyspace, an account
            // deleted mid-match. The roster still renders with the handles that already exist, and the
            // client shows its own placeholder for the rest, exactly as it does for a null today.
            _logger.LogWarning(exception, "Could not issue roster handles; serving the ones that exist.");

            return await _dbContext.PlayerDisplayNames
                .AsNoTracking()
                .Where(n => ids.Contains(n.UserId))
                .ToDictionaryAsync(n => n.UserId, n => n.Handle, cancellationToken);
        }
    }
}
