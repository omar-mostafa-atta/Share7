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

- The Team & Access screens and the Studio's own pages (UI, `impeccable`).
- **CI**, Facebook linking policy, and whether Google's audience check should fail closed — all three
  still need a decision.
