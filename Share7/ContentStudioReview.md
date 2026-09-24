# Content Studio — a review across the phases

**2026-09-24, after cutover.** A read of what P0 through P6 actually enforce, looking for rules that
are wrong rather than code that is broken. Five defects, all fixed, all with a test that fails
without the fix.

Nothing here was found by the suite. Four of the five are cases where the code does exactly what it
says and the thing it says is not the rule the platform is built on.

---

## 1. Scope was not enforced on the acts that skip drafts

**The most serious of the five.** Plan §7.3 says it plainly:

> **Scope** limits what a person can edit, not what they can see. … The server enforces it on every
> call by checking the node's path against the person's scope.

The draft machinery has done exactly that since Phase 3 — `DraftService`, `ReviewService` and
`ReleaseService` all check `StudioMember.CoversPath`, at opening, at approval and again at release.

Phase 5 added five acts that bypass drafts **on purpose**, because they do not change what a child
is shown and holding them behind a release would be theatre. Every one of them was gated on the
Studio role and nothing else:

| Act | Was | Now |
|---|---|---|
| Saying what a question measures (`MapItemAsync`) | any Author, any question on the platform | every node the question sits in |
| Replacing a stand-in (`PromoteAsync`) | any Author, any stand-in | every node the stand-ins sit in |
| Marking an anchor (`SetAnchorAsync`) | any Lead, any question | every node the question sits in |
| Stopping answers counting (`ExcludeAsync`) | any Lead, any version | every node the question sits in |
| Building a benchmark (`GenerateBenchmarkAsync`) | any Lead, any subject | that subject |
| Freezing a paper (`PublishAsync`) | any Lead, any blueprint | every node its lines' skills are taught in |

So a Lead of Primary One Mathematics could withdraw the answers to any question on the platform, or
re-point any question at any skill — and every child's proficiency on it would be recomputed from
the response log against the new claim. Nothing in the Studio's own screens offers that, which is
why it survived: the boards only ever showed questions the member was looking at. It was reachable
by anybody who could form the request.

**The fix** is one shared guard, `StudioScope` in `Share7.Infrastructure/Workspace/WorkspaceSupport.cs`,
asking the question the draft path has always asked. Two details in it are deliberate:

- **Every place, not any place.** A question can sit under more than one node and changing it
  changes it for all of them, so covering one home is not permission for the others.
- **Something mapped nowhere is nobody's.** An orphan question belongs to no part of the
  curriculum, so only a member whose scope is the whole of it may touch it.

Reads are untouched, and one of the tests says so: everyone in the Studio sees all of the
curriculum. That is the other half of the same rule.

### The board that offered it

`Quality.tsx` already drew the line — it shows *"this is outside the part of the curriculum you work
in"* when `canFix` is false — and then rendered the anchor and exclude buttons anyway. Until today
they worked. Now they would refuse, so the board hides them: saying "not yours" and then handing
somebody the chalk is worse than either.

---

## 2. A recovery rule could not be written at a grade

Phase 5 describes the placement three ways, in the plan, in the phase doc and on the board itself:
**grade, subject or lesson**. The first of them was impossible.

`DraftService` decided which nodes a draft kind fits, and the recovery rule fell through to the
default branch: `NodeKinds.IsEditable(kind)`, which is `Term or Subject or Chapter or Lesson`. It
excludes grades for a good reason — *"the fourteen Egyptian grades are fixed: grade ids are stored on
student profiles and gate game modes and worlds"* — and that reason is about **structural** editing.
Writing a recovery rule at a grade edits nothing about the grade.

It was not a silent hole. The Recovery board showed the grade, named what it was inheriting, counted
its 120 lessons, and reported `canPropose: true` — so the yellow action was drawn, and pressing it
answered `400 DRAFT_INVALID / nodeKind`, which is not a sentence anybody could act on.

`DraftKind.RecoveryRule` now has its own predicate: any live node. Confirmed against the real
development database — a draft opens on Primary One and reads back as `RecoveryRule`, "Primary One".

---

## 3. The board under-counted what a rule would not reach

The same board reports **"lessons with their own rule"** — the number a Lead reads to know how much
of what is under them a rule written here would actually govern. Its comment is exact about why it
matters:

> A lesson with its own rule is not reached by one written above it, and a board that implies
> otherwise is the board that gets somebody's change quietly ignored.

It counted rows in `RecoveryRules` whose `NodeId` was one of the lessons — so it saw only rules
written **at a lesson**. A rule on a chapter or a subject in between overrides just as completely,
and every lesson under one was reported as reachable.

