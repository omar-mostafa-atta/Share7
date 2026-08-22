// ===========================================================================
// Share7 Admin Console — Games page logic
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations } from './api.js';
import { escapeHtml, slugify, missingLanguages } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

let games = [];
let gameModalInstance = null;

// ---------------------------------------------------------------------------
// Load & render
// ---------------------------------------------------------------------------
async function loadGames() {
  const rows = await api('GET', '/api/admin/games');
  games = Array.isArray(rows) ? rows : (rows.games || []);

  document.getElementById('gameList').innerHTML = games.length
    ? `<div class="table-responsive"><table class="table table-sm table-hover align-middle mb-0">
         <thead><tr>
           <th>Name</th><th>Key</th><th class="text-center">Players</th>
           <th class="text-center">Modes</th><th class="text-center">Scenes</th>
           <th class="text-center">Active</th><th></th>
         </tr></thead>
         <tbody>${games.map(g => `<tr>
           <td>${escapeHtml(g.displayName || '—')}${g.description
                ? `<div class="muted-sm text-truncate" style="max-width:18rem">${escapeHtml(g.description)}</div>` : ''}</td>
           <td><span class="kind-token">${escapeHtml(g.gameKey)}</span></td>
           <td class="text-center">${g.minPlayers}–${g.maxPlayers}</td>
           <td class="text-center muted-sm">
             ${g.supportsSinglePlayer ? 'solo' : ''}${g.supportsSinglePlayer && g.supportsMultiplayer ? ' · ' : ''}${g.supportsMultiplayer ? 'multi' : ''}
             ${!g.supportsSinglePlayer && !g.supportsMultiplayer ? '<span class="text-danger">none</span>' : ''}
           </td>
           <td class="text-center muted-sm mono">${g.lobbyScene}/${g.gameplayScene}</td>
           <td class="text-center">${g.isActive
                ? '<span class="badge text-bg-success">yes</span>'
                : '<span class="badge text-bg-secondary">no</span>'}</td>
           <td class="text-end text-nowrap">
             <button class="btn btn-sm btn-outline-danger" title="Delete"
                     onclick="deleteGame('${g.gameId}')"><i class="bi bi-trash"></i></button>
           </td>
         </tr>`).join('')}</tbody></table></div>
       <div class="form-text mt-2 px-3 pb-2">
         Editing is deliberately not offered here. <code>PUT /api/admin/games/{id}</code> is a
         <strong>full replace including translations</strong>, and this listing only returns the name
         in your current language — so an edit from this screen would silently wipe the other
         language's text.
       </div>`
    : '<div class="empty"><i class="bi bi-controller"></i>No games yet. Add one — progress and multiplayer sessions are both keyed by game.</div>';
}

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------
function openGameModal() {
  if (!state.languages.length) {
    toast('Languages not loaded', 'Sign in first — a name is required per language.');
    return;
  }

  document.getElementById('gameKey').value = '';
  document.getElementById('gameLobbyScene').value = 0;
  document.getElementById('gameGameplayScene').value = 0;
  document.getElementById('gameMinPlayers').value = 1;
  document.getElementById('gameMaxPlayers').value = 2;
  document.getElementById('gameReadyTimeout').value = 20;

  for (const [id, on] of [['gameSingle', true], ['gameMulti', true], ['gameLobby', true],
                          ['gameMatchmaking', true], ['gameActive', true]]) {
    document.getElementById(id).checked = on;
  }

  translationFields('gameTranslations', []);
  gameModalInstance.show();
}

async function submitGame() {
  const gameKey = slugify(document.getElementById('gameKey').value);
  const minPlayers = Number(document.getElementById('gameMinPlayers').value);
  const maxPlayers = Number(document.getElementById('gameMaxPlayers').value);
  const translations = collectTranslations('gameTranslations');
  const missing = missingLanguages(translations);

  if (!gameKey) { toast('Game key required', 'It is the stable machine name everything else refers to.'); return; }
  if (missing.length) { toast('A name is required in every language', `Missing: ${missing.join(', ')}.`); return; }
  if (maxPlayers < minPlayers) {
    toast('Max players is below min', 'A session could never reach its minimum, so it could never start.');
    return;
  }

  const body = {
    gameKey,
    lobbyScene:           Number(document.getElementById('gameLobbyScene').value),
    gameplayScene:        Number(document.getElementById('gameGameplayScene').value),
    minPlayers,
    maxPlayers,
    readyTimeoutSeconds:  Number(document.getElementById('gameReadyTimeout').value),
    supportsSinglePlayer: document.getElementById('gameSingle').checked,
    supportsMultiplayer:  document.getElementById('gameMulti').checked,
    useLobby:             document.getElementById('gameLobby').checked,
    useMatchmaking:       document.getElementById('gameMatchmaking').checked,
    isActive:             document.getElementById('gameActive').checked,
    translations: translations.map(t => ({
      langId: t.langId,
      displayName: t.name,
      description: t.description || ''
    }))
  };

  try {
    await api('POST', '/api/admin/games', body);
  } catch { return; }

  gameModalInstance.hide();
  await loadGames();
}

// ---------------------------------------------------------------------------
// Delete
// ---------------------------------------------------------------------------
async function deleteGame(gameId) {
  const game = games.find(g => g.gameId === gameId);
  if (!confirm(`Delete "${game.displayName || game.gameKey}"?`)) return;

  try {
    await api('DELETE', `/api/admin/games/${gameId}`);
  } catch (e) {
    const impact = e.payload && e.payload.details;
    if (impact && impact.hasProgress) {
      if (!confirm(
        `This game has recorded progress:\n\n` +
        `  ${impact.lessonProgressRows} lesson row(s)\n` +
        `  ${impact.questionProgressRows} question row(s)\n` +
        `  ${impact.unlocks} unlock(s)\n` +
        `  across ${impact.students} student(s)\n\n` +
        `Deleting destroys all of it permanently. Deactivating is the reversible alternative.\n\n` +
        `Delete anyway?`)) return;

      try {
        await api('DELETE', `/api/admin/games/${gameId}?force=true`);
      } catch { return; }
    } else {
      return;
    }
  }

  await loadGames();
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initGames() {
  if (!guardAuth()) return;
  initNav('games');

  gameModalInstance = new bootstrap.Modal(document.getElementById('gameModal'));

  // Expose to inline onclick handlers
  window.openGameModal = openGameModal;
  window.submitGame = submitGame;
  window.deleteGame = deleteGame;
  window.loadGames = loadGames;

  try {
    await loadLanguages([]);
    await loadGames();
  } catch (e) { /* already toasted */ }
}
