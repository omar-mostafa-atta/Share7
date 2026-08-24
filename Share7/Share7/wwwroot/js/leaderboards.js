// ===========================================================================
// Share7 Admin Console — Leaderboards page logic
//
// Covers every operation on /api/admin/leaderboards: board authoring, the
// cycles under a board (rebuild / settle / author an event window), the metric
// bounds that decide what counts as a believable result, and the review queue
// of results those bounds held back.
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations } from './api.js';
import { escapeHtml, textFor } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

// ---------------------------------------------------------------------------
// Vocabulary — mirrors the server's enums and LeaderboardMetrics
//
// Offered as fixed lists rather than free text because every one of these is
// refused server-side if it is not recognised, and a board authored against a
// metric nothing raises does not fail: it stays empty forever, which is
// indistinguishable from an unpopular one.
// ---------------------------------------------------------------------------
const METRICS = [
  'LESSONS_COMPLETED', 'LESSONS_ACED', 'TOTAL_LESSON_SCORE', 'LESSON_BEST_PERCENT',
  'RUNS_SETTLED', 'RUNS_COMPLETED', 'RUN_SECONDS', 'BEST_RUN_SECONDS',
  'PICKUPS_COLLECTED', 'CURRENCY_EARNED'
];

const PERIODS      = ['AllTime', 'Daily', 'Weekly', 'Monthly', 'Event'];
const AGGREGATIONS = ['Best', 'Sum', 'Last'];
const SORTS        = ['Desc', 'Asc'];

// School, Class, Friends and Country exist on the server's enum but are refused
// at authoring until the schema can resolve them, so they are not offered here.
const COHORTS = ['All', 'Grade'];

let boards = [];
let bounds = [];
let flagged = [];
let games = [];
let grades = [];
let cycles = [];

let selectedBoardId = null;   // whose cycles are on screen
let editingBoardId = null;
let cycleBoardId = null;      // board an event cycle is being authored for

let boardModal = null;
let cycleModal = null;
let boundModal = null;

// ---------------------------------------------------------------------------
// Small shared helpers
// ---------------------------------------------------------------------------
const gameName = id => {
  if (!id) return 'all games';
  const game = games.find(g => g.gameId === id);
  return game ? (game.displayName || game.gameKey) : id;
};

const gradeName = id => {
  const grade = grades.find(g => g.id === id);
  return grade ? grade.name : id;
};

/** A board's name in the console's current language, falling back to its key. */
const boardName = board =>
  textFor(board.translations, state.selectedLangId, 'name') || board.boardKey;

const options = (values, selected) => values
  .map(v => `<option value="${v}" ${v === selected ? 'selected' : ''}>${v}</option>`)
  .join('');

/** UTC instants, rendered as such. A cycle boundary read in local time is a support ticket. */
const utc = value => {
  if (!value) return '—';
  return new Date(value).toISOString().replace('T', ' ').slice(0, 16) + 'Z';
};

const CYCLE_BADGE = {
  Scheduled: 'text-bg-secondary',
  Open:      'text-bg-success',
  Closed:    'text-bg-warning',
  Settled:   'text-bg-primary'
};

// ---------------------------------------------------------------------------
// Boards
// ---------------------------------------------------------------------------
async function loadBoards() {
  boards = await api('GET', '/api/admin/leaderboards/boards');
  renderBoards();
}

