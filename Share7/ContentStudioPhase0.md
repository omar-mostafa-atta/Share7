# Content Studio — Phase 0: foundations

**2026-09-21.** The first phase of the Content Studio plan (the separate content-team app, the
engine rebuild, SuperAdmin Team & Access). Phase 0 changes nothing a player or a content-team
member sees. It builds the safety net the engine rebuild stands on, adds the audit trail, and closes
two security gaps. Backend only; no UI.

Everything below was built and verified on this machine against the local `.\SQLEXPRESS` instance.

---

## 1. What changed

| # | Change | Why |
|---|---|---|
| 1 | **Contract tests** for every game-facing endpoint (`Share7.Tests/Contracts`) | The engine rebuild must not change anything the Unity client sees. This is how that is proven. |
| 2 | **Audit trail** — `AuditEvents` table, append-only, written with every curriculum, question and account change | Nothing recorded who created or deleted curriculum, or who created or deleted accounts (Roles.md §8). |
| 3 | **Seed admin guard** — Production never creates `admin` / `Admin123` | A missing `SeedAdmin:Password` minted a guessable admin on a public host (Roles.md §1). |
| 4 | **SuperAdmin bootstrap** — the first SuperAdmin comes from `SeedSuperAdmin` config, once | No SuperAdmin could exist, so the role's powers were unreachable (Roles.md §3). |
| 5 | **External sign-in linking** — never onto staff or privileged accounts; never with an address Google says is unverified | Google/Facebook sign-in attached itself to any account with a matching email (Roles.md §9). |
| 6 | **Two production bugs** fixed, one test-harness bug, four stale tests | Found while getting the suite green — see §6. |
| 7 | **Restore rehearsal** script, `ops/rehearse-restore.ps1` | "We have backups" means nothing until a restore has been seen to match. |

---

## 2. Contract tests — the zero-Unity-change guarantee

`Share7.Tests/Contracts/GameContractTests.cs` runs the **real, built API as its own process** on a
loopback port, against its own throwaway database, and calls every endpoint the Unity client uses the
way the client calls it. Each scenario's responses are compared, byte for byte, with a reviewed
baseline in `Contracts/Snapshots/*.json`.

**Covered:** languages, grades, time; login, refresh, `me`, a wrong password; terms, subjects,
chapters, lessons (English and Arabic readers); question and recovery-question downloads, version
checks and batch version checks; attempts (refused, perfect, retried, with a mistake); every progress
read and the snapshot; games; learning targets; exams.

**The fixture** (`ContractData.cs`) derives every id from a name, so baselines show `lesson:solids`
rather than a GUID. It deliberately includes every shape the client must cope with: a subject with no
Arabic name, a lesson on English v2 / Arabic v1, an English-only lesson, a lesson with no questions,
a recovery pool.

**Normalised** (because it changes run to run): timestamps, dates, tokens, token time claims, trace
ids, and ids the server mints mid-test — those become `<id:1>`, `<id:2>`… by first appearance, so
"the same id in both places" is still checked.

**Still exact:** routes, status codes, every property name, type, value and order, fixture ids,
version numbers, both languages' text. One deliberate strictness: the order of per-answer results in
an attempt response follows database order. It is stable today; if the rebuild changes only that
order, it is a reordering the client does not rely on (it keys answers by `questionId`), and can be
re-recorded knowingly.

```bash
dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~GameContractTests"
```

**A failure is a stop.** It means the client would receive something different. Re-record only when
the change is deliberate and the client team has agreed:

```powershell
$env:SHARE7_UPDATE_SNAPSHOTS = "1"; dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~GameContractTests"; Remove-Item Env:SHARE7_UPDATE_SNAPSHOTS
```

On a mismatch the test names the first differing line and writes `*.received.json` next to the
baseline for diffing. Verified: a one-character change to a baseline fails with the exact line.

The API must be built first (the test project references it for build order only). Override the
location with `SHARE7_API_DLL`, and the SQL Server with `SHARE7_TEST_SQL_SERVER`, as for every other
test.

---

## 3. The audit trail

`AuditEvents` — one row per thing somebody did, written **in the same transaction as the change**:
a refused or failed operation leaves no row claiming it happened, and a committed one cannot be
missing its row.

| Column | Meaning |
|---|---|
| `Sequence` | Database-assigned, strictly increasing — the order events happened in |
| `OccurredAtUtc` | When |
| `ActorUserId`, `ActorRoles` | Who, and the roles their token carried at that moment. Null actor = the platform (startup, jobs) |
| `Action`, `Area` | `curriculum.node.created`, `questions.published`, `accounts.account.deleted`… (`AuditActions`) |
| `TargetType`, `TargetId` | What was acted on |
| `Summary`, `DataJson` | One plain sentence; the specifics (versions, counts, forced or not) |
| `IpAddress`, `UserAgent`, `CorrelationId` | Where from, and the request's trace id |

**Recorded today:** adding and deleting terms, subjects, chapters and lessons (with the counts of
what a forced delete destroyed); every question publish on all three paths (paired sheet,
single-language upload/manual, recovery); account creation and deletion; the SuperAdmin bootstrap.

**Append-only, enforced by the database.** Trigger `TR_AuditEvents_AppendOnly` refuses every UPDATE
and DELETE, from any caller, including a hand-written query. Archiving under a retention policy is a
deliberate operation: disable the trigger inside the archiving transaction.

