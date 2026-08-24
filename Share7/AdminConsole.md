# The admin console

**2026-08-24.** What the console authors, the two changes made to it today, and the one backend
route that had to exist before the second was possible.

The console is the vanilla-JS app under `wwwroot/` (mirrored at `Share7 front/`). It is the only
way boards, games, offers, currencies and curriculum are authored — there is no other tool.

---

## 0. Summary

Two things were asked for, and one turned out to be a precondition of the second:

| # | Change | Why |
|---|---|---|
| 1 | Games page catches up with the scene removal | The page still authored four fields the API had dropped |
| 2 | New Leaderboards page | Eight admin operations existed with no UI at all |
| 3 | `GET /api/admin/leaderboards/boards/{id}/cycles` | Two of those eight are addressed by cycle id, and nothing could produce one |
| 4 | `.sub-pane` CSS | Pre-existing: the rule the markup and JS both assume was never written |

---

## 1. Games — catching up with the scene removal

### 1.1 What was wrong

`GameCatalogueBoundary.md` §4 records that `LobbyScene`, `GameplayScene`, `LobbySceneAddress` and
`GameplaySceneAddress` were removed from `Game`, `GameDto`, `GameAdminDto`, `SaveGameRequest`, the
EF configuration and the database (migration `20260824145252_DropGameSceneColumns`).

**Item 12 of that list — the admin console — had not actually been applied.** The doc describes it
in the past tense, but both copies of the console still carried all four inputs. What that meant in
practice:

- The catalogue table rendered a **Scenes** column reading `g.gameplaySceneAddress` and
  `` `${g.lobbyScene}/${g.gameplayScene}` ``. Every one of those is now `undefined` on the wire, so
  the column printed `undefined/undefined` on every row.
- The edit form filled four inputs from properties that no longer exist, so they showed blank or
  `undefined` regardless of the game.
- The save body sent four properties `SaveGameRequest` no longer declares. ASP.NET's model binder
  ignores unknown properties, so this was silent rather than a 400 — the worst kind of stale, since
  nothing ever complained.
- Two client-side pre-flight validations still enforced the addressable **scene-pair rules** that
  `GameAdminService.ValidateAsync` had already stopped enforcing. They could refuse a save the
  server would have accepted, on the basis of fields it no longer reads.

### 1.2 What was changed

**`js/games.js`**

1. Dropped the `Scenes` column from the table header and the `<td>` that rendered it.
2. Put **Ready** (`readyTimeoutSeconds`) in its place, exactly as §4 item 12 of
   `GameCatalogueBoundary.md` specified. It is on the listing DTO already, and it is the one
   matchmaking-relevant number that was not visible without opening the form.
3. Dropped the four scene fills from `openGameModal`.
4. Dropped `lobbySceneAddress` / `gameplaySceneAddress` from the locals and all four scene
   properties from the request body.
5. Dropped the two scene-pair pre-flight checks. They guarded fields that no longer exist; keeping
   them would have been a client refusing a save on a rule the server abandoned.
6. Added a line to the table footnote saying where scenes actually come from, so somebody who
   remembers the column is not left wondering whether it broke.

**`pages/games.html`**

7. Removed the four inputs (`gameLobbyScene`, `gameGameplayScene`, `gameLobbyAddress`,
   `gameGameplayAddress`), their two rows and their two help paragraphs. The game-key field widened
   to fill the row it now has to itself.
8. The key's help text now states the equality with `MiniGameDefinitionSO.gameId` explicitly,
   mirroring what `SaveGameRequest.GameKey`'s doc was changed to say. That equality is the entire
   join between the two catalogues, and it was previously described only as "machine-facing".
9. Rewrote the page's authority banner. It used to claim the table wins outright; it now says what
   is true after the boundary change — the table is authoritative for **availability and
   matchmaking**, and scenes are the ScriptableObject's and are not authored here, with the reason
   (a client that can download a game's content can already resolve its scenes, so serving them
   here could only add a second source of truth the server cannot validate).
10. Updated the page `<meta name="description">`, which still advertised "scene mappings".

### 1.3 Why not just leave it

The fields were harmless in the sense that nothing crashed. They were not harmless in the sense
that mattered: an operator looking at the Games page saw four inputs implying the server cares what
they type, and a column of `undefined`. A console that lies about what it controls is how somebody
ends up authoring a scene index to fix a load failure that has nothing to do with the backend.

---

## 2. Leaderboards — a page for a surface that had none

`AdminLeaderboardsController` exposed eight operations and the console offered no way to reach any
of them. Boards are deliberately **data** rather than migrations — the whole design intent is that a
seasonal event is an INSERT, no deploy, no client release — and that intent is worth nothing if the
INSERT can only be made with curl and a hand-built JSON body.

