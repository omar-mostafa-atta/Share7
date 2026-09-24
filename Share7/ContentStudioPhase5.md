# Content Studio — Phase 5: skills, answers, papers and second chances

**2026-09-24.** The phase where the platform stops describing what it teaches and starts saying
what a child can *do*. Four areas, built on the measurement layer Phase 2 laid down and the Studio
Phase 4 delivered:

| Board | What it is for |
|---|---|
| **Skills** | The official learning outcomes: imported, edited, confirmed by a specialist |
| **What the questions measure** | One subject at a time, until every question names a real skill |
| **Answers** | What children's answers say about the questions themselves |
| **Second chances** | When a struggling child is offered easier questions, and how many |
| **Papers** | What an examination covers, and whether the bank could serve it |

---

## 1. Skills come in, then get edited

Decided 24 Sep 2026: **import, then edit**. The official outcomes are somebody else's document and
the Studio does not pretend to author them. A team downloads a sheet, fills it from the ministry's
own text, and brings it back; it arrives as a set nobody has confirmed yet, and the work is turning
those lines into claims that can be true or false about one child.

Also decided that day: **one framework for the whole curriculum, all subjects in it** — because a
ministry revises a curriculum, not a subject, and a framework is versioned as a whole.

The sheet is `Code · Kind · Parent code · Band · Statement (per language)`, with two worked rows and
a page explaining every column. Three things make re-importing safe, which matters because official
documents are re-issued:

- **Nothing is written until every row reads.** One bad row imports nothing and says which row. A
  vocabulary half imported is a vocabulary nobody can trust and the team cannot tell which half.
- **An import never undoes an edit.** The first time somebody changes an outcome in the Studio it is
  stamped (`LearningTarget.EditedAtUtc`); a later sheet leaves those exactly as they are and reports
  how many it left alone.
- **Every import is a dry run first.** The board reads the sheet, shows what it would add, update and
  leave alone, and only then offers to do it.

Confirming an outcome is a named act of its own, not a side effect of saving — that is the review
loop for skills. They are **not** drafted and not released: a skill changes what a child's answers
*mean*, never what the child is asked.

---

## 2. Replacing the stand-ins

Every lesson was minted a placeholder target so that measurement could start before real outcomes
existed. Replacing them, subject by subject, is the phase's real work and its gate.

Two ways to finish one, and the board offers both:

- **Move onto an imported outcome.** The ordinary way once the official document is in. Nothing is
  written — the questions simply move onto the ministry's own claim. (This is new: the promotion
  service previously always minted into Share7's own framework, which would have produced a second
  vocabulary for the same thing.)
- **Write a new one**, only where the official document has nothing to say.

Either way the same thing happens underneath, and it is the point of the whole architecture: the
stand-in is deprecated but never deleted, a `SupersededBy` edge keeps the old claim readable, every
`ItemTargetMapping` is repointed, and **every answer any child has ever given is re-read against the
real claim it was always about** — from the immutable response log, without anybody replaying a
lesson. In a design that stored progress as current state this operation is impossible, because the
evidence it needs was overwritten the moment it was recorded.

The board reports what moved: stand-ins replaced, questions moved, answers re-read. Every number is
checkable in SQL afterwards.

---

## 3. What the answers say

The first surface where the evidence pays somebody back, and it pays the team rather than a child.
Seven flags, each drawn as a stroke with its meaning in words — a wrong answer picked more often
than the right one, a wrong answer nobody ever picks, a question answered too fast to have been
read, a question that measures nothing at all.

**A flag is not a thing to dismiss.** It is fixed by rewriting the question, which is a draft, a
reviewer and a release like every other change to what a child is asked — the board's primary action
is "Fix it in a draft", and it opens the lesson.

The only two actions that stay on the board are the two that change what an answer *means* rather
than what the question says, and both are a Lead's, immediately, with a reason kept beside the
numbers for ever (decided 24 Sep 2026):

- **Anchor.** Cheap now, impossible retroactively: an anchor only works if it was already being
  asked across cohorts while the answers accumulated.
- **Stop these answers counting.** The answers are not touched — the child did answer, that is a
  fact, and it stays. What changes is whether the answer is allowed to say anything about them.

Both refuse a reason under ten characters. A sentence nobody can understand in a year is not a
reason.

---

## 4. Second chances

Net new: nothing decided when a struggling child is offered the second-chance pool. A rule says two
things — after how many wrong answers it opens, and how many questions it serves — and is written at
a place in the curriculum. **The most specific rule wins and is used whole**: a rule half-inherited
from a grade and half from a subject is a rule nobody can predict from looking at either.

