# Content Studio — Phase 1: Team & Access, and Studio sign-in

**2026-09-21/22.** Content-team accounts, the Studio's own sign-in, and the SuperAdmin area that
creates and governs them. This is the "AAA" of the plan: **A**uthentication (who gets in),
**A**uthorization (what they may do) and **A**ccounting (a permanent record of what they did).

This file covers the backend, which is built and tested. The screens — Team & Access in the Admin
Console, and the Studio's own sign-in, activation and account pages — are the UI half of the phase
and are built with the `impeccable` skill; the Studio's look was decided on 22 Sep 2026
("The Class Board").

---

## 1. Accounts

A content-team member is an ASP.NET Identity account with the `ContentTeam` role and a
`StaffProfile`: full name, job title, optional work email (contact only — never used to link a
Google or Facebook sign-in), Studio role, scope, interface language, status and a trail of who did
what to the account.

**Only SuperAdmins create them** (`ManageStaff`, and `UserAdminService` refuses `ContentTeam` with a
message pointing at Team & Access). Regular Admins keep creating student accounts.

Lifecycle: **Invited → Active ⇄ Suspended → Deactivated.**

- **Suspend** and **deactivate** lock the Identity account, move its security stamp, end every Studio
  session, revoke legacy refresh tokens and revoke any live setup link. Deactivate also removes the
  password. A suspended member is out within 60 seconds — the per-request check's cache window.
- **Reset access** issues a new setup link and clears the password, optionally clearing 2-step.
- **Staff accounts are never deleted.** Their name stays on everything they authored, reviewed and
  published; `AccountDeletionService` and `UserAdminService.DeleteUserAsync` both refuse them.

