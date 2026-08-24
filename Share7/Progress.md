# Progress.md

# EduPlatform — Games, Progress & Unlocks

Companion to `CLAUDE.md` / `Architecture.md` / `ContentIngestion.md`. Covers the mini-game
catalog, the per-user progress model, the unlock ladder, and the curriculum restructure this
module depends on.

**Status: built.** Everything in this file exists in code. Multiplayer result recording and the
items under *Open / deferred* remain outstanding.

## Sequencing

Two migrations, in this order. The first rewrites the IDs the second points at, so it cannot be
reordered.

1. ✅ **`CurriculumSharedTreeAndTranslations`** — restructures Grade→Lesson away from
   language-partitioned rows. Verified on scratch databases: migrate-from-scratch, upgrade over
   a populated pre-restructure database, accounts and refresh tokens surviving while student
   profiles clear, plus 34 HTTP checks over ordering, per-language duplicate rejection and
   independent per-language question versions. `ContentIngestion.md` is the live reference for
   what shipped; Part 1 below is the design it was built from.
2. ✅ **`GameCatalogAndProgress`** — `Games`, `GameTranslations`, `UserQuestionProgress`,
   `UserLessonProgress`, `UserNodeUnlocks`. Planned as two migrations and merged into one:
   both are purely additive and shipped together, so splitting them bought nothing. Purely
   `CreateTable` — no data is touched, unlike migration 1. Verified with 43 HTTP checks
   covering the score thresholds, server-side regrading, the unlock ladder, rollups,
   per-game independence and the carry-forward behaviour on re-upload.

---

# Part 1 — Curriculum restructure

## What changes and why

Content is currently **language-partitioned**: "Grade 5" and "الصف الخامس" are two independent
rows with two independent subtrees and no link between them (`ContentIngestion.md`). The
consequence is that a student switching language lands in a different tree with different ids
and loses everything.

This module needs progress and unlocks to survive a language switch, so the tree becomes
**one set of language-free entities plus a translations table per level**:

```
Grades      Id, Order
Terms       Id, GradeId,   Order
Subjects    Id, TermId,    Order
Chapters    Id, SubjectId, Order
Lessons     Id, ChapterId, Order

GradeTranslations    GradeId,   LangId, Name      PK (GradeId, LangId)
TermTranslations     TermId,    LangId, Name      PK (TermId, LangId)
SubjectTranslations  SubjectId, LangId, Name      PK (SubjectId, LangId)
ChapterTranslations  ChapterId, LangId, Name      PK (ChapterId, LangId)
LessonTranslations   LessonId,  LangId, Name      PK (LessonId, LangId)
```

`AspNetUsers.PreferredLanguageId` and the `preferred_language` JWT claim are **unchanged** —
they now select which translation to serve rather than which subtree to serve.

## Questions stay language-partitioned

Questions and choices keep `LangId` exactly as they are today. This follows from the upload
workflow: the admin uploads a 4-column Arabic sheet and a separate 4-column English sheet,
each tagged with a language. Two independent uploads cannot produce one shared question with
two translations — nothing pairs row 7 of one sheet to row 7 of the other, and because a
re-upload creates brand-new question rows with new GUIDs, re-uploading one language would
orphan the other language's translations.

A shared-question model would have required a single 8-column bilingual sheet. Rejected —
the content team delivers per-language sheets.

Consequences, all accepted:

- The **denominator differs per language** (40 English questions, 38 Arabic) so the same
  lesson can score differently in each. Harmless given carry-forward semantics (below).
- **Per-question detail is language-specific.** "Which questions did I get wrong" only covers
  the language they were answered in. Lesson-level and above are shared.
- A lesson can have questions in one language and none in the other — see *Missing question
  sets*.

### `Lessons.QuestionsVersion` moves

One int cannot version two independent question sets. It becomes its own table:

```
LessonQuestionSets   LessonId, LangId, Version     PK (LessonId, LangId)
```

`Version` is 0 until the first sheet is uploaded for that lesson+language, then increments per
upload, same as today. The client cache protocol is unaffected in shape — the version
endpoints already resolve by the caller's language — but `GET /api/lessons` now returns the
caller's language's version rather than a single shared one.

### Missing question sets

A lesson with `Version = 0` in the student's language is **not playable in that language**.
Browse responses carry `hasQuestions: false` so the client can grey it out rather than letting
a student enter an empty lesson.

