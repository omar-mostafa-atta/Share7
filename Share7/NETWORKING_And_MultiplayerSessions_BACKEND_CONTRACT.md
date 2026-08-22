## 1. Conventions inherited from the existing API

Observed in the supplied Swagger and matched exactly:

| Convention | Evidence | Applied here |
|---|---|---|
| Global bearer auth | `security: [{ Bearer: [] }]` | Every route below is authenticated. Anonymous routes must be opted out explicitly; none are. |
| Ids are `uuid` | every `format: uuid` path/query param | `sessionId`, `gameId`, `userId`, `langId` are all GUIDs. |
| Route casing is mixed | `/api/Auth/...`, `/api/Grades` vs `/api/commerce/offers`, `/api/progress/attempts` | **New multi-word domains are lowercase** (`commerce`, `progress`, `admin`). Multiplayer follows: `/api/multiplayer/...`. |
| Idempotency via `requestId` | `PurchaseRequest.requestId` (maxLength 128, nullable), `SubmitAttemptRequest.requestId` | Same field name, same 128 cap, same nullability, same semantics. |
| Timestamps are `date-time` UTC | `CreateOfferRequest.expiresAtUtc` | All timestamps `...AtUtc`, ISO-8601, server clock. |
| Localised content carries one `name` | `Docs/Localization.md`; every localised entity | **No multiplayer entity is localised.** Session state and error codes are machine tokens, translated client-side through `ILocalizationService`. |
| Admin surface is separate | `/api/admin/*` with its own tags | Operator routes live under `/api/admin/multiplayer/*`. |
| Bare `200 OK` responses | every route declares only `200` with no schema | Response schemas below are the *required* shape; they are additions to the document, not contradictions of it. |

**ASSUMPTION 1.** The error envelope parsed by `ExceptionMapper` into `ApiException.ErrorCode`
(landed 2026-08-16 for commerce) is reused. Application error codes below are returned in that
envelope. If the envelope shape differs for a new controller, the client maps on HTTP status
alone and degrades to a generic message — it does not break.

**ASSUMPTION 2.** The JWT subject claim carries the Share7 `userId` (`AuthenticationSession.UserId`
is populated from the auth response, and `/api/Auth/me` exists). Every route derives the caller's
identity from the token. **No route accepts a `userId` in its body.**

---

## 2. Entities

### 2.1 `MultiplayerSession`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` PK | Server-minted. The Share7 session identity. |
| `GameId` | `uniqueidentifier` FK -> `Games.Id` | Which mini-game. Required. |
| `HostUserId` | `uniqueidentifier` FK -> `Users.Id` | Current host. Changes on migration (§8). |
| `TransportSessionName` | `nvarchar(64)` | The Photon room name. **Unique among non-terminal sessions** — see index below. Deliberately *not* named `PhotonSessionName`: the column outlives the vendor. |
| `TransportRegion` | `nvarchar(16)` NULL | Photon region token, or null for "best". |
| `JoinCode` | `nvarchar(8)` NULL | Human-typable code for private sessions. Unique among non-terminal sessions when non-null. |
| `State` | `nvarchar(16)` | See §3. Stored as the token, not an int — it appears in logs and support tickets. |
| `Visibility` | `nvarchar(8)` | `Public` \| `Private`. Private sessions never appear in matchmaking. |
| `MaxPlayers` | `int` | Copied from the game's `maxPlayers` at creation; the game row can change later and must not retroactively resize a live session. |
| `MinPlayers` | `int` | Same reasoning. |
| `CurrentPlayerCount` | `int` | **Denormalised, maintained inside the same transaction as membership.** Exists so capacity can be enforced with one atomic conditional `UPDATE` (§7.3), not to avoid a `COUNT`. |
| `ProtocolVersion` | `int` | §9. A client whose version differs is refused. |
| `CurriculumPathJson` | `nvarchar(512)` NULL | Opaque to the backend: grade/term/subject/chapter/lesson ids the match plays. Stored so matchmaking can filter and so a completed match can be correlated with `/api/progress/attempts`. **Not** a foreign key — the client already validates the path. |
| `IsRanked` | `bit` | Matchmaking filter. |
| `CreatedAtUtc` | `datetime2` | Server clock. |
| `StartedAtUtc` | `datetime2` NULL | Set on `Running`. |
| `EndedAtUtc` | `datetime2` NULL | Set on `Closed` / `Abandoned`. |
| `LastHeartbeatAtUtc` | `datetime2` | Server clock, written on every heartbeat. Never a client-supplied time (§11 of the brief). |
| `ClosedReason` | `nvarchar(32)` NULL | `HostClosed` \| `Abandoned` \| `Empty` \| `CreationFailed` \| `AdminClosed`. |
| `RowVersion` | `rowversion` | Optimistic concurrency for state transitions. |

