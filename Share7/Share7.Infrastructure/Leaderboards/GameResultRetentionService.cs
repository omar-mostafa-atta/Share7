using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <inheritdoc cref="IGameResultRetentionService"/>
public class GameResultRetentionService : IGameResultRetentionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly LeaderboardOptions _options;
    private readonly ILogger<GameResultRetentionService> _logger;

    public GameResultRetentionService(
        ApplicationDbContext dbContext,
        IOptions<LeaderboardOptions> options,
        ILogger<GameResultRetentionService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var days = _options.ResultRetentionDays;

        // Zero or negative switches retention off entirely, and that is a supported configuration: a
        // deployment small enough not to need it should not be silently deleting its own history.
        if (days <= 0)
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var batch = Math.Clamp(_options.RetentionBatchSize, 100, 50_000);

        // **Three conditions, and all three are load-bearing.**
        //
        // Older than the window — the only one anybody thinks of.
        //
        // Already projected onto the boards. A pending row still owes its entry; deleting it loses a
        // rank for gameplay that actually happened, silently and permanently.
        //
        // Behind every other consumer's watermark. The objective projector reads this same stream
        // from its own position, and deleting a row it has not folded yet does not merely lose a
        // quest step — it makes the sequence discontiguous underneath a cursor that is walking it.
        //
        // A flagged row is deliberately *not* exempt: it is excluded from projection and never gets a
        // ProjectedAtUtc, so it would otherwise sit in the table forever. The review queue's own
        // window is what governs it, and it is far shorter than this one.
        var watermark = await _dbContext.ProjectionCheckpoints
            .AsNoTracking()
            .Select(c => (long?)c.Watermark)
            .MinAsync(cancellationToken) ?? long.MaxValue;

        var deleted = await _dbContext.GameResults
            .Where(r => r.OccurredAtUtc < cutoff
                        && (r.ProjectedAtUtc != null || r.IsFlagged)
                        && r.Sequence <= watermark)
            .OrderBy(r => r.Sequence)
            .Take(batch)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Game result retention removed {Deleted} rows older than {Cutoff:u} (watermark {Watermark}).",
                deleted, cutoff, watermark);
        }

        return deleted;
    }
}