Such a lesson must **not** block the unlock ladder — one missing Arabic sheet would otherwise
freeze the whole chapter for every Arabic student. So chapter completion is evaluated per
student language, and lessons with no question set in that language count as satisfied. This
means an Arabic student can unlock a chapter that an English student cannot, from the same
content. Unlock rows are not language-scoped, so a grant earned this way persists if the
student later switches.

This is a workaround for incomplete content, not a feature. An admin report listing lessons
missing a question set per language is worth building alongside it.

## Ordering

Every level gets `Order int`, unique per parent, auto-assigned to the next available value on
create. This is a prerequisite for the unlock ladder — "lesson 2 unlocks after lesson 1" is
undefined without it, and no content table has a sort column today
(`ContentIngestion.md` → *Not built yet* → Ordering).

Note `Grades` previously had an `Order` column that was dropped by the
`CurriculumHierarchyAndQuestionVersioning` migration; this re-adds it.

A reorder endpoint is not in scope for the first pass — order is set at create time.

## Endpoint changes

**Admin create** — there is no longer a `Lang_Id` to inherit from the parent, so the create
endpoints take both names at once:

```json
POST /api/admin/chapters/{chapterId}/lessons
{ "translations": [ { "langId": "...", "name": "Photosynthesis" },
                    { "langId": "...", "name": "التمثيل الضوئي" } ] }
```

The duplicate-sibling-name check now runs **per language**: two lessons under one chapter may
collide in English while their Arabic names differ, and that must be rejected on the English
name alone. Trimmed and case-folded for comparison, original casing stored — unchanged rule,
new scope.

**Browse** — `/api/terms`, `/api/subjects`, `/api/chapters`, `/api/lessons`, `/api/grades` keep
their routes and response shapes. They resolve `name` from the translation matching the
caller's language instead of filtering rows by `Lang_Id`. Cross-tree requests can no longer
return empty, because there is only one tree.

**Deletes** — unchanged in behavior. Cascade now also removes the translation rows.

## Grade seed — Egyptian system

Grades are re-seeded from scratch against the Egyptian pre-university system, replacing the
generic "Grade 1–12" rows. Fourteen grades, each with an English and an Arabic translation:

| Order | English | Arabic |
|---|---|---|
| 1 | KG1 | الروضة الأولى |
| 2 | KG2 | الروضة الثانية |
| 3 | Primary One | الصف الأول الابتدائي |
| 4 | Primary Two | الصف الثاني الابتدائي |
| 5 | Primary Three | الصف الثالث الابتدائي |
| 6 | Primary Four | الصف الرابع الابتدائي |
| 7 | Primary Five | الصف الخامس الابتدائي |
| 8 | Primary Six | الصف السادس الابتدائي |
| 9 | Preparatory One | الصف الأول الإعدادي |
| 10 | Preparatory Two | الصف الثاني الإعدادي |
| 11 | Preparatory Three | الصف الثالث الإعدادي |
| 12 | Secondary One | الصف الأول الثانوي |
| 13 | Secondary Two | الصف الثاني الثانوي |
| 14 | Secondary Three | الصف الثالث الثانوي |

Secondary Two and Three are **not** split into علمي / أدبي. The specializations are modelled as
subjects under the same grade, so the grade list stays linear.

Seeded as static lookup data via EF Core `HasData()` with fixed GUIDs held in
`Domain/Constants/GradeIds.cs`, following the `LanguageIds.cs` pattern — not imperative
seeding in `Program.cs`. (Imperative grade seeding is what produced 24 rows instead of 12 last
time; see the `CurriculumHierarchyAndQuestionVersioning` repair.)

`GET /api/grades` sorts by `Order` from now on, fixing the current sort-by-name behavior that
places "Grade 10" before "Grade 2".

## Migration notes

The existing content is **sample data**, not real curriculum, so no EN↔AR pairing exercise is
needed — the tree below Grade is rebuilt through the admin API.

All 24 existing grade rows are dropped and replaced by the seed above, so every
`StudentProfile.GradeId` becomes dangling. **`StudentProfiles` rows are deleted**; the user
accounts, credentials, roles and the seeded admin are left intact. Affected students are sent
back through `POST /api/auth/complete-profile` once, which the client already implements as
part of the `isProfileComplete` branch.

