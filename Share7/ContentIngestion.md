# ContentIngestion.md

# EduPlatform — Curriculum Content & Question Versioning

Companion to `CLAUDE.md` / `Architecture.md`. Covers the content tables, the Excel upload
pipeline, and the question-cache protocol the Unity client uses.

## Languages and the shared tree

> **Superseded 2026-08.** Content used to be *partitioned* by language — one row per node per
> language, with the EN and AR trees entirely separate. That model shipped and was then
> replaced by the shared tree described below, because separate ids per language meant a
> student switching language lost all progress and unlocks. The migration that reversed it is
> `CurriculumSharedTreeAndTranslations`.

The tree (Grade → Term → Subject → Chapter → Lesson) is **one set of language-independent
rows**, each with a name per language in a `*Translations` child table. One lesson has one id
no matter which language you read it in. **Questions are the exception** and remain
per-language — see below.

The `Languages` lookup is unchanged:

```
Languages
  Id            uniqueidentifier PK
  Name          nvarchar(50)      -- "English", "العربية"
  Code          nvarchar(5)       -- "en", "ar"  (unique)
```

Seeded with two fixed ids, available as constants in `Share7.Domain/Constants/LanguageIds.cs`:

| Language | Id |
|---|---|
| English | `9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34` |
| Arabic  | `4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71` |

`AspNetUsers.PreferredLanguageId` (and the `preferred_language` JWT claim) now decide which
*translation* is served, not which subtree — the ids are the same either way.

Consequences worth remembering:
- A student switching language stays on the same nodes. Progress and unlocks carry over;
  only the on-device question cache has to be re-fetched, since questions are per-language.
- Cross-language multiplayer is possible on the tree, though the two players would be
  answering different question sets for the same lesson.

## Content tables

Each level is `Id` + parent FK + `Order`, with the display names in a sibling translation
table. Deleting a parent cascades down the whole chain, translations included.

```
Grades      Id, Order
Terms       Id, GradeId,   Order
Subjects    Id, TermId,    Order
Chapters    Id, SubjectId, Order
Lessons     Id, ChapterId, Order

GradeTranslations    GradeId,   Lang_Id, Name     -- PK (GradeId, Lang_Id)
TermTranslations     TermId,    Lang_Id, Name
SubjectTranslations  SubjectId, Lang_Id, Name
ChapterTranslations  ChapterId, Lang_Id, Name
LessonTranslations   LessonId,  Lang_Id, Name
```

`Order` is **unique per parent** and 1-based. It is what the progress module's unlock chain
steps through ("lesson N+1 opens once lesson N is completed"), so two siblings cannot share a
position. Create endpoints append to the end unless an explicit `order` is supplied.

`Grades` is seeded with the 14-grade Egyptian ladder — KG1, KG2, Primary One–Six, Preparatory
One–Three, Secondary One–Three — with fixed ids in `Domain/Constants/GradeIds.cs`. Secondary
is **not** split into علمي / أدبي; the specializations are modelled as subjects, which keeps
the ladder linear.

```
Questions
  Id, LessonId, Lang_Id, Question, CorrectChoiceId,
  Version, IsActive, RowNumber, CreatedAt, DeactivatedAt

QuestionChoices
  Id, QuestionId, Choice, OrderIndex        -- exactly 3 per question

LessonQuestionSets                           -- the per-language cache key
  LessonId, Lang_Id, Version                 -- PK (LessonId, Lang_Id)

LessonQuestionUploads                        -- audit trail, one row per successful upload
  Id, LessonId, Lang_Id, Version, FileName, QuestionCount, UploadedByUserId, UploadedAt
```

**Questions keep `Lang_Id`.** This follows from the upload workflow: the admin uploads a
separate 4-column sheet per language, and nothing pairs row 7 of the Arabic sheet to row 7 of
the English one. Since a re-upload creates brand-new question rows with new ids, re-uploading
one language would orphan the other's rows if they were meant to be translations of each
other. A shared-question model would need a single 8-column bilingual sheet instead.

So each language has its own question set **and its own version**. `LessonQuestionSets` is
what `Lessons.QuestionsVersion` used to be — one int could not version two independent sets.
A missing row means version 0: nothing uploaded in that language, and the lesson is not
playable in it.

`Questions.CorrectChoiceId` is deliberately **not** a database FK. Questions and
QuestionChoices reference each other, and constraining both directions creates a cycle SQL
Server rejects. The importer is the only writer and sets both sides in one transaction.

`OrderIndex` preserves the source column order (0 = Excel col 2, the correct one). The
client is expected to shuffle before assigning answers to lanes/doors.

### Recovery questions — the same four tables again

