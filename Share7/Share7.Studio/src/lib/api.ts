// ===========================================================================
// Studio API client
//
// The access token lives in this module's memory and nowhere else — not in
// localStorage, not in a cookie script can read — so a script injected into
// the page cannot lift a sign-in that outlives the tab. What keeps the member
// signed in across reloads is the refresh cookie, which is HttpOnly and scoped
// by the server to /api/studio/auth: this code never sees it, it only asks the
// server to use it.
//
// The routes that act on that cookie demand an X-Studio-Request header. A page
// on another origin cannot add it without a CORS preflight the API never
// grants, which is what stops a sibling site from riding the cookie.
// ===========================================================================

export type StudioRole = 'Author' | 'Reviewer' | 'Lead'
export type InterfaceLanguage = 'en' | 'ar'

/** Somebody named on a piece of work. Staff accounts are never deleted, so a name always resolves. */
export interface PersonName {
  userId: string
  name: string
}

export interface LocalizedTitle {
  en: string
  ar: string | null
}

export interface ScopeNode {
  id: string
  kind: string
  trail: LocalizedTitle[]
  exists: boolean
}

export interface ScopeLanguage {
  id: string
  code: string
  name: string
}

export interface StaffScope {
  allNodes: boolean
  nodes: ScopeNode[]
  allLanguages: boolean
  languages: ScopeLanguage[]
}

export interface PasswordRules {
  minimumLength: number
  requireUppercase: boolean
  requireLowercase: boolean
  requireDigit: boolean
}

export interface Me {
  userId: string
  username: string
  fullName: string
  jobTitle: string | null
  studioRole: StudioRole
  scope: StaffScope
  interfaceLanguage: InterfaceLanguage
  twoStep: { enabled: boolean; required: boolean; recoveryCodesLeft: number; setupRequired: boolean }
  passwordRules: PasswordRules
  activatedAtUtc: string | null
  session: { id: string; expiresAtUtc: string; twoStepVerified: boolean }
}

export interface SetupLinkInfo {
  fullName: string
  username: string
  purpose: 'Activation' | 'Reset'
  expiresAtUtc: string
  studioRole: StudioRole
  scope: StaffScope
  interfaceLanguage: InterfaceLanguage
  passwordRules: PasswordRules
  twoStepRequired: boolean
  twoStepEnabled: boolean
}

export interface SignedIn {
  status: 'signed-in'
  accessToken: string
  accessTokenExpiresAtUtc: string
  sessionExpiresAtUtc: string
}

export interface TwoStepRequired {
  status: 'two-step-required'
  challenge: string
  challengeExpiresAtUtc: string
}

export type SignInOutcome = SignedIn | TwoStepRequired

export interface Device {
  id: string
  createdAtUtc: string
  lastSeenAtUtc: string
  expiresAtUtc: string
  ipAddress: string | null
  device: string | null
  isCurrent: boolean
}

/** A refusal the Studio can explain: `messageKey` is looked up in the language files. */
export class StudioError extends Error {
  readonly status: number
  readonly code: string
  readonly messageKey: string
  readonly details: Record<string, unknown>

  constructor(status: number, code: string, messageKey: string, details: Record<string, unknown> = {}) {
    super(code)
    this.status = status
    this.code = code
    this.messageKey = messageKey
    this.details = details
  }

  /** The server's problem codes for a refused password (tooShort, common…). */
  get problems(): string[] {
    const problems = this.details['problems']
    return Array.isArray(problems) ? problems.map(String) : []
  }
}

const NETWORK = new StudioError(0, 'NETWORK', 'errors.network')

// ---------------------------------------------------------------------------
// The access token and who is listening for it ending
// ---------------------------------------------------------------------------

let accessToken: string | null = null
let sessionEndedHandler: (() => void) | null = null

export function setSessionEndedHandler(handler: (() => void) | null) {
  sessionEndedHandler = handler
}

export function hasAccessToken() {
  return accessToken !== null
}

export function adoptSession(session: SignedIn) {
  accessToken = session.accessToken
}

export function forgetSession() {
  accessToken = null
}

// ---------------------------------------------------------------------------
// Refresh — once at a time, across every open tab
// ---------------------------------------------------------------------------

let refreshInFlight: Promise<boolean> | null = null

async function refreshNow(): Promise<boolean> {
  for (let attempt = 0; attempt < 2; attempt++) {
    let res: Response
    try {
      res = await fetch('/api/studio/auth/refresh', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'X-Studio-Request': '1' },
      })
    } catch {
      // Offline for a moment is not a signed-out member; keep what we have.
      return false
    }

    if (res.ok) {
      adoptSession((await res.json()) as SignedIn)
      return true
    }

    // Another tab rotated the cookie a moment ago. The browser already holds the new one.
    if (res.status === 409) continue

    forgetSession()
    return false
  }

  return false
}

/**
 * One refresh at a time. Inside a tab, the shared promise; across tabs, a Web Lock, so two tabs
 * never spend the same single-use cookie at once.
 */
export function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (
    'locks' in navigator
      ? navigator.locks.request('share7-studio-refresh', refreshNow)
      : refreshNow()
  ).finally(() => {
    refreshInFlight = null
  })

  return refreshInFlight
}

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

