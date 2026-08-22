// ===========================================================================
// Share7 Admin Console — Utility helpers
// Pure functions with no side-effects. Every module can import from here.
// ===========================================================================

/** HTML-escape a string for safe DOM insertion. */
export function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

/**
 * Normalise a product/currency key: lowercase letters, digits and underscores only,
 * must start with a letter.
 */
export function slugify(text) {
  return String(text ?? '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/_{2,}/g, '_')
    .replace(/^[^a-z]+/, '')
    .replace(/_+$/, '');
}

/**
 * Reproduce what the backend does to a product-kind name before it reaches Unity.
 * e.g. "Content Pack" → "CONTENT_PACK"
 */
export function toWire(name) {
  return String(name ?? '')
    .trim()
    .replace(/(?<=[a-z0-9])([A-Z])/g, '_$1')
    .replace(/[\s\-]+/g, '_')
    .replace(/_{2,}/g, '_')
    .replace(/^_+|_+$/g, '')
    .toUpperCase();
}

/**
 * Given a translations array and a target langId, return the matching translation.
 * Falls back to the first row if the target language is not found.
 */
export function textFor(translations, langId, field) {
  const rows = translations || [];
  const row = rows.find(t => t.langId === langId) || rows[0];
  return row ? (row[field || 'name'] || '') : '';
}

/**
 * Return the names of languages that have an empty `name` field in translations.
 */
export function missingLanguages(translations) {
  return translations.filter(t => !t.name).map(t => t.langName);
}
