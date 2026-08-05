# ContentIngestion.md

# EduPlatform — Curriculum Content & Question Versioning

Companion to `CLAUDE.md` / `Architecture.md`. Covers the content tables, the Excel upload
pipeline, and the question-cache protocol the Unity client uses.

## Language partitioning

Content is **partitioned by language, not translated in place**. There is no `nameEn`/
`nameAr` pair on any table. Instead:

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

Every content node carries a `Lang_Id` FK. **The English and Arabic trees are entirely
separate sets of rows with no link between them** — "Grade 5" and "الصف الخامس" are two
independent rows, each with its own terms, subjects, chapters, lessons and questions. This
was a deliberate choice (2026-08): it keeps each table to a single name column and makes the
admin dropdowns trivial to filter, at the cost of the admin maintaining both trees by hand.

Consequences worth remembering:
- A student switching language lands in a different tree with different ids. Nothing
  carries over automatically — progress, unlocks, and cached questions are all per-tree.
- Cross-language multiplayer matchmaking is not possible without adding a link column.

## Content tables

Each level is `Id` + one name column + `Lang_Id` + parent FK. Deleting a parent cascades
down the whole chain.

```
Grades      Id, Grade,   Lang_Id
Terms       Id, Term,    Lang_Id, GradeId
Subjects    Id, Subject, Lang_Id, TermId
Chapters    Id, Chapter, Lang_Id, SubjectId
Lessons     Id, Lesson,  Lang_Id, ChapterId, QuestionsVersion
```

`Lessons.QuestionsVersion` is the cache key described below. It is `0` until the first
sheet is uploaded.

```
Questions
  Id, LessonId, Lang_Id, Question, CorrectChoiceId,
  Version, IsActive, RowNumber, CreatedAt, DeactivatedAt

QuestionChoices
  Id, QuestionId, Choice, OrderIndex        -- exactly 3 per question

LessonQuestionUploads                        -- audit trail, one row per successful upload
  Id, LessonId, Version, FileName, QuestionCount, UploadedByUserId, UploadedAt
```

`Questions.CorrectChoiceId` is deliberately **not** a database FK. Questions and
QuestionChoices reference each other, and constraining both directions creates a cycle SQL
Server rejects. The importer is the only writer and sets both sides in one transaction.

`OrderIndex` preserves the source column order (0 = Excel col 2, the correct one). The
client is expected to shuffle before assigning answers to lanes/doors.

## Excel format

One sheet, four columns, one question per row:

| Column | Meaning |
|---|---|
| 1 | Question text |
| 2 | **Correct** answer |
| 3 | Wrong answer |
| 4 | Wrong answer |

Language is **not** a column in the sheet — it is inherited from the lesson being uploaded
to, since the lesson row is already language-specific. Uploading Arabic questions means
selecting an Arabic lesson.

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

## Versioning & the client cache protocol

Every upload for a lesson increments `Lessons.QuestionsVersion` by 1 — first upload
produces version 1, then 2, 3, and so on. The previous question rows are **soft-deleted**
(`IsActive = false`, `DeactivatedAt` set) rather than removed, so student progress that
references an old `QuestionId` stays resolvable. Only `IsActive` rows are ever served.

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

Concurrency note: two admins uploading to the same lesson simultaneously would compute the
same next version. The unique index on `LessonQuestionUploads (LessonId, Version)` makes the
second one fail rather than silently produce two "version 3"s.

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

All four are scoped to the caller's content language, taken from the `preferred_language` token
claim (no database round trip; falls back to a lookup when the claim is absent).

| Endpoint | Query | Returns |
|---|---|---|
| `GET /api/terms` | `gradeId` *(optional)* | `[{ id, name, langId, gradeId }]` |
| `GET /api/subjects` | `termId` *(optional)* | `[{ id, name, langId, termId }]` |
| `GET /api/chapters` | `subjectId` **(required)** | `[{ id, name, langId, subjectId }]` |
| `GET /api/lessons` | `chapterId` **(required)** | `[{ id, name, langId, chapterId, questionsVersion }]` |

Without the optional filter, `/api/terms` and `/api/subjects` return every row in that language —
so "First Term" comes back once per grade. Pass the parent id to narrow.

Because the language filter applies alongside the parent id, passing a `subjectId` or `chapterId`
from the *other* language tree returns an empty list rather than content in the wrong language.

`/api/lessons` includes each lesson's `questionsVersion`, so a client can validate its entire
question cache for a chapter from this one response without a separate version call.

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

All four take the same body — just the name:

```json
{ "name": "First Term" }
```

**There is no language field on purpose.** Each node inherits `Lang_Id` from its parent, so
picking an English grade makes everything beneath it English. This is what prevents the two trees
from getting cross-wired; a caller cannot create an Arabic chapter under an English subject.

Responses return the created node (`{ id, name, langId, parentId }`; lessons also carry
`questionsVersion`, which starts at 0 — upload a sheet to publish version 1).

Status codes: `404` unknown parent, `409` a sibling under the same parent already has that name,
`400` blank name, `403` caller is not an admin.

**Duplicate names.** The name is trimmed and lowercased, then compared against `LOWER(name)` of the
existing siblings — explicitly, rather than leaning on the database collation happening to be
case-insensitive. So `"Science"`, `"SCIENCE"`, `"sCiEnCe"` and `"  science  "` all collide. The
**original casing is what gets stored**, so display names keep their capitals. The same name under a
*different* parent is fine — two grades can both have a "First Term".

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

### `POST /api/admin/lessons/{lessonId}/questions/upload?hasHeaderRow=true` — Admin / SuperAdmin

`multipart/form-data` with a `file` field. On success:

```json
{
  "succeeded": true,
  "lessonId": "...",
  "version": 2,
  "importedCount": 40,
  "replacedCount": 35,
  "errors": []
}
```

On failure, `400` with the same shape, `succeeded: false`, and `errors[]` of
`{ row, message }`.

### `GET /api/lessons/{lessonId}/questions/version` — authenticated

```json
{ "lessonId": "...", "version": 2, "questionCount": 40 }
```

Cheap cache check for a single lesson. `version: 0` means nothing has been uploaded yet.

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

## Not built yet

- **Rename and move.** Create, read and delete exist; there is no way to rename a node or move it
  to a different parent yet.
- **Creating and deleting grades.** The 24 seeded grades are fixed; there is no endpoint to add or
  remove one.
- **The admin HTML page** (cascading dropdowns + file picker).
- **Ordering.** No table has a sort column, so terms/chapters/lessons come back in whatever
  order the query yields and `GET /api/grades` sorts by name — which puts "Grade 10" before
  "Grade 2". Add an `Order` column when dropdown order starts to matter.
- **`recoveryQuestions`** — the secondary per-lesson pool in the Unity models has no table
  and no trigger logic defined. Still open, as noted in `Architecture.md`.
