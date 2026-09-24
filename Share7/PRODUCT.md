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
2. **SuperAdmins.** The few people who create and manage those accounts and check the audit log.
   They work in the Admin Console's Team & Access area and do not enter the Studio.

## Product Purpose

The Studio is where Share7's learning content is built, reviewed and released to the game, without
developers. Success: a new member publishes a real lesson without help.

## Positioning

Every change is reviewed by a second person and can be rolled back. Students never see unreviewed
work.

## Operating Context

- Share7 is a Unity game for students; its content (curriculum tree, lesson questions) comes from
  this backend. The game build is frozen: it must keep working unchanged.
- Accounts are created only by a SuperAdmin. There is no email sending: a SuperAdmin hands the new
  member a one-time setup link.
- Workflow the Studio is built around: Draft → Review → Release, with one-click rollback. No one
  approves their own work; a Lead publishes releases.
- **Authoring happens in the Studio and nowhere else** (since cutover, 2026-09-24). The Admin
  Console's `/content` pages are gone, its authoring routes answer `410 Gone`, and a content-team
  account is refused the old sign-in. Before that, the two ran side by side on purpose, so that
  nothing depended on the Studio until it had been used.

## Capabilities and Constraints

- Interface in English and Arabic, switchable, with full right-to-left layout.
- Optional 2-step sign-in (authenticator app) with recovery codes; a SuperAdmin can make it
  mandatory for all staff.
- Account lifecycle: Invited → Active ⇄ Suspended → Deactivated. Staff accounts are never
  hard-deleted; their names stay on what they did.
- Every change is recorded in an append-only audit trail with the person who did it.
- Published content is retired, never deleted, from the Studio.

## Product Principles

- Plain words, no developer terms.
- Nothing reaches students without a second person's review.
- Every action is recorded with the person who did it.
- Arabic is a first-class language, not a translation.
