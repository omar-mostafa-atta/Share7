// ===========================================================================
// Share7 Admin Console — Curriculum page logic
// Covers the curriculum tree, and both question pools — uploaded as a sheet or typed by hand.
// ===========================================================================

import state, { save } from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations, describeError } from './api.js';
import { escapeHtml, missingLanguages } from './utils.js';
import { guardAuth, applyLanguage } from './auth.js';
import { initNav } from './nav.js';

// ---------------------------------------------------------------------------
// Level definitions
// ---------------------------------------------------------------------------
const levels = {
  term:    { parent: 'grade',   next: 'subject', list: id => `/api/terms?gradeId=${id}`,
             create: id => `/api/admin/grades/${id}/terms`,     del: id => `/api/admin/terms/${id}` },
  subject: { parent: 'term',    next: 'chapter', list: id => `/api/subjects?termId=${id}`,
             create: id => `/api/admin/terms/${id}/subjects`,   del: id => `/api/admin/subjects/${id}` },
  chapter: { parent: 'subject', next: 'lesson',  list: id => `/api/chapters?subjectId=${id}`,
             create: id => `/api/admin/subjects/${id}/chapters`, del: id => `/api/admin/chapters/${id}` },
  lesson:  { parent: 'chapter', next: null,       list: id => `/api/lessons?chapterId=${id}`,
             create: id => `/api/admin/chapters/${id}/lessons`,  del: id => `/api/admin/lessons/${id}` }
};

const sel = { grade: null, term: null, subject: null, chapter: null, lesson: null };
let addLevel = null;
let addModalInstance = null;

// ---------------------------------------------------------------------------
// Sub-tab switching (Curriculum / Questions / Recovery)
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
// Rendering helpers
// ---------------------------------------------------------------------------
function renderList(level, items, onPick) {
  const el = document.getElementById('list-' + level);
  if (!items.length) {
    el.innerHTML = '<div class="list-group-item text-muted small">none</div>';
    return;
  }
  el.innerHTML = items.map(i => `
    <button type="button"
            class="list-group-item list-group-item-action d-flex justify-content-between align-items-center"
            data-id="${i.id}">
      <span class="text-truncate">${i.order != null ? i.order + '. ' : ''}${escapeHtml(i.name)}</span>
      ${i.hasQuestions === false ? '<span class="badge text-bg-light border">no Qs</span>' : ''}
      ${i.hasQuestions === true  ? `<span class="badge text-bg-info">v${i.questionsVersion}</span>` : ''}
    </button>`).join('');

  el.querySelectorAll('button').forEach(btn => {
    if (sel[level] && sel[level].id === btn.dataset.id) btn.classList.add('active');
    btn.onclick = () => {
      el.querySelectorAll('button').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      onPick(items.find(i => i.id === btn.dataset.id));
    };
  });
}

function showSelectedLesson(name) {
  for (const id of ['uploadLesson', 'recoveryUploadLesson', 'manualLesson', 'recoveryManualLesson']) {
    const el = document.getElementById(id);
    if (el) el.value = name || '';
  }
}

function clearBelow(level) {
  const order = ['grade', 'term', 'subject', 'chapter', 'lesson'];
  for (const l of order.slice(order.indexOf(level) + 1)) {
    sel[l] = null;
    const el = document.getElementById('list-' + l);
    if (el) el.innerHTML = '';
  }
  showSelectedLesson(sel.lesson ? sel.lesson.name : '');
}

// ---------------------------------------------------------------------------
// Tree loading
// ---------------------------------------------------------------------------
async function loadGrades() {
  const langId = document.getElementById('langSelect').value;
  state.selectedLangId = langId;
  save();

  const grades = await api('GET', `/api/grades?langId=${langId}`);

  renderList('grade', grades.map(g => ({ id: g.id, name: g.name ?? g.grade, order: g.order })), g => {
    sel.grade = g;
    clearBelow('grade');
    loadLevel('term');
  });

  if (sel.grade) await loadLevel('term');
}

async function loadLevel(level) {
  const cfg = levels[level];
  const parent = sel[cfg.parent];
  if (!parent) return;

  const items = await api('GET', cfg.list(parent.id));

  renderList(level, items, item => {
    sel[level] = item;
    clearBelow(level);
    if (level === 'lesson') showSelectedLesson(item.name);
    if (cfg.next) loadLevel(cfg.next);
  });

  if (sel[level] && items.some(i => i.id === sel[level].id) && cfg.next) {
    await loadLevel(cfg.next);
  }
}