function renderBoards() {
  const host = document.getElementById('boardList');

  if (!boards.length) {
    host.innerHTML = `<div class="empty"><i class="bi bi-bar-chart-line"></i>
      No boards yet. Until one exists, the Progress tab in the app has nothing to open.</div>`;
    return;
  }

  host.innerHTML = `<div class="table-responsive">
    <table class="table table-sm table-hover align-middle mb-0">
      <thead><tr>
        <th>Name</th><th>Key</th><th>Metric</th>
        <th class="text-center">Period</th><th class="text-center">Ranks by</th>
        <th class="text-center">Cohorts</th><th class="text-center">Scope</th>
        <th class="text-center">Cycles</th><th class="text-center">Active</th><th></th>
      </tr></thead>
      <tbody>${boards.map(b => `<tr>
        <td>${escapeHtml(boardName(b))}</td>
        <td><span class="kind-token">${escapeHtml(b.boardKey)}</span></td>
        <td class="mono muted-sm">${escapeHtml(b.metric)}</td>
        <td class="text-center muted-sm">${escapeHtml(b.period)}</td>
        <td class="text-center muted-sm">${escapeHtml(b.aggregation)} · ${escapeHtml(b.sortDirection)}</td>
        <td class="text-center muted-sm">${escapeHtml(b.supportedCohorts)}</td>
        <td class="text-center muted-sm">
          ${escapeHtml(gameName(b.gameId))}${b.gradeId ? ` · ${escapeHtml(gradeName(b.gradeId))}` : ''}
        </td>
        <td class="text-center">${b.cycleCount}</td>
        <td class="text-center">${b.isActive
             ? '<span class="badge text-bg-success">yes</span>'
             : '<span class="badge text-bg-secondary">no</span>'}</td>
        <td class="text-end text-nowrap">
          <button class="btn btn-sm btn-outline-primary" title="Cycles"
                  onclick="showCycles('${b.boardId}')"><i class="bi bi-clock-history"></i></button>
          <button class="btn btn-sm btn-outline-secondary" title="Edit"
                  onclick="openBoardModal('${b.boardId}')"><i class="bi bi-pencil"></i></button>
        </td>
      </tr>`).join('')}</tbody>
    </table></div>
    <div class="form-text mt-2 px-3 pb-2">
      There is no delete. A board's key is referenced by every settlement it has ever paid, so a
      board is retired with <em>Active</em> off rather than removed. Key, metric and aggregation are
      fixed once created — changing any of them would silently alter what the existing numbers mean.
    </div>`;
}

/**
 * Opens the board form, empty for a new board or filled for an existing one.
 *
 * On edit, the fields the server refuses to change are shown but disabled. They are still sent,
 * because the save request is validated whole — but the server ignores them, so what is on screen
 * is what will still be true afterwards.
 */
function openBoardModal(boardId) {
  if (!state.languages.length) {
    toast('Languages not loaded', 'Sign in first — a board needs a name in at least one language.');
    return;
  }

  const board = boardId ? boards.find(b => b.boardId === boardId) : null;
  editingBoardId = boardId || null;

  document.getElementById('boardTitle').textContent =
    board ? `Edit "${board.boardKey}"` : 'Add board';
  document.getElementById('boardSaveBtn').textContent = board ? 'Save' : 'Create';
  document.getElementById('boardFixedNote').classList.toggle('d-none', !board);

  const key = document.getElementById('boardKey');
  key.value = board ? board.boardKey : '';
  key.disabled = !!board;

  document.getElementById('boardMetric').innerHTML =
    options(METRICS, board ? board.metric : METRICS[0]);
  document.getElementById('boardMetric').disabled = !!board;

  document.getElementById('boardAggregation').innerHTML =
    options(AGGREGATIONS, board ? board.aggregation : 'Best');
  document.getElementById('boardAggregation').disabled = !!board;

  document.getElementById('boardPeriod').innerHTML =
    options(PERIODS, board ? board.period : 'Weekly');
  document.getElementById('boardPeriod').disabled = !!board;

  document.getElementById('boardSort').innerHTML =
    options(SORTS, board ? board.sortDirection : 'Desc');

  // Scope. Both are set at creation only — the server does not read them on update.
  document.getElementById('boardGame').innerHTML =
    `<option value="">All games (platform-wide)</option>` +
    games.map(g => `<option value="${g.gameId}" ${board && board.gameId === g.gameId ? 'selected' : ''}>
       ${escapeHtml(g.displayName || g.gameKey)}</option>`).join('');
  document.getElementById('boardGame').disabled = !!board;

  document.getElementById('boardGrade').innerHTML =
    `<option value="">Every grade</option>` +
    grades.map(g => `<option value="${g.id}" ${board && board.gradeId === g.id ? 'selected' : ''}>
       ${escapeHtml(g.name)}</option>`).join('');
  document.getElementById('boardGrade').disabled = !!board;

  const selected = (board ? board.supportedCohorts : 'All')
    .split(',').map(c => c.trim());
  document.getElementById('boardCohorts').innerHTML = COHORTS.map(c => `
    <div class="col-auto form-check">
      <input class="form-check-input board-cohort" type="checkbox" id="cohort-${c}"
             value="${c}" ${selected.includes(c) ? 'checked' : ''} />
      <label class="form-check-label lvl-label" for="cohort-${c}">${c}</label>
    </div>`).join('');

  document.getElementById('boardRankLimit').value =
    board && board.visibleRankLimit != null ? board.visibleRankLimit : '';
  document.getElementById('boardGrace').value = board ? board.graceSeconds : 60;
  document.getElementById('boardActive').checked = board ? board.isActive : true;

  translationFields('boardTranslations', board ? board.translations : []);

  boardModal.show();
}