⚠ `Program.cs` runs `dbContext.Database.MigrateAsync()` on every startup, so this deletion
executes **automatically against the MonsterASP.NET database on the next deploy**, with no
manual step. Everything destructive in this migration should be reviewed on that basis.

---

# Part 2 — Game catalog

Mirrors Unity's `MiniGameDefinitionSO`. **The backend is authoritative** — matchmaking has to
enforce player counts server-side, so the DB wins over the ScriptableObject.

```
Games
  Id                    uniqueidentifier PK     -- Unity's gameId, sent as a string
  GameKey               nvarchar(64) unique     -- readable slug, e.g. "subway_runner"
  LobbyScene            int
  GameplayScene         int
  LobbySceneAddress     nvarchar(256) null      -- Addressables scene address
  GameplaySceneAddress  nvarchar(256) null      -- null = still on the build indices
  MinPlayers            int  default 1
  MaxPlayers            int  default 2
  ReadyTimeoutSeconds   float default 20
  SupportsSinglePlayer  bit  default 1
  SupportsMultiplayer   bit  default 1
  UseLobby              bit  default 1
  UseMatchmaking        bit  default 1
  IsActive              bit  default 1

GameTranslations
  GameId, LangId, DisplayName, Description       PK (GameId, LangId)
```

One game row with N translations — **not** one row per language. Two rows would give the same
game two ids and split its progress in half.

`LobbyScene`/`GameplayScene` are Unity build indices, stored here by request. They are client
build artifacts, so a client rebuild that renumbers scenes desyncs them from the DB.

`LobbySceneAddress`/`GameplaySceneAddress` supersede them. A build index cannot name a scene that
is not in the build, so a mini-game whose scenes are downloaded on demand has no index to give —
scene identity has to be a key the client's content system can resolve. **Null means the game
still uses the indices**, which is the flag clients switch on while games are migrated one at a
time; both columns are served until no shipped build reads the indices. Nullable rather than
`NOT NULL DEFAULT ''` for exactly that reason: "not authored yet" and "authored as blank" must not
look the same.

No availability matrix (game restricted to certain grades/subjects) in this pass — every active
game is available everywhere.

---

# Part 3 — Progress

## Tables

```
UserQuestionProgress
  UserId, GameId, QuestionId          PK (UserId, GameId, QuestionId)
  LessonId                            -- denormalized; every read filters by it
  IsCorrect       bit
  Attempts        int
  LastAttemptAt   datetime2
  IX (UserId, GameId, LessonId)

UserLessonProgress
  UserId, GameId, LessonId            PK (UserId, GameId, LessonId)
  CorrectCount            int
  TotalCount              int
  Percent                 int          -- rounded to nearest, stored for sorting/reads
  Attempts                int
  CompletionState         tinyint      -- Uncompleted | Completed | Aced
  QuestionsVersion        int          -- the version this snapshot was scored against
  FirstAttemptWasPerfect  bit
  LastAttemptAt           datetime2

UserNodeUnlock
  UserId, GameId, NodeType, NodeId    PK (UserId, GameId, NodeType, NodeId)
  UnlockedAt      datetime2
```

`NodeType` is Term / Subject / Chapter / Lesson. `NodeId` carries no FK — it points at four
different tables. Accepted, because nothing aggregates off this table; it is a pure ledger.

**Nothing is stored above lesson level.** Chapter, subject, term and grade progress are
`GROUP BY` queries over `UserLessonProgress`, and wrong-question counts are `GROUP BY` over
`UserQuestionProgress`. Storing them would mean recomputing every affected user's rows every
time an admin adds a lesson to a chapter — the denominator changes and stored rollups go
silently stale.

```sql
-- chapter progress: no table needed
SELECT SUM(p.CorrectCount) AS Correct, SUM(p.TotalCount) AS Total
FROM   UserLessonProgress p
JOIN   Lessons l ON l.Id = p.LessonId
WHERE  p.UserId = @userId AND p.GameId = @gameId AND l.ChapterId = @chapterId;
```

Subject is one more join, term two, grade three.

**All three tables must be added to `UserAdminService.DeleteUserAsync`** — nothing cascades
from `AspNetUsers`, and user delete is a hard delete. Omitting them orphans rows silently.

## Progress is per game

