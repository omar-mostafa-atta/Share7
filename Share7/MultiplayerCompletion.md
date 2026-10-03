# Multiplayer continuation — 1 October 2026

## Resume point and scope

The original audit/design prompt is implemented incrementally by `MultiplayerPlatform.md`, with
the frozen client's contract in `MultiplayerUnityContract.md`. On `main` at `ae8c938`, P0–P3 were
documented as complete and P4 already contained tournament entities, bracket algorithms, service,
controllers, reservations, result-stream metrics and prize-review signals. P4 had no tests or
client handoff, and two changed constructors prevented the test project from compiling.

This continuation completes and verifies that backend foundation. It preserves the existing
endpoints and response snapshots. Future features listed as conditional in the original prompt
remain extension points; this is not authorization to provision large-scale infrastructure or
deploy changes to the shared internal testing backend.

## Completed here

| Work | Evidence |
|---|---|
| Test helper wiring for tournaments and prize-review audit | Test project builds again; real collaborators used |
| Tournament cancellation/Play race | Deterministic interleaving regression; shared tournament lock permits different pairings concurrently but excludes organiser/advance changes |
| Blocks added after seeding | Reads settle unavailable pairings neutrally; Play re-checks blocks before opening or handing over a room |
| Protocol validation on existing tournament rooms | Unsupported/incompatible builds refused even when the other player opened first |
| One non-cancelled tournament per event | Filtered unique SQL index and concurrent-creation regression |
| Limited prize quantity under overlapping payouts | Tier row held across count + award/payment transaction; deterministic race regression |
| Bounded sweeper fairness | Waiting passes rotate `AdvancedAtUtc`; 50 waiting tournaments cannot starve the next due one |
| Tournament correctness | Knockout/Swiss, seed order, byes, blocked pairing, placement, caps, duplicate entry, withdrawal, no-shows, disqualification and idempotent stream/feed completion |
| Actual match integration | Real runs settled and ordinary server verdict consumed; ties open a fresh reserved room |
| HTTP authorization and restart recovery | Real Kestrel process killed/restarted; same Play retry recovers the same room; teacher/class relationship and admin authorization exercised |
| P1 last-seat storm and HTTP harness | 100 concurrent SQL-backed joiners, exactly one winner; reusable measured driver verified over real local HTTP |
| Unity/API handoff | `MultiplayerUnityContract.md` §13; API reference and roadmap updated |

## Migration and rollout

`20261001180000_TournamentEventOwnership` upgrades the event lookup to a filtered unique index.
It does not rewrite any result, entry or award. If an existing database contains multiple
non-cancelled tournaments for an event, the migration intentionally stops: an operator must
reconcile those brackets before retrying. Find conflicts before deployment:

```sql
SELECT EventId, COUNT(*) AS Brackets
FROM Tournaments
WHERE EventId IS NOT NULL AND State <> 'CANCELLED'
GROUP BY EventId HAVING COUNT(*) > 1;
```

Use the repository's normal staged deployment after reviewing this change. Do not disable anonymous
Photon access until the ticket-sending client ships. The frozen client continues to call its
existing routes; all tournament routes are additive. No upload or remote migration was performed
by this continuation.

## Deliberate next stages

| Item | Boundary / prerequisite |
|---|---|
| Unity screens for friends, parties, ranked, tournaments | Approved Integration Brief → prototype → EN/AR references → Unity; no client screen implemented by this backend task |
| Guardian-facing consent switch and player reporting | Existing guardian consent tools remain authoritative; a real moderation reader/operations workflow must accompany reporting |
| Session archival | Agree retention and historical read/privacy policy before removing or moving any database history; no arbitrary expiry introduced |
| Battle pass / new seasonal rewards | Consumers of the existing result stream; no hardcoded multiplayer reward system |
| Public room browser, spectators, teams/new tournament formats | Deliberate product decisions and additive format/seat contracts, not a rewrite of seating or matchmaking |
| Real prizes at scale | Existing pending-review/guardian claim path, verified win criteria, fraud/eligibility review and operational rehearsal; no automatic physical fulfilment |
| P5 Redis, SignalR/backplane, regional workers | Build only when measured deployment limits justify each component |

## Validation

Detailed commands, results and limits are recorded in `MultiplayerPlatform.md` §0.5. SQL tests
create isolated temporary databases; the HTTP tests start local hidden API processes. The load
driver's local smoke run verifies the driver, not 10K+ capacity or Photon performance. Redis
failure tests are inapplicable because multiplayer has no Redis dependency. Production rolling
deployment, multi-region latency and physical-prize fulfilment remain operational rehearsals.

Full suite: **1,068 passing / 17 pre-existing failures / 0 skipped**, including 40 added checks.
All new tests and frozen contract tests pass. After final harness cleanup reporting was tightened,
its real HTTP/SQL check passed again. The full suite remains red in the documented leaderboard,
reward and signal-economy fixture group; this continuation does not claim otherwise.