async function submitBoard() {
  const boardKey = document.getElementById('boardKey').value.trim().toLowerCase();
  const cohorts = [...document.querySelectorAll('.board-cohort:checked')].map(el => el.value);

  // Only the languages actually filled in. The server asks for at least one, not for all of them,
  // and sending an empty name would author a board that renders blank in that language.
  const translations = collectTranslations('boardTranslations')
    .filter(t => t.name)
    .map(t => ({ langId: t.langId, name: t.name, description: t.description || null }));

  if (!boardKey) {
    toast('Board key required', 'It is what every settlement this board pays will reference.');
    return;
  }
  if (!/^[a-z0-9_.-]+$/.test(boardKey)) {
    toast('Board key is lowercase and dotted',
      'Letters, digits, dots, dashes and underscores — e.g. platform.all.lessons_completed.weekly.');
    return;
  }
  if (!cohorts.length) {
    toast('A board has to offer at least one cohort', 'All is the safe default.');
    return;
  }
  if (!translations.length) {
    toast('A board needs a name', 'At least one language has to be filled in.');
    return;
  }

  const rankLimit = document.getElementById('boardRankLimit').value.trim();

  const body = {
    boardKey,
    metric:           document.getElementById('boardMetric').value,
    sortDirection:    document.getElementById('boardSort').value,
    aggregation:      document.getElementById('boardAggregation').value,
    period:           document.getElementById('boardPeriod').value,
    supportedCohorts: cohorts.join(','),
    gameId:           document.getElementById('boardGame').value || null,
    gradeId:          document.getElementById('boardGrade').value || null,
    langId:           null,
    visibleRankLimit: rankLimit === '' ? null : Number(rankLimit),
    graceSeconds:     Number(document.getElementById('boardGrace').value),
    isActive:         document.getElementById('boardActive').checked,
    translations
  };

  try {
    if (editingBoardId) {
      await api('PUT', `/api/admin/leaderboards/boards/${editingBoardId}`, body);
    } else {
      await api('POST', '/api/admin/leaderboards/boards', body);
    }
  } catch { return; }

  boardModal.hide();
  await loadBoards();

  if (selectedBoardId) await showCycles(selectedBoardId);
}

// ---------------------------------------------------------------------------
// Cycles
// ---------------------------------------------------------------------------
async function showCycles(boardId) {
  selectedBoardId = boardId;

  const board = boards.find(b => b.boardId === boardId);
  document.getElementById('cycleCard').classList.remove('d-none');
  document.getElementById('cycleCardTitle').textContent =
    board ? `Cycles — ${boardName(board)}` : 'Cycles';

  // Authoring a window by hand is only meaningful for a board whose period is not a calendar:
  // every other period has its window opened for it by the rollover service.
  document.getElementById('btnAddCycle')
    .classList.toggle('d-none', !board || board.period !== 'Event');

  try {
    cycles = await api('GET', `/api/admin/leaderboards/boards/${boardId}/cycles`);
  } catch { return; }

  renderCycles();
}