**Setup links** carry a secret only in the URL fragment (`{Studio:PublicUrl}/activate#{secret}`), so
it never reaches a server log or a referrer. Only a SHA-256 hash is stored; links last 72 hours by
default and only one is live at a time. There is no email (approved default #7): the SuperAdmin sees
the link once and hands it over.

**Scope** is parts of the curriculum (or all) × languages (or all). It limits what a member may
change — never what they may see.

---

## 2. Roles

| Role | May |
|---|---|
| **Author** | write drafts inside their scope and languages, and submit them |
| **Reviewer** | that, and approve or send back other people's drafts |
| **Lead** | that, and build, schedule, publish and roll back releases, assign work, and retire a branch |

Nobody approves their own work, and SuperAdmins do not enter the Studio (approved default #4): they
see everything through Team & Access and the audit log.

---

## 3. Signing in to the Studio

Separate from the game's sign-in in every way that matters.

1. **Activation.** The member opens their setup link, sees who they are and what they may do, and
   chooses a password. The link is spent.
2. **Password.** At least 12 characters (a SuperAdmin can raise the floor), upper and lower case and
   a digit, not a common password, and not containing the username.
3. **2-step**, optional per member and switchable to required for everyone by a SuperAdmin: TOTP
   through Identity's authenticator provider, with replay protection and ten recovery codes. The
   challenge between password and code is a five-minute token of its own.
4. **Session.** A 15-minute access token for the `Share7.Studio` audience — signed with a key derived
   from the platform secret, so a game or admin token is not even readable here — plus a rotating
   refresh token in an HttpOnly, `SameSite=Strict` cookie scoped to `/api/studio/auth`. Refreshing
   requires an `X-Studio-Request` header, which another origin cannot add without a CORS preflight
   the API never grants.
5. **Every Studio request re-checks the account** against the database (cached 60 seconds): still
   active, security stamp unchanged, role and scope as they are now — never as the token claims.
   The same check now guards Admin and SuperAdmin tokens (fixes H2 for the whole back office).

Refresh tokens rotate with a 30-second grace; reuse after that ends the session and records it.
Changing a password or 2-step ends every other session and carries the current one over.

**Nothing about this is jargon on screen** (decided 22 Sep 2026): the Studio says "2-step", "backup
codes" and "signed out", never "TOTP", "token" or "session".

---

## 4. Accounting

Every Team & Access action and every Studio account action is one row in the append-only
`AuditEvents` table (`team.*` and `studio.*`), written in the same transaction as the change, with
ids and never personal details. SuperAdmins get a viewer with filters and CSV export.

Sign-ins are recorded with time, IP and device, successful or not.

---

## 5. The transition (decided 22 Sep 2026)

The old sign-in stays until Phase 4. `/content` keeps using `/api/auth/login` for everyone, the
Studio's rules apply inside the Studio only, and staff can still sign into the game until cutover.
Suspending or deactivating still locks the Identity account, so the old path refuses them too — at
worst 30 minutes later, when their old token expires.

---

## 6. How it is proven

```bash
dotnet test Share7.Tests/Share7.Tests.csproj --filter "FullyQualifiedName~TeamAccessTests|FullyQualifiedName~StudioSignInTests"
```

The member lifecycle, setup links (expiry, single use, replacement), activation, sign-in with and
without 2-step, recovery codes and their replay protection, suspension taking effect within the
cache window, the per-request privileged-token check, and the password policy. Phase 3 adds a smoke
test that signs a member in over HTTP and works a draft through the API.

## 7. Still open

- ~~The Team & Access screens~~ — built 26 Sep 2026, see §8. The Studio's own pages were built in
  Phase 4.
- **CI**, Facebook linking policy, and whether Google's audience check should fail closed — all three
  still need a decision.

## 9. The admin sets the password; the Studio has one address (26 Sep 2026, later)

**Decided by the product owner:** no setup link and no activation. Whoever creates a content-team
member types their username **and password**, and the member signs in straight away. And the Studio
has **one constant address** for every role, to hand to anybody in the content team.

- `CreateTeamMemberRequest` takes a `Password`, held to the staff rules (`StaffPasswordPolicy`: at
  least `MinimumPasswordLength`, 12 by default; upper, lower, digit; not the username; not a common
  one). With it the member is created **Active**, with no link. The console always sends it. A request
  without one still gets the old link, so nothing that relied on it broke.
- `POST /api/admin/team/{id}/password` (SuperAdmin) sets a new password: signs the member out
  everywhere, withdraws any link, and activates a member who never had one. Audited as
  `team.member.password_set`. It replaces "Reset access" in the console.
- **The Studio lives at `/studio`.** In production the API serves the built Studio there, on the same
  site as the Admin Console (`StudioHosting`, `app.Map("/studio")`, before the console's fallback;
  `Studio:Host` is gone). Locally the Vite dev server serves it at `http://localhost:5174/studio`
  (`base: '/studio/'`, router `basename`, and a redirect from the bare port). Both dev servers now use
  `strictPort`: the console had none, so a busy 5173 moved it onto 5174 and the Studio's address
  opened the Admin Console's sign-in — the bug the product owner hit.
- The Admin Console shows the address, username and password once after creating a member, and Team
  & Access shows the address at the top. `Studio:PublicUrl` sets the address
  (`http://localhost:5174/studio` in Development); when it is empty the console shows `/studio` on
  its own site, which is where the API serves it.
- **Trade-off, said plainly:** the Studio now shares an origin with the Admin Console, which the
  separate host name had avoided. Its sign-in is unaffected — access token in memory, refresh token
  in an HttpOnly cookie scoped to `/api/studio/auth`, Studio-audience tokens only.

Proven by `AdminSetPasswordTests` (created with a password → Active, no link, signs in; weak
passwords refused with nothing created; setting a password activates a never-activated member and
withdraws their link; a new password ends every session and the old one stops working) and
`AdminAccountsTests` (over HTTP: an Admin's member comes back Active with no link; an Admin cannot
set a password afterwards). In the browser: an Admin created a Lead with a generated password, and
that Lead signed in at `http://localhost:5174/studio` straight away; `/`, `/studio` and `/Studio` on
the API answer with the console, the Studio and the Studio.

## 8. Team & Access screens, and who creates accounts (26 Sep 2026)

**Decided with the product owner on 26 Sep 2026:**

1. **An Admin creates an account of every role but SuperAdmin** — Students and Admins on the Users
   page (`POST /api/admin/users`), and content-team members, with their Studio role, scope and
   one-time setup link (`POST /api/admin/team`). Only a SuperAdmin creates a SuperAdmin: one an
   Admin could mint is one they could sign in as.
2. **Creating is all an Admin does to the content team.** Everything after the account exists —
   the team list, a member's record, changing their profile, role or scope, suspending, resetting,
   closing, their sessions — and the audit log and staff security stay SuperAdmin-only. Deleting an
   Admin stays SuperAdmin-only too.
3. **Layout: one ledger.** A dense, searchable table of members; a row opens in place into that
   member's full record. The audit log is the same ledger over what everybody did.

**Server.** Two policies draw the line: `AddTeamMembers` (Admin, SuperAdmin) on a controller of its
own, `AdminTeamAddController`, which holds only `POST /api/admin/team` and
`GET /api/admin/team/scope-options`; and `ManageStaff` (SuperAdmin) on `AdminTeamController` and
`AdminAuditController`, unchanged. Keeping the two actions in their own class means nothing else in
Team & Access can be opened to Admins by a forgotten attribute. `GetAssignableRoles` offers an Admin
`Student, ContentTeam, Admin` and a SuperAdmin those plus `SuperAdmin`; `CreateUserAsync` still
refuses `ContentTeam`, which the console sends to the team endpoint.

**Console (`Share7.Web`).**

- **Users → Add a user**: the role comes first. Student and Admin get a username and password as
  before; *Content team* turns the dialog into the member form — who they are, their Studio role,
  what part of the curriculum and which languages — and it ends on the setup link, shown once.
- **Team & Access** (`/team`, SuperAdmins only; the sidebar and command palette hide it from
  anybody else, and the page sends them home): tabs *Team · Audit log · Security*. The team tab
  counts members by state, lists pre-Studio content accounts to set up, and holds the ledger; a
  member's record has their details, access, sign-in (2-step, devices, recent attempts, a pending
  link), notes for other SuperAdmins, and *Reset access · Sign out everywhere · Suspend / Let them
  back in · Close the account* (username retyped). The audit tab filters by person, area, action,
  dates and text, opens a row into everything recorded, and exports the filtered trail as CSV.
  Security holds the 2-step requirement (saying how many active members it would reach) and the
  four lifetimes.
- The member form (`features/team/MemberForm.tsx`) is one component used in all three places a
  member is described — added from Users, added from Team & Access, a pre-Studio account set up —
  so they cannot ask for different things.

**Proven.** `Contracts/AdminAccountsTests.cs` signs a real Admin in over HTTP: offered every role
but SuperAdmin; creates an Admin, refused a SuperAdmin; adds a content-team member and gets the
link; refused (403) the team list, a member's record, suspend, reset, security and the audit log.
`UserAdminCreateTests` follow the same line. In the browser, as an Admin: the dialog offers Student
· Content team · Admin, a member was added with one subject in scope and got their link, Team &
Access is absent from the sidebar and `/team` sends them home; the dialog was checked at phone
width. **The SuperAdmin side of the page has not been exercised in a browser** — it needs a
SuperAdmin sign-in.
