# MultiplayerPlan.md

# Multiplayer sessions — build plan

Response to the Unity dev's multiplayer backend spec (received 2026-08-18). This file is the
**plan**, not the design of record. When the work lands, the design moves into `Multiplayer.md`
alongside `CommerceDecisions.md` and `Progress.md`, and this file goes away.

## Status

- **2026-08-19 — Phases 1 and 2 landed.** Entities, EF configuration and the `MultiplayerSessions`
  migration; the application contracts and the twelve new error codes; `MultiplayerSessionService`
  covering create / start / join / leave / close / get / players / list; `MultiplayerController` at
  `/api/multiplayer`; DI and the `Multiplayer` config section. 37 new tests, whole suite green at
  251 passed. Two deviations from the spec are recorded in §5.
- **2026-08-19 — Phases 3 and 4 landed.** Heartbeat; `MultiplayerSweepService` plus the
  `MultiplayerSessionSweeper` hosted service; `MatchmakingService`; host transfer. Three more routes
  on the controller, DI wired with the sweeper as a hosted service. 39 further tests, whole suite
  green at 290 passed. One real config bug found and fixed — see §3.7.
- **2026-08-19 — Phase 5 landed.** `IMultiplayerAdminService` + `AdminMultiplayerController` at
  `/api/admin/multiplayer`, behind `Admin`/`SuperAdmin`. 11 more tests, suite green at 301 passed.
  Verified end to end: the app boots, the migration applies to `Shareh`, all 13 routes appear in
  Swagger, and the sweeper runs clean on its 30-second timer.
- **Phase 6 (docs) not started** — held at the user's request pending manual testing.
- The Unity dev answered every question in §4 on 2026-08-18 — answers folded in below.

All five answers were "yes, keep it simple": **no match-result endpoint** (Unity computes the
result client-side), exact-lesson matching is fine for v1, `StudentProfile.FullName` is the display
name, `AcceptedProtocolVersions: [1]` with coordinated bumps, and an empty array for a caller in no
session. §4.1 — the one open question that could have become an extra phase — is closed.

---

## 1. Verdict on the spec

**The spec is good and we should build it close to as written.** It is the first document from the
client side that treats the backend as the arbiter rather than a scoreboard, and the three
structural decisions in it are the right ones:

- Capacity enforced by **one conditional `UPDATE`**, never read-then-write (§7.3). This is the same
  shape `WalletService` already uses for balances — `SET Amount = Amount + @delta` guarded by a
  `WHERE` — so the codebase already has the muscle memory for it.
- **Filtered unique indexes as the structural defence** against double-join and duplicate rooms,
  rather than service-layer validation. `PurchaseTransactions` already relies on exactly this
  (`IX_PurchaseTransactions_UserId_RequestId`, filtered to `COMPLETED`), and the tests already run
  against real SQL Server precisely so those constraints are exercised.
- **No `userId` in any request body.** Identity from `ICurrentUserService.UserId` only. This is
  already how every authenticated endpoint in the API works.

The three assumptions it makes are checked in §2. The places it collides with existing repo
convention are in §3 — those are cosmetic but must be settled *before* the Unity dev writes the
mapping table, because each one is a client change if we settle it later. The real gaps are in §4.

---

## 2. The spec's three assumptions, checked against the code

### ASSUMPTION 1 — the error envelope is reusable — **TRUE, with a shape correction**

`ApiErrorExtensions.ToApiErrorResult` ([Share7/Extensions/ApiErrorExtensions.cs](Share7/Extensions/ApiErrorExtensions.cs))
emits:

```json
{ "code": "INSUFFICIENT_BALANCE", "messageKey": "commerce.insufficient_balance", "details": {} }
```

Two corrections to the spec's §11 table:

1. The field is **`code`**, not `errorCode`.
2. Every code is **`SCREAMING_SNAKE_CASE`** and is declared as an `ApiErrorCode(Code, MessageKey)`
   pair in [ApiErrors.cs](Share7.Application/Common/Models/ApiErrors.cs). A code without a
   `messageKey` cannot be added — the pairing is deliberate, so a new refusal can't ship with a
   code and no string for Unity to render.

So `session_not_found` becomes `SESSION_NOT_FOUND` + `multiplayer.session.not_found`. See §3.1.

