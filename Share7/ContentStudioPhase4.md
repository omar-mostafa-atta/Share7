# Content Studio — Phase 4: the Studio itself

**2026-09-22/23.** The screens. Everything the content team actually touches: the Studio's own
sign-in and activation, the boards for writing, reviewing and releasing, and the handbook that
explains them. Built on the API Phases 1 and 3 delivered — this phase adds no new endpoint.

It is a separate app, `Share7.Studio`, served on its own host name so nothing it stores can be read
by the Admin Console or the reverse. In development Vite serves it on `:5174` and proxies `/api` to
the API; in production `Studio:Host` makes the API serve the build itself, and `/api` on that host
falls through to the ordinary pipeline. No CORS anywhere.

---

## 1. The Class Board

The Studio does not look like the Admin Console (decided 22 Sep 2026), and it does not look like
every other workspace tool either. The direction was dealt and chosen on 22 Sep: **The Class
Board** — the board a specialist writes on before the class sees it. One matte slate-green board
fills every screen, held in an aluminium frame, with a ledge along the bottom carrying the one
action that matters.

Three rules run through all of it, and they are why the Studio never needs colour to be understood:

1. **State is a stroke, never a colour on its own.** Solid means written, a part-drawn line means
   still being written, dashed means waiting, a line through means ended, a double rule means
   wrong. Every state carries its name in words beside the mark.
2. **Chalk is rationed.** White chalk writes everything. Yellow marks the one action a board is
   for and where you are; red marks a problem; blue marks what students already have.
3. **The board is ruled into the same columns on every screen**, so nothing moves from one place to
   the next.

The light theme is the whiteboard that replaced the board: same rules, same strokes, marker ink
instead of chalk. A member lands on the board and switches in their account if the room is bright.

The durable version of all this is `Share7.Studio/DESIGN.md`; the strategy that produced it is in
`.impeccable/surfaces/share7-studio-src.md`.

---

## 2. The boards

The places are chalked along the top of the frame — no sidebar — and the one you are on is
underlined.

| Board | What it is for |
|---|---|
| **Home** | What is waiting on you, what you are writing, what you were given, what happened while you were away |
| **Curriculum** | The tree, one level at a time; every change to its shape starts a draft |
| **Lesson** | The written lesson (below) |
| **Question bank** | Every question the game is serving now, searched by its words |
| **Reviews** | One queue, oldest first, with the reason beside anything you may not review |
| **Releases** | What is approved, what has gone out, what would stop a release and who it reaches |
| **Activity** | Who changed what, grouped by day — a read of the permanent record |
| **Your account** | Role and scope, the Studio's language, the board's face, your password, 2-step, and where you are signed in |
| **Handbook** | Ten short chapters, in the order a new member meets them; prints cleanly |

Sign-in and activation stand on their own: no places along the top yet, only the date in the
corner, the name of the place underlined twice, one column, and the action on the ledge.

---

## 3. The written lesson

The arrangement was dealt separately and chosen on 22 Sep: **the written lesson**.

The whole lesson is on one board, written out the way it would be read aloud. No cards, no panels,
no containers anywhere — a number, the question after it, its three answers indented beneath with
the right one underlined, and the same question in each language under the same number in its own
direction. A double rule, then the second-chance questions.

- **Every problem is chalked in the outer margin beside the exact line it belongs to**, joined to
  it by a short leader. There is no tab, no tooltip and no help page anywhere in the Studio: an
  explanation sits next to the thing it explains or it does not exist.
- **The margin stays quiet until something moves.** While the draft still matches what is live it
  says nothing; the moment one line changes, every line says where it stands — and "Unchanged —
  keeps its place in every child's history" means something again.
- A question that is live but no longer in the draft is still shown, struck through, with one press
  to put it back. A question disappearing quietly is how a lesson loses its history.
- It saves as you type. A save that lands on somebody else's newer version says so and brings the
  board up to their version rather than flattening it.
- "Mona also has this open" appears on the ledge within a minute, from the presence heartbeat.

---

## 4. English and Arabic

Every string comes from `src/i18n/en.ts` or `ar.ts`, and `ar.ts` is typed against `en.ts`, so a key
added to one and forgotten in the other is a compile error rather than a blank label in production.

