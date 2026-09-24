# Content Studio — Phase 6: cutover & cleanup

**2026-09-24.** The last phase of the Content Studio plan. There is now exactly one way to author
content, and it goes through review.

Backend, the Admin Console, and the documentation. Nothing a player sees changes.

---

## 1. What cutover actually means here

The phase is small to describe and easy to get wrong, so it is worth saying plainly what was closed
and what was not.

| | Before | After |
|---|---|---|
| Authoring the curriculum and its questions | Admin Console **and** the Studio | the Studio |
| The old `/api/admin` authoring writes | worked | `410 Gone`, naming where the work went |
| The reads beside them | worked | unchanged |
| A content-team member's sign-in | `POST /api/auth/login` **and** the Studio | the Studio |
| `/content` (the Content Portal) | a portal for the ContentTeam role | gone; the address lands on a page saying so |
| `Share7 front/` (the vanilla console) | present, unmaintained, still able to author | deleted, archived |
| The legacy typed tables | written as a compatibility copy, read in Shadow | **unchanged** — see §6 |

### Four decisions, asked before anything was touched

1. **The legacy typed tables stay.** Both configs read `Curriculum:ReadModel = Shadow`, so the game
   still reads those tables on every request, and the fourteen clean `Generic` days have not
   started. Dropping them now would take the way back with them. It becomes its own short step the
   day production has run `Generic` cleanly for a fortnight.
2. **Writes refuse; reads stay.** A refusal satisfies the gate as completely as a deletion, and
   leaves something for a caller to read. A deleted route answers `404`, which is indistinguishable
   from a typo. It also keeps the way back honest: if the pilot finds something the Studio cannot
   do yet, re-opening a route is deleting one line.
3. **Examinations stays in the Admin Console.** Hand-authoring a blueprint, creating exam
   specifications and the calibration report exist nowhere else. It is a measurement tool rather
   than content authoring, and nothing it writes reaches a child until a Lead freezes it in the
   Studio — which is already refused while any line names a stand-in.
4. **The old sign-in closes now**, with the rest, rather than waiting for the pilot. Otherwise the
   old door is shut on the map and open in the building.

**A note on sequencing, recorded because it matters.** This phase is written to run *after* the
Phase 4 gate (a pilot member publishes a real lesson without help) and the Phase 5 gate (one subject
fully mapped). Neither has happened: no pilot has run, and the official learning outcomes have not
been supplied. Cutover was done anyway, on the user's instruction. What that costs is that the old
paths closed before anyone but me proved the new one. What it does not cost is recoverability —
which is why decisions 1 and 2 went the way they did.

---

## 2. The refusal

`Share7/Authorization/ClosedAtCutoverAttribute.cs`. One attribute, twelve applications.

```csharp
[ClosedAtCutover("Writing a lesson's questions", Now = "the lesson's own board")]
[HttpPost("{lessonId:guid}/questions/manual")]
```

```json
{ "errors": [
  "Writing a lesson's questions is done in the Content Studio now, on the lesson's own board.
   This way in closed when content authoring moved, so that nothing reaches a student without a
   second person's review."
] }
```

`410 Gone`, in the `{ errors: [...] }` envelope the curriculum and auth endpoints have always used,
which is what anything still calling them knows how to read.

### It is an authorization filter, and that was not the first attempt

Written first as an `IActionFilter`, which is the obvious choice and is wrong. Action filters run
*after* model binding and after `[ApiController]`'s automatic 400, so:

```
POST /api/admin/grades/{id}/terms   {}
→ 400  { "errors": { "Translations": ["At least one translation is required."] } }
```

A write whose body no longer matched anything was answered with a validation complaint rather than
with the move — a **worse** answer than the one it replaced, on the routes most likely to be called
by something old. Authorization filters run before binding, so the refusal is now the first thing
that happens, and a rejected upload is never read off the wire at all.

Caught by the test, on the first run against the real process. Nothing in the source would have
shown it.

### What carries it

