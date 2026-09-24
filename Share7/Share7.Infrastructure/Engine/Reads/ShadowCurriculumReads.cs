using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Share7.Application.Curriculum.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Content;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine.Reads;

/// <summary>
/// Serves the typed answer, and checks the node answer against it.
/// <para>
/// **What the game receives in Shadow is exactly what it received before the rebuild** — the node
/// read runs second, after the typed one has produced the response, and anything it does wrong
/// (a different answer, an exception) is tallied and logged, never returned. Answers are compared
/// as serialized JSON, so "the same" means what it means on the wire.
/// </para>
/// </summary>
public sealed class ShadowCurriculumReads : ICurriculumReads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TypedCurriculumReads _typed;
    private readonly NodeCurriculumReads _nodes;
    private readonly CurriculumReadTally _tally;
    private readonly double _sampleRate;
    private readonly ILogger<ShadowCurriculumReads> _logger;

    public ShadowCurriculumReads(
        TypedCurriculumReads typed,
        NodeCurriculumReads nodes,
        CurriculumReadTally tally,
        double sampleRate,
        ILogger<ShadowCurriculumReads> logger)
    {
        _typed = typed;
        _nodes = nodes;
        _tally = tally;
        _sampleRate = sampleRate;
        _logger = logger;
    }

    private async Task<T> Compare<T>(string read, Func<ICurriculumReads, Task<T>> ask)
    {
        var served = await ask(_typed);

        if (_sampleRate <= 0 || (_sampleRate < 1 && Random.Shared.NextDouble() >= _sampleRate))
            return served;

        try
        {
            var shadow = await ask(_nodes);

            var expected = JsonSerializer.Serialize(served, Json);
            var actual = JsonSerializer.Serialize(shadow, Json);

            if (expected == actual)
            {
                _tally.Agreed(read);
            }
            else
            {
                var sample = FirstDifference(expected, actual);
                _tally.Differed(read, sample);
                _logger.LogWarning("Curriculum shadow read {Read} differed from the typed read: {Sample}", read, sample);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _tally.Failed(read, exception.Message);
            _logger.LogWarning(exception, "Curriculum shadow read {Read} failed; the typed answer was served.", read);
        }

        return served;
    }

    /// <summary>The first differing character and some context either side, for whoever investigates.</summary>
    internal static string FirstDifference(string expected, string actual)
    {
        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;

        var start = Math.Max(0, at - 120);
        string Slice(string s) => s.Length <= start ? string.Empty : s.Substring(start, Math.Min(300, s.Length - start));

        return $"at {at}: typed …{Slice(expected)}… | nodes …{Slice(actual)}…";
    }

    public Task<IReadOnlyList<GradeDto>> GradesAsync(Guid langId, CancellationToken ct) =>
        Compare("grades", r => r.GradesAsync(langId, ct));

    public Task<IReadOnlyList<TermDto>> TermsAsync(Guid? gradeId, Guid langId, CancellationToken ct) =>
        Compare("terms", r => r.TermsAsync(gradeId, langId, ct));

    public Task<IReadOnlyList<SubjectDto>> SubjectsAsync(Guid? termId, Guid langId, CancellationToken ct) =>
        Compare("subjects", r => r.SubjectsAsync(termId, langId, ct));

    public Task<IReadOnlyList<ChapterDto>> ChaptersAsync(Guid subjectId, Guid langId, CancellationToken ct) =>
        Compare("chapters", r => r.ChaptersAsync(subjectId, langId, ct));

    public Task<IReadOnlyList<LessonDto>> LessonsAsync(Guid chapterId, Guid langId, CancellationToken ct) =>
        Compare("lessons", r => r.LessonsAsync(chapterId, langId, ct));

    public Task<IReadOnlyList<LessonVersionDto>> SetVersionsAsync(IReadOnlyList<Guid> lessonIds, NodeItemRole role, Guid langId, CancellationToken ct) =>
        Compare(role == NodeItemRole.Recovery ? "recovery-versions" : "question-versions", r => r.SetVersionsAsync(lessonIds, role, langId, ct));

    public Task<LessonQuestionsDto?> QuestionsAsync(Guid lessonId, NodeItemRole role, Guid langId, CancellationToken ct) =>
        Compare(role == NodeItemRole.Recovery ? "recovery-questions" : "questions", r => r.QuestionsAsync(lessonId, role, langId, ct));

    public Task<bool> LessonExistsAsync(Guid lessonId, CancellationToken ct) =>
        Compare("lesson-exists", r => r.LessonExistsAsync(lessonId, ct));

    public Task<LessonHeader?> LessonHeaderAsync(Guid lessonId, Guid langId, CancellationToken ct) =>
        Compare("lesson-header", r => r.LessonHeaderAsync(lessonId, langId, ct));

    public Task<IReadOnlyList<LessonTotal>> LessonTotalsAsync(TreeScope scope, Guid nodeId, Guid langId, CancellationToken ct) =>
        Compare("lesson-totals", r => r.LessonTotalsAsync(scope, nodeId, langId, ct));

    public Task<NamedNode?> GradeAsync(Guid gradeId, Guid langId, CancellationToken ct) =>
        Compare("grade", r => r.GradeAsync(gradeId, langId, ct));

    public Task<GradeTree> GradeTreeAsync(Guid gradeId, Guid langId, CancellationToken ct) =>
        Compare("grade-tree", r => r.GradeTreeAsync(gradeId, langId, ct));

    public Task<Guid?> FirstTermAsync(Guid gradeId, CancellationToken ct) =>
        Compare("first-term", r => r.FirstTermAsync(gradeId, ct));

    public Task<LessonLocation?> LessonLocationAsync(Guid lessonId, CancellationToken ct) =>
        Compare("lesson-location", r => r.LessonLocationAsync(lessonId, ct));

    public Task<IReadOnlyList<TermLesson>> LessonsInTermAsync(Guid termId, Guid langId, CancellationToken ct) =>
        Compare("term-lessons", r => r.LessonsInTermAsync(termId, langId, ct));

    public Task<Guid?> NextChapterAsync(Guid subjectId, int afterOrder, CancellationToken ct) =>
        Compare("next-chapter", r => r.NextChapterAsync(subjectId, afterOrder, ct));

    public Task<Guid?> NextTermAsync(Guid gradeId, int afterOrder, CancellationToken ct) =>
        Compare("next-term", r => r.NextTermAsync(gradeId, afterOrder, ct));

    public Task<IReadOnlyList<Guid>> SubjectsOfTermsAsync(IReadOnlyList<Guid> termIds, CancellationToken ct) =>
        Compare("term-subjects", r => r.SubjectsOfTermsAsync(termIds, ct));

    public Task<IReadOnlyList<ChildRow>> ChaptersOfSubjectsAsync(IReadOnlyList<Guid> subjectIds, CancellationToken ct) =>
        Compare("subject-chapters", r => r.ChaptersOfSubjectsAsync(subjectIds, ct));

    public Task<IReadOnlyList<ChildRow>> LessonsOfChaptersAsync(IReadOnlyList<Guid> chapterIds, CancellationToken ct) =>
        Compare("chapter-lessons", r => r.LessonsOfChaptersAsync(chapterIds, ct));

    public Task<Guid?> GradeOfSubjectAsync(Guid subjectId, CancellationToken ct) =>
        Compare("subject-grade", r => r.GradeOfSubjectAsync(subjectId, ct));

    public Task<IReadOnlyList<EligibleLesson>> EligibleLessonsAsync(Guid userId, Guid gameId, Guid subjectId, Guid langId, CancellationToken ct) =>
        Compare("eligible-lessons", r => r.EligibleLessonsAsync(userId, gameId, subjectId, langId, ct));
}