The interface language sets `<html lang>` and `<html dir>` and the whole Studio mirrors — the
places, the trail, the margin, the ledge. Layout is written in logical properties throughout, so
there is no second stylesheet for Arabic. Inside the mirrored interface **each piece of content
keeps its own direction**: an English question stays left-to-right inside an Arabic Studio, and its
answers are lettered A/B/C while the Arabic ones are أ/ب/ج.

Arabic interface text is authored here and is meant to be rewritten by the team in the pilot;
anything that reads stiffly to them should be changed in `ar.ts`.

---

## 5. Plain words

No technical vocabulary reaches the screen (decided 22 Sep 2026). The Studio says "2-step", "backup
codes", "signed out", "bring up to date" — never "TOTP", "token", "session", "rebase" or
"revision". A control names its action ("Submit for review"); an error names the problem and the
way out of it ("Somebody else saved this draft a moment ago. The board has been brought up to their
version — check your change is still there.").

Two things were fixed in the backend for the same reason: the activity feed no longer shows the
raw event key, and `DraftService` no longer writes `Started a draft: LessonContent.` into the audit
summary — it writes "a lesson's questions". The enum name is still in the detail object beside it
for anything machine-read.

---

## 6. The teaching layer

- **Empty boards teach.** A board with nothing on it says what would put something there, not
  "nothing here".
- **Guidance sits beside the field**, joined by a short line — the Studio's only way of explaining
  anything.
- **The handbook** is ten short chapters in both languages, in the order a member meets them, and
  it prints. It is deliberately short: anything that needs explaining while you work is chalked
  beside the work instead, and a handbook that grows is a sign the boards are not explaining
  themselves.
- **Practice lessons** are the sandbox — reviewed like anything else, never released — and Home
  offers one on the ledge.

---

## 7. How it is proven

```bash
npm --prefix Share7.Studio run build      # tsc, then the production bundle
dotnet test Share7.Tests/Share7.Tests.csproj
```

- **Types and build:** `tsc --noEmit` clean; the production build succeeds (≈444 kB of JavaScript,
  134 kB gzipped, and a 20 kB stylesheet).
- **Against the real API.** Every board was walked with the API running on the development
  database: a member was created, activated through a setup link, signed in, opened a real lesson
  with 5 main and 3 second-chance questions in English and Arabic, started a draft, and the
  questions, versions, checks and ledge all read correctly. The screens are captured at 1440 and
  390 in `.impeccable/review/`, in both themes and both languages.
- **The design detector** (`impeccable detect`) reports nothing on `Share7.Studio/src`.
- **A finish review** against the direction contract was run from a fresh context. It returned
  `fix` with eight material findings. Those were fixed, the boards recaptured and the fixes scored:
  five resolved, three partial, three new regressions. Those six were fixed and recaptured again,
  and the second verdict round scored every one of them resolved — **ship**. The eleven fixes are
  worth naming, because most of them are the world's own rules being kept rather than defects:
  motion is a stroke being drawn and only on a change, never on arrival; "being written" and
  "waiting for review" are different strokes; yellow is the action and nothing else, so the focused
  field, the caret and the lesson's own lines all write in chalk; the whiteboard frame clears
  4.5:1; the row chevron mirrors in Arabic and keeps its line on a phone; the ledge clears the last
  row at 390px; and no enum name reaches the board.
- **The backend is unchanged by the screens.** The suite is **797 tests, 780 pass**, the same 17
  pre-existing failures, none new.

Two backend fixes were made in the course of this phase, both surfaced by running the thing:

- `Program.cs` gives migrations an hour instead of the provider's thirty seconds. On a database
  with real content the Phase 2 engine backfill is minutes of work, and the default killed it part
  way through — the migration rolled back and the process exited, every time.
- `DraftService.cs` writes its audit summary in words rather than enum names (§5).

---

## 8. Still open

- **The pilot.** Two or three real content-team members, including a review of the Arabic interface
  text. That is the Phase 4 gate — "a pilot member publishes a real lesson without help" — and it
  is the one part that cannot be done from here.
- **Team & Access screens** (Phase 1's UI half) are still to build, in the Admin Console. The
  layout was dealt but not yet chosen.
- The old authoring paths are still open and still follow their own publish rules; freezing them is
  Phase 6.
- **CI**, the Facebook linking policy, and whether Google's audience check should fail closed — all
  three still need a decision.
