import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import type { AdminUserListItemDto, CreateAdminUserRequest } from '../../types/api'

// ===========================================================================
// Users — account creation
//
// The roster's reads live in routes/Users.tsx, where they always have. This is
// the one write that needed more than a line: creating an account with a
// username, a password and a role.
// ===========================================================================

/**
 * The roles the signed-in admin may give a new account, from the server.
 *
 * Asked rather than hard-coded because the answer depends on who is asking — only a SuperAdmin
 * may create an Admin — and the server is where that rule lives. A role the server adds appears
 * here without a console change. Fetched only while the form is open.
 */
export function useAssignableRoles(enabled: boolean) {
  return useResourceList<string>(enabled ? '/api/admin/users/assignable-roles' : null)
}

/** Silent: the form renders its own failure inline, beside the field that caused it. */
export function createUser(request: CreateAdminUserRequest) {
  return api.post<AdminUserListItemDto>('/api/admin/users', request, { silent: true })
}