/// <summary>
/// The running count of shadow comparisons, per day and per read, held in memory and written to
/// <c>CurriculumReadChecks</c> every minute. Singleton.
/// </summary>
public sealed class CurriculumReadTally
{
    internal sealed class Counter
    {
        public long Compared;
        public long Differed;
        public long Failed;
        public DateTime? LastDifferenceAtUtc;
        public string? LastDifferenceSample;
    }

    private ConcurrentDictionary<(DateOnly Day, string Read), Counter> _pending = new();

    public void Agreed(string read) => Interlocked.Increment(ref For(read).Compared);

    public void Differed(string read, string sample)
    {
        var counter = For(read);
        Interlocked.Increment(ref counter.Compared);
        Interlocked.Increment(ref counter.Differed);
        counter.LastDifferenceAtUtc = DateTime.UtcNow;
        counter.LastDifferenceSample = sample;
    }

    public void Failed(string read, string message)
    {
        var counter = For(read);
        Interlocked.Increment(ref counter.Compared);
        Interlocked.Increment(ref counter.Failed);
        counter.LastDifferenceAtUtc = DateTime.UtcNow;
        counter.LastDifferenceSample = "failed: " + message;
    }

    private Counter For(string read) =>
        _pending.GetOrAdd((DateOnly.FromDateTime(DateTime.UtcNow), read), _ => new Counter());