**Indexes**

```
IX_MultiplayerSession_Matchmaking
  (GameId, State, Visibility, IsRanked, ProtocolVersion) INCLUDE (CurrentPlayerCount, MaxPlayers)
IX_MultiplayerSession_Sweep            (State, LastHeartbeatAtUtc)
UQ_MultiplayerSession_Transport        (TransportSessionName) WHERE State NOT IN ('Closed','Abandoned','Failed')
UQ_MultiplayerSession_JoinCode         (JoinCode)             WHERE JoinCode IS NOT NULL
                                                                AND State NOT IN ('Closed','Abandoned','Failed')
```

The two filtered unique indexes are the structural defence against duplicate sessions: two
concurrent creates that mint the same room name cannot both commit.

### 2.2 `MultiplayerSessionPlayer`

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `SessionId` | `uniqueidentifier` FK -> `MultiplayerSession.Id` ON DELETE CASCADE | |
| `UserId` | `uniqueidentifier` FK -> `Users.Id` | From the JWT, never from the body. |
| `Slot` | `int` | 0-based, stable for the life of the membership. Lets a client render a deterministic seat order. |
| `IsHost` | `bit` | Mirrors `MultiplayerSession.HostUserId`; denormalised for the roster read. |
| `Status` | `nvarchar(16)` | `Joined` \| `Connected` \| `Disconnected` \| `Left` \| `Removed`. |
| `JoinedAtUtc` | `datetime2` | |
| `LeftAtUtc` | `datetime2` NULL | |
| `LastSeenAtUtc` | `datetime2` | Advanced by the host's heartbeat roster (§7.6). |
| `RowVersion` | `rowversion` | |

**Indexes**

```
UQ_SessionPlayer_Active   (SessionId, UserId) WHERE Status NOT IN ('Left','Removed')
UQ_SessionPlayer_Slot     (SessionId, Slot)   WHERE Status NOT IN ('Left','Removed')
IX_SessionPlayer_User     (UserId, Status)
```

`UQ_SessionPlayer_Active` is what makes double-join impossible even under a perfect race: the
second insert violates the index and the server returns `AlreadyInSession` rather than seating
the child twice.

### 2.3 `MultiplayerRequestLog` (idempotency)

| Column | Type | Notes |
|---|---|---|
| `RequestId` | `nvarchar(128)` PK (composite with `UserId`) | Client-minted, reused across retries. |
| `UserId` | `uniqueidentifier` PK | Scoped per user so one child's key cannot replay another's operation. |
| `Operation` | `nvarchar(32)` | `create` \| `join` \| `leave` \| `start` \| `close` \| `matchmake` \| `host-transfer`. |
| `SessionId` | `uniqueidentifier` NULL | The session the operation produced or acted on. |
| `ResponseJson` | `nvarchar(max)` | The exact response body first returned. |
| `StatusCode` | `int` | The exact status first returned. |
| `CreatedAtUtc` | `datetime2` | Rows older than 24h are swept (§10). |

**ASSUMPTION 3.** No idempotency infrastructure was observable in the Swagger beyond the
`requestId` fields, so this table is specified here. If the commerce domain already has one, use
that instead — do **not** build a second.

---

## 3. Session state machine (server-side, authoritative)

```
Creating ──► Created ──► Starting ──► Running ──► Ending ──► Closing ──► Closed
    │           │            │           │           │           │
    │           │            │           │           │           └──► Failed
    │           └────────────┴───────────┴───────────┴──────────────► Closing
    │
    └──► Failed

any non-terminal ──(TTL sweep)──► Abandoned
```

