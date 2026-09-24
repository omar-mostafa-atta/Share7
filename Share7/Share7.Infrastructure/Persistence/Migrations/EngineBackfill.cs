namespace Share7.Infrastructure.Persistence.Migrations;

/// <summary>
/// The data half of <c>EngineAuthoritative</c>: brings content that exists only in the old shape
/// into the engine's shape. Kept beside the generated migration, like
/// <see cref="EducationalBackfill"/>, because the schema half is machine-written and this is not.
/// <para>
/// **Derived, never invented.** Every row it writes is a restatement of one that exists:
/// </para>
/// <list type="number">
/// <item>Every typed row gets its node, if it has none yet (content written after the first
/// projection), keeping its id.</item>
/// <item>The recovery pool joins the item bank. Each recovery row is copied into <c>Questions</c>
/// with <b>its own id</b> and <c>Role = Recovery</c>, each choice into <c>QuestionChoices</c> with
/// its own id, and each <c>(lesson, row number)</c> becomes an item — the same lineage key the main
/// pool's backfill used, for the same reason: the importer has always written a row's languages
/// under one row number. The old tables stay, as a compatibility copy.</item>
/// <item>Each served set's version moves into <c>PublishedItemSets</c>, unchanged.</item>
/// <item>Each upload's history row moves into <c>ContentPublications</c>.</item>
/// <item>The migrated Egyptian curriculum version is marked authoritative.</item>
/// </list>
/// <para>
/// Re-runnable by construction — every insert is guarded by <c>NOT EXISTS</c> — which is also why
/// it is public: the seeder and the contract fixture write content the old way (as production data
/// was written) and then run exactly this, so the backfill itself is what the tests exercise.
/// Recovery items are deliberately <b>not</b> mapped to learning targets: nothing answers them
/// through the attempt endpoint, so they carry no evidence, and counting them would inflate every
/// target's item totals.
/// </para>
/// </summary>
public static class EngineBackfill
{
    public const string Sql = """
        SET NOCOUNT ON;

        DECLARE @now datetime2 = SYSUTCDATETIME();
        DECLARE @bank uniqueidentifier = 'D3A9E6CB-418A-4F5D-B079-2C6E0F37B4D8';
        DECLARE @version uniqueidentifier = 'A7D3E5F9-8C60-4B14-92A0-9E4C1B308577';
        DECLARE @kGrade uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000001';
        DECLARE @kTerm uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000002';
        DECLARE @kSubject uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000003';
        DECLARE @kChapter uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000004';
        DECLARE @kLesson uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000005';

        ------------------------------------------------------------------ 0. every typed row has its node
        -- The node tree becomes the source of truth here, so a typed row written without one —
        -- content seeded after the first projection, say — gets it now, with its own id, exactly as
        -- the first projection made them. Retired typed rows (a column this migration adds, so none
        -- yet) would carry their retirement across.
        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT g.Id, @version, NULL, @kGrade, 'grade', g.[Order], 0,
               '/' + LOWER(CONVERT(varchar(36), g.Id)), 0, 'Grades', @now, NULL
        FROM Grades g
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = g.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT t.Id, @version, t.GradeId, @kTerm, 'term', t.[Order], 1,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), t.Id)), 0, 'Terms', @now, t.RetiredAtUtc
        FROM Terms t
        JOIN CurriculumNodes p ON p.Id = t.GradeId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = t.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT s.Id, @version, s.TermId, @kSubject, 'subject', s.[Order], 2,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), s.Id)), 0, 'Subjects', @now, s.RetiredAtUtc
        FROM Subjects s
        JOIN CurriculumNodes p ON p.Id = s.TermId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = s.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT c.Id, @version, c.SubjectId, @kChapter, 'chapter', c.[Order], 3,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), c.Id)), 0, 'Chapters', @now, c.RetiredAtUtc
        FROM Chapters c
        JOIN CurriculumNodes p ON p.Id = c.SubjectId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = c.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT l.Id, @version, l.ChapterId, @kLesson, 'lesson', l.[Order], 4,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), l.Id)), 1, 'Lessons', @now, l.RetiredAtUtc
        FROM Lessons l
        JOIN CurriculumNodes p ON p.Id = l.ChapterId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = l.Id);

        INSERT INTO CurriculumNodeTranslations (NodeId, LangId, Title)
        SELECT src.NodeId, src.LangId, src.Title
        FROM (
            SELECT GradeId   AS NodeId, Lang_Id AS LangId, Name AS Title FROM GradeTranslations
            UNION ALL SELECT TermId,    Lang_Id, Name FROM TermTranslations
            UNION ALL SELECT SubjectId, Lang_Id, Name FROM SubjectTranslations
            UNION ALL SELECT ChapterId, Lang_Id, Name FROM ChapterTranslations
            UNION ALL SELECT LessonId,  Lang_Id, Name FROM LessonTranslations
        ) src
        JOIN CurriculumNodes n ON n.Id = src.NodeId
        WHERE NOT EXISTS (
            SELECT 1 FROM CurriculumNodeTranslations x
            WHERE x.NodeId = src.NodeId AND x.LangId = src.LangId);

        ------------------------------------------------------------------ 1. the recovery pool
        SELECT rq.LessonId, rq.RowNumber, ItemId = NEWID(),
               SourceKey = 'lesson/' + LOWER(CONVERT(varchar(36), rq.LessonId))
                           + '/recovery/' + CONVERT(varchar(11), rq.RowNumber)
        INTO #ritems
        FROM RecoveryQuestions rq
        GROUP BY rq.LessonId, rq.RowNumber;

        INSERT INTO Items (Id, ItemBankId, SourceKey, IsAnchor, CreatedAtUtc, RetiredAtUtc)
        SELECT t.ItemId, @bank, t.SourceKey, 0,
               MIN(rq.CreatedAt),
               CASE WHEN MAX(CASE WHEN rq.IsActive = 1 THEN 1 ELSE 0 END) = 1
                    THEN NULL ELSE MAX(rq.DeactivatedAt) END
        FROM #ritems t
        JOIN RecoveryQuestions rq ON rq.LessonId = t.LessonId AND rq.RowNumber = t.RowNumber
        WHERE NOT EXISTS (SELECT 1 FROM Items i WHERE i.SourceKey = t.SourceKey)
        GROUP BY t.ItemId, t.SourceKey;

        -- Re-point at whatever Items holds now, so a re-run reuses rows rather than orphaning ids.
        UPDATE t SET ItemId = i.Id
        FROM #ritems t
        JOIN Items i ON i.SourceKey = t.SourceKey;

        SELECT t.ItemId, rq.Version, VersionId = NEWID(),
               CreatedAtUtc = MIN(rq.CreatedAt),
               RetiredAtUtc = CASE WHEN MAX(CASE WHEN rq.IsActive = 1 THEN 1 ELSE 0 END) = 1
                                   THEN NULL ELSE MAX(rq.DeactivatedAt) END
        INTO #rversions
        FROM RecoveryQuestions rq
        JOIN #ritems t ON t.LessonId = rq.LessonId AND t.RowNumber = rq.RowNumber
        GROUP BY t.ItemId, rq.Version;

        INSERT INTO ItemVersions
            (Id, ItemId, VersionNumber, ItemKindKey, ResponseSpec, ScoringSpec,
             PsychometricContinuity, CreatedAtUtc, RetiredAtUtc)
        SELECT v.VersionId, v.ItemId, v.Version, 'single_choice', NULL, NULL,
               0, v.CreatedAtUtc, v.RetiredAtUtc
        FROM #rversions v
        WHERE NOT EXISTS (
            SELECT 1 FROM ItemVersions iv WHERE iv.ItemId = v.ItemId AND iv.VersionNumber = v.Version);

        UPDATE v SET VersionId = iv.Id
        FROM #rversions v
        JOIN ItemVersions iv ON iv.ItemId = v.ItemId AND iv.VersionNumber = v.Version;

        -- Same id, same text, same state, same history — only the table and the role are new.
        INSERT INTO Questions
            (Id, ItemVersionId, LessonId, Lang_Id, Question, CorrectChoiceId, Version, IsActive,
             RowNumber, CreatedAt, DeactivatedAt, Role)
        SELECT rq.Id, v.VersionId, rq.LessonId, rq.Lang_Id, rq.Question, rq.CorrectChoiceId,
               rq.Version, rq.IsActive, rq.RowNumber, rq.CreatedAt, rq.DeactivatedAt, 2
        FROM RecoveryQuestions rq
        JOIN #ritems t ON t.LessonId = rq.LessonId AND t.RowNumber = rq.RowNumber
        JOIN #rversions v ON v.ItemId = t.ItemId AND v.Version = rq.Version
        WHERE NOT EXISTS (SELECT 1 FROM Questions q WHERE q.Id = rq.Id);

        INSERT INTO QuestionChoices (Id, QuestionId, Choice, OrderIndex)
        SELECT c.Id, c.RecoveryQuestionId, c.Choice, c.OrderIndex
        FROM RecoveryQuestionChoices c
        WHERE EXISTS (SELECT 1 FROM Questions q WHERE q.Id = c.RecoveryQuestionId)
          AND NOT EXISTS (SELECT 1 FROM QuestionChoices x WHERE x.Id = c.Id);

        INSERT INTO NodeItemMappings
            (Id, CurriculumVersionId, NodeId, ItemId, Role, [Order], CreatedAtUtc, RemovedAtUtc)
        SELECT NEWID(), @version, t.LessonId, t.ItemId, 2, t.RowNumber, @now, NULL
        FROM #ritems t
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = t.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM NodeItemMappings m
            WHERE m.CurriculumVersionId = @version AND m.NodeId = t.LessonId
              AND m.ItemId = t.ItemId AND m.Role = 2);

        ------------------------------------------------------------------ 2. served versions
        INSERT INTO PublishedItemSets (NodeId, Role, LangId, Version, ItemCount, UpdatedAtUtc, UpdatedByUserId)
        SELECT s.LessonId, 0, s.Lang_Id, s.Version,
               (SELECT COUNT(*) FROM Questions q
                WHERE q.LessonId = s.LessonId AND q.Lang_Id = s.Lang_Id AND q.IsActive = 1 AND q.Role = 0),
               COALESCE((SELECT MAX(u.UploadedAt) FROM LessonQuestionUploads u
                         WHERE u.LessonId = s.LessonId AND u.Lang_Id = s.Lang_Id), @now),
               NULL
        FROM LessonQuestionSets s
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = s.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM PublishedItemSets p
            WHERE p.NodeId = s.LessonId AND p.Role = 0 AND p.LangId = s.Lang_Id);

        INSERT INTO PublishedItemSets (NodeId, Role, LangId, Version, ItemCount, UpdatedAtUtc, UpdatedByUserId)
        SELECT s.LessonId, 2, s.Lang_Id, s.Version,
               (SELECT COUNT(*) FROM Questions q
                WHERE q.LessonId = s.LessonId AND q.Lang_Id = s.Lang_Id AND q.IsActive = 1 AND q.Role = 2),
               COALESCE((SELECT MAX(u.UploadedAt) FROM LessonRecoveryQuestionUploads u
                         WHERE u.LessonId = s.LessonId AND u.Lang_Id = s.Lang_Id), @now),
               NULL
        FROM LessonRecoveryQuestionSets s
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = s.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM PublishedItemSets p
            WHERE p.NodeId = s.LessonId AND p.Role = 2 AND p.LangId = s.Lang_Id);

        ------------------------------------------------------------------ 3. publication history
        INSERT INTO ContentPublications
            (Id, NodeId, Role, LangId, Version, Source, FileName, ItemCount,
             PublishedByUserId, PublishedAtUtc, ReleaseId)
        SELECT NEWID(), u.LessonId, 0, u.Lang_Id, u.Version, u.Source, u.FileName, u.QuestionCount,
               u.UploadedByUserId, u.UploadedAt, NULL
        FROM LessonQuestionUploads u
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = u.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM ContentPublications p
            WHERE p.NodeId = u.LessonId AND p.Role = 0 AND p.LangId = u.Lang_Id AND p.Version = u.Version);

        INSERT INTO ContentPublications
            (Id, NodeId, Role, LangId, Version, Source, FileName, ItemCount,
             PublishedByUserId, PublishedAtUtc, ReleaseId)
        SELECT NEWID(), u.LessonId, 2, u.Lang_Id, u.Version, u.Source, u.FileName, u.QuestionCount,
               u.UploadedByUserId, u.UploadedAt, NULL
        FROM LessonRecoveryQuestionUploads u
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = u.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM ContentPublications p
            WHERE p.NodeId = u.LessonId AND p.Role = 2 AND p.LangId = u.Lang_Id AND p.Version = u.Version);

        ------------------------------------------------------------------ 4. the source of truth
        UPDATE CurriculumVersions SET IsAuthoritative = 1 WHERE Id = @version AND IsAuthoritative = 0;

        DROP TABLE #ritems;
        DROP TABLE #rversions;
        """;
}
