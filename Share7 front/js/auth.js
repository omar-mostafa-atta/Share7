// ===========================================================================
// Share7 Admin Console — Auth guard & login logic
// Login page uses signIn(); all other pages call guardAuth() on load.
// ===========================================================================

import state, { save, clearState, isSignedIn } from './state.js';
import { api, loadLanguages } from './api.js';

/**
 * Sign in with username + password. Stores tokens and identity in state.
 * @returns {object} The auth response from the server.
 */
export async function signIn(username, password) {
  const auth = await api('POST', '/api/auth/login', { username, password });

  state.accessToken  = auth.accessToken || '';
  state.refreshToken = auth.refreshToken || '';
  state.username     = auth.username || username;
  state.roles        = auth.roles || [];
  save();

  return auth;
}

/**
 * Change the content language. The server returns a fresh token pair.
 */
export async function applyLanguage(languageId) {
  const auth = await api('POST', '/api/users/me/preferred-language', { languageId });

  state.accessToken  = auth.accessToken || '';
  state.refreshToken = auth.refreshToken || '';
  state.username     = auth.username || state.username;
  state.roles        = auth.roles || state.roles;
  state.selectedLangId = languageId;
  save();

  return auth;
}

/**
 * Sign out: clear state and redirect to login.
 */
export function signOut() {
  clearState();
  // Navigate to login — works from any page depth
  window.location.href = getLoginPath();
}

/**
 * Guard: redirect to login if not signed in. Call at the top of every dashboard page init.
 */
export function guardAuth() {
  if (!isSignedIn()) {
    window.location.href = getLoginPath();
    return false;
  }
  return true;
}

/**
 * Render the user badge in the sidebar footer.
 */
export function renderUserBadge() {
  const nameEl = document.getElementById('sidebar-user-name');
  const roleEl = document.getElementById('sidebar-user-role');
  const avatarEl = document.getElementById('sidebar-avatar');

  if (nameEl) nameEl.textContent = state.username || 'Admin';
  if (roleEl) roleEl.textContent = (state.roles || []).join(', ') || 'No role';
  if (avatarEl) avatarEl.textContent = (state.username || 'A').charAt(0).toUpperCase();
}

// ---------------------------------------------------------------------------
// Path helpers — the login page is at the root, dashboard pages in /pages/
// ---------------------------------------------------------------------------
function getLoginPath() {
  const path = window.location.pathname;
  if (path.includes('/pages/')) return '../index.html';
  return 'index.html';
}
