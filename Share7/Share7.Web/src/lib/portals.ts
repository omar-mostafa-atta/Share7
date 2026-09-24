import { NAV, type NavGroup } from './nav'
import { can, type Permission } from './access'

// ===========================================================================
// Portals
//
// A portal is a set of pages with its own sidebar, behind its own permission,
// under its own path. Everything that has to know "which portal is this, and
// may this person open it" — the sign-in redirect, the route guard, the shell
// — reads this list.
//
//   Admin Console    /          Admin, SuperAdmin
//
// There was a second: the Content Portal at /content, for the ContentTeam
// role. It closed at cutover (plan P6) — the content team works in the Content
// Studio now, a separate application on a separate origin, and their accounts
// are refused this one's sign-in entirely. Its old addresses land on
// routes/Moved.tsx.
//
// The machinery is kept for one portal rather than collapsed into the shell,
// because a second audience (teachers, say) is still an entry here, a nav list
// and a routes component in App.tsx — which is the whole reason it was built.
// ===========================================================================

export interface Portal {
  id: 'admin'

  /** Shown under the brand mark and in the top bar. */
  name: string

  /** Where the portal lives, and where its people land after signing in. */
  home: string

  /** Who may open it — see lib/access.ts. */
  permission: Permission

  /** Its sidebar and command palette. */
  nav: NavGroup[]
}

export const ADMIN_PORTAL: Portal = {
  id: 'admin',
  name: 'Admin Console',
  home: '/',
  permission: 'console.admin',
  nav: NAV,
}

/** In landing order: someone who may open more than one lands in the first. */
const PORTALS: Portal[] = [ADMIN_PORTAL]

/** The portal a path belongs to — the one whose home is the longest prefix of it. */
export function portalForPath(pathname: string): Portal {
  let best = ADMIN_PORTAL

  for (const portal of PORTALS) {
    const inside = pathname === portal.home || pathname.startsWith(`${portal.home}/`)
    if (inside && portal.home.length > best.home.length) best = portal
  }

  return best
}

/**
 * Where to send someone holding `roles`: the page they asked for if they may open it, otherwise
 * the home of the first portal they may open, otherwise null — there is nothing here for them.
 */
export function landingFor(roles: readonly string[], requested?: string | null): string | null {
  if (requested && can(roles, portalForPath(requested).permission)) return requested
  return PORTALS.find((portal) => can(roles, portal.permission))?.home ?? null
}
