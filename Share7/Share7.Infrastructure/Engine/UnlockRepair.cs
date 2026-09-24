using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Share7.Application.Engine.Models;
using Share7.Application.Progress.Interfaces;
using Share7.Domain.Constants;
using Share7.Domain.Progress;
using Share7.Domain.Structure;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine;

/// <summary>
/// Wakes the repair worker after a structural change commits, so students are put right in
/// seconds rather than at the next sweep. Singleton; nudging is free and never blocks.
/// </summary>
public sealed class UnlockRepairSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Nudge() => _channel.Writer.TryWrite(true);

    internal ValueTask<bool> WaitAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);

    internal void Drain()
    {
        while (_channel.Reader.TryRead(out _)) { }
    }
}

/// <summary>
/// Works off <see cref="UnlockRepairJob"/>s: after the tree changed, gives every student affected
/// what the old shape had promised them. **Only ever grants.** Every step is "insert if not
/// already held", so a job run twice, or interrupted and run again, ends in the same place.
/// </summary>
public sealed class UnlockRepairRunner
{
    private const int BatchSize = 200;
    private const int MaxAttempts = 10;

    private readonly ApplicationDbContext _db;
    private readonly IUnlockService _unlocks;
    private readonly ILogger<UnlockRepairRunner> _logger;

    public UnlockRepairRunner(ApplicationDbContext db, IUnlockService unlocks, ILogger<UnlockRepairRunner> logger)
    {
        _db = db;
        _unlocks = unlocks;
        _logger = logger;
    }

    /// <summary>Runs every job still to do, oldest first. Returns how many completed.</summary>
    public async Task<int> RunPendingAsync(CancellationToken cancellationToken = default)
    {
        var completed = 0;

        while (true)
        {
            var job = await _db.UnlockRepairJobs
                .Where(j => j.CompletedAtUtc == null && j.Attempts < MaxAttempts)
                .OrderBy(j => j.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (job is null) return completed;

            job.Attempts += 1;

            try
            {
                job.StudentsRepaired = job.Kind switch
                {
                    UnlockRepairKind.PassForward => await PassForwardAsync(job, cancellationToken),
                    _ => await FillGapsAsync(job.NodeId, cancellationToken)
                };

                job.CompletedAtUtc = DateTime.UtcNow;
                job.LastError = null;
                completed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Everything a job writes is idempotent, so the next sweep simply tries again.
                _logger.LogWarning(exception, "Unlock repair {JobId} ({Kind} of {NodeId}) failed, attempt {Attempt}.",
                    job.Id, job.Kind, job.NodeId, job.Attempts);
                job.LastError = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            }

            _db.ChangeTracker.Clear();
            _db.UnlockRepairJobs.Attach(job);
            _db.Entry(job).State = EntityState.Modified;
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();

            if (job.CompletedAtUtc is null && job.Attempts >= MaxAttempts)
                _logger.LogError("Unlock repair {JobId} gave up after {Attempts} attempts: {Error}", job.Id, job.Attempts, job.LastError);
        }
    }

    // =====================================================================================
    // Pass forward — the node a student held is gone from where it was
    // =====================================================================================

    private async Task<int> PassForwardAsync(UnlockRepairJob job, CancellationToken cancellationToken)
    {
        var node = await _db.CurriculumNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == job.NodeId, cancellationToken);
        if (node is null || job.ParentNodeId is not { } parentId || job.FormerOrder is not { } formerOrder)
            return 0;

        var holders = await HoldersAsync(TypeOf(node.KindKey), node.Id, cancellationToken);
        if (holders.Count == 0) return 0;

        var parent = await _db.CurriculumNodes.AsNoTracking().FirstAsync(n => n.Id == parentId, cancellationToken);
        var repaired = new HashSet<(Guid, Guid)>();

        switch (node.KindKey)
        {
            case NodeKinds.Lesson:
            {
                // The next lesson along is what finishing this one would have opened.
                var next = await NextLiveChildAsync(parentId, formerOrder, cancellationToken);
                if (next is not null)
                {
                    repaired.UnionWith(await GrantAsync(holders, CurriculumNodeType.Lesson, next.Value, cancellationToken));
                    break;
                }

                // It was the last lesson. Whether the chapter is now finished depends on each
                // student's own results and language, so it is re-evaluated per student exactly as a
                // finished attempt would be — from the chapter's last live lesson.
                var anchor = await LastLiveLessonUnderAsync(parent, cancellationToken);
                if (anchor is not null)
                {
                    repaired.UnionWith(await ReevaluateAsync(holders, anchor.Value, cancellationToken));
                    break;
                }

                // The chapter has nothing live left: vacuously finished, so the chapter after it opens.
                if (parent.ParentNodeId is { } subjectOfParent)
                    repaired.UnionWith(await OpenNextChapterAsync(holders, subjectOfParent, parent.Order, cancellationToken));
                break;
            }

            case NodeKinds.Chapter:
                repaired.UnionWith(await OpenNextChapterAsync(holders, parentId, formerOrder, cancellationToken));
                break;

            case NodeKinds.Subject:
            {
                // Subjects do not gate, but one fewer may have finished the term.
                var anchor = await LastLiveLessonUnderAsync(parent, cancellationToken);
                if (anchor is not null)
                    repaired.UnionWith(await ReevaluateAsync(holders, anchor.Value, cancellationToken));
                break;
            }

            case NodeKinds.Term:
            {
                var next = await NextLiveChildAsync(parentId, formerOrder, cancellationToken);
                if (next is not null)
                    repaired.UnionWith(await GrantAsync(holders, CurriculumNodeType.Term, next.Value, cancellationToken));
                break;
            }
        }

        return repaired.Select(h => h.Item1).Distinct().Count();
    }