    /// <summary>What has been counted and not yet written, per read — for a report, without clearing it.</summary>
    public IReadOnlyList<(string Read, long Compared, long Differed, long Failed, string? Sample)> Pending() =>
        _pending
            .GroupBy(p => p.Key.Read)
            .Select(g => (
                g.Key,
                g.Sum(p => p.Value.Compared),
                g.Sum(p => p.Value.Differed),
                g.Sum(p => p.Value.Failed),
                g.Select(p => p.Value.LastDifferenceSample).FirstOrDefault(s => s is not null)))
            .OrderBy(r => r.Key)
            .ToList();

    /// <summary>What has been counted since the last flush, handed over and cleared.</summary>
    internal IReadOnlyDictionary<(DateOnly Day, string Read), Counter> TakePending() =>
        Interlocked.Exchange(ref _pending, new ConcurrentDictionary<(DateOnly, string), Counter>());

    /// <summary>Puts counts back after a failed write, so nothing is lost to a database blip.</summary>
    internal void Restore(IReadOnlyDictionary<(DateOnly Day, string Read), Counter> counts)
    {
        foreach (var (key, counter) in counts)
        {
            var target = _pending.GetOrAdd(key, _ => new Counter());
            Interlocked.Add(ref target.Compared, counter.Compared);
            Interlocked.Add(ref target.Differed, counter.Differed);
            Interlocked.Add(ref target.Failed, counter.Failed);
            target.LastDifferenceAtUtc ??= counter.LastDifferenceAtUtc;
            target.LastDifferenceSample ??= counter.LastDifferenceSample;
        }
    }

    /// <summary>Writes what has been counted to the database. Returns how many rows it touched.</summary>
    public async Task<int> FlushAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var pending = TakePending();
        if (pending.Count == 0) return 0;

        try
        {
            foreach (var ((day, read), counter) in pending)
            {
                var sample = counter.LastDifferenceSample is { Length: > 4000 } text ? text[..4000] : counter.LastDifferenceSample;

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE CurriculumReadChecks
                    SET Compared = Compared + {counter.Compared},
                        Differed = Differed + {counter.Differed},
                        Failed = Failed + {counter.Failed},
                        LastDifferenceAtUtc = COALESCE({counter.LastDifferenceAtUtc}, LastDifferenceAtUtc),
                        LastDifferenceSample = COALESCE({sample}, LastDifferenceSample)
                    WHERE Day = {day} AND ReadName = {read};

                    IF @@ROWCOUNT = 0
                        INSERT INTO CurriculumReadChecks (Day, ReadName, Compared, Differed, Failed, LastDifferenceAtUtc, LastDifferenceSample)
                        VALUES ({day}, {read}, {counter.Compared}, {counter.Differed}, {counter.Failed}, {counter.LastDifferenceAtUtc}, {sample});
                    """, cancellationToken);
            }

            return pending.Count;
        }
        catch
        {
            Restore(pending);
            throw;
        }
    }
}

/// <summary>Writes the shadow tally to the database once a minute, and once more on shutdown.</summary>
public sealed class CurriculumReadTallyFlusher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly CurriculumReadTally _tally;
    private readonly ILogger<CurriculumReadTallyFlusher> _logger;

    public CurriculumReadTallyFlusher(IServiceScopeFactory scopes, CurriculumReadTally tally, ILogger<CurriculumReadTallyFlusher> logger)
    {
        _scopes = scopes;
        _tally = tally;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down: fall through to one last flush.
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await _tally.FlushAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not write the curriculum shadow tally; it is kept for the next attempt.");
            }
        }
    }
}
