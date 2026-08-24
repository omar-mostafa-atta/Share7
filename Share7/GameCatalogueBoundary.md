# The game catalogue boundary

**2026-08-24.** What the `Games` table owns, what the Unity `MiniGameDefinitionSO` owns, and why
the scene columns are gone.

This records a change that spans the backend and the Unity client. The backend half is in this
repo; the client half is in the Unity repo and is summarised here so the two can be read together.

---

## 1. The question

The two catalogues describe the same thing from two sides:

- **Backend** — `Games` + `GameTranslations`, served by `GET /api/Games` as `GameDto`.
- **Unity** — `MiniGameDefinitionSO`, discovered through Addressables by `IMiniGameCatalog`.

They are joined by one string: `Games.GameKey` equals `MiniGameDefinitionSO.gameId`, which is also
the definition asset's Addressables address (`game.runner`). The backend GUID that key resolves to
is what every progress and run route is keyed by.

That join was already correct and already documented. The question was everything around it: which
side owns which *field*, and whether the two were actually behaving the way the comments claimed.

## 2. What the audit found

**The authority claim was fiction on every overlapping field.** `Game.cs` and `GamesController`
both said the database wins if the two disagree, because matchmaking has to enforce player counts
server-side. On the client, every runtime read came from the ScriptableObject:

| Read | Site |
|---|---|
| `MaxPlayers` | `MiniGameSession.MaxPlayers => Definition.MaxPlayers` |
| `ReadyTimeoutSeconds` | `MiniGameNetworkManager`, ready barrier |
| `MinPlayers` | `LobbyService.MinimumPlayers` |
| both counts | `MiniGameSessionNetworking`, create + matchmaking requests |

`GameMetadata` — the client's domain view of `GameDto` — was consumed only for its `Id`, by
`LessonProgressCoordinator` and `RunLedger`. Nine of fourteen wire fields were deserialized,
cached, language-invalidated, and never read.

**`isActive` was a kill switch that was not connected.** The list endpoint filters inactive games
and the by-id endpoint deliberately returns them so a client holding a stale id learns it is
disabled. But `MiniGamesScreen` enumerated the Addressables label, not the backend list, so a game
deactivated on the server still listed and still started. This is the only field where backend
authority buys something a shipped build genuinely cannot do for itself: pull a broken mini-game
without a store release.

**One game had three display names.** `GameTranslations` (per language, required for every
configured language), `MiniGameDefinitionSO.displayName`, and `minigame.{gameId}.name` in the
client localization catalogue. The screen drew the third. The ScriptableObject field had no
runtime consumer at all.

**The scene columns had no reader anywhere.** `LobbyScene`, `GameplayScene`,
`LobbySceneAddress`, `GameplaySceneAddress` were touched only by `GameService` (echo to DTO),
`GameAdminService` (echo to admin DTO), and their tests. Matchmaking never read them;
`MultiplayerSession` has no scene column; `grep SceneBuildIndex` across the backend returns
nothing — the build indices the client puts in a Fusion session request never reach Share7. On the
client, the DTO did not even declare the two address fields.

**Nothing checked that a definition's `gameId` exists as a backend `gameKey`.** The runtime
consequence of that mismatch is documented in the client's `ContentArchitecture.md` and has
happened here before: attempts posted against an unresolvable id are a 200 with an empty tree, so
no progress is recorded and no reward is paid.

## 3. The decisions

### The line: can the server act on this value?

| Owner | Fields |
|---|---|
| **Backend** | `gameId`, `gameKey`, translations, `isActive`, `minPlayers`, `maxPlayers`, `readyTimeoutSeconds`, `supportsSinglePlayer`, `supportsMultiplayer`, `useLobby`, `useMatchmaking` |
| **Definition** | scenes, network prefabs, spawn spacing, environments |

No field lives on both sides. Everything the backend keeps is either identity, something
matchmaking enforces itself, or availability. Everything the definition keeps is content the
server cannot see.

### Scenes leave the backend entirely

The tempting argument for keeping scene identity server-side is the downloadable mini-game: a Unity
build index cannot name a scene that is not in the build, so a game whose scenes arrive on demand
has no index to give. That is true, and it is what the `*SceneAddress` columns were added for.

It still does not justify the columns, for three reasons:

1. **The server can never validate one, and said so in its own tests.** From
   `GameSceneAddressTests`: *"The server never resolves one — it cannot see the client's content
   catalogue."* A value the authority cannot check is not authoritative data.