Note also that `ApiErrors.All` is reflected over the static fields, so nothing extra is needed to
register new codes — declare them and they're discoverable.

### ASSUMPTION 2 — the JWT subject carries the Share7 `userId` — **TRUE**

`ICurrentUserService.UserId` is `Guid?`, populated from the token by
[CurrentUserService.cs](Share7/Services/CurrentUserService.cs) and injected into every service that
needs a caller. `GET /api/auth/me` exists. Nothing to build.

### ASSUMPTION 3 — no idempotency infrastructure exists — **TRUE, but there is a pattern to follow**

There is no generic idempotency table. What exists is a **per-domain** pattern: the domain row
itself carries `RequestId` under a unique index filtered to the *successful* state.

That filter was a bug fix, and it's the most important thing to carry across
([20260816181701_PurchaseIdempotencyOnCompletedOnly.cs](Share7.Infrastructure/Persistence/Migrations/20260816181701_PurchaseIdempotencyOnCompletedOnly.cs)):
the index was originally unique across *every* state, which meant a **refused** attempt permanently
burned its `requestId`. A student told "not enough coins" who topped up and tapped buy again got
their own earlier refusal replayed back forever.

The same trap is live here, and worse. `join` can legitimately be refused with `SESSION_FULL` and
then legitimately succeed thirty seconds later when someone leaves. If a `MultiplayerRequestLog`
row is written on refusals too, that child is locked out of the session permanently.

**Decision: build the log the spec describes, but only record terminal-success outcomes.** See
§3.2.

---

## 3. Collisions with existing convention — settle these first

Each of these is a one-line change now and a client-side breaking change later.

### 3.1 Error codes — adopt repo casing

| Spec | What we ship | `messageKey` |
|---|---|---|
| `session_not_found` | `SESSION_NOT_FOUND` | `multiplayer.session.not_found` |
| `session_full` | `SESSION_FULL` | `multiplayer.session.full` |
| `session_closed` | `SESSION_CLOSED` | `multiplayer.session.closed` |
| `already_in_session` | `ALREADY_IN_SESSION` | `multiplayer.session.already_in_session` |
| `not_session_member` | `NOT_SESSION_MEMBER` | `multiplayer.session.not_member` |
| `not_session_host` | `NOT_SESSION_HOST` | `multiplayer.session.not_host` |
| `session_invalid_transition` | `SESSION_INVALID_TRANSITION` | `multiplayer.session.invalid_transition` |
| `session_below_min_players` | `SESSION_BELOW_MIN_PLAYERS` | `multiplayer.session.below_min_players` |
| `transport_name_taken` | `TRANSPORT_NAME_TAKEN` | `multiplayer.session.transport_name_taken` |
| `host_still_active` | `HOST_STILL_ACTIVE` | `multiplayer.session.host_still_active` |
| `protocol_version_mismatch` | `PROTOCOL_VERSION_MISMATCH` | `multiplayer.protocol_version_mismatch` |
| `game_not_multiplayer` | `GAME_NOT_MULTIPLAYER` | `multiplayer.game.not_multiplayer` |

The client's `NetworkSessionErrorCode` mapping is otherwise unchanged — only the string on the left
of the switch moves. The `404 game_not_found` row reuses the existing generic 404, and `401`
already falls through to `AuthenticationExpired` client-side without a code.

**`ServiceErrorKind` already covers every status the spec needs** — `NotFound` → 404, `Conflict` →
409, `Forbidden` → 403, `Validation` → 400. No new kind. `Unprocessable` (422) is not used here.

### 3.2 Idempotency — scope the log to successes

Build `MultiplayerRequestLog` as specced — composite PK `(UserId, RequestId)`, `Operation`,
`SessionId`, `ResponseJson`, `StatusCode`, `CreatedAtUtc` — with two rules:

- **A row is written only when the operation succeeded** (2xx). A refusal writes nothing, so a
  retry after the blocking condition clears is evaluated fresh. This is the `COMPLETED`-filter
  lesson, applied up front rather than after a support ticket.
- **`requestId` is optional and server-generated when absent**, exactly as `PurchaseService` does
  (`srv_{guid:N}`). The Unity client marks it optional in every request DTO; a missing one must not
  be a 400.