A student's tree in the runner game is independent of every other game. To see progress for a
lesson, you pick a game first. Unlocks are per game too — a new game starts at the seed state
of Part 4 regardless of what the student achieved elsewhere.

## Completion state

Evaluated on the **last attempt**, not the best one. Replaying badly lowers the score and can
drop the state.

| State | Rule |
|---|---|
| `Uncompleted` | never played, or last attempt < 50% |
| `Completed` | last attempt ≥ 50% and < 100% |
| `Aced` | last attempt = 100% |

`Aced` is deliberately *not* "100% on the first attempt" — combined with last-attempt
semantics that would make Aced unrepeatable, so one replay would permanently demote a perfect
lesson. The "did it in one go" fact is kept separately as `FirstAttemptWasPerfect`, which is
set once and never recalculated.

## Submitting an attempt

```
POST /api/progress/attempts
{
  "gameId": "...",
  "lessonId": "...",
  "correctChoiceIds": [ "...", "..." ],
  "correctCount": 5
}
```

Single-player only in this pass. Multiplayer result recording is out of scope.

The client grades offline against its cached question set (it holds `correctAnswerId` — see
`ContentIngestion.md`), so it sends only the choices the student got right. **The server
recomputes anyway.** `correctCount` is a checksum, never the source of truth.

Server-side handling:

1. Resolve the student's language from the `preferred_language` claim.
2. Load the active questions for `(lessonId, langId)` and their `CorrectChoiceId` values.
   `404` unknown lesson; `409` no active question set in that language.
3. `403` if the lesson is not unlocked for `(user, game)`.
4. Dedupe `correctChoiceIds`, then keep only ids that are the `CorrectChoiceId` of an active
   question in **this** lesson and language. Anything else is discarded silently — a choice id
   from another lesson, from a retired question version, or a wrong answer submitted as right.
5. `CorrectCount` = distinct questions thereby satisfied. `TotalCount` = active question count.
   `Percent` = round(Correct × 100 / Total).
6. Log a warning if `correctCount` disagrees with the server figure. Return both; do not fail
   the request.
7. Write `UserQuestionProgress` for **every** active question — `IsCorrect` true if it was in
   the verified set, false otherwise. Unanswered and wrong are the same thing, which is sound
   because one run shows every question in the lesson.
8. Upsert `UserLessonProgress`; set `FirstAttemptWasPerfect` only when `Attempts` was 0 and
   `Percent` is 100.
9. Run the unlock evaluation (Part 4).
10. Return the new lesson state plus any nodes newly unlocked by this attempt, so the client
    can play the unlock animation without a second call.

## Re-uploads carry forward

A re-upload soft-deletes the old questions and inserts a fresh set with **new GUIDs**. There is
no matching between old and new, so per-question progress cannot survive it.

Rather than resetting the lesson to 0%, the stored `UserLessonProgress` snapshot is **left
untouched** — a typo fix in one question does not wipe a student's lesson. The row keeps
scoring 20/40 = 50% while the lesson now holds 20 questions; the next attempt recomputes
against the new set and the number jumps.

Because `UserLessonProgress.QuestionsVersion` records what the snapshot was scored against,
read endpoints compare it to the current version and return **`contentUpdated: true`** when
they differ, so the game can prompt "new questions available, replay this lesson".

Wrong-question reports **filter to active questions only**, so a lesson's report goes empty
after a re-upload until the student plays it again.

---

# Part 4 — Unlock ladder

All unlocks are **permanent once earned**, scoped to `(User, Game)`. Nothing ever re-locks —
a student who aces lesson 1, unlocks lesson 2, then replays lesson 1 badly keeps lesson 2.

## Subjects are not a rung

**A term opens all of its subjects at once.** A student may start Science without having
finished Maths, and the sibling subject they ignore never blocks them. `Order` on a subject is
a display order and nothing more.

This is the one exception in the ladder, and it is a content-shape decision rather than a
progression one: a term's subjects are a *parallel split* of that term's material — the same
weeks of school, taught in different rooms — where its chapters really are a sequence through
one subject. Gating them serialised something that was never sequential, and forced a student
stuck on one subject to stop using the app rather than switch to another.

The rung above still holds: a term is complete when every lesson under it is, across all of
its subjects, so ungating them makes the next term harder to reach rather than easier.

