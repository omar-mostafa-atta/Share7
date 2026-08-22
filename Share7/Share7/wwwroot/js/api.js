// ===========================================================================
// Share7 Admin Console — API client
// Central fetch wrapper, toast notifications, and failure formatting.
// ===========================================================================

import state from './state.js';

// ---------------------------------------------------------------------------
// Toast notifications — every page includes the toast container
// ---------------------------------------------------------------------------
export function toast(title, detail, type = 'danger') {
  const host = document.getElementById('toasts');
  if (!host) { console.error('[s7]', title, detail); return; }

  const colorMap = {
    danger:  'text-bg-danger',
    success: 'text-bg-success',
    warning: 'text-bg-warning',
    info:    'text-bg-primary'
  };

  const el = document.createElement('div');
  el.className = `toast align-items-center ${colorMap[type] || colorMap.danger} border-0`;
  el.setAttribute('role', 'alert');
  el.innerHTML = `
    <div class="d-flex">
      <div class="toast-body">
        <div class="fw-semibold">${escapeForToast(title)}</div>
        <div class="small opacity-75">${escapeForToast(detail)}</div>
      </div>
      <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
    </div>`;
  host.appendChild(el);

  const shown = new bootstrap.Toast(el, { delay: 8000 });
  el.addEventListener('hidden.bs.toast', () => el.remove());
  shown.show();
}

function escapeForToast(s) {
  return String(s ?? '').replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

// ---------------------------------------------------------------------------
// Failure description — handles the three shapes this API can return
// ---------------------------------------------------------------------------
/**
 * Turns any of the three failure shapes this API can return into one sentence.
 *
 * 1. Commerce / account: { code, messageKey, details }
 * 2. Standard array: { errors: [...] }
 * 3. ValidationProblemDetails (ASP.NET): { errors: { Key: ["msg…"] } }
 */
function describeFailure(data, fallback) {
  if (!data) return fallback || 'Request failed.';

  if (data.code) return `${data.code} (${data.messageKey})`;

  // Two kinds of array live here: plain sentences from auth/curriculum, and the question
  // pipeline's { row, message } objects. Joining the latter blindly renders "[object Object]",
  // which is what a rejected question sheet used to report.
  if (Array.isArray(data.errors)) return data.errors.map(describeError).join(' ');

  if (data.errors && typeof data.errors === 'object') {
    const messages = Object.entries(data.errors)
      .flatMap(([field, list]) => (Array.isArray(list) ? list : [list])
        .map(m => (field.startsWith('$') || field === 'request') ? m : `${field}: ${m}`));
    if (messages.length) return messages.join(' ');
  }

  return data.title || data.detail || fallback || 'Request failed.';
}

/**
 * One entry of an `errors` array as a sentence. Handles both a bare string and the question
 * pipeline's `{ row, message }`, where `row` is the sheet row or the position in `questions[]`.
 */
export function describeError(entry) {
  if (typeof entry === 'string') return entry;
  if (!entry || typeof entry !== 'object') return String(entry ?? '');

  const message = entry.message || JSON.stringify(entry);
  return entry.row != null ? `#${entry.row}: ${message}` : message;
}

// ---------------------------------------------------------------------------
// Core API call
// ---------------------------------------------------------------------------
/**
 * Make an authenticated API call.
 * @param {'GET'|'POST'|'PUT'|'DELETE'} method
 * @param {string} path       - Absolute path (e.g. `/api/auth/login`)
 * @param {object|FormData} [body]
 * @param {boolean} [isForm]  - If true, body is sent as FormData (no JSON serialisation)
 * @returns {Promise<any>}    - Parsed JSON response
 */
export async function api(method, path, body, isForm) {
  const headers = {};
  if (state.accessToken) headers['Authorization'] = 'Bearer ' + state.accessToken;
  if (body && !isForm) headers['Content-Type'] = 'application/json';

  const res = await fetch(path, {
    method,
    headers,
    body: isForm ? body : (body ? JSON.stringify(body) : undefined)
  });

  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }

  if (!res.ok) {
    const reason = describeFailure(data, text);
    console.error(`[s7] ${method} ${path} → ${res.status}`, reason);
    toast(`${method} ${path} → ${res.status}`, reason);
    const error = new Error(reason);
    error.payload = data;
    error.status = res.status;
    throw error;
  }

  console.debug(`[s7] ${method} ${path} → ${res.status}`, data);
  return data;
}

// ---------------------------------------------------------------------------
// Language loader — shared across pages
// ---------------------------------------------------------------------------
/**
 * Fetch languages from the API and populate any <select> elements whose ids are passed in.
 */
export async function loadLanguages(selectIds) {
  const langs = await api('GET', '/api/languages');
  state.languages = langs;

  if (!state.selectedLangId && langs.length) {
    state.selectedLangId = langs[0].id;
  }

  for (const id of (selectIds || [])) {
    const el = document.getElementById(id);
    if (!el) continue;
    el.innerHTML = langs.map(l =>
      `<option value="${l.id}" ${l.id === state.selectedLangId ? 'selected' : ''}>
        ${escapeForToast(l.name)} (${l.code})
      </option>`).join('');
  }

  return langs;
}

// ---------------------------------------------------------------------------
// Translation field generators — shared across modals
// ---------------------------------------------------------------------------
/**
 * Render bilingual name + optional description inputs into a container element.
 * @param {string} hostId           - id of the container element
 * @param {Array}  [existing]       - existing translations to pre-fill
 * @param {object} [options]        - { description: false } to suppress description fields
 */
export function translationFields(hostId, existing, options) {
  const rows = existing || [];
  const withDescription = !options || options.description !== false;
  const langs = state.languages;

  document.getElementById(hostId).innerHTML = langs.map(l => {
    const current = rows.find(t => t.langId === l.id) || {};
    const rtl = l.code === 'ar' ? ' dir="rtl"' : '';

    return `<div class="col-md-6">
      <label class="form-label lvl-label">${escapeForToast(l.name)} (${escapeForToast(l.code)}) — name</label>
      <input class="form-control tr-name" data-lang="${l.id}" data-host="${hostId}"${rtl}
             value="${escapeForToast(current.name || '')}" />
      ${withDescription ? `
        <label class="form-label lvl-label mt-2">Description</label>
        <textarea class="form-control tr-desc" data-lang="${l.id}" data-host="${hostId}" rows="2"${rtl}
                  placeholder="optional">${escapeForToast(current.description || '')}</textarea>` : ''}
    </div>`;
  }).join('');
}

/**
 * Collect translations from the rendered fields.
 */
export function collectTranslations(hostId) {
  return state.languages.map(l => {
    const name = document.querySelector(`.tr-name[data-host="${hostId}"][data-lang="${l.id}"]`);
    const desc = document.querySelector(`.tr-desc[data-host="${hostId}"][data-lang="${l.id}"]`);
    return {
      langId: l.id,
      langName: l.name,
      name: name ? name.value.trim() : '',
      description: desc && desc.value.trim() ? desc.value.trim() : null
    };
  });
}