    /// <summary>
    /// Opens the chapter after <paramref name="chapter"/> and its first lesson, as finishing it
    /// would have — or, when it was the subject's last, re-evaluates the term per student.
    /// </summary>
    private async Task<HashSet<(Guid, Guid)>> OpenNextChapterAsync(
        List<(Guid UserId, Guid GameId)> holders, Guid subjectId, int chapterOrder, CancellationToken cancellationToken)
    {
        var repaired = new HashSet<(Guid, Guid)>();

        var next = await NextLiveChildAsync(subjectId, chapterOrder, cancellationToken);
        if (next is not null)
        {
            repaired.UnionWith(await GrantAsync(holders, CurriculumNodeType.Chapter, next.Value, cancellationToken));

            var firstLesson = await NextLiveChildAsync(next.Value, 0, cancellationToken);
            if (firstLesson is not null)
                repaired.UnionWith(await GrantAsync(holders, CurriculumNodeType.Lesson, firstLesson.Value, cancellationToken));

            return repaired;
        }

        var subject = await _db.CurriculumNodes.AsNoTracking().FirstAsync(n => n.Id == subjectId, cancellationToken);
        var term = await _db.CurriculumNodes.AsNoTracking().FirstAsync(n => n.Id == subject.ParentNodeId, cancellationToken);
        var anchor = await LastLiveLessonUnderAsync(term, cancellationToken);

        if (anchor is not null)
            repaired.UnionWith(await ReevaluateAsync(holders, anchor.Value, cancellationToken));

        return repaired;
    }