function renderCycles() {
  const host = document.getElementById('cycleList');

  if (!cycles.length) {
    host.innerHTML = `<div class="empty"><i class="bi bi-clock-history"></i>
      No cycles. A calendar board opens its own on the next rollover; an event board needs one authored.</div>`;
    return;
  }

  host.innerHTML = `<div class="table-responsive">
    <table class="table table-sm table-hover align-middle mb-0">
      <thead><tr>
        <th>Window (UTC)</th><th class="text-center">State</th>
        <th class="text-center">Ranked</th><th class="mono">Cycle id</th><th></th>
      </tr></thead>
      <tbody>${cycles.map(c => `<tr>
        <td class="muted-sm">${utc(c.startsAtUtc)} → ${c.endsAtUtc ? utc(c.endsAtUtc) : 'no end'}</td>
        <td class="text-center">
          <span class="badge ${CYCLE_BADGE[c.state] || 'text-bg-secondary'}">${escapeHtml(c.state)}</span>
        </td>
        <td class="text-center">${c.totalRanked}</td>
        <td class="mono muted-sm">${escapeHtml(c.cycleId)}</td>
        <td class="text-end text-nowrap">
          <button class="btn btn-sm btn-outline-secondary" title="Rebuild from results"
                  onclick="rebuildCycle('${c.cycleId}')"><i class="bi bi-arrow-repeat"></i></button>
          <button class="btn btn-sm btn-outline-primary" title="Settle now"
                  onclick="settleCycle('${c.cycleId}')"><i class="bi bi-cash-coin"></i></button>
        </td>
      </tr>`).join('')}</tbody>
    </table></div>
    <div class="form-text mt-2 px-3 pb-2">
      <strong>Rebuild</strong> throws a cycle's entries away and replays them from
      <code>GameResults</code>. Entries are purely derived, so this is safe against live data — if it
      ever produced different ranks, that would be a defect in the projector rather than a reason not
      to run it. <strong>Settle</strong> finalises a closed cycle and issues its rewards now instead
      of waiting for the scheduled job; it is idempotent, so a settled cycle does not pay twice.
    </div>`;
}

async function rebuildCycle(cycleId) {
  if (!confirm('Rebuild this cycle from GameResults?\n\nRanks are recomputed from scratch. Safe to run on live data.')) return;

  try {
    await api('POST', `/api/admin/leaderboards/cycles/${cycleId}/rebuild`);
  } catch { return; }

  toast('Cycle rebuilt', 'Ranks were replayed from the recorded results.', 'success');
  await showCycles(selectedBoardId);
}

async function settleCycle(cycleId) {
  if (!confirm(
    'Settle this cycle now?\n\n' +
    'Final ranks are frozen and rewards are issued. This is what the scheduled job would do ' +
    'after the grace window — running it early can pay a child before a result still in flight ' +
    'has landed.\n\nSettle anyway?')) return;

  try {
    await api('POST', `/api/admin/leaderboards/cycles/${cycleId}/settle`);
  } catch { return; }

  toast('Cycle settled', 'Final ranks are frozen and rewards were issued.', 'success');
  await showCycles(selectedBoardId);
  await loadBoards();
}

function openCycleModal() {
  cycleBoardId = selectedBoardId;
  document.getElementById('cycleStart').value = '';
  document.getElementById('cycleEnd').value = '';
  cycleModal.show();
}

async function submitCycle() {
  const start = document.getElementById('cycleStart').value;
  const end = document.getElementById('cycleEnd').value;

  if (!start || !end) { toast('Both ends are required', 'An event window has no default bounds.'); return; }
  if (new Date(end) <= new Date(start)) {
    toast('The window ends before it starts', 'Nothing could ever be ranked in it.');
    return;
  }

  // The inputs are naive wall-clock; the API takes UTC. Stamping the Z here is what stops a window
  // being authored an hour or two away from the one the operator meant.
  try {
    await api('POST', `/api/admin/leaderboards/boards/${cycleBoardId}/cycles`, {
      startsAtUtc: `${start}:00Z`,
      endsAtUtc:   `${end}:00Z`
    });
  } catch { return; }

  cycleModal.hide();
  await showCycles(cycleBoardId);
  await loadBoards();
}

// ---------------------------------------------------------------------------
// Metric bounds — the anti-cheat ceiling
// ---------------------------------------------------------------------------
async function loadBounds() {
  bounds = await api('GET', '/api/admin/leaderboards/bounds');
  renderBounds();
}