| State | Meaning | Joinable? | Heartbeat expected? |
|---|---|---|---|
| `Creating` | Record exists; the host has not confirmed the transport room. | No | No (creation TTL, §10) |
| `Created` | Transport room confirmed. Waiting for players. | **Yes** | Yes |
| `Starting` | Host committed to start; joins closed. | No | Yes |
| `Running` | Gameplay in progress. | No | Yes |
| `Ending` | Gameplay finished, results in flight. | No | Yes |
| `Closing` | Teardown begun. | No | No |
| `Closed` | Terminal, clean. | No | No |
| `Failed` | Terminal, creation or start never completed. | No | No |
| `Abandoned` | Terminal, swept for missed heartbeats. | No | No |

**Rules**

- Transitions are validated server-side against this table. An invalid transition returns `409` with `session_invalid_transition` and **does not** mutate.
- Every transition is a single conditional `UPDATE` guarded on the expected current `State` **and** `RowVersion`. Zero rows affected means someone else moved it; re-read and re-evaluate.
- Terminal states are absorbing. `close` on a `Closed` session returns `200` with the existing record — **not** an error (§7.7).
- The client mirrors this machine exactly (`NetworkSessionState`, `SessionStateMachine`) so an invalid transition is refused locally before a request is made. The server still validates; the client's copy is an optimisation and a UX affordance, never the authority.

---

## 4. Authorization matrix

| Operation | Permitted caller |
|---|---|
| `create` | Any authenticated user not already in a non-terminal session. |
| `matchmake` | Same. |
| `get` / `players` | Any **member** of the session. Non-members get `404`, not `403` — a non-member must not be able to probe which session ids exist. |
| `join` | Any authenticated user, subject to capacity, state and one-active-membership. |
| `leave` | The member themselves, only. |
| `start` | **Host only.** |
| `close` | **Host only**, or an admin. |
| `heartbeat` | **Host only.** |
| `host-transfer` | The **current host** (voluntary), or any member when the current host's `LastSeenAtUtc` is older than `HostClaimGraceSeconds` (involuntary, §8). |
| `/api/admin/multiplayer/*` | Admin role. |

**No route accepts `userId` in the body.** The three impersonation attacks in §22 of the brief
— spoofed user id, host impersonation, joining on another child's behalf — are structurally
impossible rather than validated against.

---

## 5. Configuration

Server-side, in `appsettings.json` (`Multiplayer:` section). Values are the recommended
defaults; the client mirrors the ones it needs on `NetworkingConfig`, and where the server
returns them in `create`/`join`, the server's values win.

| Key | Default | Rationale |
|---|---|---|
| `HeartbeatIntervalSeconds` | 15 | Cheap at Share7's scale; four writes/minute/session. |
| `SessionTimeoutSeconds` | 60 | 4 missed heartbeats before abandonment. Survives a lift, a tunnel and a Wi-Fi/cellular handover. |
| `CreatingTimeoutSeconds` | 30 | A session stuck in `Creating` never had a transport room. |
| `PlayerDisconnectGraceSeconds` | 45 | A member is `Disconnected`, not `Left`, for this long — the reconnect window. |
| `HostClaimGraceSeconds` | 30 | Before another member may claim host. |
| `MatchmakingCandidateLimit` | 10 | Sessions considered per attempt before creating a new one. |
| `RequestLogRetentionHours` | 24 | Idempotency replay window. Longer than any plausible client retry budget. |

---

## 6. DTOs

Field naming follows the existing document: camelCase, optional fields omitted rather than
sent empty (`RuntimeEndpoint.Query` already enforces this client-side).

