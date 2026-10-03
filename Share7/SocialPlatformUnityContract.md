# Social, Brain Pass and multiplayer extensions — Unity contract

Status: backend implementation and SQL integration checks complete; full-suite and Unity evidence
are recorded in `SocialBrainPassPlatform.md`. No hosted deployment or remote migration has run.
Use the existing authenticated API transport, typed API errors, account lifecycle and player feed.

## Safe identity, discovery and content

All player routes require authentication. Caller identity comes from the token. A private or
unavailable target returns a neutral unavailable result. A generated handle is the player-facing
identifier; email, age, grade, school, guardian contact and private profile names never belong here.
Curated official accounts use staff-approved bilingual display names and badges; a badge grants
no authorization role. Child strangers remain untappable in room and ranking rows.

| Route | Result / mutation |
|---|---|
| `GET /api/social/profiles/{userId}` | Safe equipment, approved showcase, permitted statistics/presence and current action permissions |
| `GET /api/social/directory?after=0` | Approved official identities only; up to 50 records and `nextAfter` |
| `GET /api/social/showcase/content` | Enabled approved definitions, including locked product requirements |
| `PUT /api/social/showcase` | `{keys, expectedVersion}`; select at most 12 approved owned definitions; reload on version conflict |
| `PUT /api/social/following/{userId}` | `{enabled}`; approved official account only, no synchronous follower fan-out |
| `GET /api/social/activity?before=0` | Followed official publications; up to 50, descending sequence |

`CursorPage<T>` carries `items`, nullable `nextAfter`, and `serverTimeUtc`. The activity and inbox
cursor is passed back as **before**, despite the shared envelope field name. Never increment it or
infer missing records. No student name search, public student directory or direct chat is added.

`ShowcaseContentDto`: `key, kind, assetKey, fallbackKey?, productId?, officialOnly, enabled,
minimumQuality (0..2), contractVersion (1)`. Kinds: Character, Scene, Pose, Animation, Camera, Prop,
Effect, Theme. Definitions are immutable except availability; revisions use a new key. Fallbacks
are same-kind, enabled, free, base-quality definitions with no further fallback. Equipment carries
`bodyType` and slot `slotKey, cosmeticKey, colorKey?`; use the existing avatar/equipment factory.
Load only known local asset contracts through the asset provider. Missing/disabled/incompatible
assets keep the existing dressed-avatar fallback. Never download or execute a server asset URL.

Cancel loads on navigation/account switch; release stale and successful leases exactly once.
Bound one visible stage per page, prop/effect count and RenderTexture quality to the device tier.
An official scene is authored content, not a separate hardcoded renderer or account ID branch.

## Safety and guardian consent

| Route | Request / scope |
|---|---|
| `GET/PUT /api/social/privacy` | `profile, presence, statistics, invitations, challenges` (Nobody/Friends/Connections), `friendRequests` |
| `PUT /api/social/mutes/{userId}` | `{enabled}`; filters personal presentation, separate from blocking |
| `POST /api/social/reports` | `{userId, sessionId?, reason, requestId}`; retry the same intent key |
| `GET /api/social/restrictions` | Current restrictions, deadlines, appealed state and appeal resolution code |
| `POST /api/social/restrictions/{id}/appeal` | `{reasonCode}`: mistake/context/resolved; no free text |
| `GET /api/orgs/guardian/social-consent` | The signed-in grown-up's verified active links only |
| `PUT /api/orgs/guardian/social-consent/{linkId}` | `{enabled}`; changes only the existing SocialPlay bit |

Report reasons: UnsafeBehaviour, Impersonation, Cheating, InappropriateContent, Spam. The server
captures evidence from a visible profile or a room containing both participants. Reports grant no
XP and have a daily cap. Sending a report does not itself sanction anyone. Block, consent, privacy
and active social restrictions are rechecked by the existing central social policy at action time.
Temporary/permanent restrictions have a player appeal route. Permanent decisions require a
SuperAdmin. An appeal resolution is a machine code rendered through a static localized template.

The operator console `/social` reads real cases/evidence and pending appeals. Staff decisions and
appeal reviews are audited. `/api/admin/social` owns identities, showcase contracts, official
activity, reports and restriction revocation. No player endpoint can author an official badge.

## Inbox and recovery

`GET /api/inbox?before=0` returns up to 100 current recipient events. Each item carries sequence,
eventId, category, templateKey, type, **sanitized** payload, read state and server times. The payload
whitelist contains GUID navigation identifiers, bounded machine codes/numbers and real deadlines;
legacy configured names, arbitrary URLs, tokens and free text are excluded. Muted/blocked actor
events and disabled optional categories are hidden; the cursor advances over hidden rows too.

`PUT /api/inbox/{eventId}/read` marks only the caller's event. `GET /api/inbox/preferences` and
`PUT /api/inbox/preferences/{category}` with `{enabled}` manage friend/challenge/multiplayer/
brainpass/system categories. Safety and reward notifications remain available. Use the existing
event-id deduplication and SQL feed fallback. A feed gap triggers authoritative refresh, not local
reconstruction. Unknown event types get a safe localized update row with no guessed action.