async function send(method: Method, path: string, body: unknown, withCookie: boolean): Promise<Response> {
  const headers: Record<string, string> = {}
  if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (withCookie) headers['X-Studio-Request'] = '1'

  try {
    return await fetch(path, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw NETWORK
  }
}

async function failure(res: Response): Promise<StudioError> {
  try {
    const body = (await res.json()) as { code?: string; messageKey?: string; details?: Record<string, unknown> }
    if (body.code && body.messageKey) return new StudioError(res.status, body.code, body.messageKey, body.details ?? {})
  } catch {
    // Not our envelope (a proxy's page, a bare 403); fall through to a generic message.
  }

  if (res.status === 429) return new StudioError(429, 'RATE_LIMITED', 'errors.tooMany')
  return new StudioError(res.status, 'HTTP_' + res.status, res.status === 401 ? 'errors.session.invalid' : 'errors.unexpected')
}

export async function request<T>(method: Method, path: string, body?: unknown, options: { withCookie?: boolean; authenticated?: boolean } = {}): Promise<T> {
  const authenticated = options.authenticated ?? true
  let res = await send(method, path, body, options.withCookie ?? false)

  // An expired access token — or one whose security settings moved under it — gets one quiet
  // refresh and one retry. A second 401 means the session itself is over.
  if (res.status === 401 && authenticated) {
    if (await refreshSession()) {
      res = await send(method, path, body, options.withCookie ?? false)
    }

    if (res.status === 401) {
      forgetSession()
      sessionEndedHandler?.()
    }
  }

  if (!res.ok) throw await failure(res)
  if (res.status === 204) return undefined as T

  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/**
 * A spreadsheet going up. Multipart, so the browser writes the boundary itself
 * and this code must not set a content type; everything else — the one quiet
 * refresh, the envelope on a refusal — works exactly as it does for JSON.
 */
export async function upload<T>(path: string, form: FormData): Promise<T> {
  const attempt = async () => {
    const headers: Record<string, string> = {}
    if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`
    try {
      return await fetch(path, { method: 'POST', headers, credentials: 'same-origin', body: form })
    } catch {
      throw NETWORK
    }
  }

  let res = await attempt()
  if (res.status === 401 && (await refreshSession())) res = await attempt()
  if (res.status === 401) {
    forgetSession()
    sessionEndedHandler?.()
  }

  if (!res.ok) throw await failure(res)
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/**
 * A spreadsheet coming back. It arrives as a file rather than JSON, so it
 * cannot go through an ordinary `<a download>`: that would be a request with no
 * Authorization header, and the server would refuse it.
 */
export async function download(path: string, fallbackName: string): Promise<void> {
  const attempt = async () => {
    const headers: Record<string, string> = {}
    if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`
    try {
      return await fetch(path, { headers, credentials: 'same-origin' })
    } catch {
      throw NETWORK
    }
  }

  let res = await attempt()
  if (res.status === 401 && (await refreshSession())) res = await attempt()
  if (!res.ok) throw await failure(res)

  const disposition = res.headers.get('Content-Disposition') ?? ''
  const named = /filename="?([^";]+)"?/i.exec(disposition)?.[1]
  const url = URL.createObjectURL(await res.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = named ?? fallbackName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

// ---------------------------------------------------------------------------
// The Studio's endpoints
// ---------------------------------------------------------------------------

export const studioApi = {
  signIn: (username: string, password: string) =>
    request<SignInOutcome>('POST', '/api/studio/auth/sign-in', { username, password }, { authenticated: false }),

  twoStep: (challenge: string, code: string | null, recoveryCode: string | null) =>
    request<SignInOutcome>('POST', '/api/studio/auth/two-step', { challenge, code, recoveryCode }, { authenticated: false }),

  inspectSetup: (token: string) =>
    request<SetupLinkInfo>('POST', '/api/studio/auth/setup/inspect', { token }, { authenticated: false }),

  completeSetup: (token: string, password: string, interfaceLanguage: InterfaceLanguage) =>
    request<SignInOutcome>('POST', '/api/studio/auth/setup/complete', { token, password, interfaceLanguage }, { authenticated: false }),

  signOut: () => request<void>('POST', '/api/studio/auth/sign-out', undefined, { withCookie: true, authenticated: false }),

  me: () => request<Me>('GET', '/api/studio/auth/me'),

  setLanguage: (language: InterfaceLanguage) =>
    request<void>('PUT', '/api/studio/account/interface-language', { language }),

  changePassword: (currentPassword: string, newPassword: string) =>
    request<SignedIn>('POST', '/api/studio/account/password', { currentPassword, newPassword }),

  beginTwoStep: () => request<{ sharedKey: string; authenticatorUri: string }>('POST', '/api/studio/account/two-step/begin'),

  confirmTwoStep: (code: string) =>
    request<{ recoveryCodes: string[]; session: SignedIn }>('POST', '/api/studio/account/two-step/confirm', { code }),

  disableTwoStep: (password: string) => request<SignedIn>('POST', '/api/studio/account/two-step/disable', { password }),

  newRecoveryCodes: (password: string) =>
    request<{ codes: string[] }>('POST', '/api/studio/account/two-step/recovery-codes', { password }),

  devices: () => request<Device[]>('GET', '/api/studio/account/sessions'),

  signOutDevice: (id: string) => request<void>('DELETE', `/api/studio/account/sessions/${id}`),
}