`ResponseJson` is a departure from repo practice — commerce replays by *re-reading* the transaction
and re-rendering it, it doesn't store a body. Storing the body is the right call here anyway,
because `MatchmakeResponse.outcome` (`Joined` vs `Created`) is not recoverable from any row after
the fact. Keep it `nvarchar(max)` and let the 24h sweep bound the growth.

### 3.3 Enum storage — `EnumWire`, not raw PascalCase

The spec says store `State` as `'Created'`. Repo convention (`EnumWire.Converter<T>()`,
`HasMaxLength(32)`) stores `SCREAMING_SNAKE`: `TransactionState.Completed` is the string
`COMPLETED` in the column. The spec's reasoning — "it appears in logs and support tickets" — is
served identically either way, so **use `EnumWire`** for `State`, `Visibility`, `ClosedReason` and
player `Status`.

**Separately, decide the wire form.** `Program.cs` registers a plain `JsonStringEnumConverter`, so
a raw enum property on a DTO serialises as its member name — `"Created"`, which is what the spec's
DTOs show and what the client's `NetworkSessionState` parses. Commerce DTOs instead expose `string`
properties fed through `ToWire`, giving `"COMPLETED"`. **Recommendation: keep the wire form as
PascalCase `"Created"`** (i.e. plain enum properties on the multiplayer DTOs, no `ToWire`) because
the client is already written against it and the spec's state machine is verbatim the client's
`SessionStateMachine`. Storage and wire differ; that is fine and already true elsewhere. Confirm
this with the Unity dev in writing so it doesn't get "fixed" later.

### 3.4 Routes — spec is correct

`/api/multiplayer/...` and `/api/admin/multiplayer/...` match the newer explicit-lowercase
convention (`api/progress`, `api/commerce`, `api/time`). Do **not** use `[Route("api/[controller]")]`
here — that would produce `/api/Multiplayer`. Admin controllers use
`[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]`.

### 3.5 `RowVersion` — new to this codebase