`recoveryQuestions` is the secondary per-lesson pool. It is a **structural clone** of the four
tables above — same columns, same foreign keys, same indexes, same cascade behaviour:

```
RecoveryQuestions
  Id, LessonId, Lang_Id, Question, CorrectChoiceId,
  Version, IsActive, RowNumber, CreatedAt, DeactivatedAt

RecoveryQuestionChoices
  Id, RecoveryQuestionId, Choice, OrderIndex   -- exactly 3 per question

LessonRecoveryQuestionSets                     -- the per-language cache key
  LessonId, Lang_Id, Version                   -- PK (LessonId, Lang_Id)

LessonRecoveryQuestionUploads                  -- audit trail, one row per successful upload
  Id, LessonId, Lang_Id, Version, FileName, QuestionCount, UploadedByUserId, UploadedAt
```

Everything said above about the main pool applies unchanged: `Lang_Id` partitioning, the
soft-delete-and-reversion lifecycle, `CorrectChoiceId` not being a real FK, `OrderIndex`, and
version 0 meaning "nothing uploaded in this language".

**Separate tables rather than a flag on `Questions`** because the two pools are uploaded
independently and each needs its own version counter. A client caching both compares two versions
and re-downloads only the pool that moved; a shared counter would force a re-download of both
whenever either changed.

⚠ **Trigger logic is still undefined.** The pool is stored and served; nothing specifies when the
game should show a recovery question. That is still a content-team decision.

## Excel format

One sheet, four columns, one question per row:

| Column | Meaning |
|---|---|
| 1 | Question text |
| 2 | **Correct** answer |
| 3 | Wrong answer |
| 4 | Wrong answer |

Language is **not** a column in the sheet, and it can no longer be inherited from the lesson
either, because a lesson is shared across languages. It is passed as a **required `langId`
query parameter** on the upload endpoint. Uploading Arabic questions means picking the lesson
and then saying "Arabic".

Row 1 is treated as a header and skipped by default; pass `?hasHeaderRow=false` for a sheet
with no header. Fully blank rows are ignored.

### Validation

The import is **all-or-nothing**. If any row fails, nothing is written, the lesson's version
is left untouched, and the response lists every offending row number. Rules:

- Question text and all three answers must be non-empty
- Question ≤ 1000 chars, each answer ≤ 500 chars
- The three answers must differ from each other — two identical doors where one counts as wrong
  would be unanswerable. **This comparison is case-sensitive**, unlike the one for node names:
  capitalisation is frequently the thing being tested, so `Fe` / `FE` / `fe` (iron's chemical
  symbol) is a valid row, while `Same` / `Same` / `Other` is not
- ≤ 5000 question rows per sheet
- `.xlsx` only (ClosedXML cannot read legacy `.xls`), ≤ 10 MB

The recovery sheet uses this **identical** format and these identical rules — the two importers
share one parser (`QuestionSheetParser`) rather than keeping two copies of the validation, so the
two can't drift apart.

## Versioning & the client cache protocol

Every upload increments that lesson-and-language's `LessonQuestionSets.Version` by 1 — first
upload produces version 1, then 2, 3, and so on. **The two languages version independently**:
publishing English v2 leaves Arabic sitting at v1. The previous question rows are
**soft-deleted** (`IsActive = false`, `DeactivatedAt` set) rather than removed, so student
progress that references an old `QuestionId` stays resolvable, and the deactivation is scoped
to the uploaded language so the other set is untouched. Only `IsActive` rows are ever served.

The client flow:

1. On game open, ask the API for the version(s) it holds cached.
2. If the returned version equals the cached one → play from the on-device cache, no
   further calls.
3. If it differs (or the client has nothing cached) → download the full question set and
   replace the cache for that lesson.

Because the client grades offline, the questions payload **includes `correctAnswerId`**.
This is a deliberate exception to the "never reveal the correct answer" rule — see
`CLAUDE.md`. Anything that writes progress or decides a match result must still be graded
server-side; a client claim that an answer was correct is not trusted.

Concurrency note: two admins uploading to the same lesson *in the same language* simultaneously
would compute the same next version. The unique index on
`LessonQuestionUploads (LessonId, Lang_Id, Version)` makes the second one fail rather than
silently produce two "version 3"s. Two admins uploading different languages do not collide.

## Endpoints

### `GET /api/languages` — anonymous

Returns `[{ id, name, code }]`. Feeds the language picker.

### `POST /api/users/me/preferred-language` — authenticated

```json
{ "languageId": "9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34" }
```

Stores the user's content language in `AspNetUsers.PreferredLanguageId` and **returns a fresh token
pair** (same shape as login). Because the language is cached in the `preferred_language` claim, the
caller's existing token would keep serving the old language until it expired — the client must
replace its stored tokens with the ones in this response. Returns `400` for an unknown id. `GET` on
the same route returns the current value (English when unset).