// ---------------------------------------------------------------------------
// Add & delete nodes
// ---------------------------------------------------------------------------
function openAdd(level) {
  const need = levels[level].parent;
  if (!sel[need]) { alert(`Select a ${need} first.`); return; }

  addLevel = level;
  document.getElementById('addTitle').textContent = `Add ${level} under ${sel[need].name}`;
  document.getElementById('addFields').innerHTML = state.languages.map(l => `
    <label class="form-label lvl-label mt-2">${escapeHtml(l.name)} (${l.code})</label>
    <input class="form-control add-name" data-lang="${l.id}" placeholder="name in ${escapeHtml(l.name)}" />
  `).join('');
  document.getElementById('addOrder').value = '';

  addModalInstance.show();
}

async function submitAdd() {
  const translations = [...document.querySelectorAll('.add-name')].map(i => ({
    langId: i.dataset.lang,
    name: i.value.trim()
  }));

  if (translations.some(t => !t.name)) { alert('A name is required for every language.'); return; }

  const orderValue = document.getElementById('addOrder').value;
  const body = { translations };
  if (orderValue !== '') body.order = Number(orderValue);

  const cfg = levels[addLevel];
  await api('POST', cfg.create(sel[cfg.parent].id), body);

  addModalInstance.hide();
  await loadLevel(addLevel);
}

async function deleteSelected(level) {
  const node = sel[level];
  if (!node) { alert(`Select a ${level} first.`); return; }

  const force = document.getElementById('deleteForce').checked;
  if (!confirm(`Delete ${level} "${node.name}"${force ? ' AND everything under it' : ''}? This cannot be undone.`)) return;

  try {
    await api('DELETE', levels[level].del(node.id) + (force ? '?force=true' : ''));
  } catch {
    toast('Tick "force" to delete the children too', `${level} "${node.name}" still has descendants.`);
    return;
  }

  sel[level] = null;
  clearBelow(levels[level].parent);
  await loadLevel(level);
}

// ---------------------------------------------------------------------------
// Questions upload
// ---------------------------------------------------------------------------
async function uploadQuestions() {
  if (!sel.lesson) { alert('Select a lesson on the Curriculum tab first.'); return; }

  const file = document.getElementById('uploadFile').files[0];
  if (!file) { alert('Choose an .xlsx file.'); return; }

  const langId = document.getElementById('uploadLang').value;
  const hasHeaderRow = document.getElementById('hasHeader').checked;

  const form = new FormData();
  form.append('file', file);

  await api('POST',
    `/api/admin/lessons/${sel.lesson.id}/questions/upload?langId=${langId}&hasHeaderRow=${hasHeaderRow}`,
    form, true);

  toast('Questions uploaded', `Lesson "${sel.lesson.name}" updated.`, 'success');
}

// ---------------------------------------------------------------------------
// Recovery questions upload
// ---------------------------------------------------------------------------
async function uploadRecoveryQuestions() {
  if (!sel.lesson) { alert('Select a lesson on the Curriculum tab first.'); return; }

  const file = document.getElementById('recoveryUploadFile').files[0];
  if (!file) { alert('Choose an .xlsx file.'); return; }

  const langId = document.getElementById('recoveryUploadLang').value;
  const hasHeaderRow = document.getElementById('recoveryHasHeader').checked;

  const form = new FormData();
  form.append('file', file);

  await api('POST',
    `/api/admin/lessons/${sel.lesson.id}/recovery-questions/upload?langId=${langId}&hasHeaderRow=${hasHeaderRow}`,
    form, true);

  toast('Recovery questions uploaded', `Lesson "${sel.lesson.name}" recovery set updated.`, 'success');
}

// ---------------------------------------------------------------------------
// Manual question entry
//
// One implementation for both pools. They accept identical content and differ only in which
// endpoint they publish to, so the editor is parameterised rather than written twice — the same
// reason the backend shares QuestionContentRules between them.
// ---------------------------------------------------------------------------
const pools = {
  questions: {
    label: 'Questions',
    prefix: 'manual',
    publish: (lessonId, langId) => `/api/admin/lessons/${lessonId}/questions/manual?langId=${langId}`,
    read:    (lessonId, langId) => `/api/admin/lessons/${lessonId}/questions?langId=${langId}`
  },
  recovery: {
    label: 'Recovery questions',
    prefix: 'recoveryManual',
    publish: (lessonId, langId) => `/api/admin/lessons/${lessonId}/recovery-questions/manual?langId=${langId}`,
    read:    (lessonId, langId) => `/api/admin/lessons/${lessonId}/recovery-questions?langId=${langId}`
  }
};

