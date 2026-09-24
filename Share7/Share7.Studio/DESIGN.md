---
name: Share7 Content Studio
description: The board a specialist writes on before the class sees it.
colors:
  ground: "#1d3a31"
  ground-2: "#1a352c"
  ground-3: "#16302a"
  frame: "#a7aca8"
  frame-2: "#7f857f"
  ink: "#ecefe4"
  ink-2: "rgba(236, 239, 228, 0.74)"
  ink-3: "rgba(236, 239, 228, 0.62)"
  rule: "rgba(236, 239, 228, 0.28)"
  rule-soft: "rgba(236, 239, 228, 0.13)"
  dust: "rgba(236, 239, 228, 0.06)"
  go: "#f1d35b"
  go-on: "#21332a"
  bad: "#e57f76"
  bad-ink: "#f0a79f"
  live: "#8cbfe0"
  live-ink: "#a6cfea"
typography:
  display:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "2.125rem"
    fontWeight: 300
    lineHeight: 1.2
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "1.625rem"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "normal"
  title:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "1.3125rem"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "normal"
  written:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  body:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 300
    lineHeight: 1.55
    letterSpacing: "normal"
  label:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.55
    letterSpacing: "normal"
  engraved:
    fontFamily: "Alexandria, Segoe UI, system-ui, sans-serif"
    fontSize: "0.6875rem"
    fontWeight: 500
    lineHeight: 1.55
    letterSpacing: "0.14em"
    textTransform: "uppercase"
rounded:
  none: "0"
  chalk: "2px"
  sheet: "3px"
  pill: "99px"
spacing:
  s1: "4px"
  s2: "8px"
  s3: "12px"
  s4: "16px"
  s5: "24px"
  s6: "32px"
  s7: "48px"
  s8: "64px"
components:
  act:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.chalk}"
    padding: "10px 16px"
  act-hover:
    backgroundColor: "{colors.dust}"
    textColor: "{colors.ink}"
  act-first:
    backgroundColor: "{colors.go}"
    textColor: "{colors.go-on}"
    typography: "{typography.label}"
    rounded: "{rounded.chalk}"
    padding: "10px 16px"
  act-first-disabled:
    backgroundColor: "color-mix(in srgb, var(--go) 34%, transparent)"
    textColor: "{colors.ink}"
    rounded: "{rounded.chalk}"
    padding: "10px 16px"
  act-grave:
    backgroundColor: "transparent"
    textColor: "{colors.bad-ink}"
    rounded: "{rounded.chalk}"
    padding: "10px 16px"
  act-plain:
    backgroundColor: "transparent"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.chalk}"
    padding: "10px 8px"
  write:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.written}"
    rounded: "{rounded.none}"
    padding: "12px 0"
    width: "100%"
  place:
    backgroundColor: "transparent"
    textColor: "{colors.ink-2}"
    typography: "{typography.label}"
    rounded: "{rounded.chalk}"
    padding: "8px 12px"
  place-current:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    padding: "8px 12px"
  place-count:
    backgroundColor: "{colors.go}"
    textColor: "{colors.go-on}"
    typography: "{typography.engraved}"
    rounded: "{rounded.pill}"
    padding: "0 5px"
  row:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.none}"
    padding: "12px 8px"
  rail:
    backgroundColor: "{colors.ground-3}"
    textColor: "{colors.ink}"
    height: "56px"
    padding: "0 24px"
  ledge:
    backgroundColor: "{colors.ground-3}"
    textColor: "{colors.ink}"
    height: "60px"
    padding: "12px 24px"
  told:
    backgroundColor: "{colors.ground-3}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.chalk}"
    padding: "10px 16px"
  dialog:
    backgroundColor: "{colors.ground-2}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sheet}"
    padding: "32px"
    width: "min(560px, calc(100vw - 48px))"
---

# Design System: Share7 Content Studio

## Overview

**Creative North Star: "The Class Board"**

