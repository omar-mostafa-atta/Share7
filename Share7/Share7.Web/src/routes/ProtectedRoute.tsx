import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../store/auth'
import { can } from '../lib/access'
import { landingFor, type Portal } from '../lib/portals'

/**
 * Replaces guardAuth() from the vanilla console, which every page had to remember to call at the
 * top of its own init function. Here it wraps the routes once.
 *
 * It also handles the case that console could not: the API client clears the auth store when a
 * session is genuinely dead — a 401 that survived a refresh attempt — and because this subscribes
 * to that store, the redirect happens by itself, mid-session, without any imperative navigation
 * from inside the fetch layer.
 *
 * Given a `portal`, it also checks the user may open it. Someone signed in to the wrong one — a
 * content-team member following an admin link, say — is taken to the portal they can use, rather
 * than shown pages where every request fails with a 403. This is presentation, not protection:
 * the API refuses those requests either way.
 */
export function ProtectedRoute({ children, portal }: { children: ReactNode; portal?: Portal }) {
  const accessToken = useAuth((s) => s.accessToken)
  const roles = useAuth((s) => s.roles)
  const location = useLocation()

  if (!accessToken) {
    // Carrying the attempted path means a mid-session expiry returns the admin to the page they
    // were on rather than dumping them on the default one.
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (portal && !can(roles, portal.permission)) {
    // No portal at all goes to the sign-in screen, which explains and signs the session out.
    return <Navigate to={landingFor(roles) ?? '/login'} replace />
  }

  return <>{children}</>
}
