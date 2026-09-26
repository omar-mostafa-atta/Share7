# Content Studio — declared curricula

*25 Sep 2026.* The Studio can hold more than one curriculum. The Egyptian national curriculum is the
one the game plays; any other is **declared in the Studio, authored now and played later**. A new
curriculum can have any shape — IGCSE's subject → paper → topic, an Azhar track's year → topic — and
is written, reviewed and released exactly like Egyptian content. **None of it can reach a student**
until the game is switched to read it.

## Decisions

Taken with the product owner on 25 Sep 2026:

1. **Authored now, played later.** Declared curricula live only in the node tables and are marked
   *not played yet*. The game keeps serving the Egyptian version, and the Unity build doesn't change.
2. **Any shape.** Each curriculum declares its own levels, top down. The last level is the one
   played, where questions are written.
3. **A Lead scoped to the whole curriculum declares one, immediately, with an audit row.** An
   empty, unplayable curriculum changes nothing a student sees (the same test as the Phase 5
   measurement acts). Everything built inside it goes through draft → review → release.
4. **Questions are included.** A playable node of a declared curriculum takes questions like a
   lesson does.
5. **Its questions live in their own table** (`NodeItemRenderings`), not in the game's `Questions`.

Defaults stated and not objected to:

- Levels can always be **renamed**. They can be **added, taken out or reordered** only below the
  deepest one something sits at (a node, or a new node proposed in an open draft). *Loosened on
  26 Sep 2026; it was "until anything sits at any of them".*
- Only members scoped to the whole curriculum can work in a declared one. Team & Access can't grant
  narrower scope over one yet: the scope picker lists the Egyptian tree only.

## How it is built

**One curriculum is data like any other.** A declaration writes a `Curriculum`, its first
`CurriculumVersion` (authoritative from the start), a root level plus one `CurriculumNodeKind` per
declared level (names in `CurriculumNodeKindTranslations`), and a **root node** carrying its name.
Because the root is a real node, trails, scope-by-path, one parent per top-level node and the
per-parent write lock all work inside it unchanged.

**Level keys are positional:** `curriculum` for the root, then `level1`, `level2`, … A declared
level called "Grade" is still stored as `level1`. No query that looks for `grade`, `term`,
`subject`, `chapter` or `lesson` nodes can ever match one, even a query that forgets to filter by
curriculum.

**The structure engine reads each node's shape** (`CurriculumShape`, via `CurriculumShapes`)
instead of the static `NodeKinds.ParentOf` / `IsEditable`. The Egyptian shape is the seeded data
with the same chain, so Egypt answers every question exactly as before. What differs is decided by
`IsServed` — true only for `EducationIds.EgyptianNationalAsMigrated`:

| | Egyptian (served) | Declared |
|---|---|---|
| Grades / top level editable | No (fixed) | Yes, except the root |
| Copied into the legacy typed tables (`Grades` … `Lessons`) | Yes | **Never** |
| Unlock repairs queued | Yes | No: nobody plays it |
| A playable node's question text and answers | `Questions` (+ `RecoveryQuestions`) | `NodeItemRenderings` |
| Legacy set rows (`LessonQuestionSets`, uploads) | Yes | **Never** |
| Items, versions, node mappings, `PublishedItemSets`, publications | Shared | Shared |
| Read by the game | Yes | **No** |

A node can't move between curricula (`NodeWrongParent`).

**Why a separate question table.** The item bank's rendering rows *are* the game's `Questions`
table, and `Questions.LessonId` is a foreign key to the legacy `Lessons`. Holding a declared
curriculum's questions there would need a fake legacy lesson, which is exactly what the game would
then serve. `NodeItemRenderings` is the same row keyed to the node. The reader and the publisher
choose the table by the node's curriculum; the plan, the version protocol and the item identities
are shared.

## API

`/api/studio/curricula`. Reading is for every Studio member; writing is for a Lead whose scope is
the whole curriculum.

| | | |
|---|---|---|
| `GET` | `/api/studio/curricula` | Every curriculum, the served one first: levels (with names), whether played, locked, counts, whether the caller may manage it. |
| `GET` | `/api/studio/curricula/{id}` | One of them. |
| `POST` | `/api/studio/curricula` | Declare one: `{ titles: [{langId, title}], levels: [{ names: [{langId, title}] }] }`, top level first. |
| `PUT` | `/api/studio/curricula/{id}` | Rename it, and optionally its levels: `{ titles, levels \| null }`. |
| `POST` | `/api/studio/drafts/{id}/release-now` | A Lead's draft, live in one step (26 Sep 2026): `{ revision }`. |