```jsonc
// MultiplayerSessionDto — returned by create/get/join/start/close/matchmake
{
  "id": "uuid",
  "gameId": "uuid",
  "hostUserId": "uuid",
  "transportSessionName": "r7f3a91c",
  "transportRegion": "eu",              // optional
  "joinCode": "K3F9QA",                 // optional; private sessions only
  "state": "Created",
  "visibility": "Public",
  "maxPlayers": 2,
  "minPlayers": 1,
  "currentPlayerCount": 1,
  "protocolVersion": 1,
  "isRanked": false,
  "curriculumPath": {                   // optional; echoed back opaquely
    "gradeId": "uuid", "termId": "uuid", "subjectId": "uuid",
    "chapterId": "uuid", "lessonId": "uuid"
  },
  "createdAtUtc": "2026-08-18T10:00:00Z",
  "startedAtUtc": null,
  "endedAtUtc": null,
  "serverTimeUtc": "2026-08-18T10:00:03Z",   // see note
  "players": [ /* MultiplayerSessionPlayerDto */ ]
}
```

`serverTimeUtc` is included on every response deliberately. It costs nothing, it lets the client
compute heartbeat drift without a second call, and it is a partial down-payment on **BC-10**
(server-authoritative time) which is still open.

```jsonc
// MultiplayerSessionPlayerDto
{
  "userId": "uuid",
  "displayName": "Layla",       // optional; the backend's own localised display value
  "slot": 0,
  "isHost": true,
  "status": "Connected",
  "joinedAtUtc": "2026-08-18T10:00:00Z",
  "lastSeenAtUtc": "2026-08-18T10:00:45Z"
}
```

```jsonc
// CreateMultiplayerSessionRequest
{
  "gameId": "uuid",                    // required
  "transportSessionName": "r7f3a91c",  // required, <=64, client-minted, unique
  "transportRegion": "eu",             // optional
  "visibility": "Private",             // optional, default "Public"
  "maxPlayers": 2,                     // optional; clamped to the game's maxPlayers
  "isRanked": false,                   // optional
  "protocolVersion": 1,                // required
  "curriculumPath": { ... },           // optional
  "requestId": "..."                   // optional, <=128
}
```

```jsonc
// JoinMultiplayerSessionRequest   { "protocolVersion": 1, "requestId": "..." }
// LeaveMultiplayerSessionRequest  { "requestId": "..." }
// StartMultiplayerSessionRequest  { "requestId": "..." }
// CloseMultiplayerSessionRequest  { "reason": "HostClosed", "requestId": "..." }
```

```jsonc
// MatchmakeRequest
{
  "gameId": "uuid",                    // required
  "protocolVersion": 1,                // required
  "isRanked": false,
  "maxPlayers": 2,
  "curriculumPath": { ... },           // optional filter
  "createIfNoneFound": true,           // default true
  "transportSessionName": "r7f3a91c",  // required when createIfNoneFound — the name to use if creating
  "transportRegion": "eu",
  "requestId": "..."
}

// MatchmakeResponse
{ "outcome": "Joined" | "Created" | "NoMatch", "session": { /* MultiplayerSessionDto */ } }
```

```jsonc
// HeartbeatRequest — host only
{
  "connectedUserIds": ["uuid", "uuid"],   // the host's live realtime roster
  "state": "Running",                     // optional; the host's view, for drift detection
  "requestId": "..."
}

// HeartbeatResponse
{
  "state": "Running",              // authoritative; the client obeys this
  "serverTimeUtc": "...",
  "nextHeartbeatInSeconds": 15,
  "players": [ /* MultiplayerSessionPlayerDto */ ]
}
```

The heartbeat carrying `connectedUserIds` is what keeps `LastSeenAtUtc` current per member
without a per-player request. It is the host asserting *presence*, never *membership*: a user id
the host reports who is not already a member is ignored, logged, and never inserted.

```jsonc
// TransferHostRequest  { "toUserId": "uuid", "reason": "Voluntary"|"HostUnreachable", "requestId": "..." }
```

---

## 7. Endpoints

### 7.1 `POST /api/multiplayer/sessions` — create

`201` `MultiplayerSessionDto` (state `Creating`).

- `409 already_in_session` if the caller has a non-terminal membership.
- `409 transport_name_taken` on the unique-index violation.
- `400 protocol_version_mismatch` if `protocolVersion` is not currently accepted.
- `404 game_not_found` / `409 game_not_multiplayer` (`supportsMultiplayer == false`).
- Creates the session **and** the host's `MultiplayerSessionPlayer` (slot 0, `IsHost`) in **one transaction**. A session can never exist without its host.

