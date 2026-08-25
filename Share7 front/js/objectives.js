// ===========================================================================
// Share7 Admin Console — Objectives page logic
// Daily/weekly quests and achievements: one table, differing only by cycle.
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations } from './api.js';
import { escapeHtml, textFor } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

// ---------------------------------------------------------------------------
// Vocabulary — every list here is validated again on the server
// ---------------------------------------------------------------------------

/**
 * Mirrors `LeaderboardMetrics.Known`. Authoring is validated against that list server-side, so a
 * metric invented here is refused with "nothing would ever raise it" — which is the whole point:
 * an objective on a metric nothing raises never errors and never completes.
 */
const METRICS = [
  { value: 'LESSONS_COMPLETED',   agg: 'SUM',  scope: null,
    hint: 'Lessons passed, counted once each.' },
  { value: 'LESSONS_ACED',        agg: 'SUM',  scope: null,
    hint: 'Lessons answered perfectly, counted once each.' },
  { value: 'TOTAL_LESSON_SCORE',  agg: 'SUM',  scope: null,
    hint: 'Best percent summed across every lesson. Raised as the improvement only, so replaying a lesson already at 90% adds nothing.' },
  { value: 'LESSON_BEST_PERCENT', agg: 'BEST', scope: null,
    hint: 'Best score on any single lesson, in whole percent. Tops out at 100 and never falls.' },
  { value: 'RUNS_SETTLED',        agg: 'SUM',  scope: null,
    hint: 'Runs finished and settled, whatever the outcome. A run that failed still happened.' },
  { value: 'RUNS_COMPLETED',      agg: 'SUM',  scope: null,
    hint: 'Runs whose outcome was Completed.' },
  { value: 'RUN_SECONDS',         agg: 'SUM',  scope: null,
    hint: 'Seconds played, summed. Taken from the server-bounded duration, never the reported one.' },
  { value: 'BEST_RUN_SECONDS',    agg: 'BEST', scope: null,
    hint: 'The longest single run, in seconds. Pair it with BEST, not SUM.' },
  { value: 'PICKUPS_COLLECTED',   agg: 'SUM',  scope: 'signal',
    hint: 'Signals as settled — after the per-run cap, never as reported.' },
  { value: 'CURRENCY_EARNED',     agg: 'SUM',  scope: 'currency',
    hint: 'Currency actually credited, net of caps. One result per settlement.' }
];

/** Mirrors `ObjectiveKind`. The kind is a statement about the cycle and nothing else. */
const KINDS = [
  { value: 'DAILY',       label: 'Daily',       badge: 'text-bg-primary',
    hint: 'resets every day' },
  { value: 'WEEKLY',      label: 'Weekly',      badge: 'text-bg-info',
    hint: 'resets every ISO week' },
  { value: 'MONTHLY',     label: 'Monthly',     badge: 'text-bg-secondary',
    hint: 'resets every calendar month' },
  { value: 'SEASONAL',    label: 'Seasonal',    badge: 'text-bg-warning',
    hint: 'bound to a season, ended by retiring it' },
  { value: 'ACHIEVEMENT', label: 'Achievement', badge: 'text-bg-success',
    hint: 'never resets, one counter forever' }
];

/** Mirrors `LeaderboardAggregation` — the same vocabulary boards use, deliberately. */
const AGGREGATIONS = [
  { value: 'SUM',  hint: 'add every result ("collect 200 coins")' },
  { value: 'BEST', hint: 'keep the better result ("survive 5 minutes in one run")' },
  { value: 'LAST', hint: 'overwrite with the most recent result' }
];

// The key shape the server enforces.
const KEY_SHAPE = /^[a-z][a-z0-9_.]*$/;

// ---------------------------------------------------------------------------
// Module state
// ---------------------------------------------------------------------------
let objectives = [];
let games = [];
let grades = [];
let currencyKeys = [];
let signalKinds = [];
let serverSkewMs = 0;
let editingId = null;
let objectiveModalInstance = null;

const filters = { kind: '', search: '', showRetired: true };

function getLangId() { return state.selectedLangId; }

