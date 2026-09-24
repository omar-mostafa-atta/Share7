# Content Studio — Phase 3: drafts, reviews and releases

**2026-09-22.** The authoring layer on top of the engine. Work is written as drafts nobody outside
the content team sees, a second person approves it, and a Lead publishes approved work as a release —
**the only thing that writes live content**. Any release can be rolled back.

Backend only; no UI. The Studio app (Phase 4) is built on the API this phase provides.

---

## 1. What a draft is

One draft, one target. A lesson has at most one open content draft, which the team **shares**: two
authors on one lesson edit the same draft (autosaved, with the revision as an If-Match) rather than
two drafts that would have to be merged later.

| Kind | Target | What it proposes |
|---|---|---|
| `LessonContent` | a lesson | its questions, every pool and language |
| `NewNode` | a term, subject, chapter or lesson that does not exist yet | its titles and position — and, for a lesson, its questions, so "add a lesson" is one draft and one review |
| `Rename` | a node | new titles |
| `Move` | a node | a new parent and position |
| `Reorder` | a parent | a new order for its live children |
| `Retire` / `Restore` | a node | hide it, or bring it back |

A draft carries the **live state it started from** and a fingerprint of it — the lesson's published
set versions, or the node's revision. If live content moves underneath it (another release, or an
old admin path during the transition) the draft is *out of date*: it cannot be submitted or
released until its author has brought it up to date against what is live now, which sends it back
through review.

**Practice drafts** are the sandbox: reviewed like anything else, never released, never tied to a
live lesson. Onboarding uses them.

Statuses: `Editing → InReview → Approved → Released`, with `ChangesRequested` for work sent back and
`Discarded` for work thrown away. **Any edit un-approves a draft** — what was reviewed is not what
is there any more. Discarding is the only real delete in the Studio, and only of work that never
reached students.

---

## 2. Review

- Everything waiting is in one queue, **oldest first**, with how long it has waited and whether the
  caller may review each — and why not, when they may not.
- **Nobody approves a draft they wrote any of.** Every editor is recorded as a contributor; a
  contributor cannot approve, whatever their role. Reviewers and Leads review.
- A reviewer's **scope must cover the draft's place in the curriculum and every language it
  changes**: you approve only what you could have written.
- A verdict names the exact revision it was given on, so an approval of an older revision is void.
- Sending a draft back needs a note. Comments can be pinned to a question, a language or a field,
  answered in threads, and resolved.
- The checks are re-run at approval, not only at submission: a rule can have started to apply in
  between (a sibling took the name, a language became required).

---

## 3. Release

A Lead bundles approved drafts. Before it goes out, the release says what would stop it —
`notApproved`, `outOfDate`, `practice`, `closed`, `hasProblems`, `outOfScope`, `needsDraft` (a new
node whose parent is in another draft), `sameTarget` (two changes to one thing, or a reorder beside
something that adds to or takes from those same children) — and **who it reaches**: students who have
played a lesson whose questions change, students part way through a parent being reordered, students
holding a node being retired or moved.

Publishing is **one transaction**. Every draft is checked against live state up front, under a
platform-wide release lock, and then applied in order: restores, new nodes (parents first), renames,
moves, reorders, questions, retirements. Each entry records the state it replaced. If any step is
refused, the whole release is rolled back, marked `Failed` with the reason, and **nothing it touched
changed** — the drafts are untouched and can be fixed and published again.

**Scheduling** publishes the release at a time the Lead chooses (the start of term). The scheduler
publishes it as the Lead who scheduled it, with their scope as it is then: somebody who has since
lost the right cannot publish by clock.

**Rollback** is itself a release: it puts back the "before" of every entry, in reverse. It is refused
when anything the release changed has been changed again since — that later change would be undone
along with it, so it has to be rolled back first. Versions keep going up; a rollback is a new version
whose content is the old one, so no device can mistake old content for current.

---

## 4. Excel, kept as a first-class way in

- **Trial run** — a sheet read and reported row by row, with every problem placed on its row and
  column. Nothing is saved.
- **Into a draft** — the same read, applied to the lesson's draft. A row that lands on a question
  already there (same pool, same position) keeps its identity, so re-importing a sheet with one row
  fixed keeps every other question's id.
- **Template** for any set of languages, and **export** of a lesson (live, or the open draft).
- The columns follow the content languages — four per language, then the recovery flag — so the
  console's existing nine-column English/Arabic sheet still imports. A header naming languages by
  code (`Question (EN)`) lets them come in any order.

---

## 5. The API