    /// <summary>
    /// Runs the ordinary post-attempt evaluation for each student, anchored on a live lesson — the
    /// same rules, the same language handling, the same "only grants" guarantee as a real attempt.
    /// </summary>
    private async Task<HashSet<(Guid, Guid)>> ReevaluateAsync(
        List<(Guid UserId, Guid GameId)> holders, Guid anchorLessonId, CancellationToken cancellationToken)
    {
        var repaired = new HashSet<(Guid, Guid)>();
        var userIds = holders.Select(h => h.UserId).Distinct().ToList();

        var languages = (await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.PreferredLanguageId })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => u.PreferredLanguageId ?? LanguageIds.English);

        foreach (var (userId, gameId) in holders)
        {
            var granted = await _unlocks.EvaluateAfterAttemptAsync(
                userId, gameId, anchorLessonId, languages.GetValueOrDefault(userId, LanguageIds.English), cancellationToken);

            if (granted.Count > 0) repaired.Add((userId, gameId));
        }

        return repaired;
    }

    // =====================================================================================
    // Fill gaps — something now sits before what a student already reached
    // =====================================================================================

    private async Task<int> FillGapsAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var children = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.ParentNodeId == parentId && n.RetiredAtUtc == null)
            .OrderBy(n => n.Order)
            .Select(n => new { n.Id, n.Order, n.KindKey })
            .ToListAsync(cancellationToken);

        if (children.Count == 0 || children[0].KindKey == NodeKinds.Subject) return 0;

        var type = TypeOf(children[0].KindKey);
        var ids = children.Select(c => c.Id).ToList();

        var held = await _db.UserNodeUnlocks.AsNoTracking()
            .Where(u => u.NodeType == type && ids.Contains(u.NodeId))
            .Select(u => new { u.UserId, u.GameId, u.NodeId })
            .ToListAsync(cancellationToken);

        var repaired = new HashSet<(Guid, Guid)>();
        var orderOf = children.ToDictionary(c => c.Id, c => c.Order);

        foreach (var student in held.GroupBy(h => (h.UserId, h.GameId)))
        {
            var holds = student.Select(h => h.NodeId).ToHashSet();
            var furthest = student.Max(h => orderOf[h.NodeId]);

            var missing = children.Where(c => c.Order < furthest && !holds.Contains(c.Id)).Select(c => c.Id).ToList();
            if (missing.Count == 0) continue;

            var holder = new List<(Guid, Guid)> { student.Key };
            foreach (var id in missing)
            {
                repaired.UnionWith(await GrantAsync(holder, type, id, cancellationToken));

                // A chapter on its own is somewhere with nowhere to go: open its first lesson too.
                if (type == CurriculumNodeType.Chapter && await NextLiveChildAsync(id, 0, cancellationToken) is { } first)
                    await GrantAsync(holder, CurriculumNodeType.Lesson, first, cancellationToken);

                // A term's subjects, first chapters and first lessons are topped up by the next
                // game-open (UnlockService.EnsureSeededAsync re-walks every held term).
            }
        }

        return repaired.Select(r => r.Item1).Distinct().Count();
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    private async Task<List<(Guid UserId, Guid GameId)>> HoldersAsync(
        CurriculumNodeType type, Guid nodeId, CancellationToken cancellationToken) =>
        (await _db.UserNodeUnlocks.AsNoTracking()
            .Where(u => u.NodeType == type && u.NodeId == nodeId)
            .Select(u => new { u.UserId, u.GameId })
            .ToListAsync(cancellationToken))
        .Select(u => (u.UserId, u.GameId))
        .ToList();

    /// <summary>
    /// Grants one node to many students in batches, skipping anyone who already holds it. Returns
    /// the students who were actually granted something.
    /// </summary>
    private async Task<List<(Guid, Guid)>> GrantAsync(
        List<(Guid UserId, Guid GameId)> students, CurriculumNodeType type, Guid nodeId, CancellationToken cancellationToken)
    {
        var granted = new List<(Guid, Guid)>();

        foreach (var batch in students.Chunk(BatchSize))
        {
            var userIds = batch.Select(b => b.UserId).Distinct().ToList();

            var already = (await _db.UserNodeUnlocks.AsNoTracking()
                    .Where(u => u.NodeType == type && u.NodeId == nodeId && userIds.Contains(u.UserId))
                    .Select(u => new { u.UserId, u.GameId })
                    .ToListAsync(cancellationToken))
                .Select(u => (u.UserId, u.GameId))
                .ToHashSet();

            var now = DateTime.UtcNow;
            var rows = batch.Distinct().Where(b => !already.Contains(b)).ToList();

            foreach (var (userId, gameId) in rows)
            {
                _db.UserNodeUnlocks.Add(new UserNodeUnlock
                {
                    UserId = userId, GameId = gameId, NodeType = type, NodeId = nodeId, UnlockedAt = now
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            granted.AddRange(rows);
        }

        return granted;
    }

    private async Task<Guid?> NextLiveChildAsync(Guid parentId, int afterOrder, CancellationToken cancellationToken)
    {
        var id = await _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.ParentNodeId == parentId && n.RetiredAtUtc == null && n.Order > afterOrder)
            .OrderBy(n => n.Order)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id;
    }

    /// <summary>
    /// A live lesson under <paramref name="ancestor"/> with the highest position — which makes it
    /// the last lesson of its own chapter, so re-evaluating from it opens nothing inside the chapter
    /// that finishing the chapter would not.
    /// </summary>
    private Task<Guid?> LastLiveLessonUnderAsync(CurriculumNode ancestor, CancellationToken cancellationToken) =>
        _db.CurriculumNodes.AsNoTracking()
            .Where(n => n.KindKey == NodeKinds.Lesson && n.RetiredAtUtc == null && n.Path.StartsWith(ancestor.Path + "/"))
            .OrderByDescending(n => n.Order)
            .ThenBy(n => n.Id)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static CurriculumNodeType TypeOf(string kind) => kind switch
    {
        NodeKinds.Term => CurriculumNodeType.Term,
        NodeKinds.Subject => CurriculumNodeType.Subject,
        NodeKinds.Chapter => CurriculumNodeType.Chapter,
        _ => CurriculumNodeType.Lesson
    };
}

/// <summary>
/// Runs the repair queue: straight away when nudged after a structural change, and on a slow sweep
/// otherwise, so a job interrupted by a restart is never left behind.
/// </summary>
public sealed class UnlockRepairWorker : BackgroundService
{
    private static readonly TimeSpan Sweep = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly UnlockRepairSignal _signal;
    private readonly ILogger<UnlockRepairWorker> _logger;

    public UnlockRepairWorker(IServiceScopeFactory scopes, UnlockRepairSignal signal, ILogger<UnlockRepairWorker> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<UnlockRepairRunner>().RunPendingAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "The unlock repair sweep failed; it will run again shortly.");
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(Sweep);

            try
            {
                await _signal.WaitAsync(wait.Token);
                _signal.Drain();
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The sweep interval passed with no nudge — run anyway.
            }
        }
    }
}
