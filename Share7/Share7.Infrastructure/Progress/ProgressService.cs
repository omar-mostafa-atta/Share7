using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Progress.Models;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
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

    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;
    private readonly IUnlockService _unlockService;
    private readonly IRewardService _rewardService;
    private readonly IWalletService _walletService;

    public ProgressService(
        ApplicationDbContext dbContext,
        ILanguageService languageService,
        IUnlockService unlockService,
        IRewardService rewardService,
        IWalletService walletService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
        _unlockService = unlockService;
        _rewardService = rewardService;
        _walletService = walletService;
    }

    // ------------------------------------------------------------- writing

    public async Task<ServiceResult<AttemptResultDto>> SubmitAttemptAsync(
        Guid userId, SubmitAttemptRequest request, CancellationToken cancellationToken = default)
    {
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

        var completionState = percent >= 100
            ? CompletionState.Aced
            : percent >= PassPercent
                ? CompletionState.Completed
                : CompletionState.Uncompleted;

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

        lessonRow.CorrectCount = correctCount;
        lessonRow.TotalCount = totalCount;
        lessonRow.Percent = percent;
        lessonRow.Attempts += 1;
        lessonRow.CompletionState = completionState;
        lessonRow.QuestionsVersion = lesson.Version;
        lessonRow.LastAttemptAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var unlocked = await _unlockService.EvaluateAfterAttemptAsync(
            userId, request.GameId, request.LessonId, langId, cancellationToken);

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
                CompletionState = completionState,
                RequestId = request.RequestId
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Read after the commit so what goes back is the committed truth, and so the transaction
        // is not held open for it.
        var balances = await _walletService.GetBalancesAsync(userId, cancellationToken);

        return ServiceResult<AttemptResultDto>.Success(new AttemptResultDto
        {
            GameId = request.GameId,
            LessonId = request.LessonId,
            LangId = langId,
            CorrectCount = correctCount,
            TotalCount = totalCount,
            Percent = percent,
            Attempts = lessonRow.Attempts,
            CompletionState = completionState.ToString(),
            FirstAttemptWasPerfect = lessonRow.FirstAttemptWasPerfect,
            QuestionsVersion = lesson.Version,
            Answers = answerResults,
            UnrecognisedAnswers = unrecognised,
            Unlocked = unlocked,
            Rewards = rewards,
            Balances = balances
        });
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

                        // Unplayable lessons contribute nothing either way, so a chapter waiting
                        // on an Arabic sheet is not dragged toward 0% by lessons no one can play.
                        if (lesson.LiveTotal == 0)
                            continue;

                        chapterCorrect += row?.CorrectCount ?? 0;
                        chapterTotal += lesson.LiveTotal;
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
}
