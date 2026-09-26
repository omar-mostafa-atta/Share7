# Product

<!-- impeccable:product-schema 1 -->

This record covers the content-team side of Share7: the **Content Studio** (a separate web app,
`Share7.Studio`) and the **Team & Access** area of the Admin Console. The rest of the Admin Console
predates it and is not described here. Confirmed with the product owner on 2026-09-22.

## Platform

web

## Users

1. **Content-team members.** Curriculum specialists who write and translate lesson questions in
   English and Arabic for the Egyptian curriculum (KG1 to secondary). Long desk sessions; many are
   not technical. Each holds one Studio role (Author, Reviewer or Lead), limited to parts of the
   curriculum and to the languages they work in.
2. **SuperAdmins.** The few people who manage those accounts and check the audit log. They work in
   the Admin Console's Team & Access area and do not enter the Studio. Since 2026-09-26 a regular
   Admin may also *create* a content-team account (from Users), but nothing after that.

## Product Purpose

The Studio is where Share7's learning content is built, reviewed and released to the game, without
developers. Success: a new member publishes a real lesson without help.

## Positioning

Every change by an Author or a Reviewer is approved by a second person; a Lead's can go live on
their own sign-off. Every change can be rolled back, and every one is recorded with who made it.

## Operating Context

- Share7 is a Unity game for students; its content (curriculum tree, lesson questions) comes from
  this backend. The game build is frozen: it must keep working unchanged.
- Content-team accounts are created by an Admin or a SuperAdmin, and managed afterwards by a
  SuperAdmin only (decided 2026-09-26). Whoever adds the member sets their username and password and
  hands them over with the Studio's one address (`/studio`); there is no setup link and no
  activation step.
- Workflow the Studio is built around: Draft → Review → Release, with one-click rollback. Authors
  and Reviewers never approve their own work; a Lead publishes releases, and since 2026-09-26 a
  Lead needs nobody else — they may approve their own work, or release a draft in one step, and
  each self-approval is its own audited action.
- **Authoring happens in the Studio and nowhere else** (since cutover, 2026-09-24). The Admin
  Console's `/content` pages are gone, its authoring routes answer `410 Gone`, and a content-team
  account is refused the old sign-in. Before that, the two ran side by side on purpose, so that
  nothing depended on the Studio until it had been used.

## Capabilities and Constraints

- Interface in English and Arabic, switchable, with full right-to-left layout.
- Optional 2-step sign-in (authenticator app) with recovery codes; a SuperAdmin can make it
  mandatory for all staff.
- Account lifecycle: Active ⇄ Suspended → Deactivated (Invited only for members made with the old setup link). Staff accounts are never
  hard-deleted; their names stay on what they did.
- Every change is recorded in an append-only audit trail with the person who did it.
- Published content is retired, never deleted, from the Studio.

## Product Principles

- Plain words, no developer terms.
- Nothing an Author or a Reviewer writes reaches students without a second person's review. A
  Lead's own work may go live on their sign-off alone (decided 2026-09-26), and says so in the
  audit trail.
- Every action is recorded with the person who did it.
- Arabic is a first-class language, not a translation.
