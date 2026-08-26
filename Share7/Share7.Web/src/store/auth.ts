import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import type { AuthResult } from '../types/api'

// Distinct from the `s7_state` key the hand-written console used, which mattered while both were
// served from this origin and shared a sessionStorage namespace. That console is gone; the key
// stays as it is because renaming it would sign every open session out for nothing.
const STORAGE_KEY = 's7_auth'

interface AuthState {
  accessToken: string
  refreshToken: string
  username: string
  roles: string[]

  /** ISO timestamp from the server, used to refresh slightly before expiry rather than on 401. */
  accessTokenExpiresAt: string | null

  setSession: (auth: AuthResult) => void
  clear: () => void
}

const empty = {
  accessToken: '',
  refreshToken: '',
  username: '',
  roles: [] as string[],
  accessTokenExpiresAt: null as string | null,
}

export const useAuth = create<AuthState>()(
  persist(
    (set) => ({
      ...empty,

      setSession: (auth) =>
        set({
          accessToken: auth.accessToken ?? '',
          refreshToken: auth.refreshToken ?? '',
          username: auth.username ?? '',
          roles: auth.roles ?? [],
          accessTokenExpiresAt: auth.accessTokenExpiresAt ?? null,
        }),

      clear: () => set({ ...empty }),
    }),
    {
      name: STORAGE_KEY,
      storage: createJSONStorage(() => sessionStorage),
      partialize: (s) => ({
        accessToken: s.accessToken,
        refreshToken: s.refreshToken,
        username: s.username,
        roles: s.roles,
        accessTokenExpiresAt: s.accessTokenExpiresAt,
      }),
    },
  ),
)

export const isSignedIn = () => !!useAuth.getState().accessToken

/**
 * The content language the access token actually carries, from its `preferred_language` claim.
 *
 * This is the authority for what language the tree comes back in. Only `GET /api/grades` takes an
 * explicit `?langId=`; terms, subjects, chapters and lessons all resolve from this claim. So a
 * locally-remembered choice can silently disagree with the server — which showed up as a selector
 * reading "English" above a tree rendering Arabic, and a question sheet published to whichever
 * language the *selector* happened to name.
 *
 * Decoding rather than verifying: the signature is the server's business, and a tampered claim
 * would only mislabel this admin's own picker. `atob` needs base64url translated back first, and
 * the payload is UTF-8, so it is decoded as such rather than read as Latin-1.
 */
export function tokenLanguageId(): string | null {
  const token = useAuth.getState().accessToken
  if (!token) return null

  const payload = token.split('.')[1]
  if (!payload) return null

  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const bytes = Uint8Array.from(json, (ch) => ch.charCodeAt(0))
    const claims = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>
    const value = claims['preferred_language']
    return typeof value === 'string' && value ? value : null
  } catch {
    return null
  }
}

export const isAdmin = () => {
  const roles = useAuth.getState().roles
  return roles.includes('Admin') || roles.includes('SuperAdmin')
}
