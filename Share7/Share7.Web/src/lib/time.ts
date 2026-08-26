// ===========================================================================
// Time formatting
//
// The API speaks UTC everywhere — every DTO field is named `...AtUtc` — but
// ASP.NET serialises DateTime without an offset when the value's Kind is
// Unspecified, which is what comes back from EF for a column typed
// `datetime2`. `new Date("2026-08-26T14:03:00")` is then parsed as *local*
// time and every timestamp in the console silently shifts by the viewer's
// offset.
//
// So parsing goes through one function that appends `Z` when no zone is
// present. This is the reason these helpers exist rather than each page
// calling toLocaleString directly.
// ===========================================================================

export function parseUtc(value: string | null | undefined): Date | null {
  if (!value) return null

  const hasZone = /[zZ]$|[+-]\d{2}:?\d{2}$/.test(value)
  const date = new Date(hasZone ? value : `${value}Z`)

  return Number.isNaN(date.getTime()) ? null : date
}

/** Absolute local time, for anything the admin may need to quote exactly. */
export function formatDateTime(value: string | null | undefined): string {
  const date = parseUtc(value)
  if (!date) return '—'

  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function formatDate(value: string | null | undefined): string {
  const date = parseUtc(value)
  if (!date) return '—'

  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: '2-digit' })
}

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 31_536_000_000],
  ['month', 2_592_000_000],
  ['week', 604_800_000],
  ['day', 86_400_000],
  ['hour', 3_600_000],
  ['minute', 60_000],
  ['second', 1000],
]

/**
 * "3 minutes ago" / "in 2 days".
 *
 * Intl.RelativeTimeFormat rather than a hand-rolled ladder, so it pluralises
 * and localises itself. Anything under a minute collapses to "just now" — a
 * heartbeat table refreshing every few seconds should not flicker between
 * "4 seconds ago" and "6 seconds ago".
 */
export function formatRelative(value: string | null | undefined): string {
  const date = parseUtc(value)
  if (!date) return '—'

  const delta = date.getTime() - Date.now()
  if (Math.abs(delta) < 60_000) return 'just now'

  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

  for (const [unit, ms] of UNITS) {
    if (Math.abs(delta) >= ms) return formatter.format(Math.round(delta / ms), unit)
  }

  return 'just now'
}

/** Milliseconds as a run duration: "1m 12s", "840ms". */
export function formatDuration(ms: number | null | undefined): string {
  if (ms == null) return '—'
  if (ms < 1000) return `${ms}ms`

  const totalSeconds = Math.round(ms / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60

  if (!minutes) return `${seconds}s`
  return `${minutes}m ${seconds.toString().padStart(2, '0')}s`
}

/** An ISO string suitable for `<input type="datetime-local">`, in local time. */
export function toLocalInput(value: string | null | undefined): string {
  const date = parseUtc(value)
  if (!date) return ''

  // Shift by the offset so the *local* wall-clock reading is what the control
  // shows; toISOString would render the UTC instant and the field would
  // disagree with every other timestamp on the page.
  const shifted = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return shifted.toISOString().slice(0, 16)
}

/** The inverse: a datetime-local value back to a UTC ISO string, or null. */
export function fromLocalInput(value: string): string | null {
  if (!value) return null

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}
