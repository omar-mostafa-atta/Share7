---
version: 1
slug: "share7-studio-src"
primary_target: "Share7.Studio/src"
related_targets: []
---

## Scope

The Content Studio app (`Share7.Studio`) — every screen it ships: sign-in, activation and account
security; Home, Curriculum, the lesson workspace, the question bank, Reviews, Releases, Activity,
Settings; and the teaching layer (first run, empty states that teach, in-place guidance, handbook).

Visitor mode: **operate**. Team & Access lives in the Admin Console and is not this surface.

## Audience and job

Curriculum specialists writing and translating lesson questions in English and Arabic, at a desk,
for hours. One role each (Author / Reviewer / Lead), scoped to parts of the curriculum and to the
languages they work in. Their task: write a lesson's questions, get a second person to approve
them, and let a Lead release them to the game. Plain words only — no "token", "session", "TOTP".
Arabic is a first-class language, and the whole interface mirrors for it.

## Direction contract

**THESIS:** The Studio is the board a specialist writes on before the class sees it — erasable
while drafting, signed off before it is shown. It refuses the category default: white canvas, left
sidebar, one blue accent.

**OWN-WORLD:** A matte slate-green board (#1d3a31) owns every screen, edge to edge, in an aluminium
frame with a chalk ledge along the bottom that carries the one action that matters. Chalk-white
(#ecefe4) writes everything; yellow, red and blue chalk are rationed to state and to the primary
action. **State is a stroke, never colour alone** — solid written, dashed waiting, struck ended —
and every state carries a name beside its mark. One family, Alexandria, Latin and Arabic together;
frame labels are small, wide-tracked, aluminium grey. A whiteboard-and-marker light theme runs the
same rules. The board is ruled into fixed columns that do not move between screens.

**STORY:** A member arrives at a board with their own name on it, sees what is waiting for them,
writes a lesson, watches the checks clear one by one, and hands it to a second person. Nothing
reaches a student until somebody else has signed it off, and every board says who wrote it.

**FIRST VIEWPORT:** The sign-in board: the date chalked in the top corner, the Studio's name
centred and underlined twice, the board ruled into the one column that matters — name, then
password — and **Start** on the chalk ledge along the bottom, the only coloured thing on screen.
Activation is the same board with the journey chalked across the top as named steps.

**FORM:** The Class Board, dealt as the assigned direction (seed 22b9d592, scope direction, mode
operate), raised by five declined challengers: stroke-state, rationed chalk, fixed ruled columns,
named step strips, explanations chalked beside their field. The lesson workspace's arrangement was
dealt separately (seed e73f91c1, scope surface, mode operate), and locked as **the written lesson**.

**FINISH:** unreviewed and undocumented is unfinished; this build ends with the finish review, the
verdict, DESIGN.md, and every shipping raster carrying its provenance

## The lesson board

The workspace is **the written lesson**: the whole lesson written out on one board the way it
would be read aloud. No cards, no panels, no containers — a number, the question after it, its
three answers indented beneath with the right one underlined, and the same question in Arabic in
its own right-to-left block directly under the English, sharing the number. A double rule, then
the second-chance questions. Every problem is chalked in the outer margin beside the exact line it
belongs to, joined by a short leader. Check, Save and Submit sit on the ledge and stay there.

## Signature interaction and motion

Chalk is written, not faded in. State changes redraw a stroke: an underline grows from its start,
a dashed rule resolves to solid when a check passes, a struck line draws through what is ending.
150–250 ms, one authored moment per change, nothing on page load.

## Unresolved

- Nothing. The lesson workspace arrangement was settled on 22 Sep 2026: **the written lesson**.
- Arabic interface text is authored here and reviewed by the team in the pilot.