2. **There is no bootstrapping gap to fill.** The definition asset is *itself* remote content. The
   chain is: `/api/Games` says the game exists → Addressables resolves `game.runner` → the
   `MiniGameDefinitionSO` downloads → it names the scenes. The scene address arrives through the
   same pipeline as the scene. There is never a moment where a client knows a game exists, can
   download its content, but needs REST to tell it what to load.

3. **A second writer only creates drift.** Hotfixing a broken scene is what an Addressables
   catalogue update already does, atomically with the bundle. A backend field pointing at address
   `Y` while the published catalogue only has `X` is a broken load with an extra hop. And it could
   not have driven a session anyway: Unity's `ToBuildIndex` yields `-1` — "load nothing" — for any
   path-addressed `SceneRef`, so the missing piece is a client-side `SceneRef`→addressable-scene
   path that no backend string closes.

### Only a definite "no" blocks a session

The client refuses a session for `isActive: false`. It does **not** refuse when the catalogue is
unreachable, unconfigured, or has never heard of the game. A local mini-game with no backend row is
how the project is developed, and it must not be indistinguishable from one that was deliberately
withdrawn. An unreachable catalogue must not take the platform offline.

## 4. What changed — backend

Every step below is in this repo.

1. **`Share7.Domain/Games/Game.cs`** — removed `LobbyScene`, `GameplayScene`,
   `LobbySceneAddress`, `GameplaySceneAddress`. Rewrote the class doc to state what the table owns
   and what it deliberately does not, and documented `IsActive` as the withdrawal switch.

2. **`Share7.Application/Games/Models/GameDto.cs`** — same four fields removed from the client read
   model.

3. **`Share7.Application/Games/Models/GameAdminDto.cs`** — same four removed. An author cannot
   usefully edit a value the server cannot resolve and no client reads.

4. **`Share7.Application/Games/Models/SaveGameRequest.cs`** — same four removed. `GameKey`'s doc
   now states the equality with `MiniGameDefinitionSO.gameId` explicitly, since that equality is
   the entire join.

5. **`Share7.Infrastructure/Games/GameService.cs`** — four lines out of the shared projection.

6. **`Share7.Infrastructure/Games/GameAdminService.cs`** — the four assignments out of `Apply` and
   `GetForAuthoringAsync`; the scene-address pair validation out of `ValidateAsync`; the
   `Normalize` helper deleted with the last caller.

7. **`Share7.Infrastructure/Persistence/Configurations/GameConfiguration.cs`** — the two
   `HasMaxLength(256)` address declarations removed.

8. **`Share7.Infrastructure/Persistence/Migrations/20260824145252_DropGameSceneColumns.cs`** — new,
   generated by `dotnet ef`. Drops the four columns. `Down` restores the columns but not their
   contents; they are unrecoverable from there, and re-authoring them would only recreate the
   disagreement. Carries a summary explaining the data loss is deliberate.

9. **`Share7/Controllers/GamesController.cs`** — doc updated: what the catalogue answers, and that
   it never answers what to load.

10. **`Share7.Tests/GameSceneAddressTests.cs`** — deleted. Every test in it pinned behaviour that no
    longer exists.

11. **`Share7.Tests/GameAuthoringReadTests.cs`** — scene fields removed from the request builder and
    from the read-back-then-resave round trip. The round trip now asserts `MinPlayers` in place of
    the addresses, so the test still covers a field of every kind it did before.

12. **Admin console** (`Share7 front/pages/games.html`, `Share7 front/js/games.js`) — the four scene
    inputs and their help text removed; the form fill and the save body no longer read them; the
    two client-side scene-pair pre-flight checks removed with the fields they guarded; the list's
    "Scenes" column replaced with the ready timeout. The form now explains where scenes actually
    come from.