const field = (pool, suffix) => document.getElementById(pools[pool].prefix + suffix);

/**
 * One question's inputs. The correct choice is first and marked, because that is the whole
 * contract — position decides correctness, both here and in the sheet.
 */
function manualRowHtml(pool, values = {}) {
  const rtl = isArabicSelected(pool) ? ' dir="rtl"' : '';

  return `
    <div class="manual-row border rounded p-2 mb-2">
      <div class="d-flex justify-content-between align-items-center mb-2">
        <span class="lvl-label mb-0">Question <span class="manual-index"></span></span>
        <button type="button" class="btn btn-sm btn-outline-danger manual-remove" title="Remove this question">
          <i class="bi bi-x-lg"></i>
        </button>
      </div>
      <input class="form-control mb-2 manual-text"${rtl} placeholder="Question text"
             value="${escapeHtml(values.text || '')}" />
      <div class="row g-2">
        <div class="col-md-4">
          <div class="input-group">
            <span class="input-group-text text-bg-success" title="This one is the correct answer">
              <i class="bi bi-check-lg"></i>
            </span>
            <input class="form-control manual-correct"${rtl} placeholder="Correct choice"
                   value="${escapeHtml(values.correctChoice || '')}" />
          </div>
        </div>
        <div class="col-md-4">
          <input class="form-control manual-wrong1"${rtl} placeholder="Wrong choice 1"
                 value="${escapeHtml(values.wrongChoice1 || '')}" />
        </div>
        <div class="col-md-4">
          <input class="form-control manual-wrong2"${rtl} placeholder="Wrong choice 2"
                 value="${escapeHtml(values.wrongChoice2 || '')}" />
        </div>
      </div>
    </div>`;
}

/** Arabic content is typed right-to-left; the language selector is what says so. */
function isArabicSelected(pool) {
  const langId = field(pool, 'Lang')?.value;
  const language = (state.languages || []).find(l => l.id === langId);
  return language?.code === 'ar';
}

/**
 * Re-applies text direction across the editor. New rows pick it up when they are built, but the
 * rows already on screen have to follow a language change too — otherwise the empty row an admin
 * starts from stays left-to-right while every row they add afterwards does not.
 */
function applyManualDirection(pool) {
  const rtl = isArabicSelected(pool);

  field(pool, 'Rows').querySelectorAll('input').forEach(input => {
    if (rtl) input.setAttribute('dir', 'rtl');
    else input.removeAttribute('dir');
  });
}

/** Positions are display-only — the server numbers the published set itself. */
function renumberManual(pool) {
  const host = field(pool, 'Rows');
  [...host.querySelectorAll('.manual-row')].forEach((row, index) => {
    row.querySelector('.manual-index').textContent = index + 1;
  });
}

function addManualRow(pool, values) {
  const host = field(pool, 'Rows');
  host.insertAdjacentHTML('beforeend', manualRowHtml(pool, values));

  const row = host.lastElementChild;
  row.querySelector('.manual-remove').onclick = () => { row.remove(); renumberManual(pool); };

  renumberManual(pool);
  return row;
}

function clearManual(pool) {
  field(pool, 'Rows').innerHTML = '';
  showManualErrors(pool, []);
  addManualRow(pool);
}

function showManualErrors(pool, errors) {
  const host = field(pool, 'Errors');
  if (!errors.length) { host.classList.add('d-none'); host.innerHTML = ''; return; }

  host.classList.remove('d-none');
  host.innerHTML = `
    <div class="fw-semibold mb-1">Nothing was published — fix these first:</div>
    <ul class="mb-0 ps-3">${errors.map(e => `<li>${escapeHtml(describeError(e))}</li>`).join('')}</ul>`;
}

/**
 * Reads the editor. Rows left entirely blank are dropped rather than submitted, mirroring the
 * spreadsheet parser's tolerance of a spacer row — an admin who adds one row too many should not
 * have the whole publish refused for it.
 */
function collectManual(pool) {
  return [...field(pool, 'Rows').querySelectorAll('.manual-row')]
    .map(row => ({
      text: row.querySelector('.manual-text').value.trim(),
      correctChoice: row.querySelector('.manual-correct').value.trim(),
      wrongChoice1: row.querySelector('.manual-wrong1').value.trim(),
      wrongChoice2: row.querySelector('.manual-wrong2').value.trim()
    }))
    .filter(q => q.text || q.correctChoice || q.wrongChoice1 || q.wrongChoice2);
}

