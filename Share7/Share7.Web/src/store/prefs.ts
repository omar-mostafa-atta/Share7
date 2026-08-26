import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

// ===========================================================================
// Console preferences — appearance, not session state
//
// Deliberately localStorage, where auth and languages are sessionStorage. A
// theme is a property of this browser on this machine and should survive the
// tab closing; a token is a property of one sitting and should not. Same
// reasoning, opposite answer.
// ===========================================================================

export type ThemeChoice = 'system' | 'light' | 'dark'
export type Density = 'comfortable' | 'compact'

interface PrefsState {
  theme: ThemeChoice
  density: Density
  setTheme: (theme: ThemeChoice) => void
  setDensity: (density: Density) => void
}

/**
 * Push the choice onto <html>, where tokens.css reads it.
 *
 * 'system' *removes* the attribute rather than resolving the media query and
 * writing a concrete value. That is what lets the OS switching from light to
 * dark at sunset re-theme an open tab: with an attribute present, the explicit
 * selector wins forever and the page is frozen in whichever mode it was opened.
 */
function applyTheme(theme: ThemeChoice) {
  const root = document.documentElement
  if (theme === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', theme)
}

function applyDensity(density: Density) {
  const root = document.documentElement
  if (density === 'compact') root.setAttribute('data-density', 'compact')
  else root.removeAttribute('data-density')
}

export const usePrefs = create<PrefsState>()(
  persist(
    (set) => ({
      theme: 'dark',
      density: 'comfortable',

      setTheme: (theme) => {
        applyTheme(theme)
        set({ theme })
      },

      setDensity: (density) => {
        applyDensity(density)
        set({ density })
      },
    }),
    {
      name: 's7_prefs',
      storage: createJSONStorage(() => localStorage),

      // The store rehydrates asynchronously, so the persisted value is not on
      // <html> until this fires. Applying it here rather than in a component
      // effect keeps the swap out of React's commit phase — a theme applied
      // during render flashes the default palette for one frame first.
      onRehydrateStorage: () => (state) => {
        if (!state) return
        applyTheme(state.theme)
        applyDensity(state.density)
      },
    },
  ),
)

/**
 * The concrete theme in effect, resolving 'system' against the media query.
 * Components that need to *know* (rather than merely be styled) use this —
 * an SVG picking a stroke colour, for instance.
 */
export function resolvedTheme(choice: ThemeChoice): 'light' | 'dark' {
  if (choice !== 'system') return choice
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

// Paint the default before the store rehydrates. Without this the first frame
// is light — :root's base values — and dark-mode admins see a white flash on
// every hard load.
applyTheme(usePrefs.getState().theme)
applyDensity(usePrefs.getState().density)
