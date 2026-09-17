// ===========================================================================
// API client
//
// One place that knows how to talk to the API: bearer token, error shapes, and
// the refresh-token dance. Every call is same-origin — /api is served by the
// API itself in production and forwarded by the Vite dev proxy locally — so
// there is no base URL and no CORS to configure.
// ===========================================================================

import { useAuth } from '../store/auth'
import { ApiError, describeFailure } from './errors'
import type { AuthResult } from '../types/api'

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

interface RequestOptions {
  /** Send `body` as FormData without JSON serialisation. */
  form?: boolean

  /** Skip the global error handler — the caller is rendering the failure itself. */
  silent?: boolean

  signal?: AbortSignal
}

// ---------------------------------------------------------------------------
// Global error sink
//
// Registered once at app start so the client can surface failures without
// importing the toast store and creating a cycle. Matches the old console,
// where every failed call toasted itself from inside api().
// ---------------------------------------------------------------------------

type ErrorHandler = (error: ApiError, method: Method, path: string) => void

let errorHandler: ErrorHandler | null = null

export function setApiErrorHandler(handler: ErrorHandler | null) {
  errorHandler = handler
}

// ---------------------------------------------------------------------------
// Refresh
//
// The API exposes POST /api/auth/refresh and the old console never called it —
// it stored the refresh token and let sessions die at access-token expiry.
// Doing it here means it happens once, for every endpoint, instead of in seven
// separate page scripts.
// ---------------------------------------------------------------------------

/** Endpoints that must never trigger a refresh attempt, or a failure would recurse. */
const NO_REFRESH = ['/api/auth/login', '/api/auth/refresh', '/api/auth/register']

/**
 * A page load fires several requests at once. Without a shared promise each one that meets a 401
 * would start its own refresh, and since a refresh token is single-use every attempt after the
 * first would fail and sign the admin out mid-load.
 */
let refreshInFlight: Promise<boolean> | null = null

async function refreshSession(): Promise<boolean> {
  const { refreshToken, setSession, clear } = useAuth.getState()
  if (!refreshToken) return false

  try {
    const res = await fetch('/api/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })

    if (!res.ok) {
      clear()
      return false
    }

    const auth = (await res.json()) as AuthResult
    if (!auth.accessToken) {
      clear()
      return false
    }

    setSession(auth)
    return true
  } catch {
    // A network failure is not proof the session is dead, so the tokens are left in place for
    // the next attempt rather than signing the admin out over one dropped request.
    return false
  }
}

function refreshOnce(): Promise<boolean> {
  refreshInFlight ??= refreshSession().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

/** Whether the stored access token expires within the margin, so it is worth refreshing first. */
function expiresSoon(marginMs = 30_000): boolean {
  const iso = useAuth.getState().accessTokenExpiresAt
  if (!iso) return false

  const expiry = Date.parse(iso)
  if (Number.isNaN(expiry)) return false

  return expiry - Date.now() < marginMs
}

// ---------------------------------------------------------------------------
// Core request
// ---------------------------------------------------------------------------

async function send(method: Method, path: string, body: unknown, options: RequestOptions) {
  const headers: Record<string, string> = {}

  const token = useAuth.getState().accessToken
  if (token) headers['Authorization'] = `Bearer ${token}`
  if (body !== undefined && !options.form) headers['Content-Type'] = 'application/json'

  return fetch(path, {
    method,
    headers,
    signal: options.signal,
    body: options.form
      ? (body as FormData)
      : body !== undefined
        ? JSON.stringify(body)
        : undefined,
  })
}

export async function request<T>(
  method: Method,
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<T> {
  const refreshable = !NO_REFRESH.includes(path)

  // Refresh ahead of expiry when we can see it coming. The 401 path below is still the real
  // safety net — this only avoids a guaranteed round-trip failure and the flicker it causes.
  if (refreshable && useAuth.getState().refreshToken && expiresSoon()) {
    await refreshOnce()
  }

  let res = await send(method, path, body, options)

  if (res.status === 401 && refreshable && useAuth.getState().refreshToken) {
    if (await refreshOnce()) {
      res = await send(method, path, body, options)
    }
  }

  const text = await res.text()
  let data: unknown = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = text
  }

  if (!res.ok) {
    const reason = describeFailure(data, text)
    const error = new ApiError(reason, res.status, data)

    console.error(`[s7] ${method} ${path} → ${res.status}`, reason)
    if (!options.silent) errorHandler?.(error, method, path)

    // A 401 that survived the refresh above means the session is genuinely gone. Clearing the
    // store is what redirects: ProtectedRoute watches it and sends the admin to /login.
    if (res.status === 401 && refreshable) useAuth.getState().clear()

    throw error
  }

  return data as T
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => request<T>('GET', path, undefined, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>('POST', path, body, options),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>('PUT', path, body, options),
  del: <T>(path: string, options?: RequestOptions) => request<T>('DELETE', path, undefined, options),
}
