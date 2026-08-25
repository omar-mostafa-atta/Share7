using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Domain.Economy;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Progress.Models;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Progress;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Progress;

/// <summary>
/// Records attempts and reads progress back. Two rules run through all of it:
/// <list type="bullet">
/// <item>the server regrades every attempt from the submitted choice ids — a client's claim
/// that an answer was correct is never stored;</item>
/// <item>only question-level and lesson-level rows exist. Chapter, subject, term and grade
/// figures are computed on read, so adding a lesson changes the denominator immediately
/// instead of leaving stale rollups behind.</item>
/// </list>
/// </summary>
public class ProgressService : IProgressService
{
    /// <summary>A lesson counts as passed at half marks. This is also the unlock threshold.</summary>
    private const int PassPercent = 50;

    /// <summary>What an attempt's idempotency key is spent on. One key, one operation.</summary>
    private const string AttemptOperation = "attempt";

    /// <summary>
    /// camelCase, matching the wire. The stored body is replayed to the client verbatim, so
    /// serialising it with different options here would quietly change a payload that is supposed
    /// to be identical to the first response.
    /// </summary>
    private static readonly JsonSerializerOptions AttemptJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;
    private readonly IUnlockService _unlockService;
    private readonly IRewardService _rewardService;
    private readonly IWalletService _walletService;
    private readonly IGameResultRecorder _gameResults;
    private readonly ILevelService _levels;
    private readonly ISignalPricer _pricer;
    private readonly IObjectiveProjector _objectives;

    public ProgressService(
        ApplicationDbContext dbContext,
        ILanguageService languageService,
        IUnlockService unlockService,
        IRewardService rewardService,
        IWalletService walletService,
        IGameResultRecorder gameResults,
        ILevelService levels,
        ISignalPricer pricer,
        IObjectiveProjector objectives)
    {
        _dbContext = dbContext;
        _languageService = languageService;
        _unlockService = unlockService;
        _rewardService = rewardService;
        _walletService = walletService;
        _gameResults = gameResults;
        _levels = levels;
        _pricer = pricer;
        _objectives = objectives;
    }

    // ------------------------------------------------------------- writing