Refusals: `CURRICULUM_NOT_FOUND`, `CURRICULUM_INVALID` (`details.problems`: `missing`,
`tooLong`, `taken`, `noLevels`, `tooManyLevels` (max 8), `repeated`, each with `field`, `level`,
`langId`), `CURRICULUM_LOCKED` (taking out or moving a level at or above the deepest occupied one, or leaving none below it; `details.fixedLevels`),
`CURRICULUM_SERVED` (the Egyptian curriculum isn't changed from here), `OUT_OF_SCOPE`.

`/api/studio/curriculum/nodes` now takes a declared curriculum's root as `parentId`. With no
`parentId` it still returns the Egyptian grades. Each node carries `curriculumId` and `isPlayable`.

## In the Studio

**Curriculum** opens on the list of curricula. **Add a curriculum** (Leads scoped everywhere) leads
to a board with its name in each content language and its levels, top down. A curriculum's own board
shows its levels, whether it's played, and its top level; **Add — Subject** names the level the
curriculum declares. Nodes, lessons and drafts all say their level in the curriculum's own words, in
English and Arabic.

Fixed along the way, in the Egyptian tree too:

- The curriculum's top used to offer **"Add — Grade"**, which the server always refused with
  "reload the lesson". Grades are fixed, and nothing offers to add one now.
- A grade's board offered Rename, Move and Retire, all refused. It now offers only the reorder a
  grade allows.
- **Moving a chapter or subject showed the lesson picker.** A move draft's level was read from a
  field only new-node drafts fill in. It now comes from the node the draft is about.
- A trail never linked its last step, so the parent above a board wasn't clickable. On a new-node
  draft the parent was dropped entirely.

## 26 Sep 2026: grades, not levels, and a Lead needs nobody else

The first person to use the board typed **KG1, KG2, Primary 1** as a new system's levels, taking
them for the grades themselves, then added a grade that sat in review with nobody else to approve
it. Three changes followed.

**The new-curriculum board starts from the familiar shape** — Grade → Term → Subject → Chapter →
Lesson, in English and Arabic — under the heading *What its levels are called*, which says in so
many words that these are kinds of level and that Grade 1, Grade 2 and the rest are added on the
next board. A level name with a number or an ordinal in it (*KG1*, *Grade 1*, *الأول*) gets a note
beside it. The server accepts it; the note only points it out.

**Levels are fixed only down to what is built.** `FixedLevels` on the curriculum is the deepest
level anything sits at — a node, live or retired, or a new node proposed in an open draft. That
level and every one above it keep their places and can still be renamed; the levels below it can be
added, taken out and reordered, as long as at least one stays below (something that holds nodes is
never made the played level). `LevelsLocked` is now `FixedLevels >= Levels.Count`: something sits at
the played level. The board shows the fixed lines without move or remove controls and says where
they end.

**A Lead needs nobody else** (the product owner, 26 Sep 2026, after the 25 Sep "explicit, audited
override"): a Lead may approve their own draft, and `POST /api/studio/drafts/{id}/release-now`
takes a draft from any open state to live in one step — approved by them, carried by a release of
its own, published at once, with the usual checks, lock and rollback. Each approval of their own
work is recorded as `workspace.draft.self_approved`. A one-step release that is refused is
cancelled, so its draft is free to fix. Authors and Reviewers are unchanged.

**What is proposed shows where it will go.** A level's board lists new nodes proposed under it and
not yet released, marked with their draft state, so a level never looks empty to the person who has
just added to it.

## Not in this build

- **Playing a declared curriculum.** That needs the game to read nodes (`Curriculum:ReadModel =
  Generic`, after the fourteen clean Shadow days in production) and a way to place a student in a
  curriculum. Student profiles hold only a grade id today.
- The question bank search, Skills mapping, Answers, Papers and Second chances boards read the
  Egyptian curriculum only. A declared curriculum's boards don't link to them.
- Narrower scope over a declared curriculum (Team & Access).
- A release containing only declared-curriculum changes still says **"Send to the game"**. It's
  accurate about the mechanism (a release applies the changes) but not about who sees them.

## Tests

`Share7.Tests/NewCurriculaTests.cs`, 10 tests over the real database. The main one declares a
curriculum whose top level is called "Grade", releases a node and a playable node with questions
through draft → review → release, and asserts that no typed row, no game read, no `Questions` row
and no legacy set row mentions any of it. The full suite is 827 tests: 810 pass and the same 17
failures as before (leaderboards, rewards, runs, signals), none of them new.
