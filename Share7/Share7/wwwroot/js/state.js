// ===========================================================================
// Share7 Admin Console — Shared application state
// Holds tokens, languages, and selection state across pages.
// All state is persisted in sessionStorage so refreshes survive.
// ===========================================================================

const STATE_KEY = 's7_state';

/** Default state shape. */
function defaults() {
  return {
    accessToken:  '',
    refreshToken: '',
    username:     '',
    roles:        [],
    languages:    [],
    selectedLangId: '',

    // curriculum selections (by ID so they survive JSON round-trips)
    sel: { grade: null, term: null, subject: null, chapter: null, lesson: null }
  };
}

/** Load state from sessionStorage, falling back to defaults. */
function load() {
  try {
    const raw = sessionStorage.getItem(STATE_KEY);
    if (raw) return { ...defaults(), ...JSON.parse(raw) };
  } catch { /* corrupted — start fresh */ }
  return defaults();
}

// The live state object — modules import and mutate this directly.
const state = load();

/** Persist current state. Call after every meaningful mutation. */
export function save() {
  sessionStorage.setItem(STATE_KEY, JSON.stringify(state));
}

/** Reset everything and redirect to login. */
export function clearState() {
  sessionStorage.removeItem(STATE_KEY);
  Object.assign(state, defaults());
}

/** Whether the user appears to be signed in. */
export function isSignedIn() {
  return !!state.accessToken;
}

export default state;