/** Fills the editor with what is published, so a set can be edited or extended rather than retyped. */
async function loadManual(pool) {
  if (!sel.lesson) { alert('Select a lesson on the Tree tab first.'); return; }

  const cfg = pools[pool];
  const langId = field(pool, 'Lang').value;
  const published = await api('GET', cfg.read(sel.lesson.id, langId));
  const questions = published.questions || [];

  if (!questions.length) {
    toast(`Nothing published yet`, `This lesson has no ${cfg.label.toLowerCase()} in that language.`, 'info');
    return;
  }

  field(pool, 'Rows').innerHTML = '';
  showManualErrors(pool, []);

  // The answers arrive in stored order with correctness carried by id, not position — so the
  // correct one is found rather than assumed to be first.
  for (const question of questions) {
    const answers = question.answers || [];
    const correct = answers.find(a => a.id === question.correctAnswerId);
    const wrong = answers.filter(a => a.id !== question.correctAnswerId);

    addManualRow(pool, {
      text: question.text,
      correctChoice: correct ? correct.text : '',
      wrongChoice1: wrong[0] ? wrong[0].text : '',
      wrongChoice2: wrong[1] ? wrong[1].text : ''
    });
  }

  // Loading in order to edit means replacing — appending the set to itself would double it.
  field(pool, 'Mode').value = 'REPLACE';

  toast(`Loaded v${published.version}`,
    `${questions.length} question(s) in the editor. Mode switched to Replace.`, 'info');
}

async function publishManual(pool) {
  if (!sel.lesson) { alert('Select a lesson on the Tree tab first.'); return; }

  const cfg = pools[pool];
  const langId = field(pool, 'Lang').value;
  const mode = field(pool, 'Mode').value;
  const questions = collectManual(pool);

  if (!questions.length) { alert('Add at least one question.'); return; }

  if (mode === 'REPLACE' &&
      !confirm(`Replace the whole ${cfg.label.toLowerCase()} set for "${sel.lesson.name}" with these ${questions.length}? `
               + 'The questions published now are retired.')) {
    return;
  }

  showManualErrors(pool, []);

  let result;
  try {
    result = await api('POST', cfg.publish(sel.lesson.id, langId), { mode, questions });
  } catch (e) {
    // Validation failures come back as a list naming each bad question; a toast cannot hold them.
    // Anything else — a 403, a 500 — has no list, so the thrown reason stands in rather than
    // leaving an empty panel that looks like nothing happened.
    const errors = e.payload && Array.isArray(e.payload.errors) ? e.payload.errors : [e.message];
    showManualErrors(pool, errors);
    return;
  }

  toast(`${cfg.label} published`,
    `Version ${result.version} — ${result.importedCount} live, ${result.replacedCount} retired.`, 'success');

  clearManual(pool);
  await loadLevel('lesson');
}

// ---------------------------------------------------------------------------
// Language apply
// ---------------------------------------------------------------------------
async function handleApplyLanguage() {
  const languageId = document.getElementById('langSelect').value;
  await applyLanguage(languageId);
  await loadGrades();
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initCurriculum() {
  if (!guardAuth()) return;
  initNav('curriculum');
  initSubTabs();

  addModalInstance = new bootstrap.Modal(document.getElementById('addModal'));

  // Wire up buttons
  window.openAdd = openAdd;
  window.submitAdd = submitAdd;
  window.deleteSelected = deleteSelected;
  window.uploadQuestions = uploadQuestions;
  window.uploadRecoveryQuestions = uploadRecoveryQuestions;
  window.addManualRow = addManualRow;
  window.clearManual = clearManual;
  window.loadManual = loadManual;
  window.publishManual = publishManual;
  window.applyLanguage = handleApplyLanguage;

  try {
    await loadLanguages(['langSelect', 'uploadLang', 'recoveryUploadLang', 'manualLang', 'recoveryManualLang']);

    // Each editor opens with one empty question, so the first thing an admin sees is somewhere
    // to type rather than a button they have to find first.
    for (const pool of Object.keys(pools)) {
      clearManual(pool);
      field(pool, 'Lang').addEventListener('change', () => applyManualDirection(pool));
    }
    await loadGrades();
  } catch (e) { /* already toasted */ }
}
