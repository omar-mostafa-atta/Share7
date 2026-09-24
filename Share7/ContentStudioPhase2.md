# Content Studio — Phase 2: the engine rebuild

**2026-09-22.** The second phase of the Content Studio plan. The generic curriculum tree and the
item bank stop being copies and become the source of truth; the old typed tables stay beside them as
a compatibility copy until cutover. One writer for structure, one writer for lesson content, and
every old authoring path becomes an adapter over them.

**Nothing a player sees changes.** That is proven rather than promised: the frozen game contract
(`Share7.Tests/Contracts`) runs three times — once per read model — against the same reviewed
baselines, and all three match byte for byte.

Backend only; no UI.

---

## 1. What changed

| # | Change | Why |
|---|---|---|
| 1 | **`CurriculumNodes` is the source of truth.** Every structural write goes to the node first and to the typed row beside it, in one transaction | Two shapes that cannot disagree, because one writer writes both (plan A1) |
| 2 | **Delete became Retire.** Nothing in the curriculum is hard-deleted any more; the forced cascade is gone | Approved default #5. The old force-delete destroyed questions and left progress pointing at ids that no longer existed |
| 3 | **The recovery pool joined the item bank**: its rows live in `Questions` with `Role = Recovery`, under their own ids, and its old tables are kept in step | One localization store, one publisher, and recovery questions finally have item identity (fixes M4) |
| 4 | **`PublishedItemSets` backs the version protocol**, generalising `LessonQuestionSets` and `LessonRecoveryQuestionSets` to (node, pool, language) | The version protocol stops being lesson-shaped (plan A3) |
| 5 | **A question that did not change keeps its id** across publishes — judged per language | Fixes M3. A device's cache, and a child's history on that question, are no longer thrown away by an unrelated edit |
| 6 | **Languages are data**: `IsContentLanguage`, `RequiredToPublish`, `Direction`, `SortOrder` | Fixes M1. A third language is a row, not a release (plan A4) |
| 7 | **One publisher and one structure writer.** The lesson sheet, the one-language upload, hand entry and the admin tree edits are adapters | One set of rules about what a publish does (plan A5) |
| 8 | **`Curriculum:ReadModel`** — Legacy, Shadow or Generic — decides which tables answer the game's curriculum reads, per request | The switch-over and the way back (plan A2, A6) |
| 9 | **Unlock repair**: a structural change queues work that gives every affected student what the old shape promised them | A retired lesson used to strand whoever was on it |

---

## 2. The two writers

### 2.1 Structure — `ICurriculumStructureService`

Create, rename, move, reorder, retire, restore. Each call writes the node, its translations and the
typed compatibility row in one transaction, touching only the nodes it concerns — no full re-sync of
the tree after every edit (fixes H5).

- **Revisions.** `CurriculumNode.Revision` goes up on every change to the node. A Studio draft
  records the revision it started from, which is how a change made underneath it is noticed instead
  of silently overwritten (fixes C2).
- **Retire, never delete.** Retiring a node retires every live node under it at one instant; that
  instant is how a restore knows which descendants were retired *with* it. Retired typed rows are
  hidden from every legacy reader by a global query filter, so to them a retired lesson is simply
  gone — the same thing a delete used to mean, without destroying anything.
- **Restore** puts a node back where it was, or after the last live sibling when its place was taken.
- **Orders** are unique among *live* siblings (a filtered unique index), and a reorder writes the
  typed rows in two phases so a permutation never collides with itself.
- **Grades are fixed.** Their ids are on student profiles and gate game modes and worlds.

Every change is one audit row: `curriculum.node.created`, `.renamed`, `.moved`, `.retired`,
`.restored`, `curriculum.nodes.reordered`.

### 2.2 Lesson content — `ILessonContentPublisher`

The only thing that writes questions. What it guarantees:

- A rendering whose text, answers and key did not change **keeps its row** — same question id, same
  choice ids. Judged per language, so fixing an Arabic typo leaves the English row untouched.
- The renderings that did change get new rows under **one new version of their item**; each rendering
  stays on the item version whose content it shows.
- A set's version goes up by **exactly one when what the game receives changed**, and not otherwise.
  It never goes down. Publishing the same sheet twice changes nothing and downloads nothing.
- Rows are retired, never deleted: progress and evidence name them.
- The compatibility copies — `LessonQuestionSets`, `LessonRecoveryQuestionSets`, `RecoveryQuestions`,
  both upload tables — are written in the same transaction.
- Publishes of one lesson are serialised by an application lock, so two saves cannot both claim the
  same next version.

**Rules per path** (decided 22 Sep 2026): the old admin paths keep the rules they have always had —
the one-language paths publish one language at a time and need no recovery pool; the lesson sheet
insists on a recovery question. The Studio holds every lesson to the full rules: every required
language on every question, and a recovery question whenever there are main questions. A rollback
uses a permissive set, because what it puts back was live once already.

---

## 3. Which tables answer the game

`Curriculum:ReadModel` in configuration, read per request (no restart):