### 2.1 The precondition — an admin cycles read

`RebuildCycleAsync(cycleId)` and `SettleCycleAsync(cycleId)` are addressed by **cycle id**. Nothing
on the admin surface could produce one:

- `LeaderboardBoardAdminDto` carries `CycleCount`, a number, not the cycles.
- The player-facing `GET /api/leaderboards/{boardId}/cycles` returns them — but
  `LeaderboardService.GetCyclesAsync` opens with `if (!_options.Enabled) return Disabled<…>()`, so
  it answers **409 `LB_DISABLED`** while the feature switch is off. That is precisely the window in
  which an operator is authoring boards, because the switch is documented as staying off until the
  first board exists.

So two of the eight operations were unreachable by construction. Three files:

11. **`Share7.Application/Leaderboards/Interfaces/ILeaderboardService.cs`** — added
    `GetCyclesAsync(Guid boardId, int limit = 20, CancellationToken)` to `ILeaderboardAdminService`,
    returning the existing `LeaderboardCycleDto`. Its doc says why it exists rather than deferring
    to the player-facing twin.

12. **`Share7.Infrastructure/Leaderboards/LeaderboardAdminService.cs`** — implemented it. 404s an
    unknown board through `ApiErrors.LeaderboardBoardNotFound`, orders newest-first, clamps `limit`
    to `[1, 50]` exactly as the player read does, and maps an endless cycle's end to `null` rather
    than the year 9999 so a console renders "no end" instead of four thousand years.

    **It does not consult `_options.Enabled`.** Nothing on this service does, and that is not an
    oversight being copied — authoring happens *before* the switch is flipped, and an admin surface
    that went dark exactly while the feature was off could never be used to prepare it.

13. **`Share7/Controllers/AdminLeaderboardsController.cs`** — added
    `GET api/admin/leaderboards/boards/{boardId:guid}/cycles?limit=20`. Inherits the controller's
    `[Authorize(Roles = Admin,SuperAdmin)]`. Returns `LeaderboardCycleDto[]`:
    `{ cycleId, startsAtUtc, endsAtUtc, state, totalRanked }`.

    Placed immediately above the existing `POST .../cycles` so the read and the write to the same
    address sit together.

> `ApiReference.md` has no leaderboards section at all — the wire contract lives in the Unity repo's
> `Docs/LEADERBOARD_BACKEND_CONTRACT.md`. This route is specified here rather than opening a
> one-route section there. Worth reconciling when somebody documents the surface properly.

### 2.2 The page

14. **`pages/leaderboards.html`** (new) and **`js/leaderboards.js`** (new). Three sub-tabs, covering
    all eight operations plus the new read:

    **Boards** — `GET/POST/PUT .../boards`. Table of key, localized name, metric, period,
    aggregation + sort, cohorts, scope, cycle count, active. The authoring modal offers metric,
    period, aggregation, sort and cohort as **fixed lists rather than free text**, because every one
    of them is refused server-side when unrecognised and the failure mode of getting it wrong is not
    an error — it is a board that stays empty forever, which is indistinguishable from an unpopular
    one.

    On edit, the fields `UpdateBoardAsync` refuses to change — key, metric, aggregation, period,
    game, grade — are rendered **disabled**. They are still sent, because `Validate(request)` runs
    against the whole request either way, but the server ignores them. Greying them out is what
    stops an operator typing a new metric, saving successfully, and believing it took.

    There is no delete button because there is no delete route: a board's key is referenced by every
    settlement it has ever paid. Retirement is `IsActive = false`, and the footnote says so.

    **Cycles** — the new read, plus `POST .../cycles/{id}/rebuild`, `POST .../cycles/{id}/settle`
    and `POST .../boards/{id}/cycles`. Opens as a second card under the boards table for whichever
    board's clock icon was clicked. Windows render in **UTC and are labelled UTC** — a cycle
    boundary read in local time is a support ticket. State gets a colour per
    `LeaderboardCycleState`.

    "Add event window" is hidden unless the board's period is `Event`, because every other period
    has its window opened for it by `LeaderboardRolloverService.EnsureLiveWindowAsync`; offering the
    button elsewhere would invite a hand-authored window that collides with a derived one.

    Settle carries a confirm that says what it actually risks — running early can pay a child before
    a result still in flight has landed, which is the whole reason the scheduled job waits for the
    grace window. Rebuild's confirm says the opposite, because rebuild genuinely is safe on live
    data: entries are purely derived, and a rebuild producing different ranks would be a projector
    defect rather than a reason not to run it.

    **Bounds** — `GET .../bounds`, `PUT .../bounds`. Metric + game identify a bound, so both are
    disabled on edit and the footnote explains that saving an existing pair replaces rather than
    duplicates. The client repeats the server's "a bound has to limit at least one thing" refusal
    before the round trip, since a bound limiting nothing is a row that looks like protection and is
    not.

    **Review queue** — `GET .../flagged`, `POST .../flagged/{id}/resolve`. Players appear under
    their public handle only, and the footnote states why: nothing about judging whether a score is
    real requires knowing which child earned it. Both decisions are offered as explicit buttons
    rather than a single toggle, because "clear" and "uphold" are different judgements and neither
    is a default.

