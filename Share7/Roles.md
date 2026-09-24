# Roles

The single reference for **who can do what** on the platform. Companion to `@Auth.md`
(how a token is obtained and what is in it) and `@ApiReference.md` (what each endpoint
does). This file answers only: which of the four roles is allowed to call it.

Every statement below was read out of the code, not out of the design docs. Where the
two disagree, the code is what ships and this file follows the code.

> **2026-09-21 — the content team.** A fifth role, `ContentTeam`; a way for an admin to create
> an account with a username, password and role (`POST /api/admin/users`); the first named
> authorization policies; and a Content Portal in the console at `/content`. See
> [ContentTeam](#contentteam) and [Named policies](#named-policies). Sections below were updated
> where the change made them false; the endpoint counts in the tier table were not re-audited
> and are marked as such.

> **2026-09-21 (later) — Content Studio Phase 0.** The first SuperAdmin can now be created from the
> one-time `SeedSuperAdmin` config section; Production refuses to create the seed admin with the
> default password; every curriculum, question and account change is written to an append-only
> `AuditEvents` table; Google/Facebook sign-in no longer links onto staff or privileged accounts, or
> with an address Google reports unverified. Details in `ContentStudioPhase0.md`. Items §1, §3, §8
> and §9 below carry notes saying what is done and what is still open.

> **2026-09-24 — cutover.** The content team no longer has a role *on this API in the sense this
> file means*. They have a **Studio role** (Author, Reviewer or Lead), on a separate token with its
> own audience, scoped to parts of the curriculum and to the languages they work in — and those are
> not the five roles below. What the `ContentTeam` Identity role now does is mark an account as
> belonging to the Studio: it opens nothing here, and `POST /api/auth/login` refuses it outright.
> The Content Portal at `/content` is gone with it. See [ContentTeam](#contentteam),
> [Named policies](#named-policies) and `ContentStudioPhase6.md`.
>
> A note on scope, because this file is the one people read for "who can do what": the Studio's own
> roles are **not documented here**, deliberately. They gate a different token on a different
> scheme, and folding them into this table would suggest a Lead is a bigger Admin. They are in
> `ContentStudioPhase1.md` and `ContentStudioPhase3.md`.

---

## The roles

Declared in `Share7.Domain/Constants/Roles.cs` and seeded into `AspNetRoles` on every
startup by the `Roles.All` loop in `Program.cs`:

| Role | Exists in DB | Assigned by | Users who can hold it |
| --- | --- | --- | --- |
| `Student` | yes | automatically, on every registration and first external login; or an admin, via `POST /api/admin/users` | everyone |
| `Teacher` | yes | **nothing — no code path assigns it**, and account creation refuses it | nobody |
| `Admin` | yes | the startup seed, to the `admin` account; or a SuperAdmin, via `POST /api/admin/users` | the seed account |
| `SuperAdmin` | yes | the one-time `SeedSuperAdmin` config section on startup (only while none exists); or a SuperAdmin, via `POST /api/admin/users` | whoever the bootstrap created |
| `ContentTeam` | yes | a **SuperAdmin**, via Team & Access (`POST /api/admin/team`), which also makes the Studio profile | staff who author in the Content Studio — and who may sign in *nowhere on this API* |

`Teacher` and `SuperAdmin` are still rows in a table and nothing else. See
[What should change](#what-should-change).

### How a role reaches a request

`AuthService.IssueTokensAsync` reads the user's roles from Identity and
`JwtTokenGenerator` writes one `ClaimTypes.Role` claim per role into the access token.
Authorization is therefore **as stale as the token** — up to 30 minutes
(`AccessTokenExpirationMinutes`). Granting or removing a role in the database does not
take effect until the user's current access token expires and is refreshed. There is no
revocation check on the access-token path.

`RegisterRequest` has no role field, so a client cannot ask for a role at signup.

---

## Permission tiers

Every endpoint falls into one of these tiers. There is no per-object ownership model
beyond "the caller's own id, taken from the token".

| Tier | Endpoints | Gate |
| --- | --- | --- |
| Anonymous | 9 | `[AllowAnonymous]` |
| Any signed-in user | 58 | `[Authorize]` |
| What is left of the content surface — Admin **or** SuperAdmin | 21 routes, of which the writes now answer `410` | `[Authorize(Policy = Policies.ContentAuthoring)]` |
| The Content Studio — *not a tier in this table* | ~90 | the Studio scheme, `Policies.Studio*` |
| Admin **or** SuperAdmin | 72 | `[Authorize(Roles = "Admin,SuperAdmin")]` |
| SuperAdmin only | 2 conditional branches | `User.IsInRole(Roles.SuperAdmin)` in code |

> The 9 / 58 / 72 counts are from the original audit of 139 endpoints and predate a great deal
> of growth. A 2026-09-21 grep of the controllers finds roughly 286 routes: 21 content-authoring,
> 159 admin-only, 94 any-signed-in, and 12 on controllers with no class-level gate (mostly
> `[AllowAnonymous]`). The tier *structure* above is current; re-audit the numbers before quoting
> them.

**`Student`, `Teacher`, `Admin` and `SuperAdmin` are all identical inside the
"any signed-in user" tier.** No endpoint anywhere restricts a student *out* of something
another role gets. A role only ever adds; it never subtracts.

---

## Student

The role every real user holds.

### Can

**Account and identity**
- Register, log in, refresh and revoke tokens, log in with Google or Facebook.
- Complete their profile once (`POST /api/auth/complete-profile`) and edit it afterwards
  (`PUT /api/users/profile` with no `userId`).
- Read their own profile with contact details included.
- Read **any other user's** profile — `GET /api/users/profile?userId=…` — with
  `phoneNumber` and `email` withheld (`revealContact: isSelf || callerIsAdmin` in
  `UserProfileService`). Names, age and grade are visible to any signed-in caller.
- Set their content language, which reissues the token pair.
- Read and replace their own avatar equipment, and **read anyone else's** equipment
  (`GET /api/users/me/equipment?userId=…` — deliberately open, since an outfit is
  already visible on screen in a match).
- **Permanently delete their own account** (`DELETE /api/users/me`) — immediate,
  irreversible, no grace period; purges profile, progress, unlocks, balances, ledger and
  every refresh token.

**Curriculum and play**
- Browse the whole tree: grades, terms, subjects, chapters, lessons.
- Read published questions and recovery questions for any lesson, plus the version
  handles used for client-side caching.
- List games and read a single game's definition.
- Submit progress attempts, read progress at every level of the tree, read wrong-question
  lists, read a whole-game snapshot.
- Start a run and post its result.
- Read their own level/XP progression and claim completed objectives.

**Economy**
- List currencies and read their own balances.
- Read offers and today's offers, purchase, read their own entitlements.

**Social**
- Read leaderboards, cycles, entries, their own rank, their around-me window and their
  own settlement; control their own leaderboard visibility.
- Full multiplayer: create a session (becoming its host), start, join, leave, close,
  heartbeat, transfer host, matchmake, read sessions and rosters.

### Cannot

- Reach anything under `/api/admin/**` → `403`.
- Create or edit curriculum, questions, games, objectives, offers, products, product
  kinds, product grants, reward rules, the level curve, leaderboard boards/cycles/bounds,
  or pickup valuations.
- Create or edit currencies, or grant themselves currency.
- Read another user's `email` or `phoneNumber`.
- Edit another user's profile — `UserProfileService.UpdateAsync` refuses when
  `userId != callerId && !callerIsAdmin`.
- Delete another user's account.
- Review flagged runs or flagged leaderboard results, or close someone else's
  multiplayer session through the admin route.
- Assign themselves or anyone else a role. The only route that grants a role is
  `POST /api/admin/users`, which creates a new account and is admin-only.

### Not a role, but adjacent: multiplayer **host**

`MultiplayerSessionService` enforces a second, session-scoped authority that has nothing
to do with Identity roles. The host is whoever created the session (`HostUserId`), and
only the host may `start`, `close` or `heartbeat` it. A non-host may claim the host role
only after the current host has been silent for `HostClaimGraceSeconds`. Losing host
authority is communicated as a `403` on the next heartbeat. A student is routinely a
host; an admin gets no special power over a live session except through
`POST /api/admin/multiplayer/sessions/{id}/close`.

---

## Teacher

### Can

Exactly what a Student can, and nothing more. The string `Roles.Teacher` appears in the
codebase **once** — its own declaration. It is in no `[Authorize]` attribute, no
`IsInRole` check, no policy, and no service parameter.

### Cannot

Everything an Admin can do, and everything the design intended a teacher to do. There is
no class, enrollment, or teacher–student relation anywhere in the schema
(`Architecture.md` notes this too), so "this teacher's students" is not expressible even
if the checks existed.

**A Teacher account cannot be created**: registration hardcodes `Roles.Student`, and no
endpoint assigns roles. The role exists only as a seeded row in `AspNetRoles`.

---

## Admin

Held today by exactly one account: the `admin` user created by the startup seed.

### Can

Everything a Student can, plus all 72 admin endpoints:

| Area | Route prefix | What it allows |
| --- | --- | --- |
| Curriculum | `/api/admin/{grades,terms,subjects,chapters}/…` | add and delete terms, subjects, chapters, lessons — `?force=true` cascades |
| Questions | `/api/admin/lessons/{id}/questions…` | upload a sheet, type questions by hand, read the published set for an explicit language |
| Recovery questions | `/api/admin/lessons/{id}/recovery-questions…` | the same three, for the recovery set |
| Games | `/api/admin/games` | list, read for authoring, create, update, delete |
| Objectives | `/api/admin/objectives` | full CRUD |
| Level curve | `/api/admin/progression/levels` | read and replace the whole XP curve |
| Reward rules | `/api/admin/reward-rules` | list, create, update |
| Products | `/api/admin/products` | full CRUD |
| Product kinds | `/api/admin/product-kinds` | full CRUD |
| Product grants | `/api/admin/product-grants` | full CRUD |
| Entitlements | `POST /api/admin/entitlements` | hand any product to **any account** for free |
| Offers | `/api/admin/offers` | list, read, create, delete |
| Leaderboards | `/api/admin/leaderboards/…` | create/update boards, create cycles, rebuild, settle, set bounds, list flagged results, resolve flags |
| Multiplayer | `/api/admin/multiplayer/sessions` | list any session, read any roster, force-close any session |
| Runs | `/api/admin/…` | pickup-valuation CRUD, list flagged runs, read any run, review a run |
| Users | `DELETE /api/admin/users/{id}` | hard-delete a **non-privileged** account |
| Users | `POST /api/admin/users`, `GET …/assignable-roles` | create an account with a username, password and one role — `Student` or `ContentTeam` |
| Currencies | `/api/currencies` | create and update currencies |

Plus two elevations on the ordinary user endpoints, both driven by
`UsersController.IsAdmin()`:
- **Read any user's contact details** — `email` and `phoneNumber` are no longer withheld.
- **Edit any user's profile** — `PUT /api/users/profile?userId=…`.

And one self-scoped economy power:
- `POST /api/currencies/grant` mints currency **to the caller only**. It is deliberately
  not a target-user route; topping up a test account is its whole purpose.

### Cannot

- **Delete another Admin or SuperAdmin.** `UserAdminService` refuses with
  *"Only a Super Admin can delete an Admin or Super Admin account."* Since no SuperAdmin
  exists, **no privileged account can be deleted through the API at all.**
- **Delete their own account through the admin route** — `DELETE /api/admin/users/{id}`
  refuses when the target is the caller. (They can still use `DELETE /api/users/me`,
  which has no such guard. An admin can delete themselves; they just cannot do it
  through the admin endpoint.)
- **Create an Admin or SuperAdmin account.** `UserAdminService.CreateUserAsync` refuses with
  *"Only a Super Admin can create an Admin or Super Admin account."* — the same line the delete
  path draws.
- **Change the roles of an existing account.** Creation sets one role; nothing edits it
  afterwards. Promoting an existing user to Admin still requires a manual `INSERT` into
  `AspNetUserRoles`.
- Grant currency to another user (only entitlements can be granted to a third party).
- Escape the rate limiter — the 240/min global limit and the 60/min write limit apply to
  admins exactly as they do to students.
- Drive leaderboard maintenance without the shared key. `POST /api/leaderboards/maintenance`
  is `[AllowAnonymous]` and gated on `X-Maintenance-Key`, not on a role. An admin token
  buys nothing there; an empty configured key closes the endpoint to everyone.

---

## SuperAdmin

### Can

Everything an Admin can, plus **two** things:

- Delete an account that holds `Admin` or `SuperAdmin` (`AdminUsersController` passes
  `User.IsInRole(Roles.SuperAdmin)` into `UserAdminService.DeleteUserAsync`).
- Create an account that holds `Admin` or `SuperAdmin` (the same flag, passed into
  `UserAdminService.CreateUserAsync`).

Those two conditionals are the **entire** difference between Admin and SuperAdmin in the
codebase. Every other admin route accepts both roles interchangeably.

### Cannot

- Delete their own account through the admin route (the self-target guard runs first,
  before the privilege check).
- Change the roles of an existing account. Same gap as Admin.
- **Exist.** Only a SuperAdmin can create one, and `Program.cs` grants the seed account
  `Roles.Admin`, never `Roles.SuperAdmin`. The role's powers are currently unreachable.

---

## ContentTeam

Staff who build the curriculum and author its questions — **in the Content Studio, which is a
different application with a different token**. On this API the role is a marker and nothing more.

### Can

**Nothing here.** Not one endpoint. `POST /api/auth/login` checks the role after the password and
refuses the account with a sentence naming the Studio, so no token for this API is ever minted for
them; `NeverLinkedRoles` has refused them Google and Facebook since Phase 0.

Their work happens on `/api/studio/**`, which reads a **Studio token only** — a different audience,
a different signing key, and a Studio role re-read from the database on every request rather than
carried as a claim. A game or admin token is not even inspected there, and a Studio token is not
read anywhere else.

### Cannot

- Sign in here at all. That is the change cutover made, and it is the one that matters: until then
  a content-team member had two doors, and only one of them put a reviewer between their work and a
  child.
- Reach `/api/admin/**`. They came off `Policies.ContentAuthoring` at cutover — a grant nothing can
  exercise is still a hole waiting for the day somebody reopens a door for an unrelated reason.

### What replaced it

| Was | Is |
| --- | --- |
| The Content Portal at `/content` | The Content Studio, `Share7.Studio`, on its own origin |
| `POST /api/auth/login` | `POST /api/studio/auth/sign-in`, with optional 2-step and a rotating session |
| An account made on the Users page | An account made in Team & Access by a SuperAdmin, activated through a one-time setup link |
| Publish on save | Draft → review → release, with no self-approval and one-click rollback |
| One role for everyone | Author, Reviewer or Lead, scoped to parts of the curriculum and to languages |

---

## Anonymous

### Can

- `POST /api/auth/register`, `login`, `external-login`, `refresh`, `revoke`
  (all rate-limited to 20/min per IP).
- `GET /api/grades` and `GET /api/languages` — the signup screen needs them before a
  token exists.
- `GET /api/time` — server clock, for client drift correction.
- `POST /api/leaderboards/maintenance` **with a valid `X-Maintenance-Key`**. This is a
  machine caller, not a role: the key is compared in fixed time, and an unset key closes
  the endpoint rather than opening it.

### Cannot

Anything else — every other route requires a token.

---

## Where the enforcement lives

Two layers, and it is worth knowing which is which:

1. **API layer, declarative.** An `[Authorize]` attribute on the controller — the real gate.
   Most admin controllers still spell the roles out, `[Authorize(Roles = "Admin,SuperAdmin")]`.
   The content controllers name a policy instead; see [Named policies](#named-policies).
2. **Application layer, as a parameter.** `IUserProfileService` and `IUserAdminService`
   take a `bool callerIsAdmin` / `bool actorIsSuperAdmin` supplied by the controller from
   `User.IsInRole(...)`. The services never read `HttpContext`, which keeps them testable
   (`UserProfileServiceTests` exercises both values) — but it also means a future caller
   that passes `true` bypasses the check. The flag is trusted absolutely once inside.

### Named policies

`Share7/Authorization/Policies.cs` names kinds of work; `AuthorizationExtensions.cs` maps each
to roles, in one place:

| Policy | Roles | Used by |
| --- | --- | --- |
| `ContentAuthoring` | Admin, SuperAdmin | `AdminCurriculumController`, `AdminLessonQuestionsController`, `AdminLessonRecoveryQuestionsController`, `AdminLessonSheetController`, `AdminCurriculumInsightController` |
| `ManageStaff` | SuperAdmin | Team & Access and the audit log |
| `StudioSession` / `StudioMember` / `StudioAuthor` / `StudioReviewer` / `StudioLead` | *not roles from this table* | `/api/studio/**`, on the Studio scheme only |

`ContentAuthoring` kept its name and lost its meaning: since cutover every **write** it guarded
answers `410 Gone` (`ClosedAtCutoverAttribute`), so what it actually gates now is the reads left
beside them — curriculum health, the question search, and reading a lesson's published set. The
name was kept because it still marks the same surface, and renaming a policy edits every controller
that names it without changing what any of them do.

`ContentCascadeDelete` is gone. It existed to keep a force-delete away from the content team, and
there is no force-delete left to keep away from anybody: the engine rebuild replaced deleting with
retiring, and cutover closed the route.

### The console

Share7.Web mirrors the policy table in `src/lib/access.ts` to decide what to *show*. It is
presentation, not a gate — the API refuses regardless — but the two tables should change together.
Routes are wrapped in `ProtectedRoute` with the portal's permission, so a student who signs in is
refused at the login screen rather than shown pages that 403.

There is one portal now: `console.admin`, for Admin and SuperAdmin. The Content Portal and its
`portal.content` permission went at cutover, along with `content.cascadeDelete`. The portal
machinery in `src/lib/portals.ts` was kept rather than folded into the shell, because a second
audience — teachers, say — is still an entry there, a nav list and a routes component.

The authoring pages went with it. `/curriculum`, `/quality`, `/targets` and `/content` still
resolve, to `routes/Moved.tsx`: an admin following an old link is told where the work went, which
a silent redirect to the dashboard would not do. They are not in the sidebar.

(The hand-written console this replaced checked only `isSignedIn()`, which is what the original
version of this section described. It was deleted at cutover; a copy is in
`ops/archive/share7-front-2026-09-24.zip`.)

---

## What should change

Ordered by what would hurt most if left alone.

### 1. The seeded admin still uses the default password — fix first

> **Partly done, 2026-09-21.** In Production the seed admin is no longer *created* with a missing or
> default password, and an existing admin still on `Admin123` is reported at Critical on every start
> (`IdentitySeeder`). The deployed server's password must still be checked and changed by hand.

`Program.cs` falls back to `admin` / `Admin123` / `admin@admin.com` when the `SeedAdmin`
configuration section is absent. **No `SeedAdmin` section exists in `appsettings.json`,
`appsettings.Development.json`, or `appsettings.Production.json` in this working copy.**
The host is public. `Auth.md` already flags this; it is still unfixed.

> Set `SeedAdmin:Username` and `SeedAdmin:Password` in `appsettings.Production.json`
> before the next publish. Note that publish never deletes, so the currently-deployed
> `appsettings.Production.json` is whatever was last uploaded — verify it on the server
> rather than trusting this copy.

Consider also making the seed **refuse to run** with the default password when
`ASPNETCORE_ENVIRONMENT=Production`, rather than silently creating a guessable admin.

### 2. Add a way to grant and revoke roles

> **Partly done, 2026-09-21.** `POST /api/admin/users` creates an account *with* a role
> (SuperAdmin-only for Admin/SuperAdmin). Changing the roles of an existing account is still
> not possible, and still the point of this item.

There was no endpoint. Every promotion is a hand-written SQL `INSERT` into
`AspNetUserRoles` against the production database — the highest-privilege operation on
the platform, performed by the least auditable means available.

> Add `GET/PUT /api/admin/users/{id}/roles`, SuperAdmin-only, refusing self-demotion and
> refusing to grant a role above the caller's own. Return the new roles so the console can
> reflect them. This also gives the change an audit row instead of a DBA's memory.

A related consequence worth knowing: `DELETE /api/users/me` has **no role guard**
(`AccountDeletionService` never looks at roles), so the only Admin on the platform can
delete themselves and leave nobody able to administer anything. The startup seed does
recover from this — it recreates `admin` if the username is missing — but only on the next
restart, and it recreates the account with whatever `SeedAdmin` says, which today is the
default password from #1.

### 3. Seed a SuperAdmin, or delete the role

> **Done, 2026-09-21 — kept.** `SeedSuperAdmin` creates the first SuperAdmin once; see
> `ContentStudioPhase0.md` §4. Giving the role more weight (#5) follows with Team & Access, where
> only a SuperAdmin will manage content-team accounts.

Right now `SuperAdmin` is a role nobody can hold, guarding a branch nobody can reach, so
`DELETE /api/admin/users/{id}` cannot remove any privileged account. Two coherent
options — pick one, do not leave it as-is:

- **Keep it**: seed one SuperAdmin from configuration alongside the admin seed, and give
  the role real weight (see #5).
- **Drop it**: delete `Roles.SuperAdmin`, and change the delete guard to "an Admin cannot
  delete another Admin", with a break-glass path outside the API.

### 4. Decide what `Teacher` is, then build it or delete it

A role that exists in the database, is documented in `CLAUDE.md` and `Architecture.md`,
and grants nothing is worse than no role: it reads as a working feature to anyone
skimming the constants file, and someone will eventually assign it — silently granting
nothing.

The blocker is structural, not authorization: **there is no class, enrollment, or
teacher–student relation in the schema**, so no teacher endpoint could scope its results
to "my students". That modelling work has to land first.

> Short term, the honest move is to delete `Roles.Teacher` and reintroduce it with the
> enrollment model. If it must stay for roadmap reasons, mark it in `Roles.cs` as
> reserved-and-unimplemented so nobody assigns it expecting behaviour.

### 5. Make the Admin/SuperAdmin split mean something

Eighteen controllers say `Admin,SuperAdmin`; one code branch says `SuperAdmin`. As a
privilege tier that is not a tier — it is a single exception. Either collapse it (#3) or
move the genuinely destructive operations behind SuperAdmin:

- role assignment (#2)
- `?force=true` cascading deletes of terms/subjects/chapters/lessons, which can remove
  authored content and student progress in one call
- currency creation and `POST /api/admin/entitlements`
- leaderboard cycle settlement

### 6. Replace the 18 repeated attributes with named policies

> **Started, 2026-09-21.** `Policies` / `AddShare7Authorization` exist and the five content
> controllers use `Policies.ContentAuthoring`. The remaining admin controllers still spell the
> roles out.

`[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]` is copied verbatim 18 times.
Every future admin controller is one forgotten attribute away from being public, and the
compiler will not notice.

```csharp
// Program.cs
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.ManageContent, p => p.RequireRole(Roles.Admin, Roles.SuperAdmin))
    .AddPolicy(Policies.ManagePrivilegedAccounts, p => p.RequireRole(Roles.SuperAdmin));
```

Then `[Authorize(Policy = Policies.ManageContent)]`. Widening the admin tier becomes one
edit instead of eighteen, and #5 becomes a policy change rather than an audit of every
controller.

### 7. Gate the admin console on the client

> **Done** in Share7.Web: sign-in refuses an account that can open no portal, and each portal's
> routes check the portal's permission — see [The console](#the-console).

> **Closed, 2026-09-24.** The page this described — `admin.html`, whose `guardAuth()` checked only
> that somebody was signed in — was deleted at cutover. A student who found it used to get a guided
> tour of every administrative capability the platform has, with only the responses failing. There
> is no such page any more: `Share7.Web` checks the role before it draws anything, and the only
> other front end, the Studio, refuses a game token outright.

### 8. Audit privileged actions

> **Started, 2026-09-21.** `AuditEvents` exists, append-only by trigger, and records curriculum
> node adds and deletes, every question publish, account creation and deletion, and the SuperAdmin
> bootstrap. Run review, flag resolution, cycle settlement, currency and entitlement changes are not
> recorded yet. There is no viewer yet.

Only `POST /api/admin/entitlements` records who acted (`source: "ADMIN_GRANT"` plus the
admin's id). Content deletion, run review, flag resolution, cycle settlement, currency
creation and user deletion leave no attributable trace — and user deletion is a hard
delete with a full data purge behind it.

> An `AdminAuditLog` row (actor id, action, target, timestamp, request id) written in the
> same transaction as each privileged mutation. This becomes considerably more urgent once
> #2 exists and more than one person holds Admin.

### 9. Close the external-login account-linking gap

> **Partly done, 2026-09-21.** Linking now refuses Admin, SuperAdmin and ContentTeam accounts, and any
> address Google reports unverified. Still open: Facebook reports no verification flag, so Facebook
> linking to a *student* account still follows the email alone; and the Google validator accepts any
> audience when `Authentication:Google:ClientId` is unset. Both are awaiting a decision.

`AuthService.ExternalLoginAsync` falls back to `FindByEmailAsync` and links the external
identity to whatever account carries that email — and neither `GoogleLoginValidator` nor
`FacebookLoginValidator` checks a verified-email flag on the provider's payload. Anyone
who can present a provider token asserting an admin's email address inherits that admin's
account and its roles, without ever knowing the password.

> Check the provider's verified-email flag before linking, and require an explicit
> confirmation step (or the account password) before attaching a new external login to an
> account that already holds a privileged role.

### 10. Shorten the role-staleness window, or check it

Roles are baked into a 30-minute access token and never revalidated. Removing someone's
Admin role leaves them fully privileged for up to half an hour. Acceptable today with one
admin; not acceptable once #2 ships and roles change routinely.

> Cheapest fix: on the admin-tier policy only, revalidate the role against Identity on
> each request. More thorough: a token version stamped on the user, bumped on any role
> change, and compared during token validation.