No domain entity uses a concurrency token today (only Identity's `ConcurrencyStamp`). Adding
`byte[] RowVersion` with `.IsRowVersion()` is standard EF and correct for host-transfer arbitration,
but it means `DbUpdateConcurrencyException` becomes a thing services must handle. Keep it to the two
entities that need it and handle the exception explicitly — never let it bubble to a 500.

### 3.6 This supersedes the Redis matchmaking plan

`CLAUDE.md` §Stack and `Architecture.md` §Sessions & matchmaking both describe a **Redis** queue
keyed by `GameId + SubjectId`, plus Hangfire for timeout sweeps. Neither Redis nor Hangfire is
installed; neither is referenced from any csproj. The spec replaces both with plain SQL —
candidate selection with a retry loop (§7.9) and an `IHostedService` sweeper (§10).

**That is the better design at this scale and we should take it**, but it must be written down as a
supersession, not left as two contradictory documents. `Share7.Infrastructure` already carries
`FrameworkReference Include="Microsoft.AspNetCore.App"`, so `BackgroundService` needs **no new
package**.

### 3.7 `AcceptedProtocolVersions` must default to empty — found while building Phase 4

The options class originally initialised the list to `[1]`, matching the spec's intent. **The
configuration binder appends to a collection that already has items rather than replacing it**, so
`[1]` in the initialiser plus `[2]` in `appsettings.json` binds to `[1, 2]`.

Version 1 would therefore have survived every attempt to retire it. That is not a cosmetic bug: the
entire stated reason for making this configurable is that a rollout window should be an ops change
rather than a deploy — and *closing* a window is exactly as much an ops action as opening one. As
shipped, ops could widen the window and never narrow it.

Fixed by defaulting the bound property to empty and resolving the fallback in a computed
`EffectiveProtocolVersions`, which configuration can actually override. An explicitly empty list
still falls back to `[1]` rather than accepting nothing, because a server that seats no protocol
version refuses every match — a total outage dressed up as a config value. `MultiplayerCompositionTests`
pins the binding behaviour.

**Anything else in this codebase that binds a collection with a non-empty initialiser has the same
bug.** Worth a sweep at some point; nothing in the multiplayer domain does any more.

---

## 4. Gaps in the spec — need the Unity dev's answer

These do not block starting. They block finishing.

1. **Nothing records the match result.** The session reaches `Closed` and the backend has stored no
   score, no winner, no per-player answers. `CLAUDE.md` is explicit that the server is authoritative
   for final match results and must never trust a client-submitted outcome — but there is no
   endpoint here for one. The spec hints the client will post to `/api/progress/attempts`
   separately (that's why `CurriculumPathJson` is stored), but that endpoint is
   `{ gameId, lessonId, answers[] }` with no session id and no notion of an opponent. **Ask: does
   each client post its own attempt independently, and is a multiplayer match therefore just two
   unrelated single-player attempts as far as progress is concerned?** If yes, say so explicitly in
   the doc. If no, we need a results endpoint and it is a separate phase.

2. **Matchmaking ignores studied-chapter overlap.** `Architecture.md` says matchmaking computes the
   intersection of chapters both players have studied and picks questions from it. The spec makes
   `curriculumPath` an opaque client-validated blob and filters only on exact `lessonId`. The spec's
   version is far simpler and probably right for launch — but it is a product decision, not a
   technical one. **Ask before building: is exact-lesson matching acceptable for v1?**

3. **`displayName` source is unstated.** Best available is `StudentProfile.FullName`, falling back
   to `ApplicationUser.UserName`. Note that `StudentProfile` may not exist for an account that
   hasn't completed its profile. Confirm the fallback is acceptable.

4. **`ProtocolVersion` has no initial value or owner.** Ship `AcceptedProtocolVersions: [1]` in
   config and agree that bumping it is a coordinated release, not a unilateral client change.

5. **The spec does not say what `GET /api/multiplayer/sessions` returns for a user in no session.**
   Empty array, 200. Assumed unless told otherwise.

---

## 5. Build plan

Six phases. Phases 1–3 are the minimum that produces a working two-player match; 4–6 make it
survivable in production. Each phase ends green — build passes and its tests pass — before the next
starts.

### Phase 1 — domain and persistence

New folder `Share7.Domain/Multiplayer/`:

| File | Contents |
|---|---|
| `MultiplayerSession.cs` | Entity per spec §2.1, `RowVersion` included |
| `MultiplayerSessionPlayer.cs` | Entity per spec §2.2 |
| `MultiplayerRequestLog.cs` | Entity per spec §2.3, success-only semantics documented on the type |
| `MultiplayerSessionState.cs` | `Creating, Created, Starting, Running, Ending, Closing, Closed, Failed, Abandoned` |
| `SessionVisibility.cs` | `Public, Private` |
| `SessionPlayerStatus.cs` | `Joined, Connected, Disconnected, Left, Removed` |
| `SessionClosedReason.cs` | `HostClosed, Abandoned, Empty, CreationFailed, AdminClosed` |

Plus:

- `Share7.Infrastructure/Persistence/Configurations/MultiplayerConfigurations.cs` — one file for all
  three entities, matching how `CommerceConfigurations.cs` groups a domain. All four filtered unique
  indexes and both covering indexes go here via `.HasFilter(...)`.
- Four `DbSet`s on `ApplicationDbContext`.
- One migration: `MultiplayerSessions`.

**Watch:** EF Core will not generate `WHERE State NOT IN (...)` from a LINQ filter — write the
filter string by hand in `.HasFilter()`, and write it against the **stored** form
(`'CLOSED','ABANDONED','FAILED'` under `EnumWire`, per §3.3). Getting this wrong silently disables
the entire concurrency defence, which is why test #2 and #18 exist.

**Two deviations from the spec, both found while building:**

1. **`LessonId` is its own column**, alongside the opaque `CurriculumPathJson` blob. Matchmaking
   filters on exactly one field of the curriculum path, and a filter that has to parse JSON cannot
   use an index — so the blob stays opaque and echoes back verbatim, and the one value the candidate
   query needs is lifted out and added to `IX_MultiplayerSession_Matchmaking`. Nothing else about the
   path is interpreted.
2. **Membership FK to `AspNetUsers` is `NoAction`, not `Cascade`.** A deleted account cascades to the
   sessions it hosted and those cascade to their memberships; a second cascade path straight into
   `MultiplayerSessionPlayers` is something SQL Server refuses outright at migration time. So
   memberships in *other people's* sessions are removed through `UserOwnedData.ManuallyPurged`
   instead. `AccountDeletionCoverageTests` enforces this and passes.

### Phase 2 — session lifecycle

`Share7.Application/Multiplayer/`:

- `Interfaces/IMultiplayerSessionService.cs`
- `Models/MultiplayerDtos.cs` — `MultiplayerSessionDto`, `MultiplayerSessionPlayerDto`,
  `CurriculumPathDto`
- `Models/MultiplayerRequests.cs` — the six request DTOs, `[MaxLength(128)]` on every `RequestId`
  exactly as `SubmitAttemptRequest` does
- `Models/MultiplayerOptions.cs` — `const string SectionName = "Multiplayer"`, mirroring
  `EquipmentOptions`; every value from spec §5 plus `AcceptedProtocolVersions` and
  `SweepIntervalSeconds`

`Share7.Infrastructure/Multiplayer/`:

- `MultiplayerSessionService.cs` — create, start, join, leave, close, get, players, list
- `MultiplayerMappings.cs` — entity → DTO, incl. `serverTimeUtc` stamped on every response
- `MultiplayerRequestLogStore.cs` — `TryReplayAsync` / `RecordAsync`, one place, used by all services

`Share7/Controllers/MultiplayerController.cs` — thin, `[Authorize]`, returns
`result.ToApiErrorResult()` on failure exactly like `CommerceController`.

Register everything in `Share7.Infrastructure/DependencyInjection.cs`
(`services.Configure<MultiplayerOptions>(...)` + three `AddScoped`).

**The state machine lives in one place** — a private `CanTransition(from, to)` table in the service,
not scattered `if` checks across seven methods. Every transition is a conditional `UPDATE` guarded
on the expected `State`; zero rows affected means re-read and re-evaluate, never overwrite.

### Phase 3 — heartbeat and the sweeper

- `HeartbeatRequest` / `HeartbeatResponse` DTOs.
- Heartbeat handler: `LastHeartbeatAtUtc = SYSUTCDATETIME()` server-side only. A `userId` in
  `connectedUserIds` that is not already a member is **ignored and logged**, never inserted — the
  heartbeat asserts presence, not membership.
- `IMultiplayerSweepService` (scoped) holding all five sweep rules from spec §10, `TOP (200)` per
  pass, idempotent.
- `MultiplayerSessionSweeper : BackgroundService` — a thin timer that opens a scope and calls the
  service.

**Split it that way deliberately:** the sweep rules must be unit-testable against
`SqlServerFixture` without spinning up a host. Tests #11 and #12 call the scoped service directly.

### Phase 4 — matchmaking and host transfer

- `IMatchmakingService` + `MatchmakingService` — candidate query per spec §7.9, then the retry loop
  against the Phase 2 atomic join. **Reuse the join path; do not write a second one.** A candidate
  that fills between selection and join fails its `UPDATE` and the loop moves on.
- Host transfer on `MultiplayerSessionService`, `RowVersion`-guarded, catching
  `DbUpdateConcurrencyException` → re-read → return the winner's state.
- Host reassignment on host-leave (lowest-slot `Connected` member, else close as `Empty`) — this is
  Phase 2 code but its test belongs with the transfer tests.

### Phase 5 — admin

`Share7/Controllers/AdminMultiplayerController.cs` + `IMultiplayerAdminService`. Three routes,
read-mostly. Deliberately last: it is operator tooling, and nothing about it is on the client's
critical path.

### Phase 6 — documentation and handoff

- **`Multiplayer.md`** (new) — the design of record, in the style of `CommerceDecisions.md`:
  entities, state machine, the concurrency argument, and every decision from §3 with its reasoning.
- **`ApiReference.md`** — new §, one heading per route with request shape and error codes, matching
  the existing format. `CLAUDE.md` calls this out as the file to keep in sync when endpoints change.
- **`ResponseSchemas.md`** — captured from a running server, **not hand-edited** (the file says so).
- **`CLAUDE.md`** — status entry at the top of §Current status; add multiplayer to §Core
  architectural decisions; correct §Stack to drop Redis/Hangfire for this domain.
- **`Architecture.md`** — mark §Sessions & matchmaking superseded, same way the content-hierarchy
  section is already marked.
- **`UnityIntegration.md`** — the client-facing contract: error-code table (§3.1), the wire-form
  decision (§3.3), heartbeat cadence, and the `requestId` rule ("one id per operation, reused for
  every retry of that operation").

---

## 6. Test plan

`Share7.Tests` is xunit over a **real SQL Server** database
([SqlServerFixture.cs](Share7.Tests/Infrastructure/SqlServerFixture.cs)) chosen precisely because
in-memory providers fake away unique constraints and transactions. Every one of the spec's 18 tests
is therefore runnable here as written. Server override: `SHARE7_TEST_SQL_SERVER`.

`CreateContext()` hands out a fresh context per call specifically so concurrency tests get genuinely
separate connections — the racing tests (#2, #13, #16) must use it, not a shared context.

| File | Spec tests | Status |
|---|---|---|
| `MultiplayerSessionServiceTests.cs` | 1, 2, 3, 4, 5, 6, 7, 8, 9, 17, 18 | **written, 27 passing** |
| `MultiplayerIndexTests.cs` | 21 below | **written, 5 passing** |
| `MultiplayerSweeperTests.cs` | 11, 12 | **written, 8 passing** |
| `MatchmakingServiceTests.cs` | 15, 16 | **written, 11 passing** |
| `MultiplayerHostTransferTests.cs` | 13, 14 | **written, 9 passing** |
| `MultiplayerHeartbeatTests.cs` | — | **written, 7 passing** |
| `MultiplayerCompositionTests.cs` | — | **written, 4 passing** |

`MultiplayerCompositionTests` is not in the spec and guards a hazard the service tests cannot see:
the sweeper is a `BackgroundService` and therefore a **singleton**, so taking a scoped dependency
would give it one DbContext for the life of the process. It resolves an `IServiceScopeFactory` and
opens a scope per pass instead; the test resolves it from the root provider with scope validation on,
which turns that mistake into a failure at build time rather than a slow leak in production.

Test 2 (the last-seat race) lives with the lifecycle tests rather than in a separate concurrency
file — it needs the same fixtures, and splitting it off only made it easier to forget to run.

**Test 10 (a `userId` in the body is ignored) cannot be written and should not be.** Every service
method takes the caller's id as its first parameter and no request DTO has a user id field, so there
is no body to ignore. The property the test was meant to prove is held by the shape of the code
instead of by an assertion.

Three the spec does not list, each covering a trap this codebase has already hit once. **All three
are written and passing:**

19. **A refused join does not burn its `requestId`.** Join a full session (`SESSION_FULL`), free a
    seat, retry with the *same* `requestId` — must succeed. This is the commerce bug (§2,
    ASSUMPTION 3) in its multiplayer form.
20. **Absent `requestId` is not an error.** Every mutating route, `requestId` omitted → 2xx.
21. **A filtered unique index actually exists and bites.** Insert directly, bypassing the service,
    and assert the SQL violation. Guards against §3.1's `.HasFilter()` string silently not matching
    the stored enum form — under which every service-level test still passes.

A fourth was added after the tests caught a real bug. **Closing a session marks every membership
departed**, so a membership check written as "does the caller currently hold a seat" locked the host
out of the session they had just closed: the second, idempotent close answered 404 instead of 200,
and no client could read back the final state of a match it had just played. Read authorization now
means "has the caller ever held a seat here", which leaks nothing — a row only exists for someone
genuinely in the session — and a stranger still gets the same 404. Covered by
`A_member_can_still_read_a_session_after_it_has_closed`.

`Share7.Tests` currently references only `Share7.Infrastructure`, so all of these are service-level.
No route-level test host exists and this plan does not add one; authorization (`[Authorize]`,
roles) stays covered by review rather than test, as it is everywhere else in the repo.

---

## 7. Sequencing and what to send back today

**Send to the Unity dev now**, before Phase 1 starts — all four are cheap now and expensive later:

1. §3.1 error-code casing + the `code` field name (their mapping table changes).
2. §3.3 wire form stays `"Created"`, storage is `CREATED`. Get it acknowledged in writing.
3. §3.2 `requestId` is optional, and a refusal never consumes one.
4. The four questions in §4 — especially **§4.1, match results**, which is the only one that can
   turn into a whole extra phase.

**Start immediately** regardless of their reply: Phase 1 is unaffected by every open question above,
and Phase 2's shape is settled by §3 alone.

**Rough shape:** Phases 1–3 are the working match. Phase 4 is what makes it findable. Phases 5–6
are what make it supportable. If something has to be cut for a first playable, cut Phase 5 — never
Phase 3, because without the sweeper a crashed host leaves a session that nobody can join and
nothing can clean up.