15. **`js/nav.js`** — added `Leaderboards` under the **Engagement** section, next to Objectives.
    Engagement rather than Commerce: a board ranks progress, and it never sells anything.

16. Games and grades are fetched **once** in `initLeaderboards`, not per render. They name a board's
    scope and a flagged result's origin; neither changes while somebody is filling in a form, and
    re-fetching per render would put a request behind every modal open.

### 2.3 What the page tells an operator up front

The banner states three things a newcomer would otherwise learn the hard way:

- Adding a board is an INSERT, not a release.
- **Nothing here accepts a score.** Ranking is projected from results the server graded; there is no
  route by which a client can state one, which is why a modified build cannot author its own rank.
- **Boards stay invisible to players until `Leaderboards:Enabled` is on** — every player-facing read
  answers 409 `LB_DISABLED` until then — and this console is deliberately not gated by that switch,
  so boards can be prepared before the feature opens.

That last point is the one most likely to cause a false bug report, because the Unity client's
Progress tab shows "Leaderboards are not available yet" for exactly this reason.

---

## 3. The missing `.sub-pane` rule

17. **`css/pages.css`** — added `.sub-pane { display: none }` / `.sub-pane.active { display: block }`.

This is a pre-existing defect, found while building the leaderboards page on the same pattern. The
markup has always written `class="sub-pane active"` and `curriculum.js` has always done
`querySelectorAll('.sub-pane').forEach(p => p.classList.remove('active'))` — but **no CSS rule for
`.sub-pane` existed anywhere**. `grep -rn "pane" css/` returns one unrelated comment.

So the curriculum page has been rendering its Tree, Questions and Recovery panes stacked on top of
each other, and clicking a sub-tab only moved the highlight. Adding the two-rule block makes the
tabs do what the markup and the JS both already claimed, on that page as well as the new one.

**This visibly changes the curriculum page** — from three panes at once to one at a time. That is
the behaviour it was written for, but it is a change somebody should see before it surprises them.

---

## 4. The two copies of the console

`Share7 front/` and `Share7/Share7/wwwroot/` are **byte-identical mirrors**, save for
`wwwroot/admin.html` — a redirect stub that only needs to exist where the server serves from. Both
are tracked. Every change above was written into `wwwroot/` and copied across, and
`diff -rq "Share7 front" Share7/Share7/wwwroot` now reports only `admin.html`.

Worth saying plainly: **this duplication is a hazard.** It is how `GameCatalogueBoundary.md` §4 item
12 came to describe a change that was not in either copy — with two places to edit and no build step
tying them together, "done" and "deployed" drift apart silently. A symlink, a copy task in the
`.csproj`, or deleting one of the two would all be better than the convention of remembering.

---

## 5. What was verified, and what was not

**Verified.**

- `dotnet build Share7.slnx` — **0 errors, 0 warnings**.
- `node --check` on all twelve console modules — every one parses, `leaderboards.js` included.
- Every `getElementById` in `leaderboards.js` (33 of them) resolves to an `id` in
  `leaderboards.html`, and every `onclick` handler named in that page is exposed on `window`.
  Checked by script, not by eye.
- `diff -rq` between the two console copies.

**Not verified.**

- **The backend test suite did not run.** All 58 leaderboard tests fail on
  `SqlException … error: 26 - Error Locating Server/Instance Specified` — this machine has no SQL
  Server instance. The failures are connectivity, not assertions; no test executed at all. Run
  `dotnet test Share7.Tests` against a real instance before deploying. Note the new method is a read
  with no test of its own.
- **The console was not exercised in a browser.** Neither page was opened, no board was created, no
  cycle was rebuilt or settled. The leaderboards page in particular has never had a single one of
  its requests made against a live server.
- **No live request was made against the deployed backend.**
- **The new route was not smoke-tested.** It compiles and is routed; nobody has called it.