Note that language is normally chosen at **registration** (`POST /api/auth/register` takes a
required `languageId`); this endpoint is for changing it afterwards.

### Browsing the tree — `[Authorize]`

All four resolve each node's **name** into the caller's content language, taken from the
`preferred_language` token claim (no database round trip; falls back to a lookup when the claim
is absent). They do not filter rows by language — there is only one tree.

| Endpoint | Query | Returns |
|---|---|---|
| `GET /api/terms` | `gradeId` *(optional)* | `[{ id, name, langId, gradeId, order }]` |
| `GET /api/subjects` | `termId` *(optional)* | `[{ id, name, langId, termId, order }]` |
| `GET /api/chapters` | `subjectId` **(required)** | `[{ id, name, langId, subjectId, order }]` |
| `GET /api/lessons` | `chapterId` **(required)** | `[{ id, name, langId, chapterId, order, questionsVersion, hasQuestions }]` |

Results are sorted by `order`, not by name. `langId` echoes which language the `name` was
resolved into — the `id` itself is the same in every language.

Without the optional filter, `/api/terms` and `/api/subjects` return every row — so "First Term"
comes back once per grade. Pass the parent id to narrow.

A node with no translation in the caller's language comes back with an **empty `name`** rather
than vanishing from the list, so a missing translation is visible instead of silently
truncating the tree.

`/api/lessons` includes each lesson's `questionsVersion` **for the caller's language**, so a
client can validate its entire question cache for a chapter from this one response without a
separate version call. `hasQuestions` is false when nothing has been uploaded in that language
— the lesson exists and is named, but there is nothing to play, and the client should show it
as unavailable rather than opening an empty session.

### `GET /api/grades?langId={guid}` — anonymous

Grades in one language. With a bearer token the caller's preferred language is used;
`langId` overrides it and is how the admin page picks a language explicitly. Defaults to
English.

### Building the tree — Admin / SuperAdmin

| Endpoint | Creates |
|---|---|
| `POST /api/admin/grades/{gradeId}/terms` | a term under a grade |
| `POST /api/admin/terms/{termId}/subjects` | a subject under a term |
| `POST /api/admin/subjects/{subjectId}/chapters` | a chapter under a subject |
| `POST /api/admin/chapters/{chapterId}/lessons` | a lesson under a chapter |

All four take the same body — a name per language, plus an optional position:

```json
{
  "translations": [
    { "langId": "9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34", "name": "First Term" },
    { "langId": "4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71", "name": "الفصل الأول" }
  ],
  "order": 1
}
```

**A name is required for every configured language.** A node with a missing translation would be
nameless for those students and nothing else in the system would notice, so it is rejected up
front rather than allowed and patched later.

**`order` is optional** and defaults to appending after the last sibling. Supplying a position
that is already taken is refused rather than silently shuffling the others, because the unlock
chain steps through this order.

Responses return the created node with its name in the **caller's own** language
(`{ id, name, langId, parentId, order }`; lessons also carry `questionsVersion: 0` and
`hasQuestions: false` — upload a sheet per language to publish version 1 of each).

Status codes: `404` unknown parent, `409` a sibling already has that name *in one of the supplied
languages* or already occupies the requested `order`, `400` blank name / missing a language /
unknown language / the same language twice, `403` caller is not an admin.

**Duplicate names.** Each name is trimmed and lowercased, then compared against `LOWER(name)` of
the existing siblings' translations **in that same language** — explicitly, rather than leaning on
the database collation happening to be case-insensitive. So `"Science"`, `"SCIENCE"`, `"sCiEnCe"`
and `"  science  "` all collide. A clash in *either* language rejects the whole request. The
**original casing is what gets stored**, so display names keep their capitals. The same name under
a *different* parent is fine — two grades can both have a "First Term".

Each call creates one node; there is no bulk form yet.

### Deleting nodes — Admin / SuperAdmin

| Endpoint | Removes |
|---|---|
| `DELETE /api/admin/terms/{termId}` | the term + its subjects, chapters, lessons, questions |
| `DELETE /api/admin/subjects/{subjectId}` | the subject + its chapters, lessons, questions |
| `DELETE /api/admin/chapters/{chapterId}` | the chapter + its lessons, questions |
| `DELETE /api/admin/lessons/{lessonId}` | the lesson + its questions, choices, upload history |

Deletes **cascade all the way down** — removing one subject destroys every question beneath it, with
no undo. So a delete is **refused with `409` while the node still has children**, and the response
reports what would be lost:

```json
{
  "errors": ["This term still contains 1 subject(s), 1 chapter(s), 1 lesson(s), 2 question(s). Deleting it removes all of that too — resend with force=true to confirm."],
  "details": { "subjects": 1, "chapters": 1, "lessons": 1, "questions": 2, "hasChildren": true }
}
```