Everything is under `/api/studio` and accepts **Studio sign-ins only**; a game or admin token is not
even read. The member's role and scope are resolved per request and checked against the exact target
of each call. Refusals use the Studio envelope — `{ code, messageKey, details }` — so the Studio can
say what happened in English or Arabic without the server sending prose.

| Route group | What it is for |
|---|---|
| `GET curriculum/languages`, `curriculum/nodes`, `curriculum/nodes/{id}` | the tree with statuses: open drafts, missing languages, question counts, whether it is in your scope |
| `GET curriculum/lessons/{id}/workspace` | what is live, side by side with the team's open draft |
| `GET curriculum/search` | the question bank |
| `drafts` (`GET`, `POST`), `drafts/{id}` (`GET`, `PUT`) | list, start or join, read, autosave |
| `drafts/{id}/check`, `/diff`, `/live`, `/rebase` | the checks, the change, what is live now, bringing it up to date |
| `drafts/{id}/submit`, `/withdraw`, `/discard` | through review and back |
| `drafts/{id}/approve`, `/request-changes`, `/comments` | reviewing |
| `drafts/{id}/presence` | "Mona is editing" |
| `drafts/{id}/import` | a sheet into this draft |
| `reviews/queue`, `reviews/comments/{id}` | the queue; editing and resolving comments |
| `releases` … `/publish`, `/schedule`, `/cancel`, `/rollback` | building and releasing (Leads) |
| `imports/trial`, `imports/template`, `imports/lessons/{id}/export` | Excel |
| `notifications`, `assignments`, `activity`, `team` | the inbox, work queue and team feed |

Sheets are `.xlsx` only, 10 MB and 5,000 rows at most.

---

## 6. What it writes

`20260922160306_ContentWorkspace`: `Drafts`, `DraftContributors`, `DraftComments`, `ReviewDecisions`,
`DraftPresence`, `Releases`, `ReleaseEntries`, `StudioAssignments`, `StudioNotifications`.

Nothing in this phase writes live content except a release, and a release writes it through the same
two engine writers as everything else (Phase 2) — so a release cannot do anything an admin path
could not, and every publish it makes carries its release id in the publication history.

Every workspace action is one audit row in the `workspace` area: `workspace.draft.created`,
`.submitted`, `.approved`, `.changes_requested`, `.brought_up_to_date`, `.discarded`, `.imported`,
`workspace.release.created`, `.scheduled`, `.published`, `.failed`, `.rolled_back`. The team's
activity feed is a read of that trail, and so is the SuperAdmin's audit viewer.

No email (approved default #7): everything a member needs to know is in their Studio inbox.

---

## 7. How it is proven

```bash
dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~WorkspaceCycleTests"
dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~StudioApiSmokeTests"
```

**The gate — a full Draft → Review → Release → Rollback cycle**, over the real container and
database, checking what the game reads at every step:

- an author drafts a lesson: adds the Arabic to the question that is live, adds a second question and
  a recovery question — and **nothing a student reads moves**;
- an Author cannot review; a Reviewer approves; a Lead who wrote part of a draft is refused
  ("own work"); a Reviewer cannot build a release;
- the release publishes: both languages are live, the unchanged English question **kept its id**, and
  the publication carries the release id;
- the rollback puts the lesson back, with the version **moved forward**, not rewound.

Beside it: a new lesson released and rolled back (it is retired, not deleted); a draft that an old
admin path moved underneath, refused until it is brought up to date; scope limiting an Arabic-only
author to the Arabic; practice never releasable; a release that cannot go out whole changing nothing;
and a scheduled release publishing itself when its time comes.

**The API smoke tests** run the real API process: a member is created in Team & Access, activated
through their setup link, signs in over HTTP, and works a draft through the endpoints — including
that a second save with a stale revision is a `409 DRAFT_REVISION_MOVED`, that a student's token is
refused, and that the game's own reads are untouched.

Whole suite after Phase 3: **794 tests, 777 pass**, the same 17 pre-existing failures.

---

## 8. Still open

- **The Studio app (Phase 4)** — this is its API. The look was decided on 22 Sep 2026: "The Class
  Board".
- **The education areas (Phase 5)**: skills, exams, quality and recovery rules become draftable the
  same way — the draft kinds are an enum and a payload, so each is a kind plus its checks.
- Real-time presence is a heartbeat, not a socket: "Mona is editing" appears within a minute.
- A release applies its drafts one after another; for a very large release this is the slowest thing
  in the Studio, and it holds a platform-wide lock while it runs.
