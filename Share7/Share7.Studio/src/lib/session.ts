import { useSyncExternalStore } from 'react'
import { adoptSession, forgetSession, refreshSession, setSessionEndedHandler, studioApi, type Me, type SignedIn } from './api'

// ===========================================================================
// Who is signed in
//
// One small store, read with useSyncExternalStore. It starts by asking the
// server whether the refresh cookie still opens a session — a reload keeps the
// member signed in without the page ever having stored a token — and it hears
// about every other tab through a BroadcastChannel, so signing out in one tab
// signs out all of them at once instead of each discovering it on its next
// request.
// ===========================================================================

export type SessionState =
  | { status: 'starting' }
  | { status: 'signed-out'; reason: 'none' | 'ended' | 'signed-out' }
  | { status: 'signed-in'; me: Me }

let state: SessionState = { status: 'starting' }
const listeners = new Set<() => void>()

function set(next: SessionState) {
  state = next
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useSession(): SessionState {
  return useSyncExternalStore(subscribe, () => state)
}

/** The signed-in member. Only for components that render inside the signed-in routes. */
export function useMe(): Me {
  const current = useSession()
  if (current.status !== 'signed-in') throw new Error('useMe() outside a signed-in route')
  return current.me
}

const channel = 'BroadcastChannel' in window ? new BroadcastChannel('share7-studio') : null

channel?.addEventListener('message', (event: MessageEvent<string>) => {
  if (event.data === 'signed-out') {
    forgetSession()
    set({ status: 'signed-out', reason: 'signed-out' })
  } else if (event.data === 'signed-in' && state.status !== 'signed-in') {
    void start()
  }
})

// A request met a 401 the refresh could not cure: the session was ended elsewhere — by the member
// on another device, by a password change, or by a SuperAdmin.
setSessionEndedHandler(() => set({ status: 'signed-out', reason: 'ended' }))

export async function start() {
  try {
    if (await refreshSession()) {
      set({ status: 'signed-in', me: await studioApi.me() })
      return
    }
  } catch {
    // Fall through: whatever went wrong, the member has to sign in.
  }

  set({ status: 'signed-out', reason: 'none' })
}

/** Re-reads the member after something about them changed (2-step turned on, language switched). */
export async function reloadMe() {
  set({ status: 'signed-in', me: await studioApi.me() })
}

export async function acceptSession(session: SignedIn) {
  adoptSession(session)
  await reloadMe()
  channel?.postMessage('signed-in')
}

/** A new session for this device, issued after a password or 2-step change; nothing else moves. */
export function renewSession(session: SignedIn) {
  adoptSession(session)
}

export async function signOut() {
  try {
    await studioApi.signOut()
  } catch {
    // Signed out here regardless; the server session ends with the cookie's expiry at worst.
  }

  forgetSession()
  set({ status: 'signed-out', reason: 'signed-out' })
  channel?.postMessage('signed-out')
}