Resend with `?force=true` to go through with it; the success response returns the same counts under
`deleted`. An already-empty node deletes without `force`. `404` for an unknown id.

Use the `details` counts to drive the confirmation dialog in the admin page rather than deleting
blind.

Note that questions retired by a re-upload are **soft-deleted, not removed**, so they still count
toward `questions` here — the number reflects rows that would actually be destroyed, which is what
matters for a delete confirmation.

### `POST /api/admin/lessons/{lessonId}/questions/upload?langId={guid}&hasHeaderRow=true` — Admin / SuperAdmin

`multipart/form-data` with a `file` field. **`langId` is required** — it says which of the
lesson's question sets this sheet publishes. On success:

```json
{
  "succeeded": true,
  "lessonId": "...",
  "langId": "...",
  "version": 2,
  "importedCount": 40,
  "replacedCount": 35,
  "errors": []
}
```

`replacedCount` counts only the questions retired **in that language**. Uploading English never
touches the Arabic set or its version.

On failure, `400` with the same shape, `succeeded: false`, and `errors[]` of
`{ row, message }` — including a missing or unknown `langId`.

### `GET /api/lessons/{lessonId}/questions/version` — authenticated

```json
{ "lessonId": "...", "langId": "...", "version": 2, "questionCount": 40 }
```

Cheap cache check for a single lesson, in the caller's content language. `version: 0` means
nothing has been uploaded **in that language** yet — the same lesson may be at version 3 in the
other one.

### `POST /api/lessons/questions/versions` — authenticated

```json
{ "lessonIds": ["...", "..."] }
```

Batch form of the above — the practical one for validating a whole on-device cache in a
single round trip. Unknown ids are omitted from the response rather than erroring.

### `GET /api/lessons/{lessonId}/questions` — authenticated

```json
{
  "lessonId": "...",
  "langId": "...",
  "version": 2,
  "questions": [
    {
      "questionId": "...",
      "text": "What is water at 100C?",
      "correctAnswerId": "b2...",
      "answers": [
        { "id": "b2...", "text": "Steam" },
        { "id": "c3...", "text": "Ice" },
        { "id": "d4...", "text": "Rock" }
      ]
    }
  ]
}
```

Questions come back in source-sheet order; answers in source-column order. Cache the whole
object keyed by `lessonId` along with its `version`.

### Recovery questions — the same four endpoints

Each of the four routes above exists again with `recovery-questions` substituted for `questions`,
with identical request and response shapes:

| Main pool | Recovery pool |
| --- | --- |
| `POST /api/admin/lessons/{lessonId}/questions/upload` | `POST /api/admin/lessons/{lessonId}/recovery-questions/upload` |
| `GET /api/lessons/{lessonId}/questions/version` | `GET /api/lessons/{lessonId}/recovery-questions/version` |
| `POST /api/lessons/questions/versions` | `POST /api/lessons/recovery-questions/versions` |
| `GET /api/lessons/{lessonId}/questions` | `GET /api/lessons/{lessonId}/recovery-questions` |

Same auth (upload is Admin/SuperAdmin, reads are `[Authorize]`), same `langId` rules, same
all-or-nothing validation, same `version: 0` semantics, same caching advice.

The one thing to hold onto: **the two versions move independently.** A recovery upload bumps only
the recovery version and leaves the main set alone, so a client caching both keeps two version
numbers per lesson and re-downloads only the one that changed.

## Not built yet

- **Rename and move.** Create, read and delete exist; there is no way to rename a node, edit one
  of its translations, or move it to a different parent yet.
- **Reordering.** `Order` is set at create time and there is no endpoint to change it
  afterwards. Because the unique `(ParentId, Order)` index forbids duplicates, a future reorder
  has to shuffle through a temporary value rather than swapping directly.
- **Creating and deleting grades.** The 14 seeded grades are fixed; there is no endpoint to add
  or remove one.
- **The admin HTML page** (cascading dropdowns + file picker + a language picker for uploads).
- **A missing-translation report.** Nothing surfaces nodes lacking a name in some language, or
  lessons with no question sheet in some language — both show up only as an empty `name` or
  `hasQuestions: false` at read time. Worth having, especially since the progress module treats
  a lesson that is unplayable in a student's language as satisfied for unlock purposes.
- **`recoveryQuestions` trigger logic.** The pool itself is built — tables, upload endpoint,
  read endpoints and an admin tab, all cloned from the main pool. What is still undefined is
  *when* the game should serve a recovery question, which remains a content-team decision as
  noted in `Architecture.md`. Nothing in the backend assumes an answer to it.