function renderBounds() {
  const host = document.getElementById('boundList');

  if (!bounds.length) {
    host.innerHTML = `<div class="empty"><i class="bi bi-shield-check"></i>
      No bounds. Every result is believed, and nothing is ever flagged for review.</div>`;
    return;
  }

  host.innerHTML = `<div class="table-responsive">
    <table class="table table-sm table-hover align-middle mb-0">
      <thead><tr>
        <th>Metric</th><th>Applies to</th>
        <th class="text-end">Max value</th><th class="text-end">Max / day</th>
        <th class="text-end">Max value / day</th><th class="text-center">On</th><th></th>
      </tr></thead>
      <tbody>${bounds.map(b => `<tr>
        <td class="mono">${escapeHtml(b.metric)}</td>
        <td class="muted-sm">${escapeHtml(gameName(b.gameId))}</td>
        <td class="text-end">${b.maxValue ?? '—'}</td>
        <td class="text-end">${b.maxResultsPerDay ?? '—'}</td>
        <td class="text-end">${b.maxValuePerDay ?? '—'}</td>
        <td class="text-center">${b.enabled
             ? '<span class="badge text-bg-success">yes</span>'
             : '<span class="badge text-bg-secondary">no</span>'}</td>
        <td class="text-end">
          <button class="btn btn-sm btn-outline-secondary" title="Edit"
                  onclick="openBoundModal('${b.id}')"><i class="bi bi-pencil"></i></button>
        </td>
      </tr>`).join('')}</tbody>
    </table></div>
    <div class="form-text mt-2 px-3 pb-2">
      A bound is keyed by metric + game, so saving one that already exists replaces it rather than
      adding a second. Bounds are data precisely so that tightening one after a live exploit is a row
      edit and not a release — the answer key has to be client-visible for a quiz to mark a tap right
      or wrong instantly, and this is the compensating control.
    </div>`;
}

function openBoundModal(boundId) {
  const bound = boundId ? bounds.find(b => b.id === boundId) : null;

  document.getElementById('boundTitle').textContent = bound ? 'Edit bound' : 'Add bound';

  document.getElementById('boundMetric').innerHTML =
    options(METRICS, bound ? bound.metric : METRICS[0]);
  document.getElementById('boundMetric').disabled = !!bound;

  document.getElementById('boundGame').innerHTML =
    `<option value="">Every game raising this metric</option>` +
    games.map(g => `<option value="${g.gameId}" ${bound && bound.gameId === g.gameId ? 'selected' : ''}>
       ${escapeHtml(g.displayName || g.gameKey)}</option>`).join('');
  document.getElementById('boundGame').disabled = !!bound;

  document.getElementById('boundMaxValue').value       = bound && bound.maxValue != null ? bound.maxValue : '';
  document.getElementById('boundMaxPerDay').value      = bound && bound.maxResultsPerDay != null ? bound.maxResultsPerDay : '';
  document.getElementById('boundMaxValuePerDay').value = bound && bound.maxValuePerDay != null ? bound.maxValuePerDay : '';
  document.getElementById('boundEnabled').checked      = bound ? bound.enabled : true;

  boundModal.show();
}

async function submitBound() {
  const num = id => {
    const raw = document.getElementById(id).value.trim();
    return raw === '' ? null : Number(raw);
  };

  const maxValue = num('boundMaxValue');
  const maxResultsPerDay = num('boundMaxPerDay');
  const maxValuePerDay = num('boundMaxValuePerDay');

  // The same refusal the server makes, said before the round trip. A bound that limits nothing is a
  // row that looks like protection and is not.
  if (maxValue === null && maxResultsPerDay === null && maxValuePerDay === null) {
    toast('A bound has to limit something',
      'Set at least one of value, results per day, or value per day.');
    return;
  }

  try {
    await api('PUT', '/api/admin/leaderboards/bounds', {
      gameId: document.getElementById('boundGame').value || null,
      metric: document.getElementById('boundMetric').value,
      maxValue,
      maxResultsPerDay,
      maxValuePerDay,
      enabled: document.getElementById('boundEnabled').checked
    });
  } catch { return; }

  boundModal.hide();
  await loadBounds();
}

// ---------------------------------------------------------------------------
// Review queue — results the bounds held back
// ---------------------------------------------------------------------------
async function loadFlagged() {
  flagged = await api('GET', '/api/admin/leaderboards/flagged?limit=50');
  renderFlagged();
}