The Studio is the board a specialist writes on before the class sees it — erasable while
drafting, signed off before it is shown. One matte slate-green board fills every screen, edge to
edge, held in an aluminium frame, with a ledge along the bottom that carries the one action that
matters. Chalk-white writes everything. Three colours of chalk are rationed to state and to the
single action a board is for. The board is ruled into the same columns on every screen, so
nothing moves from one place to the next.

The density is a working board, not a dashboard: thirteen-pixel labels, fifteen-pixel prose,
hairline rules instead of boxes, and long unbroken columns of work. There are no cards, no
panels, no containers — numbering, rules and indentation carry structure everywhere, including
the lesson workspace, where a whole lesson is written out on one board the way it would be read
aloud. The world was chosen against the category default it refuses: a white canvas, a left
sidebar and one blue accent. It fails the moment it turns cartoonish, so there are no chalk
handwriting faces, no doodles and no classroom props; it has to read as a real board used by a
serious person.

Motion is a stroke being drawn, and only when somebody has changed something. Nothing fades in
and nothing at all is drawn on arrival: a state mark that is simply there when the board opens
sits still, and the same mark grows its stroke from the start the moment the state changes in
front of you — a dashed rule resolving to solid when a check passes, a line drawn through what
has just ended, a message wiped across from the direction the language is read in. One authored
moment per change, 180 ms, and `prefers-reduced-motion` clamps all of it to 1 ms.

**Key Characteristics:**
- State is a stroke with a name beside it, never a colour on its own
- Chalk is rationed: white carries everything, three colours carry only state and the one action
- Two faces — slate board and whiteboard — running exactly the same rules
- Ruled, not boxed: hairline rules, numbering and indentation instead of cards
- Right-to-left is structural, not a stylesheet override
- A sticky aluminium frame: places along the top, the one action on the ledge at the bottom
- The browser's own surfaces — selection, caret, focus ring, scrollbars — are part of the board

## Colors

Slate-green board, chalk-white writing, aluminium frame, and three coloured chalks used so
sparingly that a screen with none of them is normal.

