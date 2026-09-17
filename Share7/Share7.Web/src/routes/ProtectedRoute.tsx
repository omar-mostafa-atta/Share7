import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../store/auth'

/**
 * Replaces guardAuth() from the vanilla console, which every page had to remember to call at the
 * top of its own init function. Here it wraps the routes once.
 *
 * It also handles the case that console could not: the API client clears the auth store when a
 * session is genuinely dead — a 401 that survived a refresh attempt — and because this subscribes
 * to that store, the redirect happens by itself, mid-session, without any imperative navigation
 * from inside the fetch layer.
 */
export function ProtectedRoute({ children }: { children: ReactNode }) {
  const accessToken = useAuth((s) => s.accessToken)
  const location = useLocation()

  if (!accessToken) {
    // Carrying the attempted path means a mid-session expiry returns the admin to the page they
    // were on rather than dumping them on the default one.
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <>{children}</>
}