**Ids, never personal details.** No names, usernames or emails are written into a row; a viewer
resolves them at read time. That lets rows outlive the accounts they mention. Deleting an account
keeps its audit rows (the platform's accounting, like the currency ledger) — see
`UserOwnedData.RetainedOnDeletion`, which also now covers the guidance CMS's audit log and scrubs the
admin email stored there.

There is no viewer yet; it arrives with Team & Access (Phase 1).

---

## 4. Seed admin and the first SuperAdmin

Startup account seeding moved from `Program.cs` into `Infrastructure/Identity/IdentitySeeder.cs`, so
it has tests.

**Seed admin** — behaviour unchanged except: when `ASPNETCORE_ENVIRONMENT` is Production (which is
also the default when the variable is unset), the account is **never created** with a missing or
default password; the log says so at Critical. If an existing admin still has `Admin123`, the log says
so at Critical on every start. Nothing is locked automatically — locking the only admin out of a live
platform is its own outage.

**First SuperAdmin** — add this section once, start the server, then remove the password:

```json
"SeedSuperAdmin": {
  "Username": "choose-a-username",
  "Password": "at-least-12-characters",
  "Email": "optional@yourdomain"
}
```

It creates a **new** account with the SuperAdmin role, only while no SuperAdmin exists, and records
`security.superadmin.bootstrapped` in the audit trail. It refuses a password under 12 characters or
the old default, and refuses a username that already exists (promoting an existing account by editing
a config file would be the least auditable elevation possible). Once a SuperAdmin exists, the section
is ignored on every start.

---

## 5. External sign-in

`AuthService.ExternalLoginAsync` now refuses to **link** a Google or Facebook identity to an existing
account when:

- the account holds **Admin, SuperAdmin or ContentTeam** — staff and administrators sign in with a
  username and password; or
- the provider says the address is **unverified** — Google reports this (`email_verified`).

Both refusals show the same message, which confirms neither. New accounts and ordinary student
linking with a verified (or unreported) address work as before — tested.

**Open, not changed:** Facebook's Graph API reports no verification flag, so Facebook linking to an
existing student account still follows the email alone. Closing it changes how existing students
connect Facebook. Decision pending — see §8.

---

## 6. Bugs found and fixed while getting the suite green

The suite had never run on this machine. Baseline: **675 tests, 650 passing, 25 failing**, every
failure deterministic.

**Production bugs (code fixed):**

1. **A retried attempt could 500 instead of replaying.** Two in-flight submissions of the same run —
   a client retrying a timed-out request — both did the whole attempt and collided on whichever
   unique index they reached first. The first-contact unlock seeding collided too. Fixes:
   `UnlockService.CommitAsync` treats "someone else already granted this" as success (unlocks only
   grow); `ProgressService` claims the request id as the transaction's **first** write
   (`ClaimRequestAsync`), so the twin waits on it and replays the stored answer.
2. **The player whose seat completed a subject match got a 500.** `SessionLessonMatcher` stamped the
   lesson with raw SQL, then assigned it onto its tracked copy of the session — which still carried the
   old `RowVersion`, so the request's next save failed a concurrency check at the exact moment the
   match formed. It now reloads the row.

**Test problems (tests fixed):** the snapshot tests passed no grade for a user with no profile; the
matchmaking tests simulated two players' requests on one database context (which is what hid bug 2);
the composition test lacked the API's `ICurrentUserService`; the deletion-coverage guard had no
category for audit trails.

**Now:** **705 tests, 688 passing.** The 17 still failing are the same leaderboard, signal-economy,
run and reward tests that failed at the baseline, failing identically — none newly broken. They need
someone to decide intended economy behaviour, so they were split into a separate task rather than
changed silently.

---

## 7. Restore rehearsal

```powershell
.\ops\rehearse-restore.ps1 -Database Shareh
.\ops\rehearse-restore.ps1 -Server "prod-sql\SHARE7" -Database Shareh -BackupDirectory "D:\Backups"
```

Fingerprints the source (applied migrations, rows per table), takes a **COPY_ONLY** backup with a
checksum (so the real backup chain is not disturbed and the source is only read), verifies it,
restores it under a **new** name, compares table by table, and drops the copy. Plain Windows
PowerShell 5.1; no modules.

Rehearsed here against a throwaway database built from all 45 migrations: 150 tables identical. **Not
run against production** — that needs the server and a quiet window; do it before Phase 2 starts.

---

## 8. Open questions and what is not done

- **CI.** There is no CI configuration in this copy (and no git repository), so "CI green" — the Phase 0
  gate — cannot be wired yet. Which CI does the team use? The suite needs a SQL Server; the tests
  already honour `SHARE7_TEST_SQL_SERVER` for that.
- **Facebook linking** (§5) — keep today's behaviour for students, or require a password confirmation
  before linking Facebook to an existing account?
- **Google audience.** `GoogleLoginValidator` accepts a token issued to *any* Google app when
  `Authentication:Google:ClientId` is not configured. Failing closed in Production would stop Google
  sign-in wherever the setting is missing, so it was not changed without a decision.
- **17 pre-existing failures** — separate task (§6).
- **The EF migrations tool** is blocked on this machine: `.config/dotnet-tools.json` carries the
  Windows "downloaded from another computer" mark. It was not bypassed; the `AuditEvents` migration
  was generated through EF Core's documented design-time API instead. To restore the CLI:
  `Unblock-File .config/dotnet-tools.json` (your call — it is a Windows security marker).
- **Production restore rehearsal** (§7).
