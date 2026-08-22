# ApiReference.md

# Share7 — Complete API Reference

Every endpoint the backend exposes: what it is for, what it needs, what it returns.

Companion to `UnityIntegration.md`, which explains the *flows* (token lifecycle, external login,
`JsonUtility` gotchas), and to `@ResponseSchemas.md`, which documents every **response body**
field by field with ready-to-paste C# classes. This file is the flat catalogue — when any of
them disagree, trust the code.

**Contents**

1. [Conventions](#1-conventions) · 2. [Auth](#2-auth) · 3. [Account & language](#3-account--language)
4. [Browsing the curriculum](#4-browsing-the-curriculum) · 5. [Questions](#5-questions-the-cache-protocol)
6. [Games](#6-games) · 7. [Progress](#7-progress) · 8. [Admin](#8-admin)
9. [Economy and rewards](#9-economy-and-rewards) · 10. [Products and entitlements](#10-products-and-entitlements)
11. [Account deletion](#11-account-deletion) · 12. [Typical client flows](#12-typical-client-flows)
13. [Error envelope](#13-error-envelope-commerce-and-account-endpoints-only) ·
[Not built yet](#not-built-yet)

---

## 0. Admin console

A browser page for content administration ships with the API itself at **`/admin.html`** —
sign in, walk the curriculum tree, add terms/subjects/chapters/lessons, upload question sheets,
and manage currencies. Bootstrap + vanilla JS, no build step.

It is served from `Share7/wwwroot/`, so it deploys with the normal Visual Studio publish and needs
no separate hosting. Being same-origin, its `fetch` calls carry the JWT with no CORS configuration.
`auth-test.html` in the same folder is the older auth-only console.

Grades are read-only there: the 14-step ladder is seeded by migration and has no create endpoint.

---

## 1. Conventions

**Base URL** — `https://localhost:7147` (or `http://localhost:5215`) while developing; the
MonsterASP.NET domain once deployed. Only the base URL changes.

**Auth header** — `Authorization: Bearer {accessToken}` on everything except the handful marked
*anonymous*. Access tokens last 30 minutes; refresh tokens 7 days and rotate on every use.

**Roles** — `Student`, `Teacher`, `Admin`, `SuperAdmin`. Endpoints under `/api/admin/` require
`Admin` or `SuperAdmin`; everything else needs only a signed-in user.

**Content language** — an account's language rides in the access token as a `preferred_language`
claim, so content endpoints are already language-scoped without you passing anything. Changing it
re-issues tokens (see §3).

**A newly registered account has no language** and falls back to English — registration does not
currently accept one. See the warning under `POST /api/auth/register`.

**Ids are language-independent.** A lesson has the *same* id for an Arabic and an English
student — only its `name` and its question set differ. Cache questions keyed by
`(lessonId, language)`, never by `lessonId` alone.

**Two error shapes.** Most failures return an `errors` **array**:

```json
{ "errors": ["Invalid username or password."] }
```

Model-validation failures (a missing required field) return the framework's shape, where
`errors` is an **object** keyed by field name:

```json
{ "errors": { "Age": ["The field Age must be between 3 and 100."] } }
```

Handle both — check whether `errors` is an array or an object.

**Rate limits.** Three throttles, all returning `429` with the error envelope from §13
(`RATE_LIMITED`) plus a `Retry-After` header and a matching `retryAfterSeconds` in `details`:

| Scope | Limit | Partitioned by |
|---|---|---|
| Everything | 240 / minute | user, or address when anonymous |
| `POST /api/auth/{register,login,external-login,refresh,revoke}` | 20 / minute | address |
| State-changing routes — attempts, purchase, grants, session create/join/matchmaking | 60 / minute | user |

Multiplayer **heartbeat is deliberately exempt** from the write limit: it runs on a fixed 15-second
cadence, so it is the one route a per-minute cap could bite during normal play.

None of these should be reachable by ordinary use — a client that sees `429` has a retry loop, not
a busy player. **Back off for `retryAfterSeconds` rather than retrying immediately**; a tight retry
just spends the next window too. Limits are configuration (`RateLimiting` in `appsettings.json`),
so treat the numbers here as current rather than contractual.

**Status codes used throughout:** `400` bad input · `401` missing/expired token · `403` wrong
role, or content locked · `404` unknown id · `409` a conflict that needs confirmation or a state
fix · `429` rate limited, back off and retry.

---

## 2. Auth

### `POST /api/auth/register` — anonymous

Creates an account.

```json
{ "username": "student_ahmed", "password": "Passw0rd" }
```

- `username` — 3–256 chars, unique
- `password` — 8+ chars, needs upper + lower + digit

Returns the standard auth payload (below).

> ⚠ **Registration does not set a content language, despite what this file used to say.** The
> `languageId` field and its validation are **commented out** in `AuthService.RegisterAsync`, so a
> new account is created with `PreferredLanguageId = null`, the access token carries no
> `preferred_language` claim, and every content endpoint silently falls back to **English**.
>
> Until that is re-enabled, an Arabic student must call
> `POST /api/users/me/preferred-language` immediately after registering — and **replace both
> tokens** from its response. Sending `languageId` to `register` is accepted and ignored.
>
> Verified 2026-08-14 against the running API: registering with only username and password
> succeeds.

### `POST /api/auth/login` — anonymous

```json
{ "username": "student_ahmed", "password": "Passw0rd" }
```

`401` on bad credentials.

### `POST /api/auth/external-login` — anonymous

```json
{ "provider": "Google", "token": "…" }
```

- `provider` — the literal string `"Google"` or `"Facebook"`
- `token` — Google: the **ID token** (a JWT) from the native Sign-In SDK. Facebook:
  `AccessToken.TokenString` from the Facebook SDK.

If a password account already exists with the same email, the accounts link automatically —
same `userId`, no duplicate.

### `POST /api/auth/refresh` — anonymous

```json
{ "refreshToken": "…" }
```

Rotates: the old refresh token dies the instant this succeeds. **Store both new tokens.** A
`401` means the refresh token is dead — clear storage and show the login screen.

### `POST /api/auth/revoke` — anonymous

```json
{ "refreshToken": "…" }
```

Call on logout. `204 No Content` on success, `404` if the token was already unknown.

### `POST /api/auth/complete-profile` — authenticated

Required once per account before the game should be considered playable.

```json
{
  "fullName": "Sara Ahmed",
  "age": 10,
  "phoneNumber": "+201234567890",
  "email": "sara@example.com",
  "gradeId": "…"
}
```

Everything is required except `email`. `age` 3–100. `gradeId` from `GET /api/grades`. Calling it
again **updates** the profile, so reuse it for an "edit profile" screen.

Returns `{ "succeeded": true, "errors": [] }` — tokens do not change.

### `GET /api/auth/me` — authenticated

Dumps your own JWT claims. Debugging only.

### The auth payload

Returned by register / login / external-login / refresh:

```json
{
  "succeeded": true, "errors": [],
  "userId": "…", "username": "student_ahmed", "email": null,
  "roles": ["Student"], "isProfileComplete": false,
  "accessToken": "eyJ…", "accessTokenExpiresAt": "2026-08-02T17:00:41Z",
  "refreshToken": "JOAxi…", "refreshTokenExpiresAt": "2026-08-09T16:30:41Z"
}
```

`isProfileComplete` is the field to branch on right after auth. `email` is null until the account
has one.

---

## 3. Account & language

### `GET /api/languages` — anonymous

Feeds the language picker on the registration screen.

```json
[{ "id": "…", "name": "English", "code": "en" }, { "id": "…", "name": "العربية", "code": "ar" }]
```

### `GET /api/users/me/preferred-language` — authenticated

`{ "languageId": "…" }` — the caller's current content language.

### `POST /api/users/me/preferred-language` — authenticated

```json
{ "languageId": "…" }
```

**Returns a fresh token pair**, same shape as login. The language is cached in the access-token
claim, so your existing token would keep serving the old language until it expired — you must
replace both stored tokens with the ones in this response.

Progress and unlocks are **not** affected: the tree is shared, so the player keeps everything.
Their cached questions are language-specific though, and must be re-fetched.

`400` for an unknown language id.

### `GET /api/users/me/equipment` — authenticated

The caller's saved avatar outfit. **Always `200`. Never `404`.**

```json
{
  "bodyType": "Male",
  "equipped": [{ "slotKey": "head", "cosmeticKey": "hat_wizard" }],
  "colors":   [{ "cosmeticKey": "jacketbomber", "colorKey": "crimson" }],
  "updatedAtUtc": "2026-08-17T10:00:00Z"
}
```

⚠ **`updatedAtUtc` is the field this endpoint turns on.** It is `null` **exactly when the player
has never saved**, and non-null on every stored outfit. That is the only thing distinguishing:

| Response | Meaning | What the client must do |
| --- | --- | --- |
| `equipped: []`, `colors: []`, `updatedAtUtc: null` | never dressed | upload whatever the device is wearing |
| `equipped: []`, `colors: []`, `updatedAtUtc: "…"` | deliberately wearing nothing | undress the avatar |

The two bodies are otherwise byte-identical. Treating a null timestamp as "wearing nothing" strips
every player who has not yet saved, on their first launch.

`equipped[]` and `colors[]` are **computed from the stored rows, not stored in that shape** —
storage is one row per equipped item carrying slot, cosmetic and colour together. Every
`cosmeticKey` in `colors[]` therefore also appears in `equipped[]`; an item worn with no colour
chosen is simply absent from `colors[]`.

`bodyType` is `"Male"` or `"Female"`, defaulting to `"Male"`.

### `PUT /api/users/me/equipment` — authenticated

⚠ **The request shape is not the response shape.** The colour is nested inside each equipped
entry; there is no separate `colors[]` on the way in.

```json
{
  "bodyType": "Female",
  "equipped": [
    { "slotKey": "Body", "cosmeticKey": "Armor_gold", "colorKey": "Hexa" },
    { "slotKey": "Head", "cosmeticKey": "Dia_Hat",    "colorKey": "Bronze" }
  ]
}
```

That is deliberate. A flat `colors[]` keyed by `cosmeticKey` let a client colour a cosmetic it was
not wearing — equipping `Armor_gold` while colouring `"Test"` was accepted and stored, describing
an outfit that does not exist. Nesting makes it unrepresentable rather than merely discouraged.

`colorKey` is **optional** — a cosmetic can be worn with no colour picked. The response is the
regular two-list shape shown above, with the colours split back out.

The user is taken from the **token**; any `userId` in the body is ignored. Any `updatedAtUtc` sent
is ignored too — the server stamps it.

**A save replaces the whole outfit.** An omitted or empty array means "wearing nothing", never
"leave what is stored alone" — otherwise undressing would be impossible to express.

Each `(account, slotKey)` pair is one row, so re-equipping a slot updates the row already holding
it rather than adding a second. An account can never hold the same `slotKey` twice — the database
enforces it, not just the service.

**Unequipping an item discards its colour.** Colour lives on the item row, so it does not survive
the item being taken off; re-equipping later starts with no colour.

**Cosmetic keys are not validated against a catalogue.** There is no backend cosmetic catalogue by
decision — cosmetics are Unity assets — so unknown keys are stored and handed back verbatim. That
is what lets content ship ahead of a backend deploy.

They are still bounded, which is this endpoint's only abuse control. `422` with
`{ code, messageKey, details }` when:

| Rule | Limit |
| --- | --- |
| `equipped` entries | ≤ 32 (also the row count one account can hold) |
| any key length | ≤ 64 characters |
| any key charset | `^[A-Za-z0-9._-]+$` |
| `slotKey` within `equipped` | unique, compared case-insensitively |
| `bodyType` | `Male` / `Female` (case-insensitive), or absent |

There is no separate colour cap: colours are nested inside the equipped entries, so there can
never be more of them than there are items.

```json
{ "code": "EQUIPMENT_INVALID", "messageKey": "equipment.invalid",
  "details": { "field": "equipped[].slotKey", "value": "head" } }
```

A rejected save changes nothing — the previously stored outfit is left exactly as it was.

⚠ **Ownership is not currently enforced.** The check is built (it resolves
`Entitlement → Product → ProductGrant.Reference`, which is the client-side cosmetic id) and
returns `EQUIPMENT_NOT_OWNED`, but it is **off** behind `Equipment:EnforceOwnership` in
`appsettings.json`. Nothing in the schema records a cosmetic owned *without* an entitlement —
starter outfits, defaults — so switching it on today would refuse every player wearing a default
and leave them unable to save at all. Turn it on once defaults are granted as real entitlements or
a free-cosmetic allowlist exists. Colours are never ownership-checked: a colour is a property of a
cosmetic rather than a separately owned thing.

---

## 4. Browsing the curriculum

Hierarchy: **Grade → Term → Subject → Chapter → Lesson → Question → Choice**.

All of these resolve names into the caller's language. They do not filter rows by language —
there is one shared tree. A node with no translation in that language returns an **empty
`name`** rather than disappearing, so a content gap is visible instead of silently truncating
the tree. Results are ordered by `order`, never by name.

### `GET /api/grades?langId={guid}` — anonymous

`langId` is optional; with a bearer token the caller's own language is used, and it defaults to
English otherwise. The parameter exists so an anonymous registration screen can pick explicitly.

```json
[{ "id": "…", "name": "Primary One", "langId": "…", "order": 3 }]
```

14 grades: `order` 1–2 = KG1/KG2, 3–8 = Primary One–Six, 9–11 = Preparatory One–Three,
12–14 = Secondary One–Three. **Sort by `order`** — sorting by name puts "Grade 10" before
"Grade 2".

### `GET /api/terms?gradeId={guid}` — authenticated

`gradeId` optional. Without it, every grade's terms come back (so "First Term" repeats once per
grade), ordered by grade then term.

```json
[{ "id": "…", "name": "First Term", "langId": "…", "gradeId": "…", "order": 1 }]
```

### `GET /api/subjects?termId={guid}` — authenticated

`termId` optional, same caveat as above.

```json
[{ "id": "…", "name": "Science", "langId": "…", "termId": "…", "order": 1 }]
```

### `GET /api/chapters?subjectId={guid}` — authenticated

`subjectId` **required** — `400` without it.

```json
[{ "id": "…", "name": "Matter", "langId": "…", "subjectId": "…", "order": 1 }]
```

### `GET /api/lessons?chapterId={guid}` — authenticated

`chapterId` **required**.

```json
[{
  "id": "…", "name": "States of Matter", "langId": "…", "chapterId": "…", "order": 1,
  "questionsVersion": 2, "hasQuestions": true
}]
```

- `questionsVersion` — for **the caller's language**. Lets you validate a whole chapter's cache
  from this one response, no separate version call needed.
- `hasQuestions` — false when no sheet has been uploaded in that language. The lesson is real and
  named but unplayable: grey it out, do not open an empty session. It does not block progression.

---

## 5. Questions (the cache protocol)

The client caches question sets on device and grades offline. The flow:

1. On game open, ask for the version(s) you hold.
2. Same version → play from cache, no further calls.
3. Different (or nothing cached) → download the set and replace the cache.

### `GET /api/lessons/{lessonId}/questions/version` — authenticated

```json
{ "lessonId": "…", "langId": "…", "version": 2, "questionCount": 40 }
```

`version: 0` means nothing uploaded **in that language** — the same lesson may be at version 3 in
the other one.

### `POST /api/lessons/questions/versions` — authenticated

Batch form — the practical one for validating a whole cache in one round trip.

```json
{ "lessonIds": ["…", "…"] }
```

Returns an array of the object above. Unknown ids are omitted rather than erroring.

### `GET /api/lessons/{lessonId}/questions` — authenticated

```json
{
  "lessonId": "…", "langId": "…", "version": 2,
  "questions": [{
    "questionId": "…",
    "text": "What is water at 100C?",
    "correctAnswerId": "b2…",
    "answers": [
      { "id": "b2…", "text": "Steam" },
      { "id": "c3…", "text": "Ice" },
      { "id": "d4…", "text": "Rock" }
    ]
  }]
}
```

Exactly 3 answers per question, in source-sheet order — **shuffle before assigning them to
lanes/doors**, or the correct answer is always the first door.

`correctAnswerId` is deliberately included so you can grade offline. It is the only place the
backend reveals the answer, and it does not make the client authoritative: anything that writes
progress is re-graded server-side.

An empty `questions` array with `version: 0` is the "not available in this language yet" case.

### Recovery questions — the same three calls, over the secondary pool

`recoveryQuestions` is a second, independent question pool per lesson. Every route above exists
again with `recovery-questions` in place of `questions`, with **identical request and response
shapes**:

| Main pool | Recovery pool |
| --- | --- |
| `GET /api/lessons/{lessonId}/questions/version` | `GET /api/lessons/{lessonId}/recovery-questions/version` |
| `POST /api/lessons/questions/versions` | `POST /api/lessons/recovery-questions/versions` |
| `GET /api/lessons/{lessonId}/questions` | `GET /api/lessons/{lessonId}/recovery-questions` |

Same cache protocol, same `correctAnswerId`, same language scoping, same `version: 0` meaning.
The client can deserialise a recovery response with the model it already uses for questions.

**The two versions are independent** — a lesson can be on recovery version 3 while its main
questions are still on version 1, or have recovery questions and no main questions. A client
caching both holds two version numbers per lesson and re-downloads only the pool that moved.

⚠ **Trigger logic is still undefined** — the backend stores and serves the pool, but nothing says
*when* the game should show a recovery question. That remains a content-team decision; do not
infer it from the fact that the endpoints exist.

---

## 6. Games

### `GET /api/games` — authenticated

Active games only, names in the caller's language.

```json
[{
  "gameId": "…", "gameKey": "subway_runner",
  "displayName": "Subway Runner", "description": "Steer into the right door.", "langId": "…",
  "lobbyScene": 1, "gameplayScene": 2,
  "minPlayers": 1, "maxPlayers": 2, "readyTimeoutSeconds": 20,
  "supportsSinglePlayer": true, "supportsMultiplayer": true,
  "useLobby": true, "useMatchmaking": true, "isActive": true
}]
```

Field names mirror `MiniGameDefinitionSO` so this deserializes straight into your existing model.
`gameId` is a GUID sent as a string.

**The server is authoritative for these values.** If the ScriptableObject disagrees about player
counts or timeouts, the server's numbers are what matchmaking will enforce. `lobbyScene` and
`gameplayScene` are stored here but are client build artifacts — renumbering scenes in a rebuild
will silently desync them.

### `GET /api/games/{gameId}` — authenticated

One game, **including disabled ones**, so a client holding a stale id learns it was retired
rather than getting a 404. Check `isActive`.

---

## 7. Progress

Progress is tracked **independently per game** — every route carries a `gameId`. Acing a lesson
in the runner game says nothing about the same lesson in another game. Everything is scoped to
the caller; there is no way to read another player's progress.

### `POST /api/progress/attempts` — authenticated

**The main one.** Call it when a lesson run finishes.

```json
{
  "gameId": "…",
  "lessonId": "…",
  "requestId": "run-7f3a",
  "answers": [
    { "questionId": "…", "choiceId": "…" },
    { "questionId": "…", "choiceId": null }
  ]
}
```

| Field | Meaning |
|---|---|
| `gameId` | which game this run was in |
| `lessonId` | the lesson that was played |
| `answers[].questionId` | the question that was shown |
| `answers[].choiceId` | **the choice the player picked** — right or wrong. `null` for shown-but-skipped. |
| `requestId` | **optional.** Your id for *this run*. Generate one per run and reuse it on every retry of that run, exactly like a purchase `requestId`. Max 128 chars. |

**Send what was picked; the server decides what was right.** Grading happens server-side against
each question's own correct answer, so there is no field in which a client can assert a score. One
entry per question, and **a question may appear only once** — a duplicate is refused with `400`.

`requestId` only affects **rewards**, and only rules that pay on every attempt. Without it a
resubmitted run after a lost response is indistinguishable from genuinely replaying the lesson, and
is paid twice. Progress is unaffected either way — the score is recomputed and overwritten, never
accumulated. Omitting it is supported.

**Anything not sent counts as wrong**, whether the player answered it wrongly or never reached it.
This relies on a run showing *every* question in the lesson. If that ever changes, tell the backend
— the denominator breaks.

A `choiceId` that does not belong to its question, or a `questionId` outside this lesson and
language, is graded wrong rather than rejected — losing a real score over one bad id would be worse.
They are counted in `unrecognisedAnswers`, which is the fingerprint of a **stale cached question
set**: compare `questionsVersion` and re-fetch.

⚠ **Breaking change (2026-08-16).** This replaced `correctChoiceIds` + `correctCount`, and
`clientReportedCorrectCount` / `clientCountMatched` are gone from the response. A client still
sending the old shape submits an empty `answers` array and scores zero.

```json
{
  "gameId": "…", "lessonId": "…", "langId": "…",
  "correctCount": 2, "totalCount": 4, "percent": 50,
  "attempts": 2, "completionState": "Completed",
  "firstAttemptWasPerfect": false, "questionsVersion": 1,
  "answers": [
    { "questionId": "…", "choiceId": "…", "correctChoiceId": "…", "isCorrect": true },
    { "questionId": "…", "choiceId": null, "correctChoiceId": "…", "isCorrect": false }
  ],
  "unrecognisedAnswers": 0,
  "unlocked": [{ "nodeType": "Lesson", "nodeId": "…" }],
  "rewards": [
    {
      "ruleId": "…", "ruleName": "Lesson passed",
      "eventType": "LESSON_COMPLETED", "transactionId": "…",
      "grants": [ { "currency": "coins", "amount": 10 }, { "currency": "gems", "amount": 2 } ]
    }
  ],
  "balances": [ { "currency": "coins", "amount": 1260 }, { "currency": "gems", "amount": 2 } ]
}
```

`unlocked` lists whatever this attempt just opened — play the unlock animation from it, no second
call needed. `nodeType` is `Lesson`, `Chapter`, `Subject` or `Term`.

`rewards` is what this attempt **earned** — see §9. One entry per reward rule that fired; `[]` when
nothing matched, when a once-only rule has already paid, or when a cooldown or daily limit is in
force. A perfect run can fire three rules at once (attempted, completed, aced), so treat it as a
list, not a single object.

`balances` is the player's **absolute authoritative balance** after the attempt, already including
anything `rewards` just paid.

> **`grants[].amount` is a delta to animate. `balances[].amount` is a total to assign.**
> Never add `grants` to the local wallet — that double-counts. Assign `balances` over whatever the
> client is holding, on every attempt, including ones that earned nothing. This is why the attempt
> response carries balances at all: the wallet reconciles here and needs no follow-up call to
> `GET /api/commerce/balances`.

- `403` — the lesson is still locked in this game.
- `409` — the game is disabled, or the lesson has no questions in the player's language.
- `404` — unknown game or lesson.

Single-player only. Multiplayer result recording is not built.

### Scoring and unlock rules

| State | Rule (on the **last** attempt) |
|---|---|
| `Uncompleted` | never played, or below 50% |
| `Completed` | 50–99% — **this is what opens the next lesson** |
| `Aced` | 100% |

State follows the last attempt, so a bad replay lowers it. **Unlocks never reverse.** A player
who passes a lesson, opens the next one, then replays the first one badly keeps the second open.

The ladder, applied at every level:

- the next **lesson** opens once the current one is `Completed` or `Aced`;
- the next **chapter** (and its first lesson) opens once *every* lesson in the current chapter is;
- same rule for **subjects** and **terms**, each from its own children.

Only the very first lesson of the first chapter of the first subject of the first term starts
open. **Grades never lock.** Lessons with no question set in the player's language are skipped by
the ladder rather than blocking it.

### `GET /api/progress/games/{gameId}/snapshot?gradeId={guid}` — authenticated

**Call this on game open.** The whole grade in one response — the shape `CurriculumSnapshot`
wants, instead of a request per lesson. `gradeId` is optional and defaults to the player's own
grade from their profile.

It also *seeds* a new player's starting unlock, so a first-time player gets a playable lesson 1
from this call.

```json
{
  "gameId": "…", "langId": "…", "gradeId": "…", "gradeName": "Primary One", "percent": 42,
  "terms": [{
    "id": "…", "name": "First Term", "order": 1, "isUnlocked": true, "percent": 42,
    "subjects": [{
      "id": "…", "name": "Science", "order": 1, "isUnlocked": true, "percent": 42,
      "chapters": [{
        "id": "…", "name": "Matter", "order": 1, "isUnlocked": true, "percent": 75,
        "lessons": [{
          "id": "…", "name": "States of Matter", "order": 1,
          "isUnlocked": true, "hasQuestions": true,
          "completionState": "Aced", "percent": 100, "attempts": 3,
          "contentUpdated": false
        }]
      }]
    }]
  }]
}
```

Two flags to handle on every lesson:

- **`hasQuestions: false`** — no sheet in the player's language. Grey it out.
- **`contentUpdated: true`** — the sheet was re-uploaded since this score was earned. The old
  score is deliberately kept rather than reset, so this is your cue to prompt *"new questions
  available, replay this lesson"*.

`400` if no `gradeId` was passed and the player has no profile yet.

### `GET /api/progress/games/{gameId}/lessons/{lessonId}` — authenticated

One lesson in detail.

```json
{
  "gameId": "…", "lessonId": "…",
  "correctCount": 2, "totalCount": 4, "percent": 50, "attempts": 2,
  "completionState": "Completed", "isUnlocked": true, "hasAttempted": true,
  "firstAttemptWasPerfect": false,
  "questionsVersion": 1, "currentQuestionsVersion": 2, "contentUpdated": true,
  "lastAttemptAt": "2026-08-10T12:00:00Z"
}
```

A never-played lesson returns zeros with `hasAttempted: false` rather than a 404.

### Aggregates — authenticated

| Path | Aggregates over |
|---|---|
| `/api/progress/games/{gameId}/chapters/{chapterId}` | lessons in that chapter |
| `/api/progress/games/{gameId}/subjects/{subjectId}` | lessons in that subject |
| `/api/progress/games/{gameId}/terms/{termId}` | lessons in that term |
| `/api/progress/games/{gameId}/grades/{gradeId}` | lessons in that grade |

```json
{
  "gameId": "…", "nodeType": "Chapter", "nodeId": "…",
  "lessonsTotal": 3, "lessonsAttempted": 2, "lessonsCompleted": 2, "lessonsAced": 1,
  "correctCount": 8, "totalCount": 12, "percent": 67,
  "isUnlocked": true
}
```

`lessonsTotal` counts only lessons playable in the player's language. `percent` divides by the
questions in **every** playable lesson including untouched ones, so an unstarted chapter reads
0%, not 100% of nothing. Grades report `isUnlocked: true` always.

### `GET /api/progress/games/{gameId}/lessons/{lessonId}/wrong-questions` — authenticated

What the player got wrong on their last run.

```json
[{
  "questionId": "…", "text": "Symbol for iron?",
  "correctAnswerId": "…", "correctAnswerText": "Fe",
  "attempts": 3, "lastAttemptAt": "2026-08-10T12:00:00Z"
}]
```

Only questions still active are reported, so this comes back **empty after a re-upload** until
the lesson is replayed.

---

## 8. Admin

All of §8 requires the `Admin` or `SuperAdmin` role. The Unity client does not call these — they
are for the admin tooling.

### Building the tree

| Endpoint | Creates |
|---|---|
| `POST /api/admin/grades/{gradeId}/terms` | a term under a grade |
| `POST /api/admin/terms/{termId}/subjects` | a subject under a term |
| `POST /api/admin/subjects/{subjectId}/chapters` | a chapter under a subject |
| `POST /api/admin/chapters/{chapterId}/lessons` | a lesson under a chapter |

All four take the same body:

```json
{
  "translations": [
    { "langId": "…en…", "name": "First Term" },
    { "langId": "…ar…", "name": "الفصل الأول" }
  ],
  "order": 1
}
```

- **A name is required for every configured language.** A half-translated node would be nameless
  for those students and nothing would notice.
- `order` is optional and defaults to appending last. A position already taken is rejected —
  order drives the unlock chain, so siblings cannot share one.

`404` unknown parent · `409` duplicate name in one of the languages, or the `order` slot is taken
· `400` blank name, missing language, unknown language, or the same language twice.

### Deleting nodes

`DELETE /api/admin/{terms|subjects|chapters|lessons}/{id}?force=true`

Deletes cascade all the way down, so a delete is **refused with `409` while the node has
children** and reports what would be lost:

```json
{
  "errors": ["This term still contains 1 subject(s), 1 chapter(s), 1 lesson(s), 2 question(s). …"],
  "details": { "subjects": 1, "chapters": 1, "lessons": 1, "questions": 2, "hasChildren": true }
}
```

Use `details` to drive the confirmation dialog. `?force=true` commits. Empty nodes delete without
`force`.

### `POST /api/admin/lessons/{lessonId}/questions/upload?langId={guid}&hasHeaderRow=true`

`multipart/form-data` with a `file` field. **`langId` is required** — a lesson is shared across
languages, so the sheet's language cannot be inferred from it.

Sheet format: one worksheet, 4 columns — question, **correct** answer, wrong, wrong. Row 1 is
treated as a header unless `hasHeaderRow=false`. `.xlsx` only, ≤ 10 MB, ≤ 5000 rows.

Validation is **all-or-nothing**: one bad row rejects the whole sheet and the current version is
left untouched. The three answers must differ **case-sensitively** — `Fe` / `FE` / `fe` is valid
content, because capitalisation is often the thing being tested.

```json
{
  "succeeded": true, "lessonId": "…", "langId": "…",
  "version": 2, "importedCount": 40, "replacedCount": 35, "errors": []
}
```

Each language versions independently — publishing English v2 leaves Arabic at v1. The previous
questions are soft-deleted, not removed, so existing progress stays resolvable.

On failure, `400` with the same shape and `errors[]` of `{ row, message }`.

### `POST /api/admin/lessons/{lessonId}/recovery-questions/upload?langId={guid}&hasHeaderRow=true`

Publishes the **secondary** pool. Identical in every respect to the endpoint above — same
`multipart/form-data` `file` field, same required `langId`, same 4-column sheet, same limits, same
all-or-nothing validation (both go through the same parser), same response shape.

What differs is only which set it replaces: this bumps the lesson's **recovery** version and never
touches its main question set, and vice versa. So a lesson can sit at questions v1 / recovery v4.

### `POST /api/admin/lessons/{lessonId}/questions/manual?langId={guid}`

Publishes questions **typed by hand** instead of uploaded — the admin console's "Type questions by
hand" card. Same tables, same rules, same response shape as the sheet upload above; only the input
differs.

```json
{
  "mode": "APPEND",
  "questions": [
    { "text": "Capital of Egypt?", "correctChoice": "Cairo",
      "wrongChoice1": "Alexandria", "wrongChoice2": "Giza" }
  ]
}
```

**`mode` is required and has no default.** Publishing is destructive in one of its two meanings, so
an omitted mode is refused rather than guessed:

| Mode | Does |
|---|---|
| `APPEND` | Keeps the questions already published and adds these after them. |
| `REPLACE` | Publishes these *instead of* the current set. Also how a question is edited or removed — read the set, change it, publish it back. |

**Both modes produce a new version.** A published set is immutable, so an append republishes the
existing questions alongside the new ones rather than inserting into what is there. Client caches
key on that version, so every publish costs those clients a re-download of the lesson.

Correctness is **positional**: `correctChoice` is the right answer, matching the sheet where column
2 is. There is no per-answer `isCorrect` flag, because "none of them" and "two of them" are both
unanswerable in a three-door game.

Validation is identical to the sheet's and equally all-or-nothing — same length limits, same
case-sensitive distinctness, one bad question rejects the request and leaves the current version
untouched. Every fault in every question is reported at once rather than stopping at the first.

```json
{
  "succeeded": true, "lessonId": "…", "langId": "…",
  "version": 3, "importedCount": 4, "replacedCount": 3, "errors": []
}
```

`importedCount` is the size of the **whole new set**, so an append of one onto three reports 4.

On failure, `400` with `errors[]` of `{ row, message }`, where `row` is the 1-based position in
`questions` (`null` for a problem with the request as a whole, such as a missing `mode`).

### `POST /api/admin/lessons/{lessonId}/recovery-questions/manual?langId={guid}`

The same, over the **secondary** pool and its own version counter. Publishing here never touches
the lesson's main question set.

### `GET /api/admin/lessons/{lessonId}/questions?langId={guid}`
### `GET /api/admin/lessons/{lessonId}/recovery-questions?langId={guid}`

The active set in a **named** language, so the console can load what is published before editing it.
Response is the same `LessonQuestionsDto` as the player-facing §5 reads.

Separate from `GET /api/lessons/{lessonId}/questions` because that one serves the *caller's* content
language: an admin editing Arabic while signed in with an English token would otherwise load the
English set and republish it over the Arabic one. The upload endpoints have always taken an explicit
`langId` for exactly this reason.

### Games

| Endpoint | Does |
|---|---|
| `GET /api/admin/games` | every game, disabled ones included |
| `POST /api/admin/games` | register a game |
| `PUT /api/admin/games/{gameId}` | full replace, translations included |
| `DELETE /api/admin/games/{gameId}?force=true` | delete a game **and all progress for it** |

Create/update body:

```json
{
  "gameKey": "subway_runner",
  "lobbyScene": 1, "gameplayScene": 2,
  "minPlayers": 1, "maxPlayers": 2, "readyTimeoutSeconds": 20,
  "supportsSinglePlayer": true, "supportsMultiplayer": true,
  "useLobby": true, "useMatchmaking": true, "isActive": true,
  "translations": [
    { "langId": "…en…", "displayName": "Subway Runner", "description": "Steer into the right door." },
    { "langId": "…ar…", "displayName": "عداء الأنفاق", "description": "اختر الباب الصحيح." }
  ]
}
```

`gameKey` must be unique. A `displayName` is required for every language. The player range must
be coherent with the declared modes — `maxPlayers > 1` with `supportsMultiplayer: false` is
rejected, as is a game supporting neither mode.

Delete is refused with `409` and a breakdown while any progress exists:

```json
{ "errors": ["This game has 40 lesson progress row(s)… across 12 student(s). …"],
  "details": { "students": 12, "lessonProgressRows": 40, "questionProgressRows": 160, "unlocks": 55 } }
```

**Setting `isActive: false` is the reversible alternative** and is almost always what you want
instead of deleting.

### `DELETE /api/admin/users/{userId}`

Hard delete, no undo. Removes the account plus its refresh tokens, student profile, and all
progress and unlocks. `204` on success.

Refused if you target your own account, or if the target is an Admin/SuperAdmin and you are not
a SuperAdmin.

Currency balances are **not** listed above because they carry a real FK to `AspNetUsers` and
cascade automatically — unlike refresh tokens and profiles, which have no FK and are cleared by
hand.

### Currencies — moved

Currency lives in its own controller at `/api/currencies` (see §9), not under `/api/admin`. Only
creating and updating a currency are Admin-gated; listing and granting are open to any
authenticated account.

### Commerce admin — documented with their domains

These are Admin-gated and live under `/api/admin`, but are documented beside the endpoints they
configure rather than here, so that each domain reads in one place:

| Endpoint | Documented in |
|---|---|
| `GET|POST /api/admin/reward-rules`, `PUT /api/admin/reward-rules/{ruleId}` | §9 |
| `GET|POST /api/admin/products`, `PUT /api/admin/products/{productId}` | §10 |
| `POST /api/admin/entitlements` | §10 |

---

## 9. Economy and rewards

Virtual coins. **Nothing here is real money.**

Two tables behind the wallet: `UserCurrencyBalances` is the fast current projection, and
`CurrencyLedgerEntries` is the append-only truth. Every mutation writes both inside one
transaction under a row lock, so balances can always be rebuilt by summing the ledger — if the two
ever disagree, the ledger is right.

The ledger has **no public endpoint** (Unity does not consume one). It will be part of
`GET /api/users/me/export`, which is not built yet.

| Endpoint | Auth |
|---|---|
| `GET /api/currencies` | authenticated |
| `POST /api/currencies` | **Admin / SuperAdmin** |
| `PUT /api/currencies/{currencyId}` | **Admin / SuperAdmin** |
| `POST /api/currencies/grant` | **Admin / SuperAdmin** |
| `GET /api/commerce/balances` | authenticated |
| `GET /api/admin/reward-rules` | **Admin / SuperAdmin** |
| `POST /api/admin/reward-rules` | **Admin / SuperAdmin** |
| `PUT /api/admin/reward-rules/{ruleId}` | **Admin / SuperAdmin** |

### `POST /api/currencies` — Admin only

```json
{ "key": "coins", "name": "Coins", "description": "Earned by answering questions correctly." }
```

`201` with `{ "currencyId": "…", "key": "coins", "name": "Coins", "description": "…", "enabled": true }`.

**`key` is the identifier the client speaks and is permanent.** Balances are cached against it, so
it cannot be changed after creation — `PUT` updates the name, description and enabled flag only.
Keys are lowercase `^[a-z][a-z0-9_]*$`, max 32 chars, unique; a duplicate is refused with `409`
and `CURRENCY_KEY_TAKEN`.

Retiring (`enabled: false`) keeps balances and ledger history intact and refuses further credits
and debits. There is no delete. Creating and retiring are Admin-gated because retiring the currency
the whole economy runs on would otherwise be one request away for any signed-in account.

### `POST /api/currencies/grant` — Admin only

```json
{ "currencyId": "…", "amount": 500, "reason": "optional note for the ledger" }
```

**The account comes from the bearer token — there is no `userId` field**, so this credits whoever
is signed in and cannot reach anyone else's balance. Topping up a *student* is therefore not
possible here; that would need a target-user field back, restricted to admins.

Recorded on the ledger as `ADMIN_GRANT`, or `ADMIN_ADJUSTMENT` when `amount` is negative. A
negative amount that would overdraw is **refused, not clamped**, so a mistyped correction fails
instead of quietly zeroing a wallet.

> **Admin-gated as of 2026-08-22.** This is the only route in the economy where an amount travels
> from client to server, so it was the only one where a caller could name their own balance. It was
> deliberately left open while currency had nothing to buy; reward rules and purchases have since
> landed, so the condition attached to that decision has been met and the gate is on.
>
> A `Student` token now gets `403` here. Gameplay currency comes from the server evaluating a
> validated progress attempt — see reward rules below — never from a figure the client supplied.
> The admin console (`wwwroot/admin.html`) still calls this to top up a wallet while testing, which
> works unchanged because the console is already an admin surface.

### `GET /api/commerce/balances` — authenticated

```json
{ "balances": [ { "currency": "coins", "amount": 1250 } ] }
```

`currency` is the stable **key**, not the row id. `amount` is the **absolute authoritative
balance**, never a delta — assign it to the local wallet rather than adding to it.

This is the reconciliation endpoint: call it on launch and after reconnecting, and take the
server's answer over anything held locally. A currency the user has never held is absent rather
than reported as zero.

Concurrency is handled at the row: mutations take `UPDLOCK, HOLDLOCK` on the (user, currency)
balance, so simultaneous credits cannot overwrite each other and simultaneous debits cannot
overdraw. Verified with 40 racing credits all landing, and 20 racing debits against a balance of
10 where exactly 10 succeed.

### How gameplay currency is earned

**The client never says what a reward is worth.** It reports a run through
`POST /api/progress/attempts` (§7), the server regrades it, and the server decides the payout:

```text
run finishes → POST attempts → server regrades → matching reward rules evaluated
             → balances + ledger updated → authoritative balances returned
```

All of it commits as one transaction. A reward cannot survive an attempt that rolled back, and a
misconfigured rule cannot cost the player their progress — each rule is evaluated inside its own
savepoint, so a rule that cannot be paid is skipped and the rest of the attempt still lands.

There is no endpoint anywhere that accepts a reward amount. `IWallet.Earn(amount)` on the client
should stop being the authority: earn from the `rewards` array, reconcile from `balances`.

**Which events fire.** All derived from the regraded attempt, never asserted by the client:

| Event | When |
|---|---|
| `LESSON_ATTEMPTED` | every finished run, at any score |
| `LESSON_COMPLETED` | the run landed at or above the pass mark (`Completed` or `Aced`) |
| `LESSON_ACED` | a clean sweep |

An aced run fires **all three**, so "10 coins to pass, 5 gems to ace" is two independent rules that
both pay, not one rule with a branch.

### `GET|POST /api/admin/reward-rules`, `PUT /api/admin/reward-rules/{ruleId}` — Admin only

```json
{
  "name": "Lesson passed",
  "eventType": "LESSON_COMPLETED",
  "referenceKey": null,
  "repeatPolicy": "ONCE",
  "cooldownSeconds": null,
  "dailyLimit": null,
  "transactionType": "LESSON_REWARD",
  "enabled": true,
  "grants": [
    { "currencyId": "…", "amount": 10 },
    { "currencyId": "…", "amount": 2 }
  ]
}
```

| Field | Meaning |
|---|---|
| `eventType` | one of the three above. Accepted in any spelling (`LESSON_COMPLETED`, `lesson_completed`, `LessonCompleted`); always returned as `SCREAMING_SNAKE`. |
| `referenceKey` | a **lesson id** to restrict the rule to one lesson, or `null` for every lesson |
| `repeatPolicy` | `ONCE` or `EVERY_TIME` |
| `cooldownSeconds` `dailyLimit` | `EVERY_TIME` only — **refused** on a `ONCE` rule rather than ignored |
| `transactionType` | what the ledger entries are stamped with. Defaults to `LESSON_REWARD`. |
| `grants` | one per currency, amount **positive**. At least one; each currency at most once. |

**`grants` is the multi-currency part.** Several entries make *one* reward: paid together in one
transaction, under one cooldown, recorded as one reward transaction — or not at all. Two separate
rules would instead be two independent payouts with two counters that can drift apart.

**Rules compose, they do not override.** A global rule and a lesson-specific rule for the same
event both fire and both pay, so "10 coins for any lesson, 50 more for the final one" is 60 coins.
There is no most-specific-wins precedence to reason about.

`ONCE` is scoped per **game**, matching how progress itself is tracked — the same lesson in a
different game pays again, because it is a different ladder.

`eventType` and `referenceKey` cannot be changed by `PUT` and are not accepted there: the reward
transactions already recorded against a rule claim payment for the event it used to watch. **There
is no delete** for the same reason — retire with `enabled: false` and author a replacement.

Refusals use the §13 envelope: `REWARD_RULE_INVALID` for a rule that could never pay as written
(unknown event type, a limit the policy ignores, a duplicated currency, a non-positive amount, a
`referenceKey` that is not a lesson id), `REWARD_RULE_NOT_FOUND`, `CURRENCY_NOT_FOUND`. Validation
refuses rather than silently dropping, because a stored rule that can never fire looks identical to
a working one and the only symptom is students quietly earning nothing.

A rule whose currency has been **retired** is skipped whole — never partially paid. `grants[]
.currencyEnabled` in the `GET` response is where to look when a rule has stopped paying.

---

## 10. Product kinds, products, grants and entitlements

What a purchase hands over, and who owns it. **Price is not here** — it belongs to the offer, which
is not built. A product exists whether or not anything currently sells it, and keeps existing after
every offer for it is gone.

```text
Entitlement → Product → ProductGrant → reference
                  ↳ ProductKind → COSMETIC
```

That chain is the whole design. An entitlement records *that* an account owns a product; what it
actually gets is resolved by walking through to the product's grants on read, and how the client
should *read* those grants comes from the product's kind. Which is why:

- **a product accounts already own cannot be deleted** (`PRODUCT_OWNED`) and **its grants freeze**
  (`PRODUCT_GRANTS_LOCKED`) — either would silently change or destroy what those accounts own.
  Retire it with `active: false` instead. An unowned product deletes outright, grants included.
- **retiring or delisting never revokes ownership.**

`reference` is an **opaque client identifier** — a cosmetic id, an Addressables pack id. The backend
stores it and hands it back and never resolves it, because building a backend cosmetic catalogue was
ruled out. That makes the string the entire contract: it has to stay stable on the client for as
long as anyone owns a product referencing it, and nothing on this side can detect a break.

**The same is now true of `kind`.** It used to be a backend enum; it is an admin-managed
`ProductKind` row, and it sits on the **product**, not on each grant — so every grant of one product
reports the same kind, and a bundle mixing categories is authored as two products. Names are
normalised to `SCREAMING_SNAKE` before they reach the client (`Content Pack`, `content-pack` and
`ContentPack` all arrive as `CONTENT_PACK`), and two kinds that normalise the same are refused. A
name Unity does not recognise is not detectable here.

| Endpoint | Auth |
|---|---|
| `GET /api/commerce/entitlements` | authenticated |
| `GET /api/admin/product-kinds` | **Admin / SuperAdmin** |
| `GET /api/admin/product-kinds/{productKindId}` | **Admin / SuperAdmin** |
| `POST /api/admin/product-kinds` | **Admin / SuperAdmin** |
| `PUT /api/admin/product-kinds/{productKindId}` | **Admin / SuperAdmin** |
| `DELETE /api/admin/product-kinds/{productKindId}` | **Admin / SuperAdmin** |
| `GET /api/admin/products` | **Admin / SuperAdmin** |
| `GET /api/admin/products/{productId}` | **Admin / SuperAdmin** |
| `POST /api/admin/products` | **Admin / SuperAdmin** |
| `PUT /api/admin/products/{productId}` | **Admin / SuperAdmin** |
| `DELETE /api/admin/products/{productId}` | **Admin / SuperAdmin** |
| `GET /api/admin/product-grants[?productId=]` | **Admin / SuperAdmin** |
| `GET /api/admin/product-grants/{grantId}` | **Admin / SuperAdmin** |
| `POST /api/admin/product-grants` | **Admin / SuperAdmin** |
| `PUT /api/admin/product-grants/{grantId}` | **Admin / SuperAdmin** |
| `DELETE /api/admin/product-grants/{grantId}` | **Admin / SuperAdmin** |
| `POST /api/admin/entitlements` | **Admin / SuperAdmin** |

### `GET /api/commerce/entitlements` — authenticated

```json
{
  "entitlements": [
    {
      "entitlementId": "…",
      "productId": "…",
      "grantedAtUtc": "2026-08-12T18:00:00Z",
      "source": "PURCHASE"
    }
  ]
}
```

Everything the caller owns, newest first. `source` is `PURCHASE` or `ADMIN_GRANT`.

**Products that have been retired or delisted still appear.** Do not treat a `productId` missing
from the current offer list as revoked — ownership is permanent and outlives the shop.

`grantedAtUtc` carries a trailing `Z`, unlike the older progress timestamps (see `@ResponseSchemas.md`
§2).

Grants are deliberately **not** repeated here. The client already has them from the offers response
and from the purchase that created the entitlement; re-sending the catalogue on every inventory read
would duplicate it on the wire.

### `/api/admin/product-kinds` — Admin only

```json
{
  "name": "Content Pack",
  "translations": [
    { "langId": "…en", "name": "Content packs", "description": "Delivered by Addressables." },
    { "langId": "…ar", "name": "حزم المحتوى",   "description": "تُسلَّم عبر Addressables." }
  ]
}
```

Responds with the row plus the token the client will actually see:

```json
{
  "productKindId": "…",
  "name": "Content Pack",
  "kind": "CONTENT_PACK",
  "translations": [
    { "langId": "…ar", "langCode": "ar", "name": "حزم المحتوى", "description": "…" },
    { "langId": "…en", "langCode": "en", "name": "Content packs", "description": "…" }
  ],
  "productCount": 4
}
```

**Three different names, deliberately.** `name` is the machine name an admin types and is *not*
translated; `kind` is that name normalised and **is contract** — it is what every grant of every
product of this kind reports, and `COSMETIC` has to mean the same thing to an Arabic client as to an
English one; `translations[]` is the human label the admin console renders and **never reaches
Unity**.

Machine names collide on the normalised form, so `Content Pack` and `content-pack` cannot both exist
(`PRODUCT_KIND_NAME_TAKEN`, 409, with `details.existingName`).

A label is required for **every** configured language, refused with `PRODUCT_KIND_INVALID` (400,
`details.missingLanguages`) otherwise — the same rule the curriculum tree follows.

**Renaming a kind is a contract change**, not a cosmetic edit: every product of that kind
immediately starts reporting the new token, owned ones included.

`DELETE` is refused with `PRODUCT_KIND_IN_USE` (409, `details.productCount`) while any product
references it — the FK is `Restrict`, so the database refuses it too. Deleting one that is already
gone succeeds.

The migration seeds `Cosmetic` and `Content Pack`, so the vocabulary that existed when kind was an
enum still exists.

### `POST /api/admin/products` — Admin only

```json
{
  "key": "skin_astronaut",
  "productKindId": "…",
  "imageUrl": "https://cdn.example.com/shop/astronaut.png",
  "active": true,
  "translations": [
    { "langId": "…en", "name": "Astronaut skin", "description": "Gold visor." },
    { "langId": "…ar", "name": "زي رائد الفضاء", "description": "بقناع ذهبي." }
  ]
}
```

| Field | Meaning |
|---|---|
| `key` | stable handle for configuration and seed data. Lowercase `^[a-z][a-z0-9_]*$`, unique, **permanent**. Not what the client speaks — `productId` is. |
| `translations[]` | shop name and description **per language**. A name is required for every configured language. |
| `imageUrl` | optional. Stored and handed back verbatim — the backend neither hosts, fetches nor validates it. One image for every language. |
| `productKindId` | **required.** What tells the client how to read this product's grants. |
| `active` | `false` retires it: no new grants, existing owners keep it |

**The product row carries no text of its own.** Exactly like a curriculum node: one product has one
id in every language, which is what lets an entitlement survive a language switch. A missing
translation is refused with `PRODUCT_INVALID` (400, `details.missingLanguages`) rather than left to
a fallback — a shop entry with no text in a student's language is unreadable to them, and nothing
downstream can repair it.

**The product is created granting nothing.** Grants are a separate table with their own endpoints —
see below. A product with no grants is legal here and hands over an empty entitlement, so add its
grants before anything sells it.

Refusals: `PRODUCT_KEY_TAKEN` (409), `PRODUCT_KIND_NOT_FOUND` (404), `PRODUCT_INVALID` (400 — a
missing or blank translation), plus ASP.NET's own `ValidationProblemDetails` (400) for a `key` that
does not match the pattern. ⚠ **That second shape is not the `{ code, messageKey }` envelope** — it
is `{ title, errors: { "Key": ["…"] } }`, where `errors` is an *object*, not an array. Any client
parsing failures has to handle both.

### `PUT /api/admin/products/{productId}` — Admin only

Retitles in every language, retires with `active: false`, re-categorises, or changes the art. `key`
cannot change and is not accepted; `grants` are not accepted either. `translations[]` **replaces**
the whole set, so every configured language must be present on every update.

**All of this stays available on an owned product, including changing `productKindId`** — kind
changes how the client reads the grants, not which references the owner receives, so unlike editing
the grant set it cannot hand an existing owner something different.

### `DELETE /api/admin/products/{productId}` — Admin only

`204`. Deletes the product and cascades to its grants. **Idempotent** — deleting one that is already
gone succeeds.

Refused with `PRODUCT_OWNED` (409, `details.ownerCount`) once any account owns it: their
entitlements resolve by reading through to it. Retire it with `active: false` instead. The
`Entitlements → Products` foreign key is `Restrict`, so the database enforces this independently.

`ownerCount` on the `GET` responses is how many accounts own each product, so both the locked grant
set and the refused delete are visible before either is attempted.

### `/api/admin/product-grants` — Admin only

What a product hands over. This is the join a purchase walks: resolve the offer to its `productId`,
select every grant row carrying that id, hand the account all of them together. There is no partial
ownership.

```json
{ "productId": "…", "reference": "cosmetic_astronaut", "quantity": 1 }
```

```json
{
  "grantId": "…",
  "productId": "…",
  "kind": "COSMETIC",
  "reference": "cosmetic_astronaut",
  "quantity": 1
}
```

`kind` is not stored here — it is the owning product's, repeated on every grant because that is the
shape the commerce contract specifies. `GET` takes an optional `?productId=` filter.

`PUT` changes `reference` and `quantity` only. **A grant cannot be moved between products**;
`productId` is not accepted, because moving one would silently alter what both products hand over.

`DELETE` returns `204` and is idempotent.

Refusals: `PRODUCT_GRANT_INVALID` (400 — blank reference, quantity below 1),
`PRODUCT_NOT_FOUND` (404), `PRODUCT_GRANT_NOT_FOUND` (404),
`PRODUCT_GRANT_REFERENCE_TAKEN` (409 — one product may not grant the same reference twice; combine
the quantities. Enforced by a unique index, so concurrent adds cannot both land), and
**`PRODUCT_GRANTS_LOCKED` (409, `details.ownerCount`) on every write once any account owns the
product** — add, edit and delete alike. An entitlement reads these rows on every resolution rather
than snapshotting them at purchase, so any change would retroactively alter what existing owners
have. Author a replacement product instead.

### `POST /api/admin/entitlements` — Admin only

```json
{ "userId": "…", "productId": "…" }
```

Hands a product over without a purchase — support fixes, prizes, and exercising the client's
inventory before the shop exists. Recorded as `source: "ADMIN_GRANT"` with the granting admin's id.

**Idempotent**: granting something already owned returns the existing entitlement with
`alreadyOwned: true` rather than failing or duplicating it. Under concurrency the unique
(user, product) index decides, so simultaneous grants cannot produce two rows — the same guarantee
purchase will rely on.

Refusals: `PRODUCT_NOT_FOUND` (404), `PRODUCT_INACTIVE` (400 — a retired product cannot be newly
granted, though existing owners keep it).

---

## 10a. Offers and purchase

An offer is **what a product costs**; the product is what it hands over. The split is what lets the
same product sell at two prices at once, and what lets an account keep what it bought after every
offer for it is gone.

| Endpoint | Auth |
|---|---|
| `GET /api/time` | **anonymous** |
| `GET /api/commerce/offers` | authenticated |
| `GET /api/commerce/offers/today` | authenticated |
| `POST /api/commerce/purchase` | authenticated |
| `GET`/`POST`/`DELETE` `/api/admin/offers` | **Admin / SuperAdmin** |

### `GET /api/time` — anonymous

```json
{ "utcNow": "2026-08-14T20:00:00Z" }
```

Nothing more. **This is the clock that decides whether an offer has expired** — `expiresAtUtc` is
compared against it, never against the device's. Anonymous because a clock is not a secret and a
client may need it before it holds a token.

### `GET /api/commerce/offers` — authenticated

```json
{
  "offers": [
    {
      "offerId": "…",
      "name": "Starter bundle",
      "description": "Two skins, half price.",
      "productIds": ["…", "…"],
      "currency": "coins",
      "currencyId": "…",
      "price": 100,
      "originalPrice": 150,
      "availability": "AVAILABLE",
      "canPurchase": true,
      "ineligibleReasonKey": null,
      "purchaseLimit": null,
      "purchaseCount": 0,
      "expiresAtUtc": null,
      "sortOrder": 0,
      "badgeKey": null
    }
  ],
  "products": [ { "productId": "…", "grants": [ { "kind": "COSMETIC", "reference": "…", "quantity": 1 } ] } ]
}
```

**Everything is resolved server-side for this caller at this moment.** Render it; recompute none of it.

| Field | Notes |
|---|---|
| `name` / `description` | in the caller's content language |
| `productIds` | a **list** — one offer can sell a bundle, and buying grants all of them. Their grants are in the top-level `products[]`, keyed by id, so a product in three bundles is described once. |
| `currency` | the stable **key** (`"coins"`), matching `GET /api/commerce/balances`. Compare against this, not `currencyId`. |
| `availability` | `AVAILABLE`, `DISABLED`, `EXPIRED`, `PURCHASE_LIMIT_REACHED` |
| `canPurchase` | the resolved answer. **Ignores the balance** — too few coins is a purchase-time refusal, not a hidden offer. |
| `purchaseCount` / `purchaseLimit` | **per account**, not per offer. Only completed purchases count. `null` limit = unlimited. |
| `expiresAtUtc` | UTC with a trailing `Z`, or null |

⚠ Deviations from the original contract sketch: **`productIds` replaces `productId`** (a single id
cannot express a bundle), **`metadata` is gone**, replaced by `name`/`description`, and `currencyId`
is added alongside `currency`.

**Offers the caller cannot buy are still listed**, with `canPurchase: false` and a reason key — so
the shop greys them out rather than having entries disappear.

### `GET /api/commerce/offers/today` — authenticated

Identical response shape, filtered to **only what this account can buy right now**. Any signed-in
player, not just an admin.

Drops anything expired, switched off, already bought to its per-account limit, or whose products the
account already owns in full. Everything returned has `canPurchase: true` and a null
`ineligibleReasonKey`, and `products[]` shrinks to match.

It does **not** filter on affordability: an offer costing more than the caller holds still appears,
because a student should see what they are saving towards and the client already knows both numbers.

Use this to drive a "deals" screen; use the unfiltered `GET /api/commerce/offers` when you want to
render expired and sold-out entries greyed out instead of hiding them.

### `POST /api/commerce/purchase` — authenticated

```json
{ "offerId": "…", "requestId": "client-generated-unique-id" }
```

`requestId` is **optional** — `{ "offerId": "…" }` alone is valid and the server generates one. What
it buys you is retry safety, so send your own if the client can retry.

```json
{
  "state": "COMPLETED",
  "transactionId": "…",
  "transactionAtUtc": "2026-08-14T20:00:00Z",
  "offerId": "…",
  "productIds": ["…"],
  "products": [ { "productId": "…", "grants": [ … ] } ],
  "entitlements": [ { "entitlementId": "…", "productId": "…", "grantedAtUtc": "…", "source": "PURCHASE" } ],
  "balances": [ { "currency": "coins", "amount": 400 } ],
  "failureReasonKey": null,
  "replayed": false
}
```

**Atomic.** The debit, every entitlement, the ledger entries and the transaction row commit together
or not at all. There is no outcome where an account is charged and not granted.

**Idempotent when you supply `requestId`** — **reuse it when retrying**. Replaying a completed
purchase returns the original outcome and charges nothing further; `replayed: true` says so. Omitting
`requestId` means every call is a new purchase. The unique `(userId, requestId)` index enforces it,
so simultaneous retries cannot both land.

**Only completed purchases replay. A refusal is always re-evaluated.** Idempotency exists to stop a
second *charge*, and a refusal made none — so a player told `INSUFFICIENT_BALANCE` who tops up and
retries **with the same `requestId`** goes through. Several refusals may share one key; at most one
completed purchase ever can.

A double-tapped buy button is safe either way: a purchase that would grant nothing new is refused and
unwound, which does not depend on `requestId` at all.

`200` completed, `409` refused. **Both carry `balances[]`**, so the wallet reconciles from the same
round trip. Only `5xx` means the outcome is unknown — retry that with the same `requestId`.

Refusals (`409`, each with `state: "REFUSED"` and a `failureReasonKey`): `INSUFFICIENT_BALANCE`,
`OFFER_UNAVAILABLE`, `OFFER_EXPIRED`, `PURCHASE_LIMIT_REACHED`, `ALREADY_OWNED`,
`PRODUCT_INACTIVE`. `404 OFFER_NOT_FOUND` is the one case that records no transaction — an id that
does not exist is a client bug, not a shopping outcome. **Every other refusal is written down**, so
an offer nobody can afford shows up as refusals rather than as silence.

⚠ **`ALREADY_OWNED` is the normal answer to buying the same durable product twice.** Entitlements
are unique per (user, product), so a second purchase would hand over nothing — it is refused instead
of charged. The listing agrees: such an offer reports `NOT_ELIGIBLE` /
`commerce.offer.already_owned` and drops off `offers/today`. This also means **a `purchaseLimit` above 1 is currently unreachable**: already-owned
fires first. Use `purchaseLimit: 1` for "buy once"; limits above 1 only become meaningful when
something consumable exists to sell.

### `/api/admin/offers` — Admin only

```json
{
  "currencyId": "…",
  "price": 100,
  "originalPrice": 150,
  "availability": "AVAILABLE",
  "purchaseLimit": null,
  "expiresAtUtc": null,
  "sortOrder": 0,
  "badgeKey": null,
  "productIds": ["…", "…"],
  "translations": [
    { "langId": "…en", "name": "Starter bundle", "description": "Two skins, half price." },
    { "langId": "…ar", "name": "حزمة البداية",   "description": "زيّان بنصف السعر." }
  ]
}
```

**The `GET` responses return `name` and `description` in the caller's language**, not the whole
`translations[]` set — the same rule the player-facing listing follows. Resolution is: the caller's
preferred language (from the `preferred_language` token claim, falling back to the account's stored
setting), then **English**, then whatever translation exists.

Each entry under `products[]` carries `productId`, `key`, `name` (resolved the same way), `kind`,
`active`, `grantCount` **and `grants[]`** — the actual references buying it hands over, so a bundle
whose second product grants nothing is visible before publishing.

⚠ Authoring still takes the full `translations[]` array on `POST`; only the reads are resolved. Since
there is no update endpoint, nothing needs to read both languages back.

`availability` is stored as **`AVAILABLE` or `UNAVAILABLE` only** — expiry, limits and ownership are
derived per request, not stored, which is why the client's vocabulary is wider than this one. `originalPrice`
must exceed `price` or be omitted. `purchaseLimit` is per account; omit for unlimited. At least one
product; a name in every language. Set `expiresAtUtc` from `GET /api/time`, not the browser clock.

⚠ **There is no `PUT`.** Offers are authored once, by request — `POST`, `GET`, `DELETE` and buy are
the whole surface.

`DELETE` is refused with `OFFER_PURCHASED` (409, `details.transactionCount`) once any transaction
references it, refusals included.

⚠ Those two together mean an offer becomes **permanent and immutable the moment anyone so much as
attempts to buy it** — a single refused attempt is enough. It cannot then be re-priced, taken off
sale, or removed. **Set `expiresAtUtc` when authoring anything that may need to stop selling**,
because expiry is the only remaining mechanism that ends an offer's life.

Refusals: `OFFER_INVALID` (400), `OFFER_NOT_FOUND` (404), `CURRENCY_NOT_FOUND` (404),
`CURRENCY_DISABLED` (400), `PRODUCT_NOT_FOUND` (404, `details.productIds`).

---

## 11. Account deletion

### `DELETE /api/users/me` — authenticated

No body. `204` on success.

**Immediate and irreversible.** No grace period, no pending state, nothing to cancel. Removes the
account and everything it owns — profile, progress, unlocks, balances, ledger — and deletes every
refresh token, so no new session can be obtained.

**Idempotent**: calling it again with a token for the already-deleted account also returns `204`,
so a client retrying after a dropped response is not shown an error for work that succeeded.

> **Residual token window.** Access tokens are stateless and are not checked against the database,
> so one already issued keeps passing signature validation until it expires (30 minutes by
> default). It cannot be exchanged for a new session, and any endpoint reading account data finds
> nothing. Closing the window entirely would mean a database lookup on every authenticated
> request; that trade was declined deliberately.

Refusals use the §13 envelope with code `ACCOUNT_DELETION_REFUSED`.

---

## 12. Typical client flows

**First launch**

```
GET  /api/languages                  → language picker
POST /api/auth/register              → tokens, isProfileComplete: false
POST /api/users/me/preferred-language → REQUIRED to leave English; REPLACE both tokens
GET  /api/grades                     → grade picker (sort by order)
POST /api/auth/complete-profile      → isProfileComplete: true
```

The language step is not optional for an Arabic student: `register` does not accept a language, so
the account starts with none and every content endpoint serves English until this is called.

**Every later launch**

```
POST /api/auth/refresh               → new token pair (store BOTH)   … or login screen on 401
GET  /api/games                      → game select
```

**Entering a game**

```
GET  /api/progress/games/{gameId}/snapshot
       → whole tree + locks + completion; also seeds a first-time player
```

**Entering a lesson**

```
GET  /api/lessons/{lessonId}/questions/version    → compare with cache
GET  /api/lessons/{lessonId}/questions            → only if the version moved
… play …
POST /api/progress/attempts                       → score + unlocked[]
```

**Switching language mid-session**

```
POST /api/users/me/preferred-language   → REPLACE both stored tokens
… node ids are unchanged, progress and unlocks are intact …
… but re-fetch every cached question set: they are per language …
```

---

## 13. Error envelope (commerce and account endpoints only)

The endpoints in §8 Currencies, §9, §10 and §11 return machine-readable failures:

```json
{ "code": "INSUFFICIENT_BALANCE", "messageKey": "commerce.insufficient_balance", "details": {} }
```

Codes in use today: `INSUFFICIENT_BALANCE`, `CURRENCY_NOT_FOUND`, `CURRENCY_DISABLED`,
`CURRENCY_KEY_TAKEN`, `INVALID_AMOUNT`, `REWARD_RULE_INVALID`, `REWARD_RULE_NOT_FOUND`,
`PRODUCT_KIND_NOT_FOUND`, `PRODUCT_KIND_NAME_TAKEN`, `PRODUCT_KIND_INVALID`, `PRODUCT_KIND_IN_USE`,
`PRODUCT_NOT_FOUND`, `PRODUCT_INACTIVE`, `PRODUCT_KEY_TAKEN`, `PRODUCT_INVALID`,
`PRODUCT_GRANTS_LOCKED`, `PRODUCT_OWNED`, `PRODUCT_GRANT_NOT_FOUND`, `PRODUCT_GRANT_INVALID`,
`PRODUCT_GRANT_REFERENCE_TAKEN`, `OFFER_INVALID`, `OFFER_NOT_FOUND`, `OFFER_UNAVAILABLE`,
`OFFER_EXPIRED`, `OFFER_PURCHASED`, `PURCHASE_LIMIT_REACHED`, `ALREADY_OWNED`,
`REQUEST_ID_REQUIRED`, `ACCOUNT_DELETION_REFUSED`, `RATE_LIMITED`. `OFFER_SOLD_OUT`, `NOT_ELIGIBLE` and
`GRADE_RESTRICTED` are declared but have no producer — global stock and eligibility rules were
deferred.

`RATE_LIMITED` shares this envelope but is the one code that can come back from **any** endpoint,
the older ones below included: it is written by middleware rather than by a controller, so it does
not respect that split. Its `details` carry `retryAfterSeconds`, mirroring the `Retry-After`
header — see §1.

`code` is a stable backend constant; `messageKey` is a Unity localization key. **The backend never
returns display prose** — Unity owns the localized text.

Everything older (auth, curriculum, games, progress) keeps its existing
`{ "errors": ["sentence"] }` shape unchanged. This was added alongside rather than replacing it,
so nothing already shipping had to move.

---

## Not built yet

- **The shop** — currency can be earned (§9) and products can be owned (§10), but nothing sells
  them. No `Offer`, no `GET /api/commerce/offers`, no `POST /api/commerce/purchase`. Entitlements
  can only be created by an admin grant until purchase is built.
- **Data export** — `GET /api/users/me/export` does not exist yet.
- **Content manifest, server time, profile read** — `GET /api/content/manifest`, `GET /api/time`
  and `GET /api/users/me/profile` are not built.
- **Multiplayer** — the attempt endpoint models a single-player run, not a match. Matchmaking,
  room assignment and match result recording are all outstanding. Photon Fusion owns the live
  session; the backend's part is matchmaking in and results out.
- **`recoveryQuestions` trigger logic** — the pool itself now exists (table, upload, endpoints —
  see §5), but *when* the game shows a recovery question is still undefined. Storage and delivery
  are ready; the rule that fires them is not.
- **Teacher/parent views** — there is no class, enrollment or teacher-student relation in the
  schema, so "a teacher's students" cannot currently be expressed.
- **Renaming or reordering** content, and creating/deleting grades.