13. **Docs** — `ApiReference.md` (§6 Games and the admin section), `ResponseSchemas.md` (§7 field
    table, sample, and the C# mirror), `Progress.md` (the `Games` schema block and its prose). Each
    now records that the fields were served until 2026-08-24 and are gone, so a reader who
    remembers them is not left wondering.

## 5. What changed — client (Unity repo)

Summarised; the detail lives in `Docs/ContentArchitecture.md` there, under **"Which side owns which
field"**.

1. **`GameDto` / `GameMetadata`** — `lobbyScene` and `gameplayScene` dropped, matching the wire.

2. **`MiniGameSession`** — carries an optional `GameMetadata` and exposes `MinPlayers`,
   `MaxPlayers`, `ReadyTimeoutSeconds`, `SupportsSinglePlayer`, `SupportsMultiplayer`, `UseLobby`,
   `UseMatchmaking` and `BackendGameId` as **reconciled** values: the server's where there is one,
   the definition's otherwise. A zero or negative player count from the server is ignored — a
   malformed row must not be able to make a mini-game unstartable. The ready timeout is not guarded
   the same way, because zero is a legal authored value there.

3. **`MiniGameService.StartSessionAsync`** — takes `IGameCatalogService`, asks it **before** loading
   the definition (so a withdrawn game does not pay for its asset on the way to being refused),
   fails with `NotFound` on `isActive: false`, and carries the metadata onto the session. Every
   other outcome — no catalogue, no endpoint, no network, no published row, a throwing catalogue —
   is logged and proceeds on the definition's values.

4. **Consumers moved off `Definition`** — `MiniGameSessionNetworking` (create + matchmaking
   requests), `LobbyService.MinimumPlayers`, `MiniGameNetworkManager`'s ready barrier. After this,
   `MiniGameSession` is the only file that reads those fields off the definition at all.

5. **`MiniGamesScreen`** — reads the catalogue once per open, drops games the backend reports
   inactive, and draws `GameMetadata.DisplayName`/`Description` with the client catalogue keys as
   the offline fallback. An empty map draws exactly what the screen drew before, which is what a
   child on a bad connection sees.

6. **`MiniGameDefinitionSO`** — `displayName` deleted (no consumer); the player counts, ready
   timeout and capability flags relabelled in their tooltips as offline defaults that the backend
   overrides; a class doc stating that this is the content half and the only half that names a
   scene.

7. **`Share7 ▸ Content ▸ Check Mini-Game Backend Parity`** — new editor window. Reads `/api/Games`
   with a developer-supplied token (EditorPrefs, machine-local, never committed) and reports
   definitions with no backend `gameKey` as errors, field disagreements and inactive games as
   warnings, and published games this build cannot run as info. The comparison itself is a pure
   function (`MiniGameParityRules`) over two plain lists, matching how `MiniGameRules` already
   separates judgement from gathering. It is a window rather than a build-gate validator because it
   needs the network and an authenticated session: as a pre-build callback it would block every
   build on a backend outage, or pass silently whenever a token had expired.

8. **Tests** — eight added to `MiniGameSessionWiringTests` covering the refusal, that a withdrawn
   game's definition is never loaded, the three "cannot ask" paths, the count overlay, the
   zero-count guard, and `BackendGameId`.

## 6. Deploying this

**The migration drops columns. Run it deliberately.**

```
dotnet ef database update -p Share7.Infrastructure -s Share7/Share7.API.csproj
```

Order matters only in one direction: the API must be deployed **before or with** the migration, not
after. A running instance built against the old model selects the dropped columns and fails; the
new model never mentions them, so it is happy either side of the migration.

No client change is required first. No shipped build reads these fields — the client `GameDto`
never declared the address fields, and the two integer fields were deserialized and ignored. A
missing integer deserializes to `0` under Newtonsoft, so even an old build sees no error.

**Then check parity**, because this is the first time anything has: open
`Share7 ▸ Content ▸ Check Mini-Game Backend Parity` in the Unity Editor, paste an access token, and
confirm `game.runner` resolves to a published, active row whose player counts match the definition.
If it reports "no backend game", create one with `gameKey: game.runner` in the admin console —
until then, no lesson attempt from that mini-game is being recorded.

## 7. What was verified, and what was not

**Verified.** `dotnet build Share7.slnx` — 0 errors, only pre-existing warnings. On the Unity side,
`dotnet build` on `Game.Core`, `Platform.UI`, `Platform.Multiplayer`, `Platform.Bootstrap`,
`Platform.Editor` and `Platform.Tests.EditMode` — 0 errors.

**Not verified.**

- **The backend test suite did not run.** It needs a local SQL Server and this machine has none
  installed — every test in `GameAuthoringReadTests` failed with
  `error: 26 - Error Locating Server/Instance Specified`, including
  `An_id_that_is_not_a_game_reads_back_null`, which touches nothing that changed. Run
  `dotnet test Share7.Tests` against a real instance before deploying.
- **The Unity EditMode tests did not run.** They need `ScriptableObject.CreateInstance` and
  `SerializedObject`, so they only run inside the Editor Test Runner. They compile.
- **The admin console was not exercised in a browser.** The changes there are field removals in one
  form and one table column; worth a click through Add and Edit before relying on it.
- **No live request was made against the deployed backend** after the change.
