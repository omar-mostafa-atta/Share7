namespace Share7.Infrastructure.Persistence.Migrations;

/// <summary>
/// The data half of <c>EducationalIdentityAndStructure</c>. Kept beside the generated migration
/// rather than inside it, because the schema half is machine-written and this is not.
/// <para>
/// Everything here is **derived from data that already exists** — no value is invented and no
/// judgement is guessed. The item identity was already in the rows, unpromoted: the sheet importer
/// has always written the English and Arabic renderings of one question with the same
/// <c>(LessonId, RowNumber)</c>, and a republish has always carried a new <c>Version</c>. That is a
/// complete lineage key, so the migration is exact rather than a best effort. Measured on the
/// development database before it was written: 17,116 question rows, 8,550 distinct
/// <c>(LessonId, RowNumber)</c> pairs, 8,558 distinct <c>(LessonId, RowNumber, Version)</c> triples,
/// and **zero** keys with anything other than a clean English/Arabic pair.
/// </para>
/// <para>
/// Re-runnable by construction: every insert is guarded by a <c>NOT EXISTS</c>, so a partially
/// applied migration can be finished rather than needing the database restored.
/// </para>
/// </summary>
internal static class EducationalBackfill
{
    public const string Sql = """
        SET NOCOUNT ON;

        DECLARE @now datetime2 = SYSUTCDATETIME();
        DECLARE @bank uniqueidentifier = 'D3A9E6CB-418A-4F5D-B079-2C6E0F37B4D8';
        DECLARE @framework uniqueidentifier = 'B8E4F6A0-9D71-4C25-83B1-0F5D2C419688';
        DECLARE @version uniqueidentifier = 'A7D3E5F9-8C60-4B14-92A0-9E4C1B308577';
        DECLARE @kGrade uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000001';
        DECLARE @kTerm uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000002';
        DECLARE @kSubject uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000003';
        DECLARE @kChapter uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000004';
        DECLARE @kLesson uniqueidentifier = '13D0C0B1-0000-4000-8000-000000000005';

        ------------------------------------------------------------------ 1. item identity
        -- One Item per (LessonId, RowNumber), across every version and both languages. Retired
        -- rows are included deliberately: an item whose latest publish removed it still has
        -- responses against it, and the whole point of this table is that they stay interpretable.
        SELECT q.LessonId, q.RowNumber, ItemId = NEWID()
        INTO #items
        FROM Questions q
        GROUP BY q.LessonId, q.RowNumber;

        INSERT INTO Items (Id, ItemBankId, SourceKey, IsAnchor, CreatedAtUtc, RetiredAtUtc)
        SELECT t.ItemId, @bank,
               'lesson/' + LOWER(CONVERT(varchar(36), t.LessonId)) + '/core/' + CONVERT(varchar(11), t.RowNumber),
               0,
               MIN(q.CreatedAt),
               CASE WHEN MAX(CASE WHEN q.IsActive = 1 THEN 1 ELSE 0 END) = 1
                    THEN NULL ELSE MAX(q.DeactivatedAt) END
        FROM #items t
        JOIN Questions q ON q.LessonId = t.LessonId AND q.RowNumber = t.RowNumber
        WHERE NOT EXISTS (SELECT 1 FROM Items i WHERE i.SourceKey =
              'lesson/' + LOWER(CONVERT(varchar(36), t.LessonId)) + '/core/' + CONVERT(varchar(11), t.RowNumber))
        GROUP BY t.ItemId, t.LessonId, t.RowNumber;

        -- Re-point at whatever is actually in Items now, so a re-run reuses the existing rows
        -- rather than the ids this pass generated and did not insert.
        UPDATE t SET ItemId = i.Id
        FROM #items t
        JOIN Items i ON i.SourceKey =
             'lesson/' + LOWER(CONVERT(varchar(36), t.LessonId)) + '/core/' + CONVERT(varchar(11), t.RowNumber);

        -- One ItemVersion per (item, publish version). Items whose RowNumber survived a republish
        -- gain linked lineage here — history that was previously severed at every content edit.
        SELECT t.ItemId, q.Version, VersionId = NEWID(),
               CreatedAtUtc = MIN(q.CreatedAt),
               RetiredAtUtc = CASE WHEN MAX(CASE WHEN q.IsActive = 1 THEN 1 ELSE 0 END) = 1
                                   THEN NULL ELSE MAX(q.DeactivatedAt) END
        INTO #versions
        FROM Questions q
        JOIN #items t ON t.LessonId = q.LessonId AND t.RowNumber = q.RowNumber
        GROUP BY t.ItemId, q.Version;

        INSERT INTO ItemVersions
            (Id, ItemId, VersionNumber, ItemKindKey, ResponseSpec, ScoringSpec,
             PsychometricContinuity, CreatedAtUtc, RetiredAtUtc)
        SELECT v.VersionId, v.ItemId, v.Version, 'single_choice', NULL, NULL,
               -- False for every historical link, and not as a default. Nobody recorded whether
               -- those edits were cosmetic, and a wrong `true` silently pools statistics across a
               -- rewritten question. A wrong `false` merely restarts them. Only one is recoverable.
               0,
               v.CreatedAtUtc, v.RetiredAtUtc
        FROM #versions v
        WHERE NOT EXISTS (
            SELECT 1 FROM ItemVersions iv WHERE iv.ItemId = v.ItemId AND iv.VersionNumber = v.Version);

        UPDATE v SET VersionId = iv.Id
        FROM #versions v
        JOIN ItemVersions iv ON iv.ItemId = v.ItemId AND iv.VersionNumber = v.Version;

        -- Today's Question row is an item localization: the per-language rendering of one version.
        UPDATE q SET ItemVersionId = v.VersionId
        FROM Questions q
        JOIN #items t ON t.LessonId = q.LessonId AND t.RowNumber = q.RowNumber
        JOIN #versions v ON v.ItemId = t.ItemId AND v.Version = q.Version;

        ------------------------------------------------------------------ 2. placeholder targets
        -- One target per lesson, including lessons with no questions: coverage reporting has to be
        -- able to say "this lesson teaches X and there is no evidence on it", which needs the
        -- target to exist before the content does.
        SELECT l.Id AS LessonId, TargetId = NEWID()
        INTO #targets
        FROM Lessons l;

        INSERT INTO LearningTargets
            (Id, FrameworkId, TargetKey, TargetKindKey, IsPlaceholder, ReviewState,
             DifficultyBand, CreatedAtUtc, RetiredAtUtc)
        SELECT t.TargetId, @framework, 'lesson/' + LOWER(CONVERT(varchar(36), t.LessonId)),
               'lesson_placeholder', 1, 0, NULL, @now, NULL
        FROM #targets t
        WHERE NOT EXISTS (
            SELECT 1 FROM LearningTargets lt
            WHERE lt.FrameworkId = @framework
              AND lt.TargetKey = 'lesson/' + LOWER(CONVERT(varchar(36), t.LessonId)));

        UPDATE t SET TargetId = lt.Id
        FROM #targets t
        JOIN LearningTargets lt
          ON lt.FrameworkId = @framework
         AND lt.TargetKey = 'lesson/' + LOWER(CONVERT(varchar(36), t.LessonId));

        -- The statement is the lesson's own name. That is honest about what a placeholder is: a
        -- lesson typed as a target, not a claim anybody qualified wrote.
        -- Lang_Id, not LangId: the legacy translation tables carry the older spelling, and the
        -- new ones do not inherit it.
        INSERT INTO LearningTargetTranslations (TargetId, LangId, Statement)
        SELECT t.TargetId, lt.Lang_Id, lt.Name
        FROM #targets t
        JOIN LessonTranslations lt ON lt.LessonId = t.LessonId
        WHERE NOT EXISTS (
            SELECT 1 FROM LearningTargetTranslations x
            WHERE x.TargetId = t.TargetId AND x.LangId = lt.Lang_Id);

        INSERT INTO ItemTargetMappings (Id, ItemId, TargetId, Emphasis, IsPrimary, CreatedAtUtc)
        SELECT NEWID(), i.ItemId, t.TargetId, 1.0, 1, @now
        FROM #items i
        JOIN #targets t ON t.LessonId = i.LessonId
        WHERE NOT EXISTS (
            SELECT 1 FROM ItemTargetMappings m WHERE m.ItemId = i.ItemId AND m.TargetId = t.TargetId);

        ------------------------------------------------------------------ 3. the node projection
        -- Grade / Term / Subject / Chapter / Lesson become one recursive table, **keeping their
        -- exact ids**. Every stored reference, every client cache and every
        -- UserLessonProgress.LessonId keeps resolving, which is what makes this safe to run on a
        -- live product: the two shapes cannot disagree, because they are the same identifiers.
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
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), t.Id)), 0, 'Terms', @now, NULL
        FROM Terms t
        JOIN CurriculumNodes p ON p.Id = t.GradeId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = t.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT s.Id, @version, s.TermId, @kSubject, 'subject', s.[Order], 2,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), s.Id)), 0, 'Subjects', @now, NULL
        FROM Subjects s
        JOIN CurriculumNodes p ON p.Id = s.TermId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = s.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT c.Id, @version, c.SubjectId, @kChapter, 'chapter', c.[Order], 3,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), c.Id)), 0, 'Chapters', @now, NULL
        FROM Chapters c
        JOIN CurriculumNodes p ON p.Id = c.SubjectId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = c.Id);

        INSERT INTO CurriculumNodes
            (Id, CurriculumVersionId, ParentNodeId, NodeKindId, KindKey, [Order], Depth, [Path],
             IsPlayable, LegacySource, CreatedAtUtc, RetiredAtUtc)
        SELECT l.Id, @version, l.ChapterId, @kLesson, 'lesson', l.[Order], 4,
               p.[Path] + '/' + LOWER(CONVERT(varchar(36), l.Id)), 1, 'Lessons', @now, NULL
        FROM Lessons l
        JOIN CurriculumNodes p ON p.Id = l.ChapterId
        WHERE NOT EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = l.Id);

        -- Titles, gathered from the five translation tables into one. Duplicated on purpose: the
        -- point of a generic node is that a reader never has to know which table a title lives in.
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

        INSERT INTO NodeTargetMappings (Id, CurriculumVersionId, NodeId, TargetId, Emphasis, CreatedAtUtc)
        SELECT NEWID(), @version, t.LessonId, t.TargetId, 1.0, @now
        FROM #targets t
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = t.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM NodeTargetMappings m
            WHERE m.CurriculumVersionId = @version AND m.NodeId = t.LessonId AND m.TargetId = t.TargetId);

        INSERT INTO NodeItemMappings
            (Id, CurriculumVersionId, NodeId, ItemId, Role, [Order], CreatedAtUtc, RemovedAtUtc)
        SELECT NEWID(), @version, i.LessonId, i.ItemId, 0, i.RowNumber, @now, NULL
        FROM #items i
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = i.LessonId)
          AND NOT EXISTS (
            SELECT 1 FROM NodeItemMappings m
            WHERE m.CurriculumVersionId = @version AND m.NodeId = i.LessonId
              AND m.ItemId = i.ItemId AND m.Role = 0);

        ------------------------------------------------------------------ 4. enrollment
        -- Backfilled from StudentProfile.GradeId, which stays readable and writable. Nothing is
        -- forced to migrate on a schedule: the column is deprecated by there being a better answer
        -- available, not by being taken away.
        INSERT INTO Enrollments
            (Id, LearnerId, CurriculumVersionId, PlacementNodeId, Source, IsPrimary,
             StartedAtUtc, EndedAtUtc, CreatedAtUtc)
        SELECT NEWID(), sp.UserId, @version, sp.GradeId, 3, 1,
               sp.CreatedAt, NULL, @now
        FROM StudentProfiles sp
        WHERE EXISTS (SELECT 1 FROM CurriculumNodes n WHERE n.Id = sp.GradeId)
          AND NOT EXISTS (
            SELECT 1 FROM Enrollments e
            WHERE e.LearnerId = sp.UserId AND e.IsPrimary = 1 AND e.EndedAtUtc IS NULL);

        ------------------------------------------------------------------ 5. existing evidence
        -- LearnerResponse.ItemVersionId named a Questions.Id, which is a localization. All three
        -- columns are set in one statement: SQL Server evaluates every SET expression against the
        -- pre-update row, so the old value is still readable while it is being replaced.
        UPDATE r
        SET ItemLocalizationId = r.ItemVersionId,
            ItemVersionId = q.ItemVersionId,
            ItemId = iv.ItemId
        FROM LearnerResponses r
        JOIN Questions q ON q.Id = r.ItemVersionId
        JOIN ItemVersions iv ON iv.Id = q.ItemVersionId;

        -- Attempt ordinals were derived per localization and are now per item, which is a real
        -- change of meaning: answering the same question in Arabic and then in English used to
        -- read as two first encounters and is one item answered twice. Recomputed from the log
        -- itself rather than left stale — this is the recompute line doing its job.
        WITH ordered AS (
            SELECT r.Id,
                   n = ROW_NUMBER() OVER (
                       PARTITION BY r.LearnerId, r.ItemId ORDER BY r.Sequence)
            FROM LearnerResponses r
        )
        UPDATE r
        SET AttemptOrdinal = o.n,
            IsFirstEncounter = CASE WHEN o.n = 1 THEN 1 ELSE 0 END
        FROM LearnerResponses r
        JOIN ordered o ON o.Id = r.Id;

        DROP TABLE #items;
        DROP TABLE #versions;
        DROP TABLE #targets;
        """;
}