### Primary
- **Chalk White** (`--ink`, #ecefe4): every word on the board. Headings, questions, answers,
  values, the current place, the right answer's underline. Two softened tints carry the rest of
  the voice: `--ink-2` for prose, labels and marks at rest, `--ink-3` for what has ended, what is
  placeholder, and the quiet meta beside a row.

### Secondary
- **Yellow Chalk** (`--go`, #f1d35b): the one action a board is for, and where you are. It
  appears on `.act.first`, on the underline beneath the current place, on the count badge beside
  a place, on the rule of the step you are on, on a link's underline while hovered, on the
  keyboard focus ring and on the selection highlight. Nothing else — a field taking focus firms
  up to full chalk rather than turning yellow, because on the sign-in board a yellow field would
  out-colour the yellow action it leads to. Its paired ground `--go-on` (#21332a) is the only
  text colour ever set on top of it.
- **Red Chalk** (`--bad`, #e57f76): a problem — a failed check, an invalid field, a destructive
  action, a margin note about something wrong. `--bad-ink` (#f0a79f) is the text tone; `--bad` is
  the stroke tone.
- **Blue Chalk** (`--live`, #8cbfe0): what students already have, and what somebody else has
  said — the live lesson, another member working on the same board, a reviewer's comment pinned
  in the margin. `--live-ink` (#a6cfea) is its text tone.

### Neutral
- **Board Green** (`--ground`, #1d3a31): the board itself, behind everything.
- **Board Green Deep** (`--ground-3`, #16302a): the frame's two surfaces — the rail along the
  top, the ledge along the bottom — plus the toast and a native select's option list.
- **Board Green Sheet** (`--ground-2`, #1a352c): the one raised surface, a modal dialog.
- **Aluminium** (`--frame`, #a7aca8): engraved frame labelling only — the `.engraved` class, the
  table header row, a language name above a question. Never used for content.
- **Aluminium Dim** (`--frame-2`, #7f857f): the hairline seam where the frame meets the board,
  and a dialog's edge.
- **Ruled Line** (`--rule`) and **Faint Rule** (`--rule-soft`): the two weights of hairline. A
  `--rule` line is an edge you write on or against (a field's baseline, an action's border, a
  table head). A `--rule-soft` line is a quiet separation between things of the same kind (rows,
  bands, lesson lines).
- **Chalk Dust** (`--dust`): the wash a row or an action picks up on hover, and the radial haze
  at the top of the board.

### Named Rules

**The Rationed Chalk Rule.** White chalk carries everything. `--go` is the one action a board is
for and where you are. `--bad` is a problem. `--live` is what students already have or what
somebody else has said. A colour outside those four roles is not in the system — and a screen
whose only coloured thing is the button on the ledge is the normal case, not a plain one.

**The Stroke Before Colour Rule.** Colour never carries meaning by itself. Every state is drawn
as a stroke beside the state's name in words; colour only ever confirms what the stroke and the
word have already said. Four of the six strokes (`written`, `writing`, `waiting`, `ended`) use
no colour at all — they are told apart by the line alone.

**The Two Faces, One Rulebook Rule.** `:root` is the slate board; `:root[data-surface="whiteboard"]`
is the light one. The whiteboard redefines every colour token and nothing else — no layout, no
type, no stroke and no component rule may test the surface. On the whiteboard the chalks invert
to marker ink: `--go` becomes a dark amber (#8a5d00) on near-white, `--bad` a deep red (#b8362a),
`--live` an ink blue (#1d6091), and `--ink` the board's own dark green (#1f2c26) on a paper
ground (#f1f2ec). The full whiteboard set lives in `.impeccable/design.json` under
`extensions.colorMeta.*.whiteboard`.

## Typography

**One Font:** Alexandria (with Segoe UI, system-ui, sans-serif behind it), weights 300, 400 and
500.

**Character:** Alexandria sets Latin and Arabic from the same family, with the same skeleton and
the same weights, so an English question and its Arabic translation sitting one under the other
read as one hand rather than two. The 300 weight at reading sizes gives the chalk its thinness;
500 is reserved for the frame's engraved labelling and for the two places where something is
current.

### Hierarchy
- **Display** (300, 2.125rem, 1.2): the name of a board that stands alone — the sign-in title,
  the activation welcome. Only ever used with `.twice`, the double underline. Drops to the
  headline size below 720px.
- **Headline** (400, 1.625rem, 1.2): the `h1` of a board inside the frame — the greeting, a
  lesson's title, the name of a place.
- **Title** (400, 1.3125rem, 1.2): a band's `h2`, an empty state's `h3`, a dialog's heading.
- **Written** (400, 1.0625rem, 1.5): what is read and written on the board — a lesson question,
  a line number, a value on the account board. Editable fields use the same size at weight 300,
  so a written question sits slightly heavier than the line it is typed on.
- **Body** (300, 0.9375rem, 1.55): the document default. Row titles and `.said` prose, capped at
  68ch.
- **Label** (400, 0.8125rem): the most-used size on the board. State marks, field labels, places,
  actions, `.beside` explanations, times and counts beside a row.
- **Engraved** (500, 0.6875rem, 0.14em tracking, uppercase, aluminium): the frame's own
  labelling — the date in the corner, the ledge's caption, the name above a value, a table's
  header row. Table headers use the same treatment at 0.12em.

### Named Rules

**The One Hand Rule.** One family sets both scripts at every size. There is no second face, no
serif for display, no mono for codes (recovery codes and the 2-step key are set in Alexandria
with tabular figures and wide tracking instead), and above all no chalk or handwriting face — the
world dies the moment the type pretends to be chalk.

**The Engraved Frame Rule.** Small wide-tracked uppercase in aluminium grey is the frame talking
about the board: a date, a caption, a column head, the name above a value. It never carries
content, and it never sits above a heading as a kicker.

**The Figures Line Up Rule.** Anything countable is set in tabular numerals — `.num`, `.tabular`,
every `td` and every `time`. A column of counts, a list of relative times and a grid of recovery
codes all align without a monospace face.

## Layout

**The frame.** Every signed-in screen is one grid: `auto 1fr auto` in a single column that can
never exceed the viewport. The rail (min 56px, `--rail`) sticks to the top and carries the mark,
the places, the language switch and who you are. The work scrolls between them. The ledge (min
60px, `--ledge`) sticks to the bottom and carries the one action. Sign-in and activation use the
same three-part frame with the places omitted — the date in the rail, one centred column of
460px (660px when the board has more to say), and the action still on the ledge.

**The sheet.** The work is capped at 1240px (`--page`), centred, padded 32px / 24px / 64px
(`--s6 --s5 --s8`).

**The rhythm.** An eight-step scale: 4, 8, 12, 16, 24, 32, 48, 64px (`--s1`…`--s8`). 8 and 12 do
most of the work inside a component; 16 and 24 separate components; 32 and 64 separate bands and
pad the sheet. Below 720px the middle of the scale contracts in place — `--s5` 24→16, `--s6`
32→24, `--s7` 48→32 — so every gap built on it tightens at once without a single component rule
changing.

**The ruled columns.** Where a board has a margin, the margin is a real column: the written
lesson is `minmax(0, 1fr) 270px` with a 32px gutter, and every problem, comment and standing note
is chalked in it beside the exact line it belongs to, joined by a nine-pixel leader. A lesson
line is itself ruled: a 2.4rem number column, then the body. `.ruled` (`minmax(0, 1fr) 320px`
with a sticky margin) is the generic version of the same shape.

**Measures.** Nothing is read at the full 1240px. Prose is 68ch, a lesson question 72ch, the
handbook 70ch, an empty state 62ch, a contents list 52ch, a standalone board's subtitle 44ch, and
a side-by-side pair is capped at 760px so two readings stay together rather than sitting at
opposite ends of a wide board.

**Responsive.** Six widths, each doing one job:
- **1100px** — the written lesson gives up its margin; notes move under the line they belong to,
  indented to the number column.
- **1080px** — the generic `.ruled` grid collapses and its sticky margin becomes a band below the
  work, separated by a rule instead of an edge.
- **860px** — the rail folds to two tiers: mark, language and sign-out on the top edge, the places
  on a scrolling line of their own with a fade mask at the trailing edge. The member's name is
  dropped; the language switch stays, because hiding it on the sign-in board would strand an
  Arabic-speaking member. The sheet reserves 124px at the bottom for the folded ledge.
- **760px** — side-by-side readings (live against proposed) stack.
- **720px** — the spacing scale contracts, the display size drops to the headline size, and a row
  stops being two columns: its title and its marks go one under the other, while a row's trailing
  chevron is pinned absolutely to the trailing edge so a stacked row is still visibly one row.
- **640px** — a `.pair` becomes one column.

`(hover: none)` reveals the per-line actions in the written lesson that otherwise appear on hover
or focus. `print` removes the texture, the rail, the ledge and the toasts and prints black on
white.

### Named Rules

**The No Sidebar Rule.** Navigation is chalked along the top edge of the frame and the current
place is underlined in yellow. There is no sidebar and no drawer; the board's own headings are
its navigation, and the work keeps the full width on every screen.

**The Ledge Rule.** The action a board is for lives on the ledge at the bottom of the frame, in
sight while the work scrolls, and a board has at most one `.act.first`. A screen puts its actions
there through `useLedge()`; the layout draws them, so it is always literally the same ledge.
Destructive actions stay in the work, beside the explanation of what they destroy.

**The Logical Property Rule.** Right-to-left is structural. There is no `left`, `right`,
`margin-left` or `text-align: left` anywhere — every edge, padding and gutter is
`inline-start`/`inline-end`, so the whole interface mirrors from `<html dir>` alone. The seven
places that cannot be expressed logically are flipped explicitly under `:root[dir="rtl"]`: the
`transform-origin` of the three drawn strokes and the place underline, the mask direction on the
scrolling places, the select arrow's gradients and position, the chevron at the end of a row, and
the toast's wipe direction. And content keeps its own direction inside the mirrored interface: an
Arabic question block carries `dir="rtl"` and its own `lang` whether the interface is English or
Arabic, and its answers are lettered أ ب ج rather than A B C.

## Elevation & Depth

There is no elevation. Nothing on this board floats, and no surface is lifted to signal
importance — depth is a seam in the frame, and the only thing that ever sits above the board is a
modal sheet pulled over it. `box-shadow` is used, but overwhelmingly as a drawing instrument
rather than as light.

### Shadow Vocabulary
- **Drawn rules** (`box-shadow: 0 5px 0 -1px var(--ink-3)` on `.twice`; `0 3px 0 -1px var(--rule)`
  on `.double-rule`; `inset 0 3px 0 -2px var(--rule)` on the lesson's second-chance divider):
  zero-blur offsets that draw a second line. This is how a teacher's double underline and the
  double rule before the second-chance questions are made, and it is the most common shadow in
  the system.
- **Drawn emphasis** (`0 1px 0 var(--ink)` on a focused field, `0 1px 0 var(--bad)` on an invalid
  one, `inset 0 -2px 0 var(--ink)` on a pressed segment): the same device thickening an existing
  rule to two pixels rather than colouring a box.
- **Frame seams** (`0 10px 24px -22px rgba(0,0,0,0.9)` under the rail, `0 -12px 28px -26px` above
  the ledge): almost entirely negative-spread, visible only as a darkening where the aluminium
  meets the board. Paired with a 1px `--frame-2` border, which does most of the work.
- **Lifted surfaces** (`0 10px 30px -18px rgba(0,0,0,0.85)` on a toast, `0 30px 80px -40px
  rgba(0,0,0,0.95)` on a dialog, over a `rgba(8,18,15,0.62)` backdrop): the only two things in
  the Studio allowed to sit above the board.

### Texture
The board's material is two fixed, pointer-transparent layers painted once behind everything: an
inline SVG fractal-noise grain at 0.45 opacity in `overlay` blend mode, and a 38vh radial haze of
`--dust` at the top edge, the dust that gathers where a board is wiped. Both are `position: fixed`
so scrolling costs nothing, and both are removed in print.

### Named Rules

**The Flat Board Rule.** A surface is never raised to mean something. Hierarchy comes from the
stroke, the rule weight and the type size. If a new component reaches for a drop shadow to
separate itself from the board, it wants a `--rule-soft` hairline instead.

**The Shadow Draws, It Does Not Light Rule.** A new `box-shadow` must be either a zero-blur offset
that draws a line, a near-fully-negative-spread seam, or one of the two lifted surfaces. There is
no ambient shadow vocabulary to extend.

## Shapes

The form language is ruled lines, not boxes. Hairlines separate; nothing is enclosed.

**Corners.** Effectively square. 2px (`--rounded.chalk`) on everything that has a border at all —
actions, segments, the toast, the recovery-code cells, the 2-step key, a place, the loading
shimmer and the focus ring. 3px on the one dialog. 99px on exactly two things: the count badge
beside a place, and the scrollbar thumb. Fields are explicitly `border-radius: 0` — a ruled line
has no corners.

**Borders.** One pixel, always, in one of four weights of meaning: `--rule` for an edge you write
on or against, `--rule-soft` for a quiet separation between like things, `--frame-2` for the
frame's seams, and `1px dashed --rule` for something written down but not yet used — a recovery
code, the 2-step key, a disabled field, a step not yet reached.

**Strokes.** The state marks are 18px wide and 2px thick, drawn as a `border-top` on a
zero-height element of their own; the `wrong` stroke is a 4px double rule; the journey steps are
26px of 2px dashed rule. A leader joining an explanation to its field is 9px at 1px. The right answer
in a lesson is underlined at 2px with a 0.28em offset — on the words, never across the column,
because a rule running the full width would read as a ruled line, which is a different thing.

**Icons.** Line icons only, at 14–16px with `strokeWidth: 1.5`, never filled, never boxed, and
never the only carrier of meaning — every icon in the Studio sits beside its own words or has an
accessible name. The product mark is a 20px inline SVG of the board itself: an aluminium frame,
two chalk lines and one yellow one.

### Named Rules

**The Ruled, Not Boxed Rule.** No cards, no panels, no containers. A list is rows separated by
hairlines; a section is a band separated by a hairline; a lesson is a numbered, indented column.
If something needs to be set apart, rule it, number it or indent it.

## Components

### State Marks
The signature component, and the rule the whole world is built on. `<Mark stroke="…">` draws a
stroke before its children and will not render without them, so a state can never ship as a
colour or a dot alone. The stroke is a real element (`.mark-line`), not a pseudo-element, because
it has to be able to be drawn *again*: `<Mark>` counts how many times the stroke has changed and
remounts the line on each change, so a dashed rule genuinely resolves into a solid one in front
of you rather than snapping between two static marks.

- **Six strokes:** `written` (solid, full-strength ink — done, approved, published, live),
  `writing` (a part-drawn line with the chalk still on it — a draft being edited), `waiting`
  (dashed — in review, building, scheduled, unread), `ended` (a line drawn through the words,
  70 ms after the stroke — discarded, cancelled, a superseded approval), `wrong` (a 4px double
  rule in red chalk — changes requested, failed, out of date, a missing language), `live` (blue
  chalk — what students already have, a practice lesson, somebody else on the same board).
- **Mapping:** the product's statuses are turned into strokes in exactly one file
  (`src/screens/bits.tsx`), so a mark and its word can never drift apart.
- **Motion:** `draws` — the stroke scales from 0 to 1 along its own inline axis, 180 ms,
  `cubic-bezier(0.16, 0.84, 0.36, 1)`, with `transform-origin` flipped in RTL. `ended` adds
  `strikes`, a second line drawn through the words after a 70 ms beat. Both animations are gated
  behind `data-draws="yes"`, which `<Mark>` sets only after the stroke has actually changed: a
  mark the board was already holding when you opened it arrives still, and an ended thing arrives
  already struck unless it ends while you are looking at it.
- **Wrapping:** the mark is `max-inline-size: 100%` so a long state name wraps rather than setting
  the minimum width of everything above it.

### Buttons
- **Shape:** 2px corners, a 1px `--rule` border, 10px/16px padding, label type at weight 400.
- **Chalk action** (`.act`): the default. Transparent, chalk-white text, hairline border. Hover
  brightens the border to `--ink-3` and washes the inside with `--dust`; active nudges it down 1px.
- **The one action** (`.act.first`): yellow chalk ground, `--go-on` text, weight 500. One per
  board, on the ledge.
- **Unready primary:** dimmed, never drained — 34% yellow behind full-strength chalk text, border
  still solid. The yellow has to say which action the board is for before the form is filled in
  as well as after, so a disabled primary never falls back to the dashed grey treatment; and the
  fill is what says "not ready", so the label stays fully readable rather than dimming too.
- **Grave** (`.act.grave`): red-chalk text on a half-strength red border; hover brings the border
  to full `--bad` over a 12% wash. Used for the few real deletions, always beside the sentence
  explaining what is lost.
- **Plain** (`.act.plain`) and **icon** (`.act.icon`): borderless, `--ink-2`, 8px padding, for
  actions that sit inside the work.
- **Small** (`.act.small`): 5px/12px at the engraved size, for a row's own action.
- **Disabled:** `--ink-3` text on a dashed `--rule-soft` border — the same dashed language as a
  disabled field and an unreached step.

### Fields
- **Style:** a ruled line to write on. No background, no box, no radius — just a 1px `--rule`
  bottom border, 12px of vertical padding, and the written type size at weight 300.
- **Focus:** the rule firms up to full chalk and thickens to two pixels (`box-shadow: 0 1px 0
  var(--ink)`) — the chalk is on this line. It is deliberately not yellow: yellow says which
  action a board is for, and a focused field would out-colour the action it leads to on the one
  screen (sign-in) where that action is the whole point. The caret is chalk for the same reason.
  The 2px `--go` focus ring at a 3px offset remains the global `:focus-visible` treatment for
  everything that is not a ruled line.
- **Invalid:** the same rule in `--bad`, with the problem chalked beneath it.
- **Disabled:** `--ink-3` text on a dashed rule.
- **Select:** the same ruled line with a hand-drawn caret made of two gradients, mirrored in RTL.
- **Explanation:** a hint or a problem is a `.beside` — chalked under the field, joined to it by a
  nine-pixel leader, and a problem replaces the hint rather than stacking with it.

### Navigation
- **Places** (`.place`): chalked along the top of the frame at label size, weight 300, `--ink-2`.
  Hover brings them to full ink. The current place is weight 500, full ink, and underlined by a
  2px yellow rule that scales in from its start — the only navigation state in the system.
- **Counts:** a yellow pill of engraved type beside a place, and only two of them exist in the
  whole Studio (what is waiting for review, what happened while you were away). Both refresh
  quietly every minute.
- **Trail** (`.trail`): the way down to a node, dot-separated, `--ink-3` with the last step in
  full ink; hovering a link underlines it in yellow.
- **Mobile:** the rail folds to two tiers below 860px; the places take a scrolling line of their
  own with a fade mask on the trailing edge.

### Rows
A register, not a table of cards. Two columns — the work on one side, its marks and times on the
other — separated by a `--rule-soft` hairline, with a `--dust` wash on hover and no border on the
last row. The title is `overflow-wrap: anywhere`; the side is a wrapping flex of state marks,
counts and a `<time>`. Below 720px the two halves stack rather than squeezing the title.

### The Written Lesson
The Studio's signature screen. A whole lesson written out on one board the way it would be read
aloud: a 2.4rem number, the question after it, its three answers indented 24px beneath with the
right one underlined, and the same question in each other language directly under it, sharing the
number, in its own direction and its own script. Languages are separated by a dashed hairline and
labelled in engraved aluminium. A double rule runs right across the board, margin included, where
the second-chance questions begin. A question and an answer are edited where they read: no
border at rest, a faint rule on hover, and a yellow rule on focus — the one place in the Studio
where a focused field is yellow rather than chalk, because a lesson line has no label above it to
say which one you are in. Questions grow with what is typed into them — no scrollbar inside a
line of a lesson. A question that is live but no longer in the draft stays on the board
struck through, with one press to bring it back. Every problem, every pinned comment and every
"this line has changed" note is chalked in the outer margin on the exact line it belongs to; a
line's own actions stay invisible until it is hovered or focused.

### Steps
A journey chalked across the top of a standalone board at one scale — a 26px dashed rule before
each name, solid for a step that is done, solid and yellow for the one you are on, `--ink-3`
dashed for one still ahead. The steps do not animate: a journey you are being shown is not a
change you just made, and the board it sits on has only just arrived.

### Empty and Loading States
- **Nothing** (`.nothing`): an empty band teaches what would put something in it rather than
  saying "nothing here". Left-aligned, capped at 62ch, optionally carrying the action that would
  fill it. Inside a band whose heading has already named what is missing, it omits its own title
  rather than saying it twice.
- **Wiping** (`.wiping`): the board being wiped clean while what goes on it is fetched — a 100°
  chalk-dust sweep at 260% width looping over 1.5s, in rows of varying width (100%, 84%, 62%).
  It is skeleton-shaped because it is literally a wipe, not a shimmer borrowed from elsewhere.

### What the Board Says Back
Toasts (`.told`) are centred above the ledge, on the frame's own dark green with a 1px `--rule`
border, and are written across rather than faded in: `clip-path: inset(0 100% 0 0)` → `inset(0)`,
with a mirrored `chalked-rtl` keyframe so an Arabic message is written right to left. A plain
message carries a check and lasts 4 seconds; a problem carries a warning triangle in `--bad-ink`
and lasts 7.

### The Sheet
A native `<dialog>`, the only surface allowed above the board: `--ground-2`, a `--frame-2` edge,
3px corners, 32px of padding, over a `rgba(8,18,15,0.62)` backdrop. Used only for the few tasks
that need protecting — turning on 2-step, showing recovery codes, confirming a rollback.

### The Browser's Own Surfaces
Treated as part of the system, not an afterthought, because left alone they ship somebody else's
design: the selection is 34% yellow chalk, the caret is chalk white, the focus ring is a 2px `--go`
outline at 3px offset, scrollbars are thin `--rule` thumbs on transparent tracks that brighten to
`--ink-3` on hover, links carry a `--rule` underline at 0.22em offset that turns yellow on hover,
figures are tabular, and `<meta name="theme-color">` is repainted to match the chosen face so the
very first paint is already the board.

### Named Rules

**The Named Mark Rule.** `<Mark>` takes the state's name as its children and renders nothing
without it. A status pill, a coloured dot, a bare icon or a colour-only badge is not a thing this
system has.

**The Beside Rule.** An explanation is chalked beside the exact thing it explains, joined by a
short leader. `<Beside>` is the only way the Studio explains anything at the point of use; there
is no help page, no tooltip that is the sole carrier of a fact, and no expandable "learn more".
The handbook exists, but nothing on the board depends on having read it.

**The Drawn, Not Faded Rule.** A state change redraws a stroke; a state you are merely being shown
does not. Transitions are limited to colour, border-colour, background-colour, transform and
opacity at 180 ms on `cubic-bezier(0.16, 0.84, 0.36, 1)`. There is no entrance animation, no
stagger, and nothing at all happens on arrival — a board that animates everything it is holding
the moment you open it is telling you nothing.

## Do's and Don'ts

### Do:
- **Do** render every state with `<Mark>`, one of the six strokes, and the state's name as its
  children — and add a new status by mapping it to an existing stroke in `src/screens/bits.tsx`,
  not by inventing a seventh.
- **Do** keep exactly one `.act.first` per board, and put it on the ledge through `useLedge()`.
- **Do** dim an unready primary action (34% `--go` behind full-strength `--ink`, border still
  solid) rather than draining it to the dashed grey disabled treatment.
- **Do** define every new colour in both `:root` and `:root[data-surface="whiteboard"]` in the
  same change, and reach for `--go`, `--bad` or `--live` only in their one role each.
- **Do** write every edge as a logical property, and give content that is in another language its
  own `dir` and `lang` inside the mirrored interface.
- **Do** chalk an explanation beside the thing it explains with `<Beside>`, and let a problem
  replace the hint rather than stack with it.
- **Do** separate with a hairline — `--rule` for an edge you write against, `--rule-soft` for a
  quiet separation between like things.
- **Do** set anything countable in tabular figures (`.num`, `.tabular`, `td`, `time`).
- **Do** teach in an empty state: say what would put something here, and offer the action that
  would.

### Don't:
- **Don't** let colour carry a meaning on its own, and never ship a mark, dot, pill or icon
  without the words beside it.
- **Don't** add a card, a panel or any container with its own background — structure is
  numbering, rules and indentation.
- **Don't** raise a surface to signal importance. The only two things allowed above the board are
  the toast and the dialog, and no new ambient shadow may be added.
- **Don't** introduce a second typeface, and above all never a chalk, handwriting or blackboard
  display face, a doodle, or any classroom prop. The world dies the moment it looks cartoonish.
- **Don't** write `left`, `right`, `margin-left`, `padding-right` or `text-align: left`. If a
  physical direction is genuinely unavoidable, flip it explicitly under `:root[dir="rtl"]`.
- **Don't** fade anything in, stagger anything, or animate anything on arrival. A change is a
  stroke being drawn, once, at 180 ms — and a state the board was already holding is not a change.
- **Don't** build a left sidebar, a white canvas or a single blue accent. That is the category
  default this world was chosen to refuse.
- **Don't** put a primary action inline in the work, or a destructive action on the ledge.
- **Don't** create a help page, a tour, or a tooltip that is the only place something is
  explained.
- **Don't** let a radius exceed 3px, except the two pills (the place count and the scrollbar
  thumb) at 99px. Fields stay at 0.
- **Don't** write a selector that tests `data-surface`. The two faces differ in colour tokens and
  nothing else.
