import { useAuth } from '../store/auth'
import type { BadgeTone } from '../components/ui/primitives'

// ===========================================================================
// Access — roles, and what each one may see
//
// The console's copy of the server's authorization table
// (Share7/Authorization/AuthorizationExtensions.cs). The server's copy is the
// gate; this one only decides what to *show*, so nobody is offered a page or a
// button the API will refuse. Change the two together.
//
// Components ask `useCan('some.permission')` rather than checking role names
// themselves. That keeps "who may do this" in the table below instead of
// spread across every page — which is what lets a new role be introduced by
// editing one list.
// ===========================================================================

// ---------------------------------------------------------------------------
// Roles
// ---------------------------------------------------------------------------

/** Role names exactly as Identity stores them — Share7.Domain/Constants/Roles.cs. */
export const Role = {
  Student: 'Student',
  Teacher: 'Teacher',
  Admin: 'Admin',
  SuperAdmin: 'SuperAdmin',
  ContentTeam: 'ContentTeam',
} as const

export type RoleName = (typeof Role)[keyof typeof Role]

interface RoleInfo {
  /** What a person calls it. The stored name is for code. */
  label: string
  tone: BadgeTone

  /** One line on what the role opens, shown where an admin chooses one. */
  description: string
}

const ROLE_INFO: Record<RoleName, RoleInfo> = {
  Student: {
    label: 'Student',
    tone: 'muted',
    description: 'A player. Signs in to the game only — no console access.',
  },
  Teacher: {
    label: 'Teacher',
    tone: 'muted',
    description: 'Reserved. Grants nothing yet.',
  },
  ContentTeam: {
    label: 'Content team',
    tone: 'info',
    description:
      'Works in the Content Studio, a separate application. Opens nothing here, and cannot sign in.',
  },
  Admin: {
    label: 'Admin',
    tone: 'brand',
    description: 'Opens the Admin Console: everything, except creating or removing other admins.',
  },
  SuperAdmin: {
    label: 'Super admin',
    tone: 'danger',
    description: 'Everything an admin can do, plus creating and removing admins.',
  },
}

/** Display details for a role, degrading to the raw name for one this build does not know. */
export function roleInfo(role: string): RoleInfo {
  return ROLE_INFO[role as RoleName] ?? { label: role, tone: 'muted', description: '' }
}

// ---------------------------------------------------------------------------
// Permissions
// ---------------------------------------------------------------------------

const PERMISSIONS = {
  /** Open the Admin Console — every page in it. */
  'console.admin': [Role.Admin, Role.SuperAdmin],
} satisfies Record<string, readonly RoleName[]>

export type Permission = keyof typeof PERMISSIONS

export function can(roles: readonly string[], permission: Permission): boolean {
  const allowed: readonly string[] = PERMISSIONS[permission]
  return roles.some((role) => allowed.includes(role))
}

/** Whether the signed-in user holds a permission. Re-renders when their roles change. */
export function useCan(permission: Permission): boolean {
  const roles = useAuth((s) => s.roles)
  return can(roles, permission)
}