function renderFlagged() {
  const host = document.getElementById('flaggedList');

  if (!flagged.length) {
    host.innerHTML = `<div class="empty"><i class="bi bi-check2-circle"></i>
      Nothing waiting. No result is currently held out of ranking.</div>`;
    return;
  }

  host.innerHTML = `<div class="table-responsive">
    <table class="table table-sm table-hover align-middle mb-0">
      <thead><tr>
        <th>Player</th><th>Game</th><th>Metric</th>
        <th class="text-end">Value</th><th>When (UTC)</th><th>Why</th><th></th>
      </tr></thead>
      <tbody>${flagged.map(f => `<tr>
        <td>${escapeHtml(f.displayName)}</td>
        <td class="muted-sm">${escapeHtml(gameName(f.gameId))}</td>
        <td class="mono muted-sm">${escapeHtml(f.metric)}</td>
        <td class="text-end">${f.value}</td>
        <td class="muted-sm">${utc(f.occurredAtUtc)}</td>
        <td class="muted-sm">${escapeHtml(f.flagReason || '—')}</td>
        <td class="text-end text-nowrap">
          <button class="btn btn-sm btn-outline-success" title="Legitimate — let it rank"
                  onclick="resolveFlag('${f.resultId}', true)"><i class="bi bi-check-lg"></i></button>
          <button class="btn btn-sm btn-outline-danger" title="Uphold — keep it excluded"
                  onclick="resolveFlag('${f.resultId}', false)"><i class="bi bi-x-lg"></i></button>
        </td>
      </tr>`).join('')}</tbody>
    </table></div>
    <div class="form-text mt-2 px-3 pb-2">
      Players appear under their public handle only — nothing about judging whether a score is real
      requires knowing which child earned it. Clearing a flag re-queues the result so the player takes
      the rank they should have had; upholding one leaves it excluded. The row survives either way,
      because judgements get revisited and deleting the evidence would make that impossible.
    </div>`;
}

async function resolveFlag(resultId, legitimate) {
  const verb = legitimate
    ? 'Clear this flag and let the result rank?'
    : 'Uphold this flag and keep the result out of ranking?';

  if (!confirm(verb)) return;

  try {
    await api('POST', `/api/admin/leaderboards/flagged/${resultId}/resolve`, { legitimate });
  } catch { return; }

  toast('Decision recorded',
    legitimate ? 'The result was re-queued for projection.' : 'The result stays excluded.',
    'success');
  await loadFlagged();
}

// ---------------------------------------------------------------------------
// Sub-tabs
// ---------------------------------------------------------------------------
function initSubTabs() {
  document.querySelectorAll('.sub-tab').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.sub-tab').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      document.querySelectorAll('.sub-pane').forEach(p => p.classList.remove('active'));
      const target = document.getElementById(btn.dataset.target);
      if (target) target.classList.add('active');
    });
  });
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initLeaderboards() {
  if (!guardAuth()) return;
  initNav('leaderboards');
  initSubTabs();

  boardModal = new bootstrap.Modal(document.getElementById('boardModal'));
  cycleModal = new bootstrap.Modal(document.getElementById('cycleModal'));
  boundModal = new bootstrap.Modal(document.getElementById('boundModal'));

  // Exposed for the inline onclick handlers, the same way every other page does it.
  Object.assign(window, {
    openBoardModal, submitBoard,
    showCycles, rebuildCycle, settleCycle, openCycleModal, submitCycle,
    openBoundModal, submitBound,
    resolveFlag,
    loadBoards, loadBounds, loadFlagged
  });

  try {
    await loadLanguages([]);

    // Games and grades name a board's scope and a flagged result's origin. Both are read once:
    // neither changes while somebody is authoring a board, and re-fetching per render would put a
    // request behind every keystroke that opens a form.
    games = await api('GET', '/api/admin/games').catch(() => []);
    const gradeRows = await api('GET', `/api/grades?langId=${state.selectedLangId}`).catch(() => []);
    grades = (gradeRows || []).map(g => ({ id: g.id, name: g.name ?? g.grade }));

    await loadBoards();
    await loadBounds();
    await loadFlagged();
  } catch { /* already toasted */ }
}
