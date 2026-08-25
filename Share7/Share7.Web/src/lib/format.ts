// ===========================================================================
// Pure helpers, ported from wwwroot/js/utils.js
//
// escapeHtml is deliberately absent: React escapes interpolated text by
// default, so the only way to reintroduce that class of bug is
// dangerouslySetInnerHTML, which this app does not use.
// ===========================================================================

/**
 * Normalise a currency/product key: lowercase letters, digits and underscores only, and it must
 * start with a letter. Matches the server's regex — `^[a-z][a-z0-9_]*$`.
 */
export function slugify(text: string): string {
  return String(text ?? '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/_{2,}/g, '_')
    .replace(/^[^a-z]+/, '')
    .replace(/_+$/, '')
}

/**
 * Reproduce what the backend does to a product-kind name before it reaches Unity.
 * e.g. "Content Pack" → "CONTENT_PACK"
 */
export function toWire(name: string): string {
  return String(name ?? '')
    .trim()
    .replace(/(?<=[a-z0-9])([A-Z])/g, '_$1')
    .replace(/[\s-]+/g, '_')
    .replace(/_{2,}/g, '_')
    .replace(/^_+|_+$/g, '')
    .toUpperCase()
}

export interface Translation {
  langId: string
  langName?: string
  name: string
  description?: string | null
}

/**
 * Given a translations array and a target langId, return the matching text. Falls back to the
 * first row if the target language is not present.
 */
export function textFor(
  translations: Translation[] | null | undefined,
  langId: string,
  field: 'name' | 'description' = 'name',
): string {
  const rows = translations || []
  const row = rows.find((t) => t.langId === langId) || rows[0]
  return row ? row[field] || '' : ''
}

/** Names of languages whose `name` field is still empty. */
export function missingLanguages(translations: Translation[]): string[] {
  return translations.filter((t) => !t.name).map((t) => t.langName ?? t.langId)
}

/** Thousands separators, matching the old console's `amount.toLocaleString()`. */
export function formatNumber(value: number): string {
  return value.toLocaleString()
}

/** The server's key rule, so the form can refuse before the round-trip. */
export const KEY_PATTERN = /^[a-z][a-z0-9_]*$/