Decided 24 Sep 2026: **a recovery rule is drafted, reviewed and released**, like a lesson's
questions. It changes what every child in its scope is shown, and nothing else that does is allowed
to skip review. It is the eighth kind of draft, and it goes through the machinery Phase 3 built with
no special case — its own base state, its own check, its own diff, its own release step.

Its shape is **simple now, per-skill later**: the `TargetId` column exists and is null for the whole
of the pilot, so adding per-skill recovery is a new row and a new branch rather than a migration on
a table the game is already reading.

**The game opts in.** `GET /api/recovery/lessons/{lessonId}` answers what happens in that lesson and
where it was decided. Nothing the Studio releases changes what the game does until the game asks —
which is what lets a team write, review and release rules for a whole curriculum without a client
release. A client that never calls it behaves exactly as it does today. It always answers: where
nobody has written a rule the platform's own defaults come back with `isDefault` set, because a game
that has to handle "no answer" is a game that will handle it differently from the one beside it.

---

## 5. Papers

A blueprint is a claim about what a real examination contains, so nothing here invents one. The team
reads what exists and sees the two things that would make a blueprint unusable: lines naming a
stand-in rather than a real skill, and lines no question in the bank can satisfy.

Freezing one is a Lead's act and it is final — a published blueprint is never edited, because every
coverage figure ever computed from it would quietly change meaning. **Freezing is refused while any
line still names a stand-in**, and the refusal says why: that paper measures whether a child sat
through lessons, not what they can do.

Where nobody has authored a syllabus at all, a Lead can build Share7's own benchmark over one
subject — its chapters as the paper's parts, the skills its lessons teach as its lines. It carries
its own source note, and the board says plainly that it is Share7's benchmark and not a ministry
paper. A structural derivation from the platform's own content is a legitimate benchmark and an
illegitimate national paper, and which one a reader is looking at has to be visible on the page.

---

## 6. What changed in the backend

- **`RecoveryRule`** and its configuration — one live rule per node, resolved by longest matching
  path. Superseded rows are kept for ever: a child's sitting last week was governed by one of these.
- **`DraftKind.RecoveryRule`** through `DraftKinds` (live state, default proposal, shape, check,
  languages touched, diff), `ReleaseService` (apply and impact) and `DraftService` (audit words).
- **`LearningTarget.EditedAtUtc` / `EditedByUserId`** — what makes re-importing a curriculum safe.
- **`PromoteTargetRequest.UseExistingTargetId` / `IntoFrameworkId`** — so a stand-in can be replaced
  by an outcome that already exists rather than always minting a Share7 duplicate.
- One migration, `20260924065636_SkillsAndRecovery`, covering both.
- Eight new Studio endpoints groups: `/api/studio/{skills,quality,recovery,exams}` and the game's
  one opt-in read at `/api/recovery/lessons/{id}`.

One thing the finish review found that matters more than the rest: **`live` was not a stroke.** It
set a colour and inherited the same solid 2px line as `written`, so the two differed by hue alone at
1.42:1 — and the second-chances board is the first place they are alternatives in one slot, where
"decided here" and "decided three levels up" is the whole point of the screen. `live` is now dotted.
All six strokes are distinct in shape: solid, part-drawn, dashed, dotted, double, struck.

Five more from the same review: stand-in rows carried a count and no state, so 1,710 of them read as
finished work; `<Mark>` was being spent on click-selection, where a solid `written` stroke said "done"
about something somebody had merely clicked (selection is an underline now, the same gesture the board
uses for the place you are on and the answer that is right); the quality board's ledge carried
"Refresh", which said the board was for reloading itself; counted facts were laid out on the full-width
row grid, so a label and its figure sat a metre apart; and a hover tint was advertising an action on
rows that are not controls.

**The one that would have shipped broken.** To put a `live` mark on screen for the review, a
recovery rule was driven the whole way for the first time: an Author proposed one, a Lead approved
it, and a release published it. The release refused — `outOfDate`. `DraftKinds.OutOfDateAsync`, the
batch staleness check that lists, queues and releases all use, compares every non-lesson draft's
fingerprint against its node's revision. A recovery rule's fingerprint is the *rule* it was written
against, so **every recovery-rule draft was born stale and no release would ever have carried one.**
The kind had been wired into the single-draft check and not the batch one. A rename does not change
what happens to a child getting things wrong, and the batch now knows that.

Nothing found it but running it. The draft saved, checked, submitted and approved cleanly; only the
release said no.

Five more things were fixed because running the thing showed them:

