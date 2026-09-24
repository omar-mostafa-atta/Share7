import { useSyncExternalStore } from 'react'

// ===========================================================================
// Which face of the board you are looking at
//
// The Studio lands on the board — slate green, chalk — because that is what it
// is. The whiteboard is the same board under the same rules for anyone working
// in a bright room, and it is a switch in Settings, never guesswork: a member
// who picked one gets it back on every machine this browser remembers.
// ===========================================================================

export type Surface = 'board' | 'whiteboard'

const STORAGE_KEY = 'share7-studio.surface'

function stored(): Surface {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'whiteboard' ? 'whiteboard' : 'board'
  } catch {
    return 'board'
  }
}

let surface: Surface = stored()
const listeners = new Set<() => void>()

function paint() {
  document.documentElement.dataset.surface = surface
  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute('content', surface === 'whiteboard' ? '#dfe2d8' : '#16302a')
}

paint()

export function setSurface(next: Surface) {
  surface = next
  try {
    localStorage.setItem(STORAGE_KEY, next)
  } catch {
    // A convenience only; the board is the default either way.
  }
  paint()
  listeners.forEach((listener) => listener())
}

export function useSurface(): Surface {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => surface,
  )
}
