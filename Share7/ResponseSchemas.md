# ResponseSchemas.md

# Share7 — Response Schemas

Every response body the API returns, field by field, with ready-to-paste C# classes.

**Every example in this file was captured from a running server**, not written from the C#
classes — so what is here is what actually goes over the wire, including the quirks in §2.

Companion to `@ApiReference.md`, which covers what each endpoint is *for* and what it *needs*.
This file covers what comes back.

**Contents**

1. [Serialization rules](#1-serialization-rules) · 2. [Two gotchas](#2-two-gotchas-read-before-writing-parsers)
3. [Endpoint → schema index](#3-endpoint--schema-index) · 4. [Auth](#4-auth-schemas)
5. [Content](#5-content-schemas) · 6. [Questions](#6-question-schemas) · 7. [Games](#7-game-schemas)
8. [Progress](#8-progress-schemas) · 9. [Admin](#9-admin-schemas) · 10. [Errors](#10-error-schemas)
11. [C# classes](#11-c-classes-for-unity)

---

## 1. Serialization rules

| C# type | JSON | Example |
|---|---|---|
| `Guid` | string | `"a0000000-0000-4000-8000-000000000003"` |
| `string?` | string or `null` | `"admin@admin.com"` / `null` |
| `int` | number | `75` |
| `float` | number, no forced decimal | `20` (not `20.0`) |
| `bool` | `true` / `false` | |
| `DateTime` | ISO 8601 string | see §2 |
| `DateTime?` | string or `null` | |
| `enum` | **string**, not a number | `"Completed"` |
| `List<T>` | array, `[]` when empty — never `null` | |

Property names are **camelCase**. Enums serialize as strings because
`JsonStringEnumConverter` is registered globally in `Program.cs`.

---

## 2. Two gotchas (read before writing parsers)

### Timestamps are inconsistent about the `Z`

Auth timestamps carry a UTC marker; progress timestamps do not:

```json
"accessTokenExpiresAt": "2026-08-12T06:51:02.3451257Z"   ← has Z
"lastAttemptAt":        "2026-08-12T06:21:06.4898462"    ← no Z
```

**Both are UTC.** The auth ones are generated in memory and keep `DateTimeKind.Utc`; the progress
ones are read back from SQL Server, which returns `DateTimeKind.Unspecified`, so the serializer
has nothing to mark.

The trap: a naive `DateTime.Parse` treats the second one as **local time**, silently shifting it
by your timezone offset. Parse progress timestamps explicitly as UTC:

```csharp
DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
```

This is a backend wart, not a deliberate contract — worth fixing server-side. Until it is, assume
UTC everywhere regardless of the suffix.

### `JsonUtility` cannot parse these responses

Several endpoints return a **bare JSON array at the root** (`/api/languages`, `/api/grades`,
`/api/terms`, `/api/subjects`, `/api/chapters`, `/api/lessons`, `/api/games`,
`/api/lessons/questions/versions`, `/api/auth/me`, wrong-questions). Unity's `JsonUtility`
cannot deserialize a root-level array, and it does not handle `null` or nested lists well
either — and the snapshot is four levels deep.

Use **Newtonsoft.Json for Unity** (`com.unity.nuget.newtonsoft-json`). The classes in §11 assume it.

---

## 3. Endpoint → schema index

| Endpoint | Returns |
|---|---|
| `POST /api/auth/register` \| `login` \| `external-login` \| `refresh` | [`AuthResult`](#authresult) |
| `POST /api/users/me/preferred-language` | [`AuthResult`](#authresult) |
| `POST /api/auth/revoke` | *(204, no body)* |
| `POST /api/auth/complete-profile` | [`CompleteProfileResult`](#completeprofileresult) |
| `GET /api/auth/me` | [`Claim[]`](#claim) |
| `GET /api/users/me/preferred-language` | [`PreferredLanguage`](#preferredlanguage) |
| `GET /api/users/me/equipment` | [`Equipment`](#equipment) |
| `PUT /api/users/me/equipment` | [`Equipment`](#equipment) |
| `GET /api/languages` | [`Language[]`](#language) |
| `GET /api/grades` | [`Grade[]`](#grade) |
| `GET /api/terms` | [`Term[]`](#term) |
| `GET /api/subjects` | [`Subject[]`](#subject) |
| `GET /api/chapters` | [`Chapter[]`](#chapter) |
| `GET /api/lessons` | [`Lesson[]`](#lesson) |
| `GET /api/lessons/{id}/questions/version` | [`LessonVersion`](#lessonversion) |
| `POST /api/lessons/questions/versions` | [`LessonVersion[]`](#lessonversion) |
| `GET /api/lessons/{id}/questions` | [`LessonQuestions`](#lessonquestions) |
| `GET /api/lessons/{id}/recovery-questions/version` | [`LessonVersion`](#lessonversion) |
| `POST /api/lessons/recovery-questions/versions` | [`LessonVersion[]`](#lessonversion) |
| `GET /api/lessons/{id}/recovery-questions` | [`LessonQuestions`](#lessonquestions) |
| `GET /api/games` | [`Game[]`](#game-1) |
| `GET /api/games/{id}` | [`Game`](#game-1) |
| `POST /api/progress/attempts` | [`AttemptResult`](#attemptresult) |
| `GET .../snapshot` | [`ProgressSnapshot`](#progresssnapshot) |
| `GET .../lessons/{id}` | [`LessonProgress`](#lessonprogress) |
| `GET .../chapters|subjects|terms|grades/{id}` | [`NodeProgress`](#nodeprogress) |
| `GET .../wrong-questions` | [`WrongQuestion[]`](#wrongquestion) |
| `POST /api/admin/…/{terms\|subjects\|chapters\|lessons}` | [`Term`](#term) / [`Subject`](#subject) / [`Chapter`](#chapter) / [`Lesson`](#lesson) |
| `DELETE /api/admin/{terms\|subjects\|chapters\|lessons}/{id}` | [`DeletedCounts`](#deletedcounts) |
| `GET /api/commerce/balances` | [`Balances`](#balances) |
| `GET /api/commerce/entitlements` | [`Entitlements`](#entitlements) |
| `POST /api/admin/lessons/{id}/questions/upload` | [`QuestionImportResult`](#questionimportresult) |
| `POST` \| `PUT /api/admin/games` | [`Game`](#game-1) |
| `DELETE /api/admin/games/{id}` | [`DeletedGame`](#deletedgame) |
| `DELETE /api/admin/users/{id}` | *(204, no body)* |

---

## 4. Auth schemas

### AuthResult

Returned by register, login, external-login, refresh, **and the language switch**.

| Field | Type | Notes |
|---|---|---|
| `succeeded` | bool | |
| `errors` | string[] | empty on success |
| `userId` | string (guid) | |
| `username` | string? | |
| `email` | string? | **null** until the account has one |
| `roles` | string[] | `["Student"]`, `["Admin"]`, … |
| `isProfileComplete` | bool | the field to branch on after auth |
| `accessToken` | string? | 30-minute lifetime |
| `accessTokenExpiresAt` | string? (date, **has `Z`**) | |
| `refreshToken` | string? | 7-day lifetime, rotates on use |
| `refreshTokenExpiresAt` | string? (date, **has `Z`**) | |

```json
{
  "succeeded": true, "errors": [],
  "userId": "c4acba17-4004-4090-9137-08def839d88a",
  "username": "admin", "email": "admin@admin.com",
  "roles": ["Admin"], "isProfileComplete": false,
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9…",
  "accessTokenExpiresAt": "2026-08-12T06:51:02.3451257Z",
  "refreshToken": "7QwoFjHCcIZVVNqS0pXxpmtA49Ho…==",
  "refreshTokenExpiresAt": "2026-08-19T06:21:02.405477Z"
}
```

Note `refreshToken` is base64 and **can contain `+`, `/` and `=`** — do not put it in a URL
without encoding it.

### CompleteProfileResult

```json
{ "succeeded": true, "errors": [] }
```

Tokens do not change, so nothing needs re-storing after this call.

### Claim

`GET /api/auth/me` — a bare array. Debugging only.

```json
[
  { "type": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", "value": "admin" },
  { "type": "preferred_language", "value": "9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34" },
  { "type": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "value": "Admin" }
]
```

### PreferredLanguage

```json
{ "languageId": "9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34" }
```

### Equipment

Returned by both `GET` and `PUT /api/users/me/equipment`.

```json
{
  "bodyType": "Female",
  "equipped": [
    { "slotKey": "Body", "cosmeticKey": "Armor_gold" },
    { "slotKey": "Head", "cosmeticKey": "Dia_Hat" }
  ],
  "colors": [
    { "cosmeticKey": "Armor_gold", "colorKey": "Hexa" },
    { "cosmeticKey": "Dia_Hat",    "colorKey": "Bronze" }
  ],
  "updatedAtUtc": "2026-08-17T23:43:08.2516558Z"
}
```

| Field | Notes |
| --- | --- |
| `bodyType` | `"Male"` or `"Female"`. Defaults to `"Male"`. |
| `equipped[]` | What is worn. At most one entry per `slotKey`. Up to 32. |
| `colors[]` | Colour per cosmetic. Every `cosmeticKey` here also appears in `equipped[]`. An item worn with no colour chosen is absent. |
| `updatedAtUtc` | **Nullable.** `null` ⇔ nothing has ever been saved. |

⚠ **The `PUT` body is a different shape** — the colour is nested inside each equipped entry and
there is no `colors[]` on the way in:

```json
{
  "bodyType": "Female",
  "equipped": [
    { "slotKey": "Body", "cosmeticKey": "Armor_gold", "colorKey": "Hexa" }
  ]
}
```

`colorKey` is optional there. The two lists in the response are **computed** from the stored rows
(one row per equipped item), which is why the shapes differ: nesting on the way in is what stops a
colour naming a cosmetic the player is not wearing.

⚠ **`updatedAtUtc` is nullable and load-bearing.** It is the only thing separating "never dressed"
(`null` → upload the device's outfit) from "deliberately wearing nothing" (a timestamp → undress
the avatar). Both carry empty `equipped` and `colors`. Model it as a nullable type on the client;
mapping it to a non-nullable `DateTime` collapses the two states into one and strips every player
who has not yet saved.

**This one does carry the `Z`** — unlike the progress timestamps described in §2, the read path
explicitly re-stamps it as UTC before serialising, precisely because a client shifting it by a
local offset would corrupt the comparison this field exists for.

An account with no saved outfit gets `{"bodyType":"Male","equipped":[],"colors":[],"updatedAtUtc":null}`
with a `200` — never a `404`.

---

## 5. Content schemas

### Language

```json
[{ "id": "9c4d7f2a-…", "name": "English", "code": "en" },
 { "id": "4b8e1d6f-…", "name": "العربية", "code": "ar" }]
```

`code` is the stable one to switch on — `"en"` / `"ar"`.

### Grade

| Field | Type | Notes |
|---|---|---|
| `id` | string (guid) | **same in every language** |
| `name` | string | already resolved into the caller's language |
| `langId` | string (guid) | which language `name` is in |
| `order` | int | 1–14; sort by this, never by name |

```json
[{ "id": "a0000000-0000-4000-8000-000000000001", "name": "KG1", "langId": "9c4d7f2a-…", "order": 1 },
 { "id": "a0000000-0000-4000-8000-000000000003", "name": "Primary One", "langId": "9c4d7f2a-…", "order": 3 }]
```

### Term

```json
[{ "id": "79777f10-…", "name": "First Term", "langId": "9c4d7f2a-…",
   "gradeId": "a0000000-0000-4000-8000-000000000003", "order": 1 }]
```

### Subject

```json
[{ "id": "feba0dd3-…", "name": "Science", "langId": "9c4d7f2a-…", "termId": "79777f10-…", "order": 1 }]
```

### Chapter

```json
[{ "id": "adf130e1-…", "name": "Matter", "langId": "9c4d7f2a-…", "subjectId": "feba0dd3-…", "order": 1 }]
```

### Lesson

| Field | Type | Notes |
|---|---|---|
| `id` `name` `langId` `chapterId` `order` | | as above |
| `questionsVersion` | int | for **the caller's language**. `0` = nothing uploaded |
| `hasQuestions` | bool | false → named but unplayable in this language; grey it out |

```json
[{ "id": "ae1b13e0-…", "name": "States of Matter", "langId": "9c4d7f2a-…",
   "chapterId": "adf130e1-…", "order": 1, "questionsVersion": 1, "hasQuestions": true }]
```

A node with no translation in the caller's language returns `"name": ""` rather than
disappearing from the list.

---

## 6. Question schemas

### LessonVersion

Single (`/questions/version`) and batch (`/questions/versions`, a bare array) share this shape.

```json
{ "lessonId": "ae1b13e0-…", "langId": "9c4d7f2a-…", "version": 1, "questionCount": 4 }
```

### LessonQuestions

```json
{
  "lessonId": "ae1b13e0-…", "langId": "9c4d7f2a-…", "version": 1,
  "questions": [{
    "questionId": "9b9d4cf2-…",
    "text": "Q1",
    "correctAnswerId": "e1a6ffae-…",
    "answers": [
      { "id": "e1a6ffae-…", "text": "correct1" },
      { "id": "4c02b402-…", "text": "wrongA1" },
      { "id": "de78d1ea-…", "text": "wrongB1" }
    ]
  }]
}
```

Always exactly **3 answers**, in source-sheet order — **the correct one is always first, so
shuffle before assigning doors**. `correctAnswerId` always matches one of the `answers[].id`.

Not-yet-uploaded in this language → `"version": 0, "questions": []` (still a `200`).

### Recovery questions reuse both schemas above

The `recovery-questions` routes return the **same two types**, not lookalikes — the same
`LessonVersion` and `LessonQuestions` classes serialise both pools, so one set of client models
covers both and a field added to one appears in the other.

The `version` field is the thing to keep straight: it is the *recovery* counter, which moves
independently of the main one. Cache the two pools as separate entries keyed by `lessonId` and
compare each against its own version, or a recovery upload will look like no change at all.

---

## 7. Game schemas

### Game

Field names mirror `MiniGameDefinitionSO`. Note the id field is **`gameId`**, not `id`.

| Field | Type | Notes |
|---|---|---|
| `gameId` | string (guid) | Unity's `gameId` |
| `gameKey` | string | stable slug, e.g. `"subway_runner"` |
| `displayName` `description` | string | resolved into `langId` |
| `langId` | string (guid) | |
| `lobbyScene` `gameplayScene` | int | Unity build indices. Superseded by the addresses below; still served |
| `lobbySceneAddress` `gameplaySceneAddress` | string \| null | Addressables scene addresses. **Null means this game still uses the build indices** |
| `minPlayers` `maxPlayers` | int | **server is authoritative** |
| `readyTimeoutSeconds` | number | `20`, not `20.0` |
| `supportsSinglePlayer` `supportsMultiplayer` `useLobby` `useMatchmaking` `isActive` | bool | |

```json
{
  "gameId": "2a1c2578-cb44-4382-9f6d-172dd589c03a", "gameKey": "subway_runner",
  "displayName": "Subway Runner", "description": "Steer.", "langId": "9c4d7f2a-…",
  "lobbyScene": 1, "gameplayScene": 2,
  "lobbySceneAddress": null, "gameplaySceneAddress": null,
  "minPlayers": 1, "maxPlayers": 2, "readyTimeoutSeconds": 20,
  "supportsSinglePlayer": true, "supportsMultiplayer": true,
  "useLobby": true, "useMatchmaking": true, "isActive": true
}
```

`GET /api/games` returns a bare array of these; `GET /api/games/{id}` returns one object.

**Read `gameplaySceneAddress` first, and fall back to the indices when it is null.** A build index
cannot name a scene that is not in the build, so a mini-game whose scenes are downloaded on demand
has no index to give. Null is the discriminator, not a missing value — the two fields are the
migration, and both are served until no shipped build reads the indices.

---

## 8. Progress schemas

`completionState` is always one of `"Uncompleted"`, `"Completed"`, `"Aced"` — a **string**.

### AttemptResult

Response to `POST /api/progress/attempts`.

| Field | Type | Notes |
|---|---|---|
| `gameId` `lessonId` `langId` | string (guid) | |
| `correctCount` | int | **server-computed**, not yours |
| `totalCount` | int | active questions in the lesson |
| `percent` | int | rounded to nearest whole |
| `attempts` | int | total runs of this lesson in this game |
| `completionState` | string | |
| `firstAttemptWasPerfect` | bool | set once, never recalculated |
| `questionsVersion` | int | version this run was scored against |
| `clientReportedCorrectCount` | int | echo of what you sent |
| `clientCountMatched` | bool | false → your cache is probably stale |
| `unlocked` | array | what this attempt opened; `[]` if nothing |
| `rewards` | array | what this attempt **earned**; `[]` if nothing |
| `balances` | array | absolute wallet totals after the attempt |

```json
{
  "gameId": "2a1c2578-…", "lessonId": "ae1b13e0-…", "langId": "9c4d7f2a-…",
  "correctCount": 3, "totalCount": 4, "percent": 75, "attempts": 1,
  "completionState": "Completed", "firstAttemptWasPerfect": false, "questionsVersion": 1,
  "clientReportedCorrectCount": 3, "clientCountMatched": true,
  "unlocked": [{ "nodeType": "Lesson", "nodeId": "12fca698-…" }],
  "rewards": [
    {
      "ruleId": "6b0f21d4-…",
      "ruleName": "Lesson passed",
      "eventType": "LESSON_COMPLETED",
      "transactionId": "9d47cc02-…",
      "grants": [
        { "currency": "coins", "amount": 10 },
        { "currency": "gems", "amount": 2 }
      ]
    }
  ],
  "balances": [
    { "currency": "coins", "amount": 1260 },
    { "currency": "gems", "amount": 2 }
  ]
}
```

`unlocked[].nodeType` is `"Lesson"`, `"Chapter"`, `"Subject"` or `"Term"`.

`rewards[].eventType` is `"LESSON_ATTEMPTED"`, `"LESSON_COMPLETED"` or `"LESSON_ACED"` — a
**string in `SCREAMING_SNAKE`**, not the PascalCase used by `completionState`. A perfect run fires
all three, so `rewards` can hold several entries and each can grant several currencies.

⚠ **`rewards[].grants[].amount` is a delta; `balances[].amount` is a total.** Animate the first,
assign the second. Adding `grants` to a local wallet double-counts, because `balances` already
includes them. Assign `balances` on every attempt, including ones where `rewards` is `[]` — that is
what keeps the wallet reconciled without a second call.

`rewards[].transactionId` is stable across retries: resubmitting the same run with the same
`requestId` returns the same id and does not pay again.

### LessonProgress

```json
{
  "gameId": "2a1c2578-…", "lessonId": "ae1b13e0-…",
  "correctCount": 3, "totalCount": 4, "percent": 75, "attempts": 1,
  "completionState": "Completed", "isUnlocked": true, "hasAttempted": true,
  "firstAttemptWasPerfect": false,
  "questionsVersion": 1, "currentQuestionsVersion": 1, "contentUpdated": false,
  "lastAttemptAt": "2026-08-12T06:21:06.4898462"
}
```

A never-played lesson returns zeros with `"hasAttempted": false` and `"lastAttemptAt": null` —
**not** a 404. `contentUpdated` is `questionsVersion != currentQuestionsVersion`; when true, the
sheet was re-uploaded and the old score was deliberately kept.

⚠ `lastAttemptAt` has **no `Z`** — see §2.

### NodeProgress

Chapters, subjects, terms and grades all return this.

```json
{
  "gameId": "2a1c2578-…", "nodeType": "Chapter", "nodeId": "adf130e1-…",
  "lessonsTotal": 2, "lessonsAttempted": 1, "lessonsCompleted": 1, "lessonsAced": 0,
  "correctCount": 3, "totalCount": 8, "percent": 38,
  "isUnlocked": true
}
```

`lessonsTotal` counts only lessons playable in the caller's language. `totalCount` is the
questions across **all** of those, including untouched lessons — which is why 3/4 on one lesson
of two reads 38%, not 75%. `lessonsCompleted` includes aced ones. Grades always report
`"isUnlocked": true`.

### ProgressSnapshot

Four levels deep. Every level has `isUnlocked` and `percent`; only lessons have completion.

```json
{
  "gameId": "2a1c2578-…", "langId": "9c4d7f2a-…",
  "gradeId": "a0000000-0000-4000-8000-000000000003", "gradeName": "Primary One",
  "percent": 38,
  "terms": [{
    "id": "79777f10-…", "name": "First Term", "order": 1, "isUnlocked": true, "percent": 38,
    "subjects": [{
      "id": "feba0dd3-…", "name": "Science", "order": 1, "isUnlocked": true, "percent": 38,
      "chapters": [{
        "id": "adf130e1-…", "name": "Matter", "order": 1, "isUnlocked": true, "percent": 38,
        "lessons": [
          { "id": "ae1b13e0-…", "name": "States of Matter", "order": 1,
            "isUnlocked": true, "hasQuestions": true,
            "completionState": "Completed", "percent": 75, "attempts": 1,
            "contentUpdated": false },
          { "id": "12fca698-…", "name": "Density", "order": 2,
            "isUnlocked": true, "hasQuestions": true,
            "completionState": "Uncompleted", "percent": 0, "attempts": 0,
            "contentUpdated": false }
        ]
      }]
    }]
  }]
}
```

Child arrays are `[]` when empty, never `null`. Nodes appear whether locked or not — filter on
`isUnlocked`, do not expect the server to hide them.

### WrongQuestion

Bare array.

```json
[{
  "questionId": "ef8cb30b-…", "text": "Q4",
  "correctAnswerId": "3b027cdb-…", "correctAnswerText": "correct4",
  "attempts": 1, "lastAttemptAt": "2026-08-12T06:21:06.4898462"
}]
```

Empty array after a re-upload until the lesson is replayed.

---

## 8b. Commerce schemas

### Balances

Response to `GET /api/commerce/balances`.

```json
{ "balances": [ { "currency": "coins", "amount": 1250 } ] }
```

`currency` is the stable **key**, not a row id. `amount` is **absolute** — assign it to the local
wallet, never add to it. A currency the account has never held is **absent**, not zero.

The same array comes back on every `POST /api/progress/attempts`, so the wallet reconciles there
and this endpoint is only needed on launch and after reconnecting.

### Entitlements

Response to `GET /api/commerce/entitlements`.

| Field | Type | Notes |
|---|---|---|
| `entitlementId` | string (guid) | |
| `productId` | string (guid) | what is owned |
| `grantedAtUtc` | string (ISO 8601) | **has a trailing `Z`** — unlike the progress timestamps, see §2 |
| `source` | string | `"PURCHASE"` or `"ADMIN_GRANT"` |

```json
{
  "entitlements": [
    {
      "entitlementId": "3f7c1a90-…",
      "productId": "b21e4d78-…",
      "grantedAtUtc": "2026-08-12T18:00:00Z",
      "source": "PURCHASE"
    }
  ]
}
```

Newest first. **Retired and delisted products still appear** — ownership is permanent and outlives
the shop, so a `productId` missing from the current offer list is not revoked.

What each entitlement grants is **not** included. The client already has the grants from the offers
response and from the purchase that created it; resolve locally by `productId`.

### ProductGrant

Not returned by an endpoint yet — it will appear under `products[]` in the offers response, and it
is what a `productId` resolves to.

```json
{ "kind": "COSMETIC", "reference": "cosmetic_astronaut", "quantity": 1 }
```

**`reference` is your own id** — the backend stores and returns it without ever resolving it, so it
must stay stable on the client for as long as anyone owns a product referencing it.

`kind` is `"COSMETIC"` or `"CONTENT_PACK"` today, but **it is no longer a fixed backend enum** — it
is an admin-managed row, normalised to `SCREAMING_SNAKE` on the way out (`Content Pack` and
`content-pack` both arrive as `CONTENT_PACK`). Treat it as an open vocabulary: match the kinds you
know and ignore the rest rather than failing to parse, since a new category can be added without a
backend deployment.

It is also a property of the **product**, not of the individual grant — so every grant under one
`productId` carries the same `kind`, and a product mixing categories does not exist. It is repeated
on each grant because that is the shape the commerce contract specifies.

⚠ **`kind` is not translated and never will be.** The backend does store a product name and
description per language, but the *token* is deliberately language-neutral so an Arabic and an
English client match on the same string. No client-facing response carries the localized text today;
if the offers response should include it, that is a contract change to agree first.

---

## 9. Admin schemas

### QuestionImportResult

Same shape on success (`200`) and failure (`400`) — branch on `succeeded`.

```json
{ "succeeded": true, "lessonId": "ae1b13e0-…", "langId": "9c4d7f2a-…",
  "version": 1, "importedCount": 4, "replacedCount": 0, "errors": [] }
```

```json
{ "succeeded": false, "lessonId": "ae1b13e0-…", "langId": "9ca63c29-…",
  "version": 0, "importedCount": 0, "replacedCount": 0,
  "errors": [{ "row": null, "message": "Unknown language." }] }
```

`errors[].row` is the 1-based sheet row, or **`null`** for whole-file problems.

### DeletedCounts

`DELETE` on a term/subject/chapter/lesson. Success wraps it in `deleted`:

```json
{ "deleted": { "subjects": 0, "chapters": 0, "lessons": 0, "questions": 4, "hasChildren": true } }
```

The refusal (`409`) puts the same object under `details` — see §10.

### DeletedGame

```json
{ "deleted": { "students": 1, "lessonProgressRows": 2, "questionProgressRows": 8,
               "unlocks": 5, "hasProgress": true } }
```

---

## 10. Error schemas

Three shapes. You must handle all three.

**A — plain error list.** Most failures: `401`, `403`, `404`, `409`, and service-level `400`s.

```json
{ "errors": ["Invalid username or password."] }
{ "errors": ["This grade already has a term named 'First Term'."] }
```

**B — error list plus payload.** A refused delete carries what it would have destroyed:

```json
{
  "errors": ["This term still contains 1 subject(s), 1 chapter(s), 2 lesson(s), 8 question(s). Deleting it removes all of that too — resend with force=true to confirm."],
  "details": { "subjects": 1, "chapters": 1, "lessons": 2, "questions": 8, "hasChildren": true }
}
```

Use `details` to drive the confirmation dialog rather than parsing the sentence.

**C — model validation (`400`).** ASP.NET's `ProblemDetails`, where `errors` is an **object**
keyed by field name:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Username": ["The field Username must be a string or array type with a minimum length of '3'."],
    "Password": ["The field Password must be a string or array type with a minimum length of '8'."]
  },
  "traceId": "00-5edf0486a1fe5338…"
}
```

Field keys are **PascalCase** here, unlike everywhere else. Detect the shape by checking whether
`errors` is an array (A/B) or an object (C).

---

## 11. C# classes for Unity

Newtonsoft handles camelCase against PascalCase properties case-insensitively, so these work
as-is. `[Serializable]` is included so they survive Unity's inspector/domain reload.

```csharp
using System;
using System.Collections.Generic;

[Serializable] public class AuthResult {
    public bool Succeeded; public List<string> Errors;
    public Guid UserId; public string Username; public string Email;
    public List<string> Roles; public bool IsProfileComplete;
    public string AccessToken; public DateTime? AccessTokenExpiresAt;
    public string RefreshToken; public DateTime? RefreshTokenExpiresAt;
}

[Serializable] public class SimpleResult { public bool Succeeded; public List<string> Errors; }

[Serializable] public class LanguageDto { public Guid Id; public string Name; public string Code; }

[Serializable] public class GradeDto   { public Guid Id; public string Name; public Guid LangId; public int Order; }
[Serializable] public class TermDto    { public Guid Id; public string Name; public Guid LangId; public Guid GradeId;   public int Order; }
[Serializable] public class SubjectDto { public Guid Id; public string Name; public Guid LangId; public Guid TermId;    public int Order; }
[Serializable] public class ChapterDto { public Guid Id; public string Name; public Guid LangId; public Guid SubjectId; public int Order; }
[Serializable] public class LessonDto  {
    public Guid Id; public string Name; public Guid LangId; public Guid ChapterId; public int Order;
    public int QuestionsVersion; public bool HasQuestions;
}

[Serializable] public class LessonVersionDto {
    public Guid LessonId; public Guid LangId; public int Version; public int QuestionCount;
}

[Serializable] public class AnswerDto   { public Guid Id; public string Text; }
[Serializable] public class QuestionDto {
    public Guid QuestionId; public string Text; public Guid CorrectAnswerId; public List<AnswerDto> Answers;
}
[Serializable] public class LessonQuestionsDto {
    public Guid LessonId; public Guid LangId; public int Version; public List<QuestionDto> Questions;
}

[Serializable] public class GameDto {
    public Guid GameId; public string GameKey;
    public string DisplayName; public string Description; public Guid LangId;
    public int LobbyScene; public int GameplayScene;                 // legacy; null-check the addresses first
    public string LobbySceneAddress; public string GameplaySceneAddress;   // null = still on the indices
    public int MinPlayers; public int MaxPlayers; public float ReadyTimeoutSeconds;
    public bool SupportsSinglePlayer; public bool SupportsMultiplayer;
    public bool UseLobby; public bool UseMatchmaking; public bool IsActive;
}

// ---- progress ----

public enum CompletionState { Uncompleted, Completed, Aced }   // arrives as a string
public enum CurriculumNodeType { Term, Subject, Chapter, Lesson }

// What the player picked. Never a claim about correctness — the server grades it.
// A skipped question can be sent with a null choiceId, or simply left out of the list entirely:
// both count as wrong, so a serializer that cannot express null has nothing to work around.
[Serializable] public class SubmittedAnswer {
    public Guid QuestionId; public Guid? ChoiceId;
}

[Serializable] public class SubmitAttemptRequest {
    public Guid GameId; public Guid LessonId;
    public List<SubmittedAnswer> Answers;
    public string RequestId;             // optional; one per run, reused on every retry of that run
}

// The server's verdict per question, for a review screen that regrades nothing.
[Serializable] public class AnswerResultDto {
    public Guid QuestionId; public Guid? ChoiceId;
    public Guid CorrectChoiceId; public bool IsCorrect;
}

[Serializable] public class UnlockedNodeDto { public CurriculumNodeType NodeType; public Guid NodeId; }

// A delta — what was just earned. Not a balance.
[Serializable] public class RewardGrantDto { public string Currency; public long Amount; }

[Serializable] public class RewardDto {
    public Guid RuleId; public string RuleName;
    public string EventType;             // "LESSON_ATTEMPTED" | "LESSON_COMPLETED" | "LESSON_ACED"
    public Guid TransactionId;           // stable across retries of the same requestId
    public List<RewardGrantDto> Grants;
}

// An absolute total — assign it, never add to it.
[Serializable] public class BalanceDto { public string Currency; public long Amount; }

[Serializable] public class BalancesResponse { public List<BalanceDto> Balances; }

// ---- commerce ----

[Serializable] public class EntitlementDto {
    public Guid EntitlementId; public Guid ProductId;
    public DateTime GrantedAtUtc;        // has a trailing Z, unlike the progress timestamps
    public string Source;                // "PURCHASE" | "ADMIN_GRANT"
}

[Serializable] public class EntitlementsResponse { public List<EntitlementDto> Entitlements; }

// Not returned yet — appears under products[] in the offers response.
[Serializable] public class ProductGrantDto {
    public string Kind;                  // "COSMETIC" | "CONTENT_PACK"
    public string Reference;             // YOUR id. The backend never resolves it.
    public int Quantity;
}

[Serializable] public class ProductDto {
    public Guid ProductId; public List<ProductGrantDto> Grants;
}

[Serializable] public class AttemptResultDto {
    public Guid GameId; public Guid LessonId; public Guid LangId;
    public int CorrectCount; public int TotalCount; public int Percent; public int Attempts;
    public CompletionState CompletionState;
    public bool FirstAttemptWasPerfect; public int QuestionsVersion;
    public List<AnswerResultDto> Answers;   // every question, graded — build the review screen from this
    public int UnrecognisedAnswers;         // non-zero ⇒ stale question cache; re-fetch the lesson
    public List<UnlockedNodeDto> Unlocked;
    public List<RewardDto> Rewards;      // animate these
    public List<BalanceDto> Balances;    // then assign these over the local wallet
}

[Serializable] public class LessonProgressDto {
    public Guid GameId; public Guid LessonId;
    public int CorrectCount; public int TotalCount; public int Percent; public int Attempts;
    public CompletionState CompletionState;
    public bool IsUnlocked; public bool HasAttempted; public bool FirstAttemptWasPerfect;
    public int QuestionsVersion; public int CurrentQuestionsVersion; public bool ContentUpdated;
    public DateTime? LastAttemptAt;      // UTC, but sent without a Z — see §2
}

[Serializable] public class NodeProgressDto {
    public Guid GameId; public CurriculumNodeType NodeType; public Guid NodeId;
    public int LessonsTotal; public int LessonsAttempted; public int LessonsCompleted; public int LessonsAced;
    public int CorrectCount; public int TotalCount; public int Percent; public bool IsUnlocked;
}

[Serializable] public class WrongQuestionDto {
    public Guid QuestionId; public string Text;
    public Guid CorrectAnswerId; public string CorrectAnswerText;
    public int Attempts; public DateTime LastAttemptAt;
}

[Serializable] public class SnapshotLessonDto {
    public Guid Id; public string Name; public int Order;
    public bool IsUnlocked; public bool HasQuestions;
    public CompletionState CompletionState; public int Percent; public int Attempts;
    public bool ContentUpdated;
}
[Serializable] public class SnapshotChapterDto {
    public Guid Id; public string Name; public int Order; public bool IsUnlocked; public int Percent;
    public List<SnapshotLessonDto> Lessons;
}
[Serializable] public class SnapshotSubjectDto {
    public Guid Id; public string Name; public int Order; public bool IsUnlocked; public int Percent;
    public List<SnapshotChapterDto> Chapters;
}
[Serializable] public class SnapshotTermDto {
    public Guid Id; public string Name; public int Order; public bool IsUnlocked; public int Percent;
    public List<SnapshotSubjectDto> Subjects;
}
[Serializable] public class ProgressSnapshotDto {
    public Guid GameId; public Guid LangId; public Guid GradeId; public string GradeName;
    public int Percent; public List<SnapshotTermDto> Terms;
}

// ---- errors ----

[Serializable] public class ApiErrors { public List<string> Errors; }
[Serializable] public class ValidationProblem {
    public string Title; public int Status;
    public Dictionary<string, List<string>> Errors;   // PascalCase field names
}
```

Recommended settings — the `DateTimeZoneHandling` line is what defuses the missing-`Z` problem
in §2 for every response at once:

```csharp
static readonly JsonSerializerSettings Json = new JsonSerializerSettings {
    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
    NullValueHandling    = NullValueHandling.Ignore,
    Converters           = { new Newtonsoft.Json.Converters.StringEnumConverter() }
};
```