- **On a phone the Mapping ledge was 532px wide on a 390px screen.** `.ledge-end` did not wrap and
  its three `white-space: nowrap` buttons formed one unbreakable row, so the board's only yellow
  action sat off the side behind a sideways scroll. The end wraps now — and the ledge carries two
  controls rather than three, because getting back to the curriculum is a place along the top of the
  frame, not an act.

- **The blueprint id was the exam specification's.** `GenerateBenchmarkAsync` returns the
  specification because that is what it wrote last, so the Studio sent the team to a blueprint that
  does not exist. Found by its key now. It also refuses to build a paper with no language, because
  the name and the source note are read from the subject's title and it was writing
  "Share7 benchmark — " into the permanent record.
- **The startup seeder ran the engine backfill on the provider's thirty seconds** and timed out, so
  the whole host failed to start. Phase 4 gave migrations an hour for exactly this; the seeder
  reaches the same SQL by a second road.

- **The quality summary took 30 seconds and then failed.** The Admin Console's platform-wide summary
  counts every response and every observation in the database; putting it at the top of a Studio
  board meant the board could not be read until it came back, and on real data it timed out. The
  Studio now computes its own five numbers, about the team's own questions, in about a second.
- **A `<button class="row">` was centred**, because the browser's own centring for buttons reached
  every line inside one and `.row` never overrode it. Register rows on the new boards were centred
  while the identical link rows on the old ones were flush.

---

## 7. How it is proven

```bash
npm --prefix Share7.Studio run build
dotnet test Share7.Tests/Share7.Tests.csproj
```

- **Types and build:** `tsc --noEmit` clean; the production build succeeds.
- **Against the real development database**, which has 13,888 live questions and 1,710 stand-ins:
  every board was walked signed in as a Lead, and the numbers on them are that database's real
  numbers — 5,130 questions currently measuring nothing is the actual size of the work. Mathematics
  in Primary One First term is the worked case: 15 lessons, 120 questions, 75 on stand-ins, 45
  mapped to nothing.
- **One recovery rule was driven the whole way**, by two people, because nobody approves their own
  work: proposed by an Author, approved by a Lead, published in a release. The subject now reads
  "Decided here" and every lesson under it reads "Decided at Mathematics" — most specific wins,
  proven rather than asserted. A benchmark paper was built over the same subject; it has 15 lines,
  every one of them a stand-in, and **Freeze it is refused**, which is the check that matters.
- **The game's endpoint refuses a Studio token.** Confirmed by trying: `/api/recovery/lessons/{id}`
  answered 401 to a Studio bearer. The separation the plan promised is real in both directions.
- **The design detector** reports no non-advisory finding on `Share7.Studio/src`; the ten advisories
  are pre-existing literal colours in print rules and shadows, none in this phase's files.
- **A finish review** against the direction contract ran from a fresh context and took three rounds.
  It returned `fix` with eight material findings, then `fix` again on two partials and a regression
  the first batch introduced, and finally **ship** with nothing open. Its sharpest demand was for
  evidence rather than for a change: it would not score the `live` stroke from the stylesheet, and
  making that capture exist is what found the release bug above.

  **Where the evidence stops**, stated so a ship is not read as wider than it is: every `<dialog>` in
  this phase — the import dry run, the two mapping sheets, the two reason sheets behind the Lead-only
  acts — is verified in source and CSS and was never captured, and that is also where the selection
  underline lives. The whiteboard theme was captured on Skills alone, and Arabic on Skills and
  Quality alone; neither reached the mapping, papers or second-chances boards. The next phase's
  captures should start there.
- **The backend suite** is unchanged: 797 tests, 780 pass, the same 17 pre-existing failures.

---

## 8. Still open

- **The outcomes themselves.** The template and the importer are built; the official learning
  outcomes have to be typed into the sheet, subject by subject. That is the pilot's first real job
  and the only thing standing between here and the Phase 5 gate.
- **Per-skill recovery rules.** Deliberately deferred; the column is there.
- **Counted things do not pluralise.** `Counted` renders "1 papers". Doing it properly means
  `Intl.PluralRules` and per-key forms — Arabic has six categories, not two — across some six
  hundred strings. Written down rather than half-done; for now no count sits anywhere its noun
  would read wrongly at one.
- **Blueprint authoring by hand** stays in the Admin Console. The Studio reads blueprints, builds
  the benchmark and freezes them; writing areas and lines by hand is not a content-team job yet.
- The three decisions still outstanding from earlier phases: **CI**, the Facebook linking policy,
  and whether Google's audience check should fail closed.