`reward.granted` is staged once in the existing reward transaction; replay stages nothing. Brain
Pass also emits `brainpass.reward.claimed`. Treat both as refresh signals, not wallet deltas, and
do not celebrate one transaction twice. Friend/team/tournament events resolve current state before
offering Accept, Play, or a detail link. Expired/revoked actions get an actionable neutral state.

## Brain Pass

| Route | Contract |
|---|---|
| `GET /api/brain-pass/current` | `{available, season}`; a real empty season is a 200 response with `available:false` |
| `GET /api/brain-pass/{seasonId}` | Localized name, earning/claim times, XP, premium ownership, objective group, tiers, catchingUp and serverTime |
| `POST /api/brain-pass/{seasonId}/tiers/{number}/{Free|Premium}/claim` | Server payout/replay; no XP or price accepted from client |

Tiers return unlock/claimed/**canClaim** independently plus currency/product previews from real
reward rules. Claim is atomic with the existing wallet/entitlement reward engine. A lost response
can be retried after backend restart, season expiry or disable; a cached successful claim returns
the original grants. A new disabled/expired/unearned/unowned claim is refused. Refresh balances,
entitlements and progression through their existing owners; the UI never grants rewards locally.

XP comes from the existing authoritative result stream: completed/aced lessons, best-percent
improvements, qualifying settled run time and meaningful completed matches. Flagged, unverified,
too-short or non-participating sources earn zero. Result-ID credits survive late commits;
per-source maxima and server-day caps prevent replay farming. Social clicks/follows/reports earn
zero. `catchingUp` means bounded projection is still processing; retain the last server value and
refresh without promising invented progress. Existing objectives own daily/weekly/season quests.

Admin `/brain-pass` saves bilingual drafts and real reward/product references. Publish posts
`{expectedVersion}` to `/api/admin/brain-pass/{id}/publish`, verifies the reviewed saved draft,
rejects overlapping earning windows and freezes terms. Disable pauses new earning/claims while
preserving claim recovery. Published reward configuration cannot be edited through Rewards.
Premium ownership is checked in existing entitlements; no real-currency purchase flow is added.

## Multiplayer additions

Friends, parties, ranked and tournaments use `MultiplayerUnityContract.md`, including friend-request
cancel, typed errors, consent checks, reserved seating, ranked result validation and pairing Play.
RoundRobin is an additive format (up to 16 entrants); existing knockout/Swiss wire values remain.
Every entrant plays each other once with stable seeded rounds and honest byes. A withdrawn or
blocked pairing keeps its place in the schedule and becomes `not_played`; no replacement opponent.

`GET /api/multiplayer/rooms?protocolVersion=1&after=0&gameId=...` lists at most 50 compatible public,
open, nonreserved rooms with space. It returns game/mode/capacity/ranked state, not private room
codes or transport names. The existing atomic Join service grants a seat; discovery grants none.

`/api/social/teams` creates/lists the caller's private consenting rosters; owner invite/removal,
member acceptance/leave and owner disband are separate actions. At most 8 members including pending
invites and 5 teams per player. These do not introduce team wallets, text names/chat or doubles
tournaments. New formats must extend the seat/format contracts deliberately.

Observers are a separate lease, never a player seat. Default-off game/protocol V1 capabilities need
an audited operator enable after a real client adapter is verified. Host opts in only on public,
unranked, nonreserved, non-event rooms. Join/renew uses
`POST /api/multiplayer/sessions/{id}/observe` with protocolVersion; membership returns role
`observer`, transport information and a 90-second expiry. Host reads the authoritative observer
roster, enforces it alongside transport authentication, excludes observers from input/spawn/ready/
scoring/start counts and revokes them when permission/capability/lease is lost. The existing Photon
identity ticket alone is not proof of observer admission. Unknown adapters fail closed.

## Retention and real prizes

Approved on 1 October: terminal full sessions stay 90 days after their server end. Then only a
minimal participant/admin-readable summary remains for one further year. Its expiry is end+455
days even if archival is delayed. No names, room code, transport secret, answer payload, equipment
or whole opponent roster is copied. `GET /api/multiplayer/history/{sessionId}` requires participant
proof; the distinct admin reader is audited. Reward, prize and rating records keep their own rules.

Physical-prize events require verified win criteria. Awards open the existing PendingReview queue.
An operator records eligibility and fraud reviews before AwaitingGuardian; a minor/unknown-age
winner needs confirmation through a real verified guardian link before Fulfilled. Expired claims
cannot be approved/fulfilled. State changes serialize and audit in the claim transaction. No
delivery/payment/address capture or automatic physical fulfillment is introduced. The SQL prize
test rehearses the review/guardian/fulfillment path; real operating capacity still needs an operator
rehearsal before a large prize launch.

## UX and runtime acceptance

Use the UX_PLAYBOOK Integration Brief → EN/AR prototype/references → visual review → Unity importer.
Retain prior screen versions. Real avatars, active-palette tokens, mirrored Arabic layouts, Western
numbers, at least 48dp targets, reduced motion, one sheet and the existing results/reward chain.
Every new region needs loading, slow, empty/locked, error, offline, success and retry/cancel paths.
Account switch/logout cancels reads/mutations/polls/asset loads and clears private state. No old
account response may refresh the new player's screen. Cached reads never enable offline mutations.
Production completeness requires actual client tests, editor previews and live EN/AR flow evidence.
