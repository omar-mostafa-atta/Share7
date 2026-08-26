using Microsoft.EntityFrameworkCore;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Share7.Domain.Constants;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// Builds the curriculum tree — grade → term → subject → chapter → lesson — and fills every lesson
/// with a playable question set in both content languages.
/// <para>
/// <b>The tree is matched by position, not by id.</b> A term is "the second term of Primary Three",
/// a chapter is "the third chapter of that subject". Databases that already have a hand-authored
/// node in one of those positions keep it and gain the rest of the tree around it, rather than
/// ending up with two second terms — which is what matching on a generated id would have produced.
/// </para>
/// <para>
/// <b>A lesson that already has questions is never touched.</b> The question set is versioned by
/// upload, and re-running the seeder over an authored sheet would either duplicate it or bump a
/// version nobody published. The check is the presence of a <see cref="LessonQuestionSet"/> row for
/// that (lesson, language) pair, which is exactly the row the importer writes.
/// </para>
/// </summary>
internal sealed class CurriculumSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly ContentSeedOptions _options;

    public CurriculumSeeder(ApplicationDbContext db, ContentSeedOptions options)
    {
        _db = db;
        _options = options;
    }

    private static readonly (Guid Id, bool IsArabic)[] Languages =
    [
        (LanguageIds.English, false),
        (LanguageIds.Arabic, true)
    ];

    public async Task SeedAsync(ContentSeedReport report, CancellationToken ct)
    {
        var lessons = await BuildTreeAsync(report, ct);
        await FillQuestionsAsync(lessons, report, ct);
    }

    /// <summary>Where a lesson sits, which is everything the question generator needs to know.</summary>
    private readonly record struct LessonSite(Guid LessonId, string SubjectKey, int GradeOrder, string Path);

    // ── the tree ──────────────────────────────────────────────────────────────

    private async Task<List<LessonSite>> BuildTreeAsync(ContentSeedReport report, CancellationToken ct)
    {
        var grades = await _db.Grades.OrderBy(g => g.Order).ToListAsync(ct);

        var existingTerms = await _db.Terms.ToDictionaryAsync(t => (t.GradeId, t.Order), t => t.Id, ct);
        var existingSubjects = await _db.Subjects.ToDictionaryAsync(s => (s.TermId, s.Order), s => s.Id, ct);
        var existingChapters = await _db.Chapters.ToDictionaryAsync(c => (c.SubjectId, c.Order), c => c.Id, ct);
        var existingLessons = await _db.Lessons.ToDictionaryAsync(l => (l.ChapterId, l.Order), l => l.Id, ct);

        var sites = new List<LessonSite>();

        foreach (var grade in grades)
        {
            var subjectKeys = CurriculumBlueprint.SubjectsFor(grade.Order);

            for (var termIndex = 0; termIndex < CurriculumBlueprint.TermNames.Length; termIndex++)
            {
                var termOrder = termIndex + 1;
                var termId = existingTerms.GetValueOrDefault((grade.Id, termOrder));

                if (termId == Guid.Empty)
                {
                    termId = SeedId.For("term", grade.Order.ToString(), termOrder.ToString());
                    var name = CurriculumBlueprint.TermNames[termIndex];

                    _db.Terms.Add(new Term
                    {
                        Id = termId,
                        GradeId = grade.Id,
                        Order = termOrder,
                        Translations = Names<TermTranslation>(name, (tr, lang, text) =>
                        {
                            tr.TermId = termId;
                            tr.LangId = lang;
                            tr.Name = text;
                        })
                    });

                    existingTerms[(grade.Id, termOrder)] = termId;
                    report.Terms++;
                }

                for (var subjectIndex = 0; subjectIndex < subjectKeys.Count; subjectIndex++)
                {
                    var subjectKey = subjectKeys[subjectIndex];
                    var subjectOrder = subjectIndex + 1;
                    var subjectId = existingSubjects.GetValueOrDefault((termId, subjectOrder));

                    if (subjectId == Guid.Empty)
                    {
                        subjectId = SeedId.For("subject", grade.Order.ToString(), termOrder.ToString(), subjectKey);
                        var name = CurriculumBlueprint.SubjectName(subjectKey);

                        _db.Subjects.Add(new Subject
                        {
                            Id = subjectId,
                            TermId = termId,
                            Order = subjectOrder,
                            Translations = Names<SubjectTranslation>(name, (tr, lang, text) =>
                            {
                                tr.SubjectId = subjectId;
                                tr.LangId = lang;
                                tr.Name = text;
                            })
                        });

                        existingSubjects[(termId, subjectOrder)] = subjectId;
                        report.Subjects++;
                    }

                    for (var chapterIndex = 0; chapterIndex < _options.ChaptersPerSubject; chapterIndex++)
                    {
                        var chapterOrder = chapterIndex + 1;
                        var chapterId = existingChapters.GetValueOrDefault((subjectId, chapterOrder));
                        var chapterName = CurriculumBlueprint.ChapterName(subjectKey, grade.Order, chapterIndex);

                        if (chapterId == Guid.Empty)
                        {
                            chapterId = SeedId.For("chapter", grade.Order.ToString(), termOrder.ToString(),
                                subjectKey, chapterOrder.ToString());

                            _db.Chapters.Add(new Chapter
                            {
                                Id = chapterId,
                                SubjectId = subjectId,
                                Order = chapterOrder,
                                Translations = Names<ChapterTranslation>(chapterName, (tr, lang, text) =>
                                {
                                    tr.ChapterId = chapterId;
                                    tr.LangId = lang;
                                    tr.Name = text;
                                })
                            });

                            existingChapters[(subjectId, chapterOrder)] = chapterId;
                            report.Chapters++;
                        }

                        for (var lessonIndex = 0; lessonIndex < _options.LessonsPerChapter; lessonIndex++)
                        {
                            var lessonOrder = lessonIndex + 1;
                            var lessonId = existingLessons.GetValueOrDefault((chapterId, lessonOrder));

                            if (lessonId == Guid.Empty)
                            {
                                lessonId = SeedId.For("lesson", grade.Order.ToString(), termOrder.ToString(),
                                    subjectKey, chapterOrder.ToString(), lessonOrder.ToString());

                                var aspect = CurriculumBlueprint.LessonAspects[
                                    lessonIndex % CurriculumBlueprint.LessonAspects.Length];

                                var lessonName = new Bilingual(
                                    $"{aspect.En}: {chapterName.En}",
                                    $"{aspect.Ar}: {chapterName.Ar}");

                                _db.Lessons.Add(new Lesson
                                {
                                    Id = lessonId,
                                    ChapterId = chapterId,
                                    Order = lessonOrder,
                                    Translations = Names<LessonTranslation>(lessonName, (tr, lang, text) =>
                                    {
                                        tr.LessonId = lessonId;
                                        tr.LangId = lang;
                                        tr.Name = text;
                                    })
                                });

                                existingLessons[(chapterId, lessonOrder)] = lessonId;
                                report.Lessons++;
                            }

                            sites.Add(new LessonSite(
                                lessonId,
                                subjectKey,
                                grade.Order,
                                $"{grade.Order}:{termOrder}:{subjectKey}:{chapterOrder}:{lessonOrder}"));
                        }
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        return sites;
    }

    /// <summary>Builds the English and Arabic translation rows for one node.</summary>
    private static List<T> Names<T>(Bilingual name, Action<T, Guid, string> assign) where T : new()
    {
        var rows = new List<T>(Languages.Length);

        foreach (var (langId, isArabic) in Languages)
        {
            var row = new T();
            assign(row, langId, name.For(isArabic));
            rows.Add(row);
        }

        return rows;
    }

    // ── the questions ─────────────────────────────────────────────────────────

    private async Task FillQuestionsAsync(List<LessonSite> sites, ContentSeedReport report, CancellationToken ct)
    {
        if (_options.QuestionsPerLesson <= 0 && _options.RecoveryQuestionsPerLesson <= 0) return;

        var haveMain = (await _db.LessonQuestionSets.Select(s => new { s.LessonId, s.LangId }).ToListAsync(ct))
            .Select(s => (s.LessonId, s.LangId)).ToHashSet();

        var haveRecovery = (await _db.LessonRecoveryQuestionSets.Select(s => new { s.LessonId, s.LangId }).ToListAsync(ct))
            .Select(s => (s.LessonId, s.LangId)).ToHashSet();

        _db.ChangeTracker.Clear();
        var autoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            var pending = 0;
            var now = DateTime.UtcNow;

            foreach (var site in sites)
            {
                foreach (var (langId, isArabic) in Languages)
                {
                    if (_options.QuestionsPerLesson > 0 && !haveMain.Contains((site.LessonId, langId)))
                    {
                        pending += WriteMain(site, langId, isArabic, now, report);
                    }

                    if (_options.RecoveryQuestionsPerLesson > 0 && !haveRecovery.Contains((site.LessonId, langId)))
                    {
                        pending += WriteRecovery(site, langId, isArabic, now, report);
                    }
                }

                if (pending < _options.BatchSize) continue;

                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
                pending = 0;
            }

            if (pending > 0)
            {
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
            }
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private int WriteMain(LessonSite site, Guid langId, bool isArabic, DateTime now, ContentSeedReport report)
    {
        var written = 0;
        var langTag = isArabic ? "ar" : "en";

        for (var i = 0; i < _options.QuestionsPerLesson; i++)
        {
            var stream = QuestionBank.StreamFor(site.Path, langTag, "main", i);
            var generated = QuestionBank.For(site.SubjectKey, site.GradeOrder, isArabic, stream);

            // The correct answer rotates across the three lanes rather than sitting in one of them.
            var ordered = generated.Ordered(stream % 3);
            var correctSlot = stream % 3;

            var questionId = SeedId.For("question", site.Path, langTag, i.ToString());
            var choiceIds = new Guid[3];

            for (var slot = 0; slot < 3; slot++)
                choiceIds[slot] = SeedId.For("choice", site.Path, langTag, i.ToString(), slot.ToString());

            _db.Questions.Add(new Question
            {
                Id = questionId,
                LessonId = site.LessonId,
                LangId = langId,
                Text = generated.Text,
                CorrectChoiceId = choiceIds[correctSlot],
                Version = 1,
                IsActive = true,
                RowNumber = i + 1,
                CreatedAt = now
            });

            for (var slot = 0; slot < 3; slot++)
            {
                _db.QuestionChoices.Add(new QuestionChoice
                {
                    Id = choiceIds[slot],
                    QuestionId = questionId,
                    Text = ordered[slot],
                    OrderIndex = slot
                });
            }

            written += 4;
            report.Questions++;
        }

        _db.LessonQuestionSets.Add(new LessonQuestionSet
        {
            LessonId = site.LessonId,
            LangId = langId,
            Version = 1
        });

        _db.LessonQuestionUploads.Add(new LessonQuestionUpload
        {
            Id = SeedId.For("question-upload", site.Path, langTag),
            LessonId = site.LessonId,
            LangId = langId,
            Version = 1,
            FileName = string.Empty,
            Source = QuestionSetSource.ManualEntry,
            QuestionCount = _options.QuestionsPerLesson,
            UploadedByUserId = null,
            UploadedAt = now
        });

        return written + 2;
    }

    private int WriteRecovery(LessonSite site, Guid langId, bool isArabic, DateTime now, ContentSeedReport report)
    {
        var written = 0;
        var langTag = isArabic ? "ar" : "en";

        for (var i = 0; i < _options.RecoveryQuestionsPerLesson; i++)
        {
            // Offset into a different part of the bank than the main set, so the recovery pool is a
            // second chance at the topic rather than the same five questions again.
            var stream = QuestionBank.StreamFor(site.Path, langTag, "recovery", i);
            var generated = QuestionBank.For(site.SubjectKey, site.GradeOrder, isArabic, stream);

            var correctSlot = stream % 3;
            var ordered = generated.Ordered(correctSlot);

            var questionId = SeedId.For("recovery-question", site.Path, langTag, i.ToString());
            var choiceIds = new Guid[3];

            for (var slot = 0; slot < 3; slot++)
                choiceIds[slot] = SeedId.For("recovery-choice", site.Path, langTag, i.ToString(), slot.ToString());

            _db.RecoveryQuestions.Add(new RecoveryQuestion
            {
                Id = questionId,
                LessonId = site.LessonId,
                LangId = langId,
                Text = generated.Text,
                CorrectChoiceId = choiceIds[correctSlot],
                Version = 1,
                IsActive = true,
                RowNumber = i + 1,
                CreatedAt = now
            });

            for (var slot = 0; slot < 3; slot++)
            {
                _db.RecoveryQuestionChoices.Add(new RecoveryQuestionChoice
                {
                    Id = choiceIds[slot],
                    RecoveryQuestionId = questionId,
                    Text = ordered[slot],
                    OrderIndex = slot
                });
            }

            written += 4;
            report.RecoveryQuestions++;
        }

        _db.LessonRecoveryQuestionSets.Add(new LessonRecoveryQuestionSet
        {
            LessonId = site.LessonId,
            LangId = langId,
            Version = 1
        });

        _db.LessonRecoveryQuestionUploads.Add(new LessonRecoveryQuestionUpload
        {
            Id = SeedId.For("recovery-upload", site.Path, langTag),
            LessonId = site.LessonId,
            LangId = langId,
            Version = 1,
            FileName = string.Empty,
            Source = QuestionSetSource.ManualEntry,
            QuestionCount = _options.RecoveryQuestionsPerLesson,
            UploadedByUserId = null,
            UploadedAt = now
        });

        return written + 2;
    }
}