### 7.2 `POST /api/multiplayer/sessions/{id}/start` — host confirms and/or starts

Serves both transitions, chosen by the current state:

- `Creating -> Created` — the host confirms the transport room came up. This is the confirmation half of the create saga (§11 of the audit).
- `Created -> Starting -> Running` — the host commits to start; joins close and `StartedAtUtc` is set.

`200` `MultiplayerSessionDto`. `403 not_session_host`. `409 session_invalid_transition`.
`409 session_below_min_players` when starting under `MinPlayers`.

### 7.3 `POST /api/multiplayer/sessions/{id}/join`

`200` `MultiplayerSessionDto` including the full roster.

Capacity is enforced by **one atomic conditional update**, not by read-then-write:

```sql
BEGIN TRAN;

UPDATE MultiplayerSessions
   SET CurrentPlayerCount = CurrentPlayerCount + 1
 WHERE Id = @id
   AND State = 'Created'
   AND CurrentPlayerCount < MaxPlayers
   AND ProtocolVersion = @protocolVersion;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK;               -- re-read to distinguish full / closed / not-found / version
    RETURN;
END

INSERT INTO MultiplayerSessionPlayers (...)   -- UQ_SessionPlayer_Active catches double-join
VALUES (...);

COMMIT;
```

Two clients racing for the last seat: exactly one `UPDATE` sees `CurrentPlayerCount < MaxPlayers`
and the other affects zero rows. No `SELECT` participates in the decision, so no isolation level
above `READ COMMITTED` is required and there is no lock ordering to get wrong.

Errors: `409 session_full`, `409 session_closed`, `409 already_in_session`,
`400 protocol_version_mismatch`, `404 session_not_found`.

### 7.4 `POST /api/multiplayer/sessions/{id}/leave`

`200` `MultiplayerSessionDto`. **Idempotent**: leaving twice, or leaving a session already
closed, returns `200`. Sets `Status = 'Left'`, `LeftAtUtc`, and decrements `CurrentPlayerCount`
in the same transaction.

If the leaver is the host: if members remain, transfer host to the lowest-slot `Connected`
member (§8); otherwise close the session with `ClosedReason = 'Empty'`.

### 7.5 `POST /api/multiplayer/sessions/{id}/close`

`200` `MultiplayerSessionDto`. Host or admin. **Idempotent and absorbing** — on an
already-terminal session it returns `200` with the existing record and does not change
`EndedAtUtc` or `ClosedReason`. Marks every non-terminal membership `Left`.

### 7.6 `POST /api/multiplayer/sessions/{id}/heartbeat`

`200` `HeartbeatResponse`. Host only. Sets `LastHeartbeatAtUtc = SYSUTCDATETIME()` and advances
`LastSeenAtUtc` for every member in `connectedUserIds` who is already a member. Members absent
for longer than `PlayerDisconnectGraceSeconds` move `Connected -> Disconnected`; the sweep, not
the heartbeat, is what eventually marks them `Left`.

`403 not_session_host` — including for the **old host after a migration**, which is the
mechanism that stops a returning stale host from resurrecting a session it no longer owns.

### 7.7 `GET /api/multiplayer/sessions/{id}` and `.../players`

`200` `MultiplayerSessionDto` / `MultiplayerSessionPlayerDto[]`. Members only; `404` otherwise.
`.../players` is the answer to §10 of the brief — "get all player ids in a session" — and is
what the host maps onto realtime peers.

### 7.8 `GET /api/multiplayer/sessions`

Query: `gameId`, `state`, `visibility`, `isRanked`, `lessonId`. Returns **only sessions the
caller is a member of**, unless the caller is an admin. It is a "where am I?" route for recovery
after a crash or reinstall — not a public lobby browser. Photon's own room list serves discovery.

### 7.9 `POST /api/multiplayer/matchmaking`

`200` `MatchmakeResponse`.