Live, before the fix: Primary One said **0 of 120** lessons had their own rule, while the released
Mathematics rule governed 15 of them. A Lead writing a grade-wide rule would have been told it
reached all 120. It reaches 105.

Now asked of the reader instead of counted off the table: a lesson is out of reach exactly when the
rule it resolves to is not the one that resolves here — and since these lessons are all underneath,
anything else can only be more specific. Live, after: **15 of 120**.

---

## 4. The outcomes importer accepted loops

A sheet could say an outcome is part of itself, or that two outcomes are part of each other, and both
were written straight through as `ComponentOf` edges. The importer checked that a parent code
*exists*; it never checked where following it leads.

Nothing walks those edges yet — which is the argument for closing it now rather than against. The
first thing that rolls a mastery figure up through them would not come back, and by then the bad
rows would be years old and indistinguishable from good ones.

Both now come back as ordinary row problems (`parentIsSelf`, `parentLoops`) on the dry run, before
anything is written — which is where a typing mistake on a hand-copied ministry document belongs.

---

## 5. A rejected sheet reported work it would not have done

The import report's counts disagreed with each other: `Added` and `Updated` were zeroed when the
sheet had problems, `LeftAlone` was not. So a sheet that would write nothing came back as
"0 added, 0 updated, **7 left alone**" — which reads as a partial import that preserved seven
specialist edits, when in fact the file was rejected whole.

All three or none, now.

---

## What was looked at and found sound

Recorded because "we reviewed it" is worth nothing without saying what.

- **The review rules** (`ReviewService`). Self-approval is refused on *contributors*, not just the
  creator; scope and language are re-checked at the verdict; the draft's revision must match what
  the reviewer read; staleness is re-checked at approval, not only at submission.
- **Release blockers** (`ReleaseService.BlockersAsync`). Closed, practice, not approved, out of
  date, out of the publishing Lead's scope, still has problems — plus a new node whose parent is
  only a draft, two structural changes to one node, and a reorder sharing a release with anything
  that adds to or removes from the children it reorders. The whole thing runs under an application
  lock, one release at a time platform-wide.
- **The path prefix checks.** Every scan over the materialised path uses `path + "/"`. There is no
  sibling-matches-a-prefix bug anywhere in the tree code.
- **Revision bumps** (`CurriculumStructureService`). Create, move, reorder, retire and restore all
  touch the parent as well as the node, so a reorder draft goes stale when its children change and
  not when one of them is merely renamed.
- **Shadow reads.** The typed answer is produced first and always returned; the node answer runs
  after it and any difference is tallied and logged, never served.
- **Unlock repair.** Only ever grants, and every step is insert-if-absent, so a job run twice ends
  in the same place.
- **Exclusions.** Honoured in mastery, exam coverage and organisation reporting, and carried across
  a projection rebuild. They deliberately do **not** change the quality flags: withdrawing answers
  stops them counting towards what a child knows, and says nothing about whether the question is
  any good.
- **Setup links** (Phase 1). Single-use, expiring, hashed, consumed in the same transaction that
  sets the password, resets the lockout and writes the audit row.
- **Publish rules** (`LessonContentRules`). Every required language on every question, a recovery
  question whenever there are main questions, three different answers case-sensitively, and a
  rollback held only to the per-question rules — because refusing to put back what was live would
  make the release that replaced it impossible to undo.

---

## Proof

- **817 tests, 800 pass, 17 fail** — the same leaderboard, signal-economy, run and reward tests that
  have failed identically since the Phase 0 baseline. Nine of the new ones are these findings.
- `Share7.Tests/Contracts/StudioScopeTests.cs` — the five acts refused outside a member's chapter,
  the same member reading the whole curriculum, and a recovery rule opening at a grade. Over HTTP,
  against the real API process.
- `Share7.Tests/SkillImportLoopTests.cs` — self-parent, a two-row ring, an ordinary three-deep
  chain that is not a ring, and the report's counts agreeing with each other.
- The frozen game contract unchanged; `tsc --noEmit` clean on both apps; the design detector clean
  on the changed screen.
- Findings 2 and 3 confirmed live against the development database before and after.

## Still open, and not a defect

- **Two recovery-rule drafts on one node cannot share a release.** They can today; the second
  supersedes the first and the end state is deterministic, but both are recorded as released when
  only one took effect. `BlockersAsync` catches this for every structural kind and not for this one.
  Worth adding when per-skill rules land, which is when it stops being theoretical.
- **`ComponentOf` edges are still unused.** Nothing rolls mastery up through the outcome tree yet.
  Finding 4 is what makes that safe to build.