| Value | What happens |
|---|---|
| `Legacy` | The typed tables answer, exactly as before the rebuild |
| `Shadow` | The typed tables answer **and the node tables are read too and compared**, byte for byte; differences are tallied and logged, never served |
| `Generic` | The node tree, the published sets and the item bank answer |

`ShadowSampleRate` (0–1) is the share of reads compared. Production defaults to `Shadow` at 0.25;
development compares every read.

**The switch-over rule (plan A6):** run Shadow until fourteen days in a row show nothing differed and
nothing failed, then set `Generic`. `GET /api/admin/engine/read-model` shows every day's tally per
read and `consecutiveCleanDays`. Going back is the same one setting.

Every list is ordered in SQL down to a unique key. Several of the original queries had no `ORDER BY`
and what the game receives is whatever order SQL Server returned — the batch version check is one,
and its recorded baseline depends on it. Ordering by id in the query keeps exactly that and makes
both paths break ties the same way.

---

## 4. Nobody is stranded by a change to the tree

Unlocks are a ledger walked forward one lesson at a time. Change the tree underneath and the walk can
break: retire the lesson a student is on and nothing will ever open the next one; move a lesson ahead
of where they reached and it sits locked behind them.

A structural change writes an `UnlockRepairJob` **in its own transaction**, and the worker runs it
straight after the commit (and sweeps every minute, so a job outlives a restart):

- **Pass forward** — the node a student held is gone from where it was: they get what finishing it
  would have given them. The next lesson; or, when it was the last, the chapter or term is
  re-evaluated per student, with their own results and language, exactly as a finished attempt does.
- **Fill gaps** — a parent's children are in a new order, or a node was added or restored among them:
  anyone holding a later child also gets every earlier one.

Only ever grants. Re-running a job is harmless, which is what makes the retry safe.
`GET /api/admin/engine/unlock-repairs` shows anything still to do.

---

## 5. What the migration does

`20260922145343_EngineAuthoritative` and `20260922152233_UnlockRepairJobs`.

Schema: retirement columns on the typed tables (with live-only unique order indexes), `Role` on
`Questions`, the language columns, node revision and last-change columns, `PublishedItemSets`,
`ContentPublications`, `CurriculumReadChecks`, `UnlockRepairJobs`.

Data — `EngineBackfill.Sql`, every statement guarded by `NOT EXISTS` so it can be re-run:

1. Every typed row gets its node, if it has none (content seeded after the first projection).
2. The recovery pool joins the item bank: each row copied into `Questions` **with its own id** and
   `Role = Recovery`, each choice into `QuestionChoices` with its own id, one item per
   `(lesson, row number)` — the same lineage key the main pool's backfill used. Recovery items are
   deliberately mapped to no learning target: nothing answers them, so they carry no evidence, and
   counting them would report every one as unmeasurable content to fix.
3. Served versions into `PublishedItemSets`; publication history into `ContentPublications`.
4. The migrated Egyptian curriculum version is marked authoritative.

The same SQL runs after the development seeder and in the test fixtures, so what the tests exercise
is the migration itself.

---

## 6. How it is proven

```bash
# The frozen game contract, three times: Legacy, Generic and Shadow
dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~Share7.Tests.Contracts"

# The engine's own behaviour
dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~CurriculumStructureTests|FullyQualifiedName~ContentPublisherTests"
```

- **Contract, all three modes: 27 of 27 pass.** Nine scenarios — lookups, sign-in, browsing in both
  languages, question and recovery downloads, version checks, a whole play-through with attempts and
  every progress read, games and learning — compared with the reviewed baselines.
- **The whole suite with `SHARE7_TEST_READ_MODEL=Generic`**: every progress, unlock and matchmaking
  test on the node tree. No new failures.
- **With `Shadow`**: 862 comparisons across 13 reads, **0 differed, 0 failed**
  (`SHARE7_TEST_SHADOW_REPORT` writes the tally).
- **Whole suite: 794 tests, 777 pass.** The 17 failures all predate this work and fail exactly as
  they did before it (`ContentStudioPhase0.md` §6).

Two tests changed on purpose, and both are the point of the phase: appending a question no longer
"replaces" the ones already there (they keep their ids), and a republish adds a version rather than
reusing the number of an orphaned one.

---

## 7. Deliberately not done

- **Admin-only readers** — curriculum health, question search, the overview and the quality surface —
  still read the typed tables. They are correct either way (the compatibility copy is written in the
  same transaction) and none of them is part of the frozen game contract; they move at cutover (P6),
  with the tables they read.
- **`Hidden` as a third node state.** The plan had it so a new chapter could wait unseen until its
  release; drafts hold new nodes off the live tree entirely (Phase 3), so it would have been a state
  nothing could reach.
- **ETags and output caching on the game reads** (plan §12). Nothing in the rebuild depends on them.

## 8. Still open

- **CI.** There is none in this copy; the three contract runs and the suite are the obvious gate.
- The **fourteen clean Shadow days** in production, then `Generic` — and the on-device run-through
  with the current Unity build before the switch.