function kindMeta(kind) {
  return KINDS.find(k => k.value === kind) || { label: kind, badge: 'text-bg-light', hint: '' };
}

function metricMeta(metric) {
  return METRICS.find(m => m.value === metric) || null;
}

// ---------------------------------------------------------------------------
// Server time — the availability window is server UTC, not the browser clock
// ---------------------------------------------------------------------------
async function loadServerTime() {
  const data = await api('GET', '/api/time');
  serverSkewMs = new Date(data.utcNow).getTime() - Date.now();
  document.getElementById('serverTime').textContent = data.utcNow;
}

function serverNow() {
  return new Date(Date.now() + serverSkewMs);
}

function setWindowFromServer(fieldId, hours) {
  const when = new Date(serverNow().getTime() + hours * 3600 * 1000);
  document.getElementById(fieldId).value = when.toISOString().slice(0, 16);
}

function clearWindow(fieldId) {
  document.getElementById(fieldId).value = '';
}

/** `2026-08-23T18:30:39.123Z` to `2026-08-23T18:30`, which is what datetime-local wants. */
function isoToInput(iso) {
  return iso ? String(iso).slice(0, 16) : '';
}

/** The inverse. An empty box means "no bound", which the API spells `null`. */
function inputToIso(value) {
  return value === '' ? null : value + ':00Z';
}

function shortWhen(iso) {
  return iso ? String(iso).replace('T', ' ').slice(0, 16) : '';
}

// ---------------------------------------------------------------------------
// Load
// ---------------------------------------------------------------------------
async function loadObjectives() {
  const data = await api('GET', '/api/admin/objectives');
  objectives = data.objectives || [];
  renderObjectives();
}

async function loadGames() {
  const rows = await api('GET', '/api/admin/games');
  games = Array.isArray(rows) ? rows : (rows.games || []);
}

async function loadGrades() {
  const rows = await api('GET', '/api/grades?langId=' + (getLangId() || ''));
  grades = (Array.isArray(rows) ? rows : []).map(g => ({
    id: g.id,
    name: g.name || g.grade || g.id
  }));
}

/**
 * Scope suggestions, and nothing more — the field stays free text. A scope is only ever matched
 * against what a game result carries, so a signal kind that is not priced yet is still a legal
 * thing to author an objective on today.
 */
