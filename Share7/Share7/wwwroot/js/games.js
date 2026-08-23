// ===========================================================================
// Share7 Admin Console — Games page logic
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations } from './api.js';
import { escapeHtml, slugify, missingLanguages } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

let games = [];
let editingGameId = null;
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
           <td class="text-center muted-sm mono">${g.gameplaySceneAddress
                ? 'addressable'
                : `${g.lobbyScene}/${g.gameplayScene}`}</td>
           <td class="text-center">${g.isActive
                ? '<span class="badge text-bg-success">yes</span>'
                : '<span class="badge text-bg-secondary">no</span>'}</td>
           <td class="text-end text-nowrap">
             <button class="btn btn-sm btn-outline-secondary" title="Edit"
                     onclick="openGameModal('${g.gameId}')"><i class="bi bi-pencil"></i></button>
             <button class="btn btn-sm btn-outline-danger" title="Delete"
                     onclick="deleteGame('${g.gameId}')"><i class="bi bi-trash"></i></button>
           </td>
         </tr>`).join('')}</tbody></table></div>
       <div class="form-text mt-2 px-3 pb-2">
         <code>PUT /api/admin/games/{id}</code> is a <strong>full replace including
         translations</strong>, so Edit fills its form from <code>GET /api/admin/games/{id}</code> —
         the authoring read, which returns every language — rather than from this listing, which
         only carries the name in your current one. Retire a game with <em>Active</em> off; Delete
         destroys every student's progress for it.
       </div>`
    : '<div class="empty"><i class="bi bi-controller"></i>No games yet. Add one — progress and multiplayer sessions are both keyed by game.</div>';
}

// ---------------------------------------------------------------------------
// Create & edit
// ---------------------------------------------------------------------------
/**
 * Opens the authoring form, empty for a new game or filled for an existing one.
 *
 * The fill comes from `GET /api/admin/games/{id}`, **not** from the row in `games`. `PUT` is a full
 * replace, and the listing resolves one translation from the caller's token — filling from it would
 * send that language back on its own and delete every other one. Every field the save request takes
 * is on this form for the same reason: anything left out is not "unchanged", it is cleared.
 */
async function openGameModal(gameId) {
  if (!state.languages.length) {
    toast('Languages not loaded', 'Sign in first — a name is required per language.');
    return;
  }

  let game = null;
  if (gameId) {
    try {
      game = await api('GET', `/api/admin/games/${gameId}`);
    } catch { return; }
  }

  editingGameId = gameId || null;

  document.getElementById('gameTitle').textContent = game ? `Edit "${game.gameKey}"` : 'Add game';
  document.getElementById('gameSaveBtn').textContent = game ? 'Save' : 'Create';
  document.getElementById('gameReplaceNote').classList.toggle('d-none', !game);

  // The key is the stable machine name the Unity catalog refers to, so it is authored once —
  // the same stance the product and objective keys take.
  const keyInput = document.getElementById('gameKey');
  keyInput.value = game ? game.gameKey : '';
  keyInput.disabled = !!game;

  document.getElementById('gameLobbyScene').value      = game ? game.lobbyScene : 0;
  document.getElementById('gameGameplayScene').value   = game ? game.gameplayScene : 0;
  document.getElementById('gameLobbyAddress').value    = (game && game.lobbySceneAddress) || '';
  document.getElementById('gameGameplayAddress').value = (game && game.gameplaySceneAddress) || '';
  document.getElementById('gameMinPlayers').value      = game ? game.minPlayers : 1;
  document.getElementById('gameMaxPlayers').value      = game ? game.maxPlayers : 2;
  document.getElementById('gameReadyTimeout').value    = game ? game.readyTimeoutSeconds : 20;

  for (const [id, field] of [['gameSingle', 'supportsSinglePlayer'], ['gameMulti', 'supportsMultiplayer'],
                             ['gameLobby', 'useLobby'], ['gameMatchmaking', 'useMatchmaking'],
                             ['gameActive', 'isActive']]) {
    document.getElementById(id).checked = game ? game[field] : true;
  }

  // The save request calls it `displayName`; the shared translation fields call it `name`.
  translationFields('gameTranslations', (game ? game.translations : []).map(t => ({
    langId: t.langId,
    name: t.displayName,
    description: t.description
  })));

  gameModalInstance.show();
}

async function submitGame() {
  const gameKey = editingGameId
    ? document.getElementById('gameKey').value.trim()
    : slugify(document.getElementById('gameKey').value);
  const minPlayers = Number(document.getElementById('gameMinPlayers').value);
  const maxPlayers = Number(document.getElementById('gameMaxPlayers').value);
  const supportsSinglePlayer = document.getElementById('gameSingle').checked;
  const supportsMultiplayer = document.getElementById('gameMulti').checked;
  const useLobby = document.getElementById('gameLobby').checked;
  const lobbySceneAddress = document.getElementById('gameLobbyAddress').value.trim();
  const gameplaySceneAddress = document.getElementById('gameGameplayAddress').value.trim();
  const translations = collectTranslations('gameTranslations');
  const missing = missingLanguages(translations);

  if (!gameKey) { toast('Game key required', 'It is the stable machine name everything else refers to.'); return; }
  if (missing.length) { toast('A name is required in every language', `Missing: ${missing.join(', ')}.`); return; }

  if (!supportsSinglePlayer && !supportsMultiplayer) {
    toast('A game must support a mode', 'Tick single-player, multiplayer, or both.');
    return;
  }
  if (maxPlayers < minPlayers) {
    toast('Max players is below min', 'A session could never reach its minimum, so it could never start.');
    return;
  }
  if (!supportsMultiplayer && maxPlayers > 1) {
    toast('Max players must be 1 without multiplayer', 'Matchmaking and the declared modes would disagree.');
    return;
  }
  if (!supportsSinglePlayer && minPlayers < 2) {
    toast('Min players must be at least 2 without single-player', 'One player could start a game the mode says needs two.');
    return;
  }

  // The same pair rules the server enforces, said before the round trip rather than after it.
  if (!gameplaySceneAddress && lobbySceneAddress) {
    toast('A lobby address on its own is never read',
      'The gameplay address is what puts a game on addressable scenes. Give it one, or clear both.');
    return;
  }
  if (gameplaySceneAddress && useLobby && !lobbySceneAddress) {
    toast('A lobby needs an address on addressable scenes',
      'Give the lobby an address, or turn Lobby off.');
    return;
  }

  const body = {
    gameKey,
    lobbyScene:           Number(document.getElementById('gameLobbyScene').value),
    gameplayScene:        Number(document.getElementById('gameGameplayScene').value),
    lobbySceneAddress:    lobbySceneAddress || null,
    gameplaySceneAddress: gameplaySceneAddress || null,
    minPlayers,
    maxPlayers,
    readyTimeoutSeconds:  Number(document.getElementById('gameReadyTimeout').value),
    supportsSinglePlayer,
    supportsMultiplayer,
    useLobby,
    useMatchmaking:       document.getElementById('gameMatchmaking').checked,
    isActive:             document.getElementById('gameActive').checked,
    translations: translations.map(t => ({
      langId: t.langId,
      displayName: t.name,
      description: t.description || ''
    }))
  };

  try {
    if (editingGameId) {
      await api('PUT', `/api/admin/games/${editingGameId}`, body);
    } else {
      await api('POST', '/api/admin/games', body);
    }
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