| Controller | Closed | Left open |
|---|---|---|
| `AdminCurriculumController` | the whole class — every action on it is a write | — |
| `AdminLessonQuestionsController` | `questions/upload`, `questions/manual` | `GET questions` |
| `AdminLessonRecoveryQuestionsController` | `recovery-questions/upload`, `…/manual` | `GET recovery-questions` |
| `AdminLessonSheetController` | `POST upload`, `PUT`, `DELETE {row}` | `GET`, `GET template` |
| `AdminEducationController` | `PUT …/anchor`, `POST …/exclude-observations` | the quality reads, the two projection rebuilds |
| `AdminAssessmentController` | `targets/promote`, `targets/{id}/review-state` | everything else — blueprints, exams, calibration, the target reads |

The two measurement acts in `AdminEducationController` are closed because they moved, not because
they are dangerous: Phase 5 made them Lead-only, immediate, and recorded with a reason. Doing them
here would have been doing them without the reason.

---

## 3. The old sign-in

`AuthService.LoginAsync` now refuses an account whose only standing is content authoring:

```csharp
if (await IsStudioOnlyStaffAsync(user))
    return AuthResult.Failure(… "Content-team accounts sign in to the Content Studio, not here: "
                              + studio …);
```

Three things about it that were deliberate:

- **Asked after the password has been checked.** Before it, this would have been a way to ask the
  server which usernames belong to the content team.
- **Somebody who is also an administrator keeps their console.** The refusal is for accounts whose
  standing is content authoring alone. Shutting an administrator out of the Admin Console is not
  what this phase is for.
- **It carries the address** when `Studio:PublicUrl` is configured. This sentence is the only thing
  a member who has not heard about cutover will be shown, and "somewhere else" is not directions.

Approved default #2 — staff accounts are staff-only — has been true of Google and Facebook sign-in
since Phase 0 (`NeverLinkedRoles`). This is the password half of it, held open on purpose until the
Studio had a workspace to move people into.

`Policies.ContentAuthoring` also lost the `ContentTeam` role. They cannot reach it in any case now,
but a grant nothing can exercise is a hole waiting for the day somebody reopens a door for an
unrelated reason. `ContentCascadeDelete` is gone entirely: it existed to keep a force-delete away
from the content team, and there is no force-delete left to keep away from anybody.

---

## 4. The Admin Console

Removed: `routes/Curriculum.tsx`, `routes/ContentQuality.tsx`, `routes/LearningTargets.tsx`,
`features/curriculum/`, `features/questions/`, `features/quality/`, `portals/content/`, and
`useTargetActions` from `features/assessment/data.ts`. The Content section of the sidebar keeps
Organizations, Examinations, Games and Modes & Worlds.

### The old addresses answer

`/curriculum`, `/quality`, `/targets` and `/content` render `routes/Moved.tsx` rather than bouncing
to the dashboard. A silent redirect is indistinguishable from a bug: the admin who followed a
three-year-old link would go looking for the tree in the sidebar and find nothing. This is the
console's half of the `410`.

It does not link to the Studio. Admins are not Studio members — approved default #4 keeps even
SuperAdmins out — so a button there would be a door that will not open for whoever is reading it.
What it links to instead is the measurement that stayed.

### Two defects found while verifying it

**The sign-in screen had no blank-page guard.** `console.css` holds animated elements at their
resting state until `data-motion="ready"` appears, because Motion never creates an animation for a
document that mounted hidden — a background tab, a restored session, a collapsed pane. The list
covered the console's pages and had left out the screen standing in front of them, so a sign-in
loaded hidden rendered **a blank purple column beside a blank card**, with no way in and nothing
saying why. Five selectors added.

**Then the new page had the same bug.** `Moved.tsx` put `riseVariants` on the `.s7-stack`
container, and the stylesheet's guard covers `.s7-stack > *`, not `.s7-stack` itself — so the whole
body of the page would have been invisible in exactly the case the guard exists for. Both
containers now animate nothing but timing, and everything that starts at `opacity: 0` is a direct
child. Verified by deleting the attribute in the live page and reading back the computed values.

### Craft