Seed state for a new `(user, game)` pair: **the first term of the student's grade by `Order`,
every subject in it, and each of those subjects opened down to its first chapter and that
chapter's first lesson.** Everything else locked.

Rules, evaluated after every attempt submission and idempotent:

| Node | Unlocks when |
|---|---|
| Lesson N+1 | Lesson N in the same chapter is `Completed` or `Aced` |
| Chapter N+1 *(and its first lesson)* | Every lesson in chapter N is `Completed` or `Aced` |
| Subject | *never gated* — it opens with its term |
| Term N+1 *(and all of its subjects, each to its first lesson)* | Every lesson in term N is `Completed` or `Aced` |

A gated node is "complete" when all of its children are. This is the one rule at every level
that has one — it subsumes the special case of "the last lesson of chapter 1 opens chapter 2",
which was ambiguous on its own: because completion follows the last attempt and can drop,
finishing the last lesson does not guarantee the earlier ones are still complete.

**Existing students are repaired in place, not migrated.** The seed call runs on every
game-open, and it now tops up the terms a student already holds instead of returning early.
Anyone who started while subjects still gated each other picks up the missing ones on their
next snapshot — as does everyone in a term an author adds a subject to later.

The practical effect: a student who completes lessons 1–3 of a chapter, then replays lesson 2
and scores 20%, is blocked from the next chapter until they redo lesson 2. Lessons 1–3
themselves stay unlocked throughout.

**Grades do not lock.** A student is pinned to `StudentProfile.GradeId` and only sees their own
grade, so the ladder tops out at Term. Multi-grade progression would be one more rule in the
same evaluator.

Lessons not playable in the student's language are treated as satisfied for the chapter check —
see *Missing question sets*.

---

# Part 5 — Reads

| Endpoint | Returns |
|---|---|
| `GET /api/progress/games/{gameId}/lessons/{lessonId}` | stored row + `contentUpdated` |
| `GET /api/progress/games/{gameId}/chapters/{chapterId}` | rollup |
| `GET /api/progress/games/{gameId}/subjects/{subjectId}` | rollup |
| `GET /api/progress/games/{gameId}/terms/{termId}` | rollup |
| `GET /api/progress/games/{gameId}/grades/{gradeId}` | rollup |
| `GET /api/progress/games/{gameId}/lessons/{lessonId}/wrong-questions` | active questions with `IsCorrect = 0` |
| `GET /api/progress/games/{gameId}/snapshot` | the whole tree with availability + completion per node |

The snapshot endpoint is the one the game client actually wants on launch — it mirrors Unity's
`CurriculumSnapshot`, which expects availability and completion state on every node. Serving it
as one call avoids a request per lesson.

Access: a student reads only their own progress. Teacher and parent views are **out of scope** —
there is no class, enrollment or teacher-student relation anywhere in the schema, and inventing
one belongs to its own module.

---

# Open / deferred

- **Multiplayer result recording.** Deferred by decision. When it lands, decide whether a match
  writes curriculum progress for both players, and how a question a player never got to answer
  is scored.
- **`recoveryQuestions`** — the pool now has tables and endpoints (2026-08-17) but still no
  trigger logic (`Architecture.md` open question 2). **Untouched by this module either way**:
  nothing in progress reads, scores or unlocks against recovery questions, and
  `UserQuestionProgress` still points only at `Questions`. If recovery answers should ever count
  toward progress, that is a new decision, not an oversight.
- **Rate limiting / anti-cheat** on attempt submission — explicitly not wanted for now. The
  server-side recompute is the only guard; a client can still under-report.
- **Reorder endpoint** for content ordering.
- **Admin report** of lessons missing a question set per language.
- **Game availability matrix** (restricting a game to certain grades or subjects).

# Docs updated when Part 1 landed

All done — listed here so the trail is visible, not as outstanding work.

- `ContentIngestion.md` — rewritten for the shared tree, per-language question versioning,
  the `translations[]` create body and the required `langId` on upload.
- `Architecture.md` — open question 3 ("node unlock rules") marked answered; its progress-model
  sketch marked superseded by this file.
- `CLAUDE.md` — the language-partitioning decision reversed under *Core architectural
  decisions*, progress bullet pointed here, status entry added.
- `UnityIntegration.md` — the stale `nameEn`/`nameAr` grades shape corrected, and the
  "only Auth exists" section replaced.