    public async Task<ServiceResult<AttemptResultDto>> SubmitAttemptAsync(
        Guid userId, SubmitAttemptRequest request, CancellationToken cancellationToken = default)
    {
        // A retry of a run already recorded replays the original answer rather than recording a
        // second attempt. Checked before any work: the whole point is that the retry is free.
        // Only successes are ever logged, so a run refused for a locked lesson is not pinned to
        // its "no" and succeeds normally once the lesson opens.
        if (await TryReplayAttemptAsync(userId, request.RequestId, cancellationToken) is { } replayed)
            return ServiceResult<AttemptResultDto>.Success(replayed);

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var game = await _dbContext.Games
            .Where(g => g.Id == request.GameId)
            .Select(g => new { g.Id, g.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        if (game is null)
            return ServiceResult<AttemptResultDto>.NotFound("Game not found.");

        if (!game.IsActive)
            return ServiceResult<AttemptResultDto>.Conflict("This game is disabled.");

        var lesson = await _dbContext.Lessons
            .Where(l => l.Id == request.LessonId)
            .Select(l => new
            {
                l.Id,
                GradeId = l.Chapter!.Subject!.Term!.GradeId,
                Version = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return ServiceResult<AttemptResultDto>.NotFound("Lesson not found.");

        // The tree is shared across languages but question sets are not, so a lesson can be
        // perfectly real and still have nothing to play in this student's language.
        var questions = await _dbContext.Questions
            .Where(q => q.LessonId == request.LessonId && q.LangId == langId && q.IsActive)
            .Select(q => new
            {
                q.Id,
                q.CorrectChoiceId,
                ChoiceIds = q.Choices.Select(c => c.Id).ToList()
            })
            .ToListAsync(cancellationToken);

        if (questions.Count == 0)
            return ServiceResult<AttemptResultDto>.Conflict(
                "This lesson has no questions in your language yet, so there is nothing to record.");

        // Two answers for the same question have no defensible resolution — taking the first would
        // reward padding the payload, taking the last would reward it differently. Refuse.
        var duplicates = request.Answers
            .GroupBy(a => a.QuestionId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
            return ServiceResult<AttemptResultDto>.Invalid(
                $"The same question was answered more than once: {string.Join(", ", duplicates)}.");

        // A student's first contact with a game gives them their starting point.
        var gradeId = await ResolveGradeIdAsync(userId, lesson.GradeId, cancellationToken);
        await _unlockService.EnsureSeededAsync(userId, request.GameId, gradeId, cancellationToken);

        var isUnlocked = await _dbContext.UserNodeUnlocks.AnyAsync(
            u => u.UserId == userId && u.GameId == request.GameId
                 && u.NodeType == CurriculumNodeType.Lesson && u.NodeId == request.LessonId,
            cancellationToken);

        if (!isUnlocked)
            return ServiceResult<AttemptResultDto>.Forbidden("This lesson is still locked for this game.");

        // From here on the attempt writes. Progress, unlocks and any currency it earns commit as
        // one unit: a reward that survived a rolled-back attempt would be currency for gameplay
        // that never happened. Deliberately opened *after* the guard clauses so a refused
        // submission does not undo the unlock seeding above.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // **Grading happens here and nowhere else.** The payload says which choice was picked; this
        // is where it is compared against the question's own correct answer. A client cannot assert
        // a score because there is no field in which to assert one.
        var picked = request.Answers.ToDictionary(a => a.QuestionId, a => a.ChoiceId);
        var choicesByQuestion = questions.ToDictionary(q => q.Id, q => q.ChoiceIds.ToHashSet());

        var answerResults = new List<AnswerResultDto>(questions.Count);
        var correctQuestionIds = new HashSet<Guid>();

        foreach (var question in questions)
        {
            // Absent from the payload means the question was never reached, which counts as wrong —
            // a run shows every question in the lesson.
            var choiceId = picked.GetValueOrDefault(question.Id);

            // A choice that does not belong to this question is graded wrong rather than rejected:
            // it is almost always a stale cache, and failing the whole run would lose a real score
            // over one bad id. It is counted and reported instead.
            var belongs = choiceId is { } id && choicesByQuestion[question.Id].Contains(id);
            var isCorrect = belongs && choiceId == question.CorrectChoiceId;

            if (isCorrect)
                correctQuestionIds.Add(question.Id);

            answerResults.Add(new AnswerResultDto
            {
                QuestionId = question.Id,
                ChoiceId = belongs ? choiceId : null,
                CorrectChoiceId = question.CorrectChoiceId,
                IsCorrect = isCorrect
            });
        }

        // Answers naming a question outside this lesson/language, or a choice outside its question.
        // Reported rather than refused — it is the fingerprint of a stale cached question set.
        var unrecognised = request.Answers.Count(answer =>
            !choicesByQuestion.TryGetValue(answer.QuestionId, out var valid)
            || (answer.ChoiceId is { } id && !valid.Contains(id)));

        var correctCount = correctQuestionIds.Count;
        var totalCount = questions.Count;
        var percent = (int)Math.Round(correctCount * 100.0 / totalCount, MidpointRounding.AwayFromZero);

        // What *this* run scored. Rewards read this — an ace already paid for must not pay again
        // just because the record still says Aced.
        var attemptState = StateFor(percent);

        var now = DateTime.UtcNow;

        var existingQuestionRows = await _dbContext.UserQuestionProgress
            .Where(p => p.UserId == userId && p.GameId == request.GameId && p.LessonId == request.LessonId)
            .ToListAsync(cancellationToken);

        var byQuestionId = existingQuestionRows.ToDictionary(p => p.QuestionId);

        // Written for every active question, not just the correct ones: a question missing from
        // the payload was either answered wrongly or not reached, and both count as wrong.
        foreach (var question in questions)
        {
            var wasCorrect = correctQuestionIds.Contains(question.Id);

            if (byQuestionId.TryGetValue(question.Id, out var row))
            {
                row.IsCorrect = wasCorrect;
                row.Attempts += 1;
                row.LastAttemptAt = now;
            }
            else
            {
                _dbContext.UserQuestionProgress.Add(new UserQuestionProgress
                {
                    UserId = userId,
                    GameId = request.GameId,
                    QuestionId = question.Id,
                    LessonId = request.LessonId,
                    IsCorrect = wasCorrect,
                    Attempts = 1,
                    LastAttemptAt = now
                });
            }
        }

        var lessonRow = await _dbContext.UserLessonProgress.FirstOrDefaultAsync(
            p => p.UserId == userId && p.GameId == request.GameId && p.LessonId == request.LessonId,
            cancellationToken);

        if (lessonRow is null)
        {
            lessonRow = new UserLessonProgress
            {
                UserId = userId,
                GameId = request.GameId,
                LessonId = request.LessonId,
                // Set once and never recalculated — it is a historical fact, not part of the state machine.
                FirstAttemptWasPerfect = percent >= 100
            };
            _dbContext.UserLessonProgress.Add(lessonRow);
        }

        // Captured before the row moves, because every ranked metric below is a *transition*
        // rather than a level: "first time this lesson was aced", "how much the best score
        // improved". Reading them after the update would make each of them zero.
        var previousState = lessonRow.CompletionState;
        var previousBest = lessonRow.BestPercent;

        // How many *more* questions this attempt got right than the player has ever managed on this
        // lesson. The unit variable XP is paid in — see UserLessonProgress.BestCorrectCount for why
        // it is improvement rather than the raw count.
        var correctImprovement = Math.Max(0, correctCount - lessonRow.BestCorrectCount);

        // Last-attempt figures: what the student just scored, which is what the results screen shows.
        lessonRow.CorrectCount = correctCount;
        lessonRow.TotalCount = totalCount;
        lessonRow.Percent = percent;
        lessonRow.Attempts += 1;
        lessonRow.QuestionsVersion = lesson.Version;
        lessonRow.LastAttemptAt = now;

        // Record figures: monotonic, and the only thing unlocks and rankings read. A worse replay
        // updates the figures above and deliberately leaves these alone.
        lessonRow.BestPercent = Math.Max(lessonRow.BestPercent, percent);
        lessonRow.BestCorrectCount = Math.Max(lessonRow.BestCorrectCount, correctCount);

        var recordState = StateFor(lessonRow.BestPercent);
        lessonRow.CompletionState = recordState;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var unlocked = await _unlockService.EvaluateAfterAttemptAsync(
            userId, request.GameId, request.LessonId, langId, cancellationToken);

        // Captured before the payout for the same reason the ranked metrics were: the level is a
        // transition, and reading it afterwards would report where the player is rather than how
        // far they moved. The XP figure goes to the reward engine as its baseline, because the
        // variable grant below moves the balance before any rule runs.
        var levelSnapshot = await _levels.GetForUserAsync(userId, cancellationToken);
        var levelBefore = levelSnapshot.Level;

        // --- the variable half: what the questions were worth -------------------------------------
        // Priced by the same service, from the same table, under the same cap ladder as a run's
        // coins. **The count is the server's own** — it comes from re-grading against the answer key,
        // never from anything the client said — which is why `correct_answer` is an attempt-owned
        // signal that a run reporting it can never be paid for.
        var signalRewards = await PaySignalsAsync(
            userId, request.GameId, request.LessonId,
            correctImprovement, lessonRow.BestCorrectCount, now, cancellationToken);

        // The client sent choice ids and nothing else that touches money. Everything the reward
        // engine reads below is the server's own recomputation.
        var rewards = await _rewardService.EvaluateProgressAttemptAsync(
            new ProgressRewardContext
            {
                UserId = userId,
                GameId = request.GameId,
                LessonId = request.LessonId,
                AttemptNumber = lessonRow.Attempts,
                Percent = percent,
                // This run's state, not the record's. A replay of an already-aced lesson must not
                // re-fire LessonAced simply because the stored state is still Aced.
                CompletionState = attemptState,
                RequestId = request.RequestId,
                XpBaseline = levelSnapshot.Xp
            },
            cancellationToken);

        // One list, ordered variable-first, so a results screen reads "45 XP for 9 right answers"
        // above "20 XP for finishing" rather than in whatever order two mechanisms happened to run.
        rewards = [.. signalRewards, .. rewards];

        // Ranking's only seam into gameplay. Inside the transaction on purpose: the result is the
        // source of truth for every board, so an attempt that committed without it would be a rank
        // silently lost. It writes rows and queues a job — no board is walked here, so finishing a
        // lesson never waits on a leaderboard.
        await _gameResults.RecordAsync(
            new GameResultContext
            {
                UserId = userId,
                GameId = request.GameId,
                SourceId = request.LessonId,
                OccurredAtUtc = now,
                GradeId = gradeId,
                LangId = langId,
                RequestId = request.RequestId,
                Metrics = RankedMetricsFor(
                    previousState, recordState, previousBest, lessonRow.BestPercent, percent, rewards)
            },
            cancellationToken);

        // Read inside the transaction rather than after it. The reward grants above are only
        // visible from in here, and the response about to be stored for replay has to be the same
        // one returned — a balance read on the other side of the commit could differ from it.
        // Objectives are a second projection of the results just recorded, folded inline so a child
        // who finishes their third lesson sees the quest complete now rather than whenever a job
        // next runs. Same code the batch pass uses, same transaction as the progress it counts.
        await _objectives.ProjectForUserAsync(userId, cancellationToken);

        var balances = await _walletService.GetBalancesAsync(userId, cancellationToken);

        // Read here rather than derived from the rewards: absolute, like the balances beside it,
        // and correct even when the XP that moved the level came from a rule this attempt did not
        // fire. Inside the transaction for the same reason the balances are.
        var level = await _levels.GetForUserAsync(userId, cancellationToken);

        var response = new AttemptResultDto
        {
            GameId = request.GameId,
            LessonId = request.LessonId,
            LangId = langId,
            CorrectCount = correctCount,
            TotalCount = totalCount,
            Percent = percent,
            Attempts = lessonRow.Attempts,
            // The record, so a worse replay reports the ace it did not lose.
            CompletionState = recordState.ToString(),
            FirstAttemptWasPerfect = lessonRow.FirstAttemptWasPerfect,
            QuestionsVersion = lesson.Version,
            Answers = answerResults,
            UnrecognisedAnswers = unrecognised,
            Unlocked = unlocked,
            Rewards = rewards,
            Balances = balances,
            Level = level,
            LevelsGained = level.Level > levelBefore
                ? [.. Enumerable.Range(levelBefore + 1, level.Level - levelBefore)]
                : []
        };

        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            var log = new ProgressRequestLog
            {
                UserId = userId,
                RequestId = request.RequestId.Trim(),
                Operation = AttemptOperation,
                LessonId = request.LessonId,
                ResponseJson = JsonSerializer.Serialize(response, AttemptJson),
                CreatedAtUtc = now
            };

            _dbContext.ProgressRequestLogs.Add(log);

            try
            {
                // Inside the transaction on purpose: the log row and the attempt it describes
                // commit together, so there is no window where the attempt is recorded but the
                // key that guards it is not.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another retry of this same run won the race and committed first. The key is the
                // concurrency guard: roll this one back and hand back their answer, so two
                // in-flight retries still produce exactly one attempt.
                _dbContext.Entry(log).State = EntityState.Detached;
                await transaction.RollbackAsync(cancellationToken);

                var winner = await TryReplayAttemptAsync(userId, request.RequestId, cancellationToken);

                return winner is not null
                    ? ServiceResult<AttemptResultDto>.Success(winner)
                    : ServiceResult<AttemptResultDto>.Conflict(
                        "This attempt is already being recorded. Retry with the same requestId.");
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return ServiceResult<AttemptResultDto>.Success(response);
    }

    /// <summary>
    /// What this attempt is worth to a leaderboard.
    /// <para>
    /// **Every metric here is a transition, never a level.** A counting metric that fired on each
    /// attempt would rank whoever replayed the most rather than whoever learned the most, and no
    /// aggregation downstream can undo that — by the time the projector sees the number, the
    /// difference between "passed a new lesson" and "passed the same lesson again" is gone.
    /// </para>
    /// <para>
    /// So a lesson counts once when it is first passed, once more when it is first aced, and
    /// contributes only the amount by which its best score actually improved. A run that improved
    /// nothing returns an empty list and records nothing at all.
    /// </para>
    /// </summary>
    /// <summary>
    /// Prices and grants the attempt's variable half: what the questions this player got right were
    /// worth, at whatever an operator has priced <c>correct_answer</c> at.
    /// <para>
    /// **The same mechanism a run's coins go through, deliberately.** One valuation table, one cap
    /// ladder, one set of daily counters, one granting path into the wallet. The alternative — a
    /// second proportional-payout mechanism living on the attempt path — is how an economy ends up
    /// with two answers to "how much can a child earn in a day".
    /// </para>
    /// <para>
    /// Returns reward lines shaped exactly like a rule's, so the client renders one list and does not
    /// need to know which of the two mechanisms paid. <c>RuleId</c> is empty because no rule fired:
    /// the provenance is on <c>EventType</c> as <c>signal:{kind}</c>.
    /// </para>
    /// </summary>
    private async Task<List<RewardDto>> PaySignalsAsync(
        Guid userId,
        Guid gameId,
        Guid lessonId,
        int correctImprovement,
        int bestCorrectCount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (correctImprovement <= 0)
            return [];

        var pricing = await _pricer.PriceAsync(
            new SignalPricingRequest
            {
                UserId = userId,
                GameId = gameId,
                Surface = SignalSurface.Attempt,
                Counts = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [SignalKinds.CorrectAnswer] = correctImprovement
                },
                NowUtc = now
                // No duration: an attempt is bounded by how many questions the lesson has, which is a
                // far tighter bound than any rate a clock could give.
            },
            cancellationToken);

        if (!pricing.Any)
            return [];

        var paid = new List<RewardDto>();
        List<SignalLine> granted = [];

        foreach (var line in pricing.Lines)
        {
            var applied = await _walletService.ApplyAsync(
                new WalletMutation
                {
                    UserId = userId,
                    CurrencyId = line.CurrencyId,
                    Delta = line.NetAmount,
                    TransactionType = CurrencyTransactionType.LessonReward,
                    SourceType = LedgerSourceType.ProgressAttempt,
                    SourceId = lessonId.ToString(),
                    // Keyed on the record this payout bought, not on the moment it happened.
                    //
                    // BestCorrectCount only ever rises, so every improvement has a key of its own and
                    // no two grants can collide — while a re-run of the same improvement produces the
                    // same key and is refused. A timestamp here would have been unique every time,
                    // which is the same as having no idempotency key at all: the guard would exist
                    // and never fire. Replay is already stopped twice over (the request log, and an
                    // improvement of zero), and this is the third, cheapest layer.
                    IdempotencyKey = $"attempt:{gameId}:{lessonId}:{line.Kind}:{bestCorrectCount}",
                    Metadata = JsonSerializer.Serialize(new
                    {
                        gameId,
                        lessonId,
                        source = line.Source,
                        improved = line.ReportedCount,
                        paid = line.PaidCount,
                        unitValue = line.UnitValue,
                        bestCorrectCount
                    })
                },
                cancellationToken);

            // A retired currency loses the line rather than the attempt. The progress itself is
            // already recorded; failing here would throw away a lesson the child actually finished.
            if (!applied.Succeeded)
                continue;

            granted.Add(line);

            paid.Add(new RewardDto
            {
                RuleId = Guid.Empty,
                RuleName = line.Source,
                EventType = line.Source,
                TransactionId = Guid.Empty,
                Grants = [new RewardGrantDto { Currency = line.CurrencyKey, Amount = line.NetAmount }]
            });
        }

        await _pricer.AccrueAsync(userId, granted, now, cancellationToken);

        return paid;
    }

    private static IReadOnlyList<GameResultDraft> RankedMetricsFor(
        CompletionState previousState,
        CompletionState recordState,
        int previousBest,
        int currentBest,
        int attemptPercent,
        IReadOnlyList<RewardDto> rewards)
    {
        var metrics = new List<GameResultDraft>(6);

        var passed = recordState is CompletionState.Completed or CompletionState.Aced;
        var wasPassed = previousState is CompletionState.Completed or CompletionState.Aced;

        if (passed && !wasPassed)
            metrics.Add(new GameResultDraft(LeaderboardMetrics.LessonsCompleted, 1));

        if (recordState == CompletionState.Aced && previousState != CompletionState.Aced)
            metrics.Add(new GameResultDraft(LeaderboardMetrics.LessonsAced, 1));

        var improvement = currentBest - previousBest;

        if (improvement > 0)
            metrics.Add(new GameResultDraft(LeaderboardMetrics.TotalLessonScore, improvement));

        // Aggregated with Best, so sending this run's own score is correct even when it is worse
        // than the player's record — the projector keeps the higher of the two.
        if (attemptPercent > 0)
            metrics.Add(new GameResultDraft(LeaderboardMetrics.LessonBestPercent, attemptPercent));

        // What this attempt actually paid, scoped by currency key — both mechanisms, since the caller
        // hands over one combined list. The run path emits the same metric from its own totals; until
        // both did, "earn 100 XP today" was a quest that could only ever count half of a child's day.
        foreach (var earned in rewards
                     .SelectMany(reward => reward.Grants)
                     .GroupBy(grant => grant.Currency, StringComparer.Ordinal))
        {
            var amount = earned.Sum(grant => grant.Amount);

            if (amount > 0)
                metrics.Add(new GameResultDraft(LeaderboardMetrics.CurrencyEarned, amount, earned.Key));
        }

        return metrics;
    }

    /// <summary>Maps a score onto the completion ladder. The one place the thresholds live.</summary>
    private static CompletionState StateFor(int percent) => percent >= 100
        ? CompletionState.Aced
        : percent >= PassPercent
            ? CompletionState.Completed
            : CompletionState.Uncompleted;

    /// <summary>
    /// The stored answer for a run already recorded, or null when this is the first time it has
    /// been seen — including whenever the client sent no <c>requestId</c>, which stays
    /// non-idempotent exactly as before.
    /// </summary>
    private async Task<AttemptResultDto?> TryReplayAttemptAsync(
        Guid userId, string? requestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        var key = requestId.Trim();

        var logged = await _dbContext.ProgressRequestLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.UserId == userId && l.RequestId == key && l.Operation == AttemptOperation,
                cancellationToken);

        if (logged is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<AttemptResultDto>(logged.ResponseJson, AttemptJson);
        }
        catch (JsonException)
        {
            // A body this deployment can no longer parse, because the DTO changed shape since it
            // was written. Re-running is the safe fallback: the attempt is graded from scratch and
            // the reward engine still deduplicates on the same requestId, so nothing is paid twice.
            return null;
        }
    }

    // ------------------------------------------------------------- reading

    public async Task<ServiceResult<LessonProgressDto>> GetLessonProgressAsync(
        Guid userId, Guid gameId, Guid lessonId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var lesson = await _dbContext.Lessons
            .Where(l => l.Id == lessonId)
            .Select(l => new
            {
                l.Id,
                CurrentVersion = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return ServiceResult<LessonProgressDto>.NotFound("Lesson not found.");

        var row = await _dbContext.UserLessonProgress
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GameId == gameId && p.LessonId == lessonId, cancellationToken);

        var isUnlocked = await _dbContext.UserNodeUnlocks.AnyAsync(
            u => u.UserId == userId && u.GameId == gameId
                 && u.NodeType == CurriculumNodeType.Lesson && u.NodeId == lessonId,
            cancellationToken);

        return ServiceResult<LessonProgressDto>.Success(new LessonProgressDto
        {
            GameId = gameId,
            LessonId = lessonId,
            CorrectCount = row?.CorrectCount ?? 0,
            TotalCount = row?.TotalCount ?? 0,
            Percent = row?.Percent ?? 0,
            Attempts = row?.Attempts ?? 0,
            CompletionState = (row?.CompletionState ?? CompletionState.Uncompleted).ToString(),
            IsUnlocked = isUnlocked,
            HasAttempted = row is not null,
            FirstAttemptWasPerfect = row?.FirstAttemptWasPerfect ?? false,
            QuestionsVersion = row?.QuestionsVersion ?? 0,
            CurrentQuestionsVersion = lesson.CurrentVersion,
            ContentUpdated = row is not null && row.QuestionsVersion != lesson.CurrentVersion,
            LastAttemptAt = row?.LastAttemptAt
        });
    }

    public Task<ServiceResult<NodeProgressDto>> GetNodeProgressAsync(
        Guid userId, Guid gameId, CurriculumNodeType nodeType, Guid nodeId, CancellationToken cancellationToken = default)
    {
        var lessons = _dbContext.Lessons.AsQueryable();

        lessons = nodeType switch
        {
            CurriculumNodeType.Chapter => lessons.Where(l => l.ChapterId == nodeId),
            CurriculumNodeType.Subject => lessons.Where(l => l.Chapter!.SubjectId == nodeId),
            CurriculumNodeType.Term => lessons.Where(l => l.Chapter!.Subject!.TermId == nodeId),
            _ => lessons.Where(l => l.Id == nodeId)
        };

        return AggregateAsync(userId, gameId, nodeType.ToString(), nodeId, lessons, checkUnlock: true, cancellationToken);
    }

    public Task<ServiceResult<NodeProgressDto>> GetGradeProgressAsync(
        Guid userId, Guid gameId, Guid gradeId, CancellationToken cancellationToken = default)
    {
        var lessons = _dbContext.Lessons.Where(l => l.Chapter!.Subject!.Term!.GradeId == gradeId);

        // Grades are never locked — a student only ever sees their own — so there is no unlock
        // row to look for and IsUnlocked is reported as true.
        return AggregateAsync(userId, gameId, "Grade", gradeId, lessons, checkUnlock: false, cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<WrongQuestionDto>>> GetWrongQuestionsAsync(
        Guid userId, Guid gameId, Guid lessonId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        if (!await _dbContext.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken))
            return ServiceResult<IReadOnlyList<WrongQuestionDto>>.NotFound("Lesson not found.");

        // Joined against active questions only. After a re-upload the old question rows still
        // exist but no longer describe anything the student can be shown, so the report goes
        // quiet until the lesson is replayed.
        var wrong = await _dbContext.UserQuestionProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.GameId == gameId && p.LessonId == lessonId && !p.IsCorrect)
            .Join(
                _dbContext.Questions.Where(q => q.IsActive && q.LangId == langId),
                p => p.QuestionId,
                q => q.Id,
                (p, q) => new WrongQuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    CorrectAnswerId = q.CorrectChoiceId,
                    CorrectAnswerText = q.Choices
                        .Where(c => c.Id == q.CorrectChoiceId)
                        .Select(c => c.Text)
                        .FirstOrDefault() ?? string.Empty,
                    Attempts = p.Attempts,
                    LastAttemptAt = p.LastAttemptAt
                })
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<WrongQuestionDto>>.Success(wrong);
    }

    public async Task<ServiceResult<ProgressSnapshotDto>> GetSnapshotAsync(
        Guid userId, Guid gameId, Guid? gradeId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        if (!await _dbContext.Games.AnyAsync(g => g.Id == gameId, cancellationToken))
            return ServiceResult<ProgressSnapshotDto>.NotFound("Game not found.");

        var resolvedGradeId = gradeId ?? await _dbContext.StudentProfiles
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.GradeId)
            .FirstOrDefaultAsync(cancellationToken) ?? Guid.Empty;

        if (resolvedGradeId == Guid.Empty)
            return ServiceResult<ProgressSnapshotDto>.Invalid(
                "No grade to snapshot: pass gradeId, or complete your profile so it can be inferred.");

        var grade = await _dbContext.Grades
            .Where(g => g.Id == resolvedGradeId)
            .Select(g => new
            {
                g.Id,
                Name = g.Translations.Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault() ?? string.Empty
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (grade is null)
            return ServiceResult<ProgressSnapshotDto>.NotFound("Grade not found.");

        // The snapshot is the game-open call, so it is also where a student who has never played
        // gets their starting point. Without this they would open the game to a fully locked
        // tree and have no legal first move — seeding only on submit is too late.
        await _unlockService.EnsureSeededAsync(userId, gameId, resolvedGradeId, cancellationToken);

        // Loaded flat and assembled in memory — clearer than a four-deep nested projection, and
        // it keeps the query count fixed regardless of how big the grade is.
        var terms = await _dbContext.Terms
            .Where(t => t.GradeId == resolvedGradeId)
            .OrderBy(t => t.Order)
            .Select(t => new
            {
                t.Id,
                t.Order,
                Name = t.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        var subjects = await _dbContext.Subjects
            .Where(s => s.Term!.GradeId == resolvedGradeId)
            .OrderBy(s => s.Order)
            .Select(s => new
            {
                s.Id,
                s.TermId,
                s.Order,
                Name = s.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        var chapters = await _dbContext.Chapters
            .Where(c => c.Subject!.Term!.GradeId == resolvedGradeId)
            .OrderBy(c => c.Order)
            .Select(c => new
            {
                c.Id,
                c.SubjectId,
                c.Order,
                Name = c.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        var lessons = await _dbContext.Lessons
            .Where(l => l.Chapter!.Subject!.Term!.GradeId == resolvedGradeId)
            .OrderBy(l => l.Order)
            .Select(l => new
            {
                l.Id,
                l.ChapterId,
                l.Order,
                Name = l.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LiveTotal = l.Questions.Count(q => q.IsActive && q.LangId == langId),
                CurrentVersion = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var lessonIds = lessons.Select(l => l.Id).ToList();

        var progress = await _dbContext.UserLessonProgress
            .Where(p => p.UserId == userId && p.GameId == gameId && lessonIds.Contains(p.LessonId))
            .ToDictionaryAsync(p => p.LessonId, cancellationToken);

        var unlockedIds = await _unlockService.GetUnlockedNodeIdsAsync(userId, gameId, cancellationToken);

        var snapshot = new ProgressSnapshotDto
        {
            GameId = gameId,
            LangId = langId,
            GradeId = grade.Id,
            GradeName = grade.Name
        };

        var gradeCorrect = 0;
        var gradeTotal = 0;

        foreach (var term in terms)
        {
            var termDto = new SnapshotTermDto
            {
                Id = term.Id,
                Name = term.Name,
                Order = term.Order,
                IsUnlocked = unlockedIds.Contains(term.Id)
            };

            var termCorrect = 0;
            var termTotal = 0;

            foreach (var subject in subjects.Where(s => s.TermId == term.Id))
            {
                var subjectDto = new SnapshotSubjectDto
                {
                    Id = subject.Id,
                    Name = subject.Name,
                    Order = subject.Order,
                    IsUnlocked = unlockedIds.Contains(subject.Id)
                };

                var subjectCorrect = 0;
                var subjectTotal = 0;

                foreach (var chapter in chapters.Where(c => c.SubjectId == subject.Id))
                {
                    var chapterDto = new SnapshotChapterDto
                    {
                        Id = chapter.Id,
                        Name = chapter.Name,
                        Order = chapter.Order,
                        IsUnlocked = unlockedIds.Contains(chapter.Id)
                    };

                    var chapterCorrect = 0;
                    var chapterTotal = 0;

                    foreach (var lesson in lessons.Where(l => l.ChapterId == chapter.Id))
                    {
                        progress.TryGetValue(lesson.Id, out var row);

                        chapterDto.Lessons.Add(new SnapshotLessonDto
                        {
                            Id = lesson.Id,
                            Name = lesson.Name,
                            Order = lesson.Order,
                            IsUnlocked = unlockedIds.Contains(lesson.Id),
                            HasQuestions = lesson.LiveTotal > 0,
                            CompletionState = (row?.CompletionState ?? CompletionState.Uncompleted).ToString(),
                            Percent = row?.Percent ?? 0,
                            Attempts = row?.Attempts ?? 0,
                            ContentUpdated = row is not null && row.QuestionsVersion != lesson.CurrentVersion
                        });

                        // **A lesson counts against the questions it can be measured by, and the
                        // language being read is not one of them.** LiveTotal is scoped to langId,
                        // so a lesson authored only in Arabic vanished from every rollup the moment
                        // an English reader asked for the snapshot — numerator and denominator both.
                        // The tile beside it kept showing the child's real score, because that comes
                        // from their stored row and is language-independent, so a subject bar froze
                        // at whatever the lessons that *did* have live text added up to and never
                        // moved again however much the child finished.
                        //
                        // Content ids are stable across languages and only the text is localized, so
                        // progress is not language-scoped either. An attempted lesson falls back to
                        // the question count the child actually answered, which is the set their
                        // BestPercent was measured against and therefore the honest denominator.
                        var lessonTotal = lesson.LiveTotal > 0 ? lesson.LiveTotal : row?.TotalCount ?? 0;

                        // Never attempted and nothing live to play: genuinely nothing to measure, so
                        // it stays out of the rollup rather than dragging a chapter toward 0% while
                        // it waits on an Arabic sheet.
                        if (lessonTotal == 0)
                            continue;

                        // **The child's best, not their latest.** This summed CorrectCount — the
                        // most recent attempt — while the lesson beside it reports CompletionState
                        // derived from BestPercent. Acing every lesson in a subject and then
                        // replaying one for fun dropped the subject to 95%, with every lesson still
                        // showing Aced: the two figures were measuring different things and
                        // disagreeing in public.
                        //
                        // Derived from BestPercent rather than a stored best count so this needs no
                        // migration, and it is the same number the lesson's own state is decided
                        // from — which is the property that makes the rollup and the tiles agree.
                        chapterCorrect += BestCorrectFor(row, lessonTotal);
                        chapterTotal += lessonTotal;
                    }

                    chapterDto.Percent = ToPercent(chapterCorrect, chapterTotal);
                    subjectDto.Chapters.Add(chapterDto);

                    subjectCorrect += chapterCorrect;
                    subjectTotal += chapterTotal;
                }

                subjectDto.Percent = ToPercent(subjectCorrect, subjectTotal);
                termDto.Subjects.Add(subjectDto);

                termCorrect += subjectCorrect;
                termTotal += subjectTotal;
            }

            termDto.Percent = ToPercent(termCorrect, termTotal);
            snapshot.Terms.Add(termDto);

            gradeCorrect += termCorrect;
            gradeTotal += termTotal;
        }

        snapshot.Percent = ToPercent(gradeCorrect, gradeTotal);
        return ServiceResult<ProgressSnapshotDto>.Success(snapshot);
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// Shared rollup for every level above a lesson. The denominator is the <b>live</b> active
    /// question count of each playable lesson, not the stored snapshot total, so a chapter the
    /// student has never touched reads 0% rather than 100% of nothing.
    /// </summary>
    private async Task<ServiceResult<NodeProgressDto>> AggregateAsync(
        Guid userId,
        Guid gameId,
        string nodeType,
        Guid nodeId,
        IQueryable<Domain.Curriculum.Lesson> lessons,
        bool checkUnlock,
        CancellationToken cancellationToken)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var shape = await lessons
            .Select(l => new
            {
                l.Id,
                LiveTotal = l.Questions.Count(q => q.IsActive && q.LangId == langId)
            })
            .ToListAsync(cancellationToken);

        var playable = shape.Where(l => l.LiveTotal > 0).ToList();
        var playableIds = playable.Select(l => l.Id).ToList();

        var rows = await _dbContext.UserLessonProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.GameId == gameId && playableIds.Contains(p.LessonId))
            .Select(p => new { p.LessonId, p.CorrectCount, p.CompletionState })
            .ToListAsync(cancellationToken);

        var totalQuestions = playable.Sum(l => l.LiveTotal);
        var correct = rows.Sum(r => r.CorrectCount);

        var dto = new NodeProgressDto
        {
            GameId = gameId,
            NodeType = nodeType,
            NodeId = nodeId,
            LessonsTotal = playable.Count,
            LessonsAttempted = rows.Count,
            LessonsCompleted = rows.Count(r => r.CompletionState is CompletionState.Completed or CompletionState.Aced),
            LessonsAced = rows.Count(r => r.CompletionState == CompletionState.Aced),
            CorrectCount = correct,
            TotalCount = totalQuestions,
            Percent = ToPercent(correct, totalQuestions),
            IsUnlocked = !checkUnlock || await _dbContext.UserNodeUnlocks.AnyAsync(
                u => u.UserId == userId && u.GameId == gameId && u.NodeId == nodeId, cancellationToken)
        };

        return ServiceResult<NodeProgressDto>.Success(dto);
    }

    /// <summary>
    /// The student's own grade when they have a profile, otherwise the grade the lesson sits
    /// under — so an admin without a student profile can still exercise a game.
    /// </summary>
    private async Task<Guid> ResolveGradeIdAsync(Guid userId, Guid lessonGradeId, CancellationToken cancellationToken)
    {
        var profileGradeId = await _dbContext.StudentProfiles
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.GradeId)
            .FirstOrDefaultAsync(cancellationToken);

        return profileGradeId ?? lessonGradeId;
    }

    private static int ToPercent(int correct, int total) =>
        total == 0 ? 0 : (int)Math.Round(correct * 100.0 / total, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A lesson's contribution to every rollup above it: its <b>best</b> score, expressed against
    /// the question count that is live today.
    /// <para>
    /// Rounded away from zero so a lesson recorded at 100% contributes its whole total and a
    /// subject of aced lessons reads exactly 100 — the figure a child is owed for finishing
    /// everything, and the one they will check.
    /// </para>
    /// <para>
    /// A lesson with no row has never been attempted and contributes nothing.
    /// </para>
    /// </summary>
    private static int BestCorrectFor(UserLessonProgress? row, int liveTotal)
    {
        if (row is null || liveTotal <= 0) return 0;

        // Clamped because BestPercent is a stored figure from an earlier attempt and the live
        // question count can have moved under it since.
        var best = Math.Clamp(row.BestPercent, 0, 100);

        return (int)Math.Round(best / 100.0 * liveTotal, MidpointRounding.AwayFromZero);
    }
}