Measures read off the rendered page, not intended: the paragraph 71ch, the note 75ch, the
descriptions 72ch — a first pass at `max-width: 68ch` on the container came out at **96ch**, because
`ch` resolved against the container's inherited 16px while everything inside set its own smaller
size. Contrast on the dark canvas: body 7.18:1, links 4.70:1. Mobile at 375: no horizontal scroll,
a 16px gutter, nothing overflowing.

`impeccable detect` over the changed files: two advisories, both on `border-left: 3px` in
pre-existing rules (`.s7-note`, and one other), neither in anything this phase wrote.

*Not fixed, and not this phase's:* the console's top bar and page titles collide with the drawer
button and page actions at 375px — worse on the incumbent pages than on the new one.

---

## 5. The vanilla console

`Share7 front/` deleted. It was a byte-identical mirror of what `wwwroot/` held before the React
console replaced it, which `AdminConsole.md` §4 called a hazard in August and proposed deleting.
By September it was not two copies of one console but two consoles, one unmaintained and still able
to author.

Archived first, because this is not a git repository and a deletion here does not come back:
`ops/archive/share7-front-2026-09-24.zip`. The four superseded React bundles left in
`wwwroot/assets` by earlier builds were removed too; `emptyOutDir` is off there on purpose, so they
accumulate.

---

## 6. What was deliberately not done

- **The legacy typed tables are still there and still written.** `Curriculum:ReadModel` is `Shadow`
  in both `appsettings.json` and `appsettings.Development.json`. Dropping them is a separate step
  that waits on fourteen clean `Generic` days in production.
- **Examinations stays.** Blueprint hand-authoring, exam specifications and calibration are still
  only in the Admin Console. If a real syllabus paper is ever needed and the Studio is the only
  place anyone works, that screen has to be built there.
- **The `Portal` machinery is kept** with one portal in it, rather than folded into the shell. A
  second audience — teachers — is still an entry in `lib/portals.ts`, a nav list and a routes
  component, which is what it was built for.

---

## 7. Proof

- **808 tests, 791 pass, 17 fail** — the same leaderboard, signal-economy, run and reward tests
  that have failed identically since the Phase 0 baseline. The suite grew by the 11 below.
- **`Share7.Tests/Contracts/CutoverTests.cs`**, against the real API process over HTTP: eight closed
  writes answering `410` with the right sentence, three kept reads answering `200`, a content-team
  account refused the old sign-in and accepted by the Studio, and an administrator still signing in
  to their own console.
- **Walked against the real development database.** Twelve closed routes refused, four reads served,
  `malakH` refused at `/api/auth/login` with the Studio's address and accepted at the Studio,
  `omarContent` likewise, and `GET /api/grades` — the game's own read — unchanged at `200`.
- `dotnet build`: 0 errors. `tsc --noEmit`: clean. Production build of the console: clean.

### Where the evidence stops

The Moved page was captured at 1440 and at 375, in the dark theme, signed in as an administrator.
**The light theme was not captured on it**, and neither was the Overview note that now reads "the
content team writes them in the Content Studio" — the development database has no lesson without
questions, so that branch never rendered. The refusals were verified by their JSON, not by watching
a person meet one.

---

## 8. Still open

- **The pilot.** Two or three real content-team members, including a native review of the Arabic.
  That is the Phase 4 gate, and cutover has now removed their fallback — which is an argument for
  running it soon rather than an argument against having done this.
- **The official learning outcomes**, typed into the template the Skills board hands out. Phase 5's
  gate, and the only thing between here and a subject that is really mapped.
- **Fourteen clean `Generic` days** in production, then dropping the typed tables.
- **CI**, the **Facebook linking policy**, and whether **Google's audience check should fail
  closed** — unanswered since Phase 0.

---

## 9. After cutover: a review across the phases

A read of P0 through P6 for rules that are wrong rather than code that is broken found **five
defects**, all fixed, all now with a test that fails without the fix. The most serious: **scope was
not enforced on the six Phase 5 acts that skip drafts** — a Lead of one subject could withdraw the
answers to any question on the platform. Written up in `ContentStudioReview.md`.

Suite after: **817 tests, 800 pass, the same 17 pre-existing failures.**