async function loadScopeSuggestions() {
  try {
    const data = await api('GET', '/api/currencies');
    currencyKeys = (data.currencies || []).map(c => c.key).filter(Boolean);
  } catch { currencyKeys = []; }

  try {
    const data = await api('GET', '/api/admin/signal-valuations');
    // signalKind is the field; pickupKind is the legacy alias the API still emits, read here so a
    // console served against a backend mid-rollout does not lose its suggestions.
    signalKinds = [...new Set(
      (data.valuations || []).map(v => v.signalKind || v.pickupKind).filter(Boolean))];
  } catch { signalKinds = []; }
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------
function offeredTo(objective) {
  const parts = [];

  if (objective.gameId) {
    const game = games.find(g => g.gameId === objective.gameId);
    parts.push('game: ' + escapeHtml(game ? (game.displayName || game.gameKey) : objective.gameId));
  }
  if (objective.gradeId) {
    const grade = grades.find(g => g.id === objective.gradeId);
    parts.push('grade: ' + escapeHtml(grade ? grade.name : objective.gradeId));
  }
  if (objective.langId) {
    const lang = (state.languages || []).find(l => l.id === objective.langId);
    parts.push('lang: ' + escapeHtml(lang ? lang.code : objective.langId));
  }

  return parts.length
    ? parts.map(p => `<span class="d-block">${p}</span>`).join('')
    : '<span class="muted-sm">everyone</span>';
}

function windowCell(objective) {
  const from = objective.availableFromUtc;
  const to = objective.availableToUtc;
  if (!from && !to) return '<span class="muted-sm">always</span>';

  const now = serverNow().toISOString();
  const pending = from && from > now;
  const over = to && to < now;

  return `<div class="muted-sm text-nowrap">from ${from ? escapeHtml(shortWhen(from)) : '—'}</div>
          <div class="muted-sm text-nowrap">to ${to ? escapeHtml(shortWhen(to)) : '—'}</div>
          ${pending ? '<span class="badge text-bg-warning">not yet</span>' : ''}
          ${over ? '<span class="badge text-bg-danger">window over</span>' : ''}`;
}

function visibleObjectives() {
  const needle = filters.search.trim().toLowerCase();

  return objectives.filter(o => {
    if (filters.kind && o.kind !== filters.kind) return false;
    if (!filters.showRetired && !o.isActive) return false;
    if (!needle) return true;

    const name = textFor(o.translations, getLangId()) || '';
    return o.key.toLowerCase().includes(needle)
        || name.toLowerCase().includes(needle)
        || o.metric.toLowerCase().includes(needle)
        || (o.scope || '').toLowerCase().includes(needle);
  });
}

function renderKindTabs() {
  const counts = { '': objectives.length };
  for (const k of KINDS) counts[k.value] = objectives.filter(o => o.kind === k.value).length;

  const tab = (value, label) => `
    <button class="sub-tab ${filters.kind === value ? 'active' : ''}" onclick="filterKind('${value}')">
      ${escapeHtml(label)}
      <span class="badge text-bg-light border ms-1">${counts[value] || 0}</span>
    </button>`;

  document.getElementById('kindTabs').innerHTML =
    tab('', 'All') + KINDS.map(k => tab(k.value, k.label)).join('');
}

function renderObjectives() {
  renderKindTabs();

  const rows = visibleObjectives();
  const host = document.getElementById('objectiveList');
  const languageCount = (state.languages || []).length;

  if (!rows.length) {
    host.innerHTML = objectives.length
      ? '<div class="empty"><i class="bi bi-funnel"></i>No objective matches this filter.</div>'
      : '<div class="empty"><i class="bi bi-trophy"></i>No objectives yet. A quest is an INSERT — no migration, no client release.</div>';
    return;
  }

  host.innerHTML = `
    <div class="table-responsive"><table class="table table-sm table-hover align-middle mb-0">
      <thead><tr>
        <th>Objective</th><th>Kind</th><th>Counts</th><th class="text-end">Target</th>
        <th class="text-center">Agg</th><th>Offered to</th><th>Window</th>
        <th class="text-center">Sort</th><th class="text-center">State</th><th></th>
      </tr></thead>
      <tbody>${rows.map(o => {
        const kind = kindMeta(o.kind);
        const name = textFor(o.translations, getLangId());
        const description = textFor(o.translations, getLangId(), 'description');
        const missing = languageCount - (o.translations || []).length;

        return `<tr class="${o.isActive ? '' : 'opacity-75'}">
          <td>
            <div>${escapeHtml(name || '(no name in any language)')}</div>
            <div class="mono muted-sm">${escapeHtml(o.key)}</div>
            ${description
              ? `<div class="muted-sm text-truncate" style="max-width:20rem">${escapeHtml(description)}</div>`
              : ''}
            ${missing > 0
              ? `<span class="badge text-bg-warning">${missing} language(s) untranslated</span>`
              : ''}
          </td>
          <td><span class="badge ${kind.badge}">${escapeHtml(kind.label)}</span></td>
          <td>
            <span class="kind-token">${escapeHtml(o.metric)}</span>
            ${o.scope ? `<div class="mono muted-sm">scope: ${escapeHtml(o.scope)}</div>` : ''}
          </td>
          <td class="text-end"><strong>${o.target}</strong></td>
          <td class="text-center muted-sm mono">${escapeHtml(o.aggregation)}</td>
          <td class="muted-sm">${offeredTo(o)}</td>
          <td>${windowCell(o)}</td>
          <td class="text-center muted-sm">${o.sortOrder}</td>
          <td class="text-center">${o.isActive
            ? '<span class="badge text-bg-success">active</span>'
            : '<span class="badge text-bg-secondary">retired</span>'}</td>
          <td class="text-end text-nowrap">
            <button class="btn btn-sm btn-outline-secondary" title="Edit"
                    onclick="openObjectiveModal('${o.objectiveId}')"><i class="bi bi-pencil"></i></button>
            <button class="btn btn-sm btn-outline-danger" title="Delete"
                    onclick="deleteObjective('${o.objectiveId}')"><i class="bi bi-trash"></i></button>
          </td>
        </tr>`;
      }).join('')}</tbody>
    </table></div>`;
}

// ---------------------------------------------------------------------------
// Filters
// ---------------------------------------------------------------------------
function filterKind(kind) {
  filters.kind = kind;
  renderObjectives();
}

function applyFilters() {
  filters.search = document.getElementById('objectiveSearch').value;
  filters.showRetired = document.getElementById('showRetired').checked;
  renderObjectives();
}

// ---------------------------------------------------------------------------
// Modal
// ---------------------------------------------------------------------------
function previewKey() {
  const input = document.getElementById('objKey');
  const cleaned = input.value.trim().toLowerCase().replace(/[^a-z0-9_.]+/g, '.');
  if (cleaned !== input.value) input.value = cleaned;

  const hint = document.getElementById('objKeyHint');

  if (!cleaned) {
    hint.className = 'form-text';
    hint.textContent = 'Lowercase, dot-separated — e.g. daily.lessons.complete.3';
  } else if (KEY_SHAPE.test(cleaned)) {
    hint.className = 'form-text text-success';
    hint.textContent = 'Valid. Frozen once saved — the reward rule that pays for this keys on it.';
  } else {
    hint.className = 'form-text text-danger';
    hint.textContent = 'Must start with a letter, then lowercase letters, digits, underscores and dots.';
  }
}

/** The scope hint and its suggestions depend entirely on which metric is selected. */
function onMetricChange() {
  const metric = metricMeta(document.getElementById('objMetric').value);
  const scope = document.getElementById('objScope');
  const hint = document.getElementById('objScopeHint');
  const list = document.getElementById('scopeSuggestions');

  document.getElementById('objMetricHint').textContent = metric ? metric.hint : '';

  if (!metric || !metric.scope) {
    list.innerHTML = '';
    scope.placeholder = 'not used by this metric';
    hint.textContent = 'This metric has no sub-dimension. Leave it blank.';
    return;
  }

  const options = metric.scope === 'currency' ? currencyKeys : signalKinds;
  list.innerHTML = options.map(o => `<option value="${escapeHtml(o)}"></option>`).join('');

  scope.placeholder = metric.scope === 'currency' ? 'coins' : 'coin';
  hint.textContent = metric.scope === 'currency'
    ? 'A currency key. This is what makes "earn 200 coins" and "earn 20 gems" two rows over one metric.'
    : 'A signal kind — coin, near_miss, distance_m. Blank counts every kind together.';
}

/** Selecting a metric also proposes the aggregation it is meant to be read with. */
function onMetricPicked() {
  const metric = metricMeta(document.getElementById('objMetric').value);
  if (metric) document.getElementById('objAggregation').value = metric.agg;
  onMetricChange();
}

/**
 * Locks the fields the API refuses to change. Key, kind, metric, scope, aggregation and the three
 * filters are absent from `UpdateObjectiveRequest` on purpose: every progress row already counting
 * is counting under the old meaning, and the reward transactions already paid claim against the old
 * key. Retire it and author a new one.
 */
function setFrozen(frozen) {
  for (const id of ['objKey', 'objKind', 'objMetric', 'objScope', 'objAggregation',
                    'objGame', 'objGrade', 'objLang']) {
    document.getElementById(id).disabled = frozen;
  }
  document.getElementById('frozenNote').classList.toggle('d-none', !frozen);
}

function openObjectiveModal(objectiveId) {
  if (!(state.languages || []).length) {
    toast('Languages not loaded', 'An objective needs a name in at least one language.');
    return;
  }

  editingId = objectiveId || null;
  const objective = objectiveId ? objectives.find(o => o.objectiveId === objectiveId) : null;

  document.getElementById('objectiveTitle').textContent =
    objective ? 'Edit "' + objective.key + '"' : 'Add objective';

  document.getElementById('objKind').innerHTML = KINDS.map(k =>
    `<option value="${k.value}" ${objective && objective.kind === k.value ? 'selected' : ''}>
       ${escapeHtml(k.value)} — ${escapeHtml(k.hint)}</option>`).join('');

  document.getElementById('objMetric').innerHTML = METRICS.map(m =>
    `<option value="${m.value}" ${objective && objective.metric === m.value ? 'selected' : ''}>
       ${escapeHtml(m.value)}</option>`).join('');

  document.getElementById('objAggregation').innerHTML = AGGREGATIONS.map(a =>
    `<option value="${a.value}" ${objective && objective.aggregation === a.value ? 'selected' : ''}>
       ${escapeHtml(a.value)} — ${escapeHtml(a.hint)}</option>`).join('');

  document.getElementById('objGame').innerHTML =
    '<option value="">Any game</option>' + games.map(g =>
      `<option value="${g.gameId}" ${objective && objective.gameId === g.gameId ? 'selected' : ''}>
         ${escapeHtml(g.displayName || g.gameKey)}</option>`).join('');

  document.getElementById('objGrade').innerHTML =
    '<option value="">Any grade</option>' + grades.map(g =>
      `<option value="${g.id}" ${objective && objective.gradeId === g.id ? 'selected' : ''}>
         ${escapeHtml(g.name)}</option>`).join('');

  document.getElementById('objLang').innerHTML =
    '<option value="">Any language</option>' + (state.languages || []).map(l =>
      `<option value="${l.id}" ${objective && objective.langId === l.id ? 'selected' : ''}>
         ${escapeHtml(l.name)} (${escapeHtml(l.code)})</option>`).join('');

  document.getElementById('objKey').value      = objective ? objective.key : '';
  document.getElementById('objScope').value    = objective && objective.scope ? objective.scope : '';
  document.getElementById('objTarget').value   = objective ? objective.target : 1;
  document.getElementById('objIcon').value     = objective && objective.iconKey ? objective.iconKey : '';
  document.getElementById('objSort').value     = objective ? objective.sortOrder : 0;
  document.getElementById('objActive').checked = objective ? objective.isActive : true;
  document.getElementById('objFrom').value     = objective ? isoToInput(objective.availableFromUtc) : '';
  document.getElementById('objTo').value       = objective ? isoToInput(objective.availableToUtc) : '';

  translationFields('objTranslations', objective ? objective.translations : []);

  setFrozen(!!objective);
  previewKey();
  onMetricChange();

  objectiveModalInstance.show();
}

// ---------------------------------------------------------------------------
// Save
// ---------------------------------------------------------------------------
async function submitObjective() {
  // Unlike a product or an offer, an objective needs a name in *one* language, not all of them —
  // so blanks are dropped here rather than refused. An empty name fails model validation server-side.
  const translations = collectTranslations('objTranslations')
    .filter(t => t.name)
    .map(t => ({ langId: t.langId, name: t.name, description: t.description }));

  if (!translations.length) {
    toast('An objective needs a name', 'At least one language — a client has nothing to render otherwise.');
    return;
  }

  const target = Number(document.getElementById('objTarget').value);
  if (!Number.isFinite(target) || target < 1) {
    toast('Target must be at least 1', 'A target of zero is complete before it starts.');
    return;
  }

  const availableFromUtc = inputToIso(document.getElementById('objFrom').value);
  const availableToUtc   = inputToIso(document.getElementById('objTo').value);
  if (availableFromUtc && availableToUtc && availableFromUtc >= availableToUtc) {
    toast('The window closes before it opens', 'The objective would never be offered.');
    return;
  }

  const body = {
    target,
    availableFromUtc,
    availableToUtc,
    iconKey: document.getElementById('objIcon').value.trim() || null,
    sortOrder: Number(document.getElementById('objSort').value),
    isActive: document.getElementById('objActive').checked,
    translations
  };

  try {
    if (editingId) {
      await api('PUT', '/api/admin/objectives/' + editingId, body);
    } else {
      // Trailing separators are only trimmed here, never while typing — stripping them on every
      // keystroke would make a dot impossible to type in the middle of a key.
      previewKey();
      const input = document.getElementById('objKey');
      input.value = input.value.replace(/[._]+$/, '');
      const key = input.value.trim();
      if (!KEY_SHAPE.test(key)) {
        toast('A key is required',
          'Lowercase letters, digits, underscores and dots, starting with a letter.');
        return;
      }

      const scope = document.getElementById('objScope').value.trim();

      Object.assign(body, {
        key,
        kind:        document.getElementById('objKind').value,
        metric:      document.getElementById('objMetric').value,
        scope:       scope || null,
        aggregation: document.getElementById('objAggregation').value,
        gameId:      document.getElementById('objGame').value || null,
        gradeId:     document.getElementById('objGrade').value || null,
        langId:      document.getElementById('objLang').value || null
      });

      await api('POST', '/api/admin/objectives', body);
    }
  } catch { return; }

  objectiveModalInstance.hide();
  await loadObjectives();
}

// ---------------------------------------------------------------------------
// Delete
// ---------------------------------------------------------------------------
/**
 * Deletes an objective, and every player counter against it.
 *
 * Retiring is the reversible alternative and is what an operator almost always wants, so the second
 * confirm spells out exactly what is being destroyed rather than asking "are you sure?" again. The
 * server refuses the first call with `409` and the breakdown under `details` — the same two-step
 * shape the game delete uses, so the force flag is never sent by a caller who has not been shown
 * what it costs.
 */
async function deleteObjective(objectiveId) {
  const objective = objectives.find(o => o.objectiveId === objectiveId);
  if (!objective) return;

  const name = textFor(objective.translations, getLangId()) || objective.key;
  if (!confirm(`Delete "${name}"?\n\nRetiring it instead (clear Active) stops it being offered and stops it counting, and loses nothing.`)) return;

  try {
    await api('DELETE', '/api/admin/objectives/' + objectiveId);
  } catch (e) {
    const impact = e.payload && e.payload.details;
    if (!impact || !impact.hasProgress) return;

    if (!confirm(
      `"${objective.key}" has been played:\n\n` +
      `  ${impact.progressRows} progress row(s)\n` +
      `  across ${impact.students} student(s)\n` +
      `  ${impact.completed} completed, awaiting a reward\n` +
      `  ${impact.claimed} already paid\n\n` +
      (impact.rewardRules
        ? `${impact.rewardRules} reward rule(s) key on "${objective.key}" and are NOT deleted — they\n` +
          `will survive as rules nothing can trigger. Clean them up in Reward rules.\n\n`
        : '') +
      `Deleting destroys all of it permanently, and the ledger entries that paid the claimed ones\n` +
      `will no longer resolve to anything. Retiring is the reversible alternative.\n\n` +
      `Delete anyway?`)) return;

    try {
      await api('DELETE', '/api/admin/objectives/' + objectiveId + '?force=true');
    } catch { return; }
  }

  toast('Objective deleted', `"${objective.key}" and its counters are gone.`, 'success');
  await loadObjectives();
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initObjectives() {
  if (!guardAuth()) return;
  initNav('objectives');

  objectiveModalInstance = new bootstrap.Modal(document.getElementById('objectiveModal'));

  // Exposed for the inline handlers, the way every other page in this console does it.
  window.loadObjectives      = loadObjectives;
  window.loadServerTime      = loadServerTime;
  window.openObjectiveModal  = openObjectiveModal;
  window.submitObjective     = submitObjective;
  window.deleteObjective     = deleteObjective;
  window.setWindowFromServer = setWindowFromServer;
  window.clearWindow         = clearWindow;
  window.filterKind          = filterKind;
  window.applyFilters        = applyFilters;
  window.previewKey          = previewKey;
  window.onMetricPicked      = onMetricPicked;

  try {
    await loadLanguages([]);
    await loadServerTime();
    await loadGames();
    await loadGrades();
    await loadScopeSuggestions();
    await loadObjectives();
  } catch (e) { /* already toasted */ }
}