1. Reject if the caller already has a non-terminal membership (`409 already_in_session`).
2. Select up to `MatchmakingCandidateLimit` candidates: same `gameId`, `State = 'Created'`, `Visibility = 'Public'`, matching `isRanked` and `protocolVersion`, `CurrentPlayerCount < MaxPlayers`, `LastHeartbeatAtUtc > now - SessionTimeoutSeconds` (**stale sessions are never offered**), matching `lessonId` when supplied. Order by `CurrentPlayerCount DESC` (fill nearly-full sessions first — it is the shortest wait), then `CreatedAtUtc ASC`.
3. Attempt §7.3's atomic join against each candidate in turn. A candidate that fills between selection and join simply fails its `UPDATE`; move to the next. **This retry loop is the entire race defence** — no distributed lock, no queue.
4. If none succeed and `createIfNoneFound`, create per §7.1 and return `Created`.
5. Otherwise `NoMatch`.

### 7.10 `POST /api/multiplayer/sessions/{id}/host-transfer`

`200` `MultiplayerSessionDto`. Accepted when the caller is the current host (voluntary), or when
the caller is a member and the current host's `LastSeenAtUtc` is older than
`HostClaimGraceSeconds` (involuntary claim). Updates `HostUserId` and both `IsHost` flags in one
transaction, guarded on `RowVersion`. **Simultaneous claims:** the `RowVersion` guard means
exactly one commits; the loser re-reads and sees the winner — it does not retry blindly.
`409 host_still_active` when the grace period has not elapsed.

### 7.11 Admin

```
GET    /api/admin/multiplayer/sessions            ?state=&gameId=&olderThan=
POST   /api/admin/multiplayer/sessions/{id}/close
GET    /api/admin/multiplayer/sessions/{id}/players
```

Operator tooling for the 3 AM page. Read-mostly; the one mutation is a forced close.

---

## 8. Host migration

Photon may elect a new host without asking anyone. The backend must be told, and must arbitrate:

1. New host detects it holds authority (`OnHostMigration` / state authority acquired).
2. It calls `host-transfer` with `toUserId = self`, `reason = HostUnreachable`.
3. The `RowVersion`-guarded update means simultaneous claims produce exactly one winner.
4. The winner starts heartbeating. The loser stops trying and re-reads.
5. The old host, if it returns, gets `403 not_session_host` on its next heartbeat and transitions locally to a non-host member — it can neither close nor restart the session.

**If the new host cannot reach the backend:** the match continues (Photon is still simulating);
the client tolerates `HeartbeatGraceCount` failures and keeps retrying. If the backend stays
unreachable past `SessionTimeoutSeconds`, the sweep marks the session `Abandoned` while the match
plays out locally. That is the correct trade: a match already in progress must not be killed by a
backend outage, and an abandoned record is recoverable where an interrupted lesson is not.

**Host migration is not proven unnecessary.** Fusion in `Host` mode does not migrate by default —
`HostMigrationToken` handling is opt-in and this project has never implemented it. The client
therefore implements the *backend* half unconditionally and treats loss of the host as
`ConnectionState.Disconnected -> session Ending` when the transport does not migrate. Enabling
Fusion host migration later requires **no backend change**.

---

## 9. Protocol version

`ProtocolVersion` is an `int` owned by the platform, bumped whenever the realtime contract
changes incompatibly (networked properties, RPC signatures, spawn order, input struct).
It is **not** the app version — `ToSessionProperties` writes `Application.version` today and
nothing reads it, which is worse than useless because it implies a check that does not happen.

- Client sends it on create/join/matchmake.
- Server refuses a mismatch with `400 protocol_version_mismatch`.
- The client also writes it into Photon session properties, so a mismatched room is filtered before a request is made.

Accepted versions are server configuration (`Multiplayer:AcceptedProtocolVersions`), so a
transitional window during a staged rollout is an ops change, not a deploy.

---

## 10. Background cleanup

An `IHostedService` (`MultiplayerSessionSweeper`) — **not** work performed inside controllers.
Runs every `SweepIntervalSeconds` (default 30) and, in one batched pass:

| Condition | Action |
|---|---|
| `State = 'Creating'` and `CreatedAtUtc < now - CreatingTimeoutSeconds` | -> `Failed`, `ClosedReason = 'CreationFailed'` |
| `State` non-terminal and `LastHeartbeatAtUtc < now - SessionTimeoutSeconds` | -> `Abandoned`, `ClosedReason = 'Abandoned'`, all memberships -> `Left` |
| `State = 'Created'` and `CurrentPlayerCount = 0` | -> `Closed`, `ClosedReason = 'Empty'` |
| Member `Status = 'Disconnected'` and `LastSeenAtUtc < now - PlayerDisconnectGraceSeconds` | -> `Left`, decrement `CurrentPlayerCount` |
| `MultiplayerRequestLog.CreatedAtUtc < now - RequestLogRetentionHours` | delete |

Batched (`TOP (200)`) per pass so a backlog cannot hold a long transaction open. Idempotent, so
overlapping runs across instances are harmless. **This sweep is the only thing that makes the
"host crashed" case recoverable**, which is why the audit's compensation table lists it as the
last resort for nearly every failure.

---

## 11. Application error codes

Returned in the `ErrorCode` envelope. The client maps each to `NetworkSessionErrorCode`
(`Assets/Platform/Scripts/Platform/Core/Multiplayer/Domain/NetworkSessionErrorCode.cs`) and
never parses a message string.

| HTTP | `errorCode` | Client code |
|---|---|---|
| 404 | `session_not_found` | `SessionNotFound` |
| 409 | `session_full` | `SessionFull` |
| 409 | `session_closed` | `SessionClosed` |
| 409 | `already_in_session` | `AlreadyInSession` |
| 403 | `not_session_member` | `NotSessionMember` |
| 403 | `not_session_host` | `NotSessionHost` |
| 409 | `session_invalid_transition` | `InvalidStateTransition` |
| 409 | `session_below_min_players` | `BelowMinimumPlayers` |
| 409 | `transport_name_taken` | `SessionCreationFailed` |
| 409 | `host_still_active` | `HostMigrationFailed` |
| 400 | `protocol_version_mismatch` | `ProtocolVersionMismatch` |
| 409 | `game_not_multiplayer` | `SessionCreationFailed` |
| 401 | (any) | `AuthenticationExpired` |

Unmapped statuses fall back on the HTTP code alone: 5xx -> `NetworkUnavailable`, everything else
-> `Unknown`. An unrecognised `errorCode` never throws.

---

## 12. Backend test requirements

Specified so the backend team implements against the same expectations the client was built to.
**None of these can run in this repository** — there is no backend to run them against.

| # | Test | Asserts |
|---|---|---|
| 1 | Create then get | Session exists in `Creating` with exactly one player, the host. |
| 2 | **Concurrent join for the last seat** (N threads, `MaxPlayers = 2`, 1 seat free) | Exactly one `200`; the rest `409 session_full`; `CurrentPlayerCount == MaxPlayers`; membership row count matches. |
| 3 | Double join, same user | Second is `409 already_in_session`; one membership row. |
| 4 | Join a `Closed` session | `409 session_closed`. |
| 5 | Leave twice | Both `200`; one `LeftAtUtc`; count decremented once. |
| 6 | Close twice | Both `200`; `EndedAtUtc` unchanged by the second. |
| 7 | Replayed `requestId` on create | One session; identical body both times. |
| 8 | Non-member `GET` | `404`, not `403`. |
| 9 | Non-host `start` / `close` / `heartbeat` | `403 not_session_host`. |
| 10 | `userId` in body is ignored | Membership is the JWT subject regardless. |
| 11 | Sweep abandons a stale session | Non-terminal + stale heartbeat -> `Abandoned`; memberships `Left`. |
| 12 | Sweep fails a stuck `Creating` | -> `Failed`, `CreationFailed`. |
| 13 | **Concurrent host claims** | Exactly one `200`; `HostUserId` single-valued; the loser sees the winner. |
| 14 | Old host heartbeat after migration | `403`. |
| 15 | Matchmake fills the fullest joinable session | Ordering honoured; a stale session is never returned. |
| 16 | Matchmake under concurrency | No session ever exceeds `MaxPlayers`. |
| 17 | Protocol mismatch on join | `400`; no membership created. |
| 18 | Transport-name collision | Second create `409 transport_name_taken`; no orphan row. |
