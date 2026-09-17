// ===========================================================================
// Failure formatting
//
// Ported from wwwroot/js/api.js. This API returns three different failure
// shapes and the knowledge of which is which was expensive to acquire, so the
// logic is kept as-is rather than rewritten.
// ===========================================================================

/** One entry of an `errors` array, as either a sentence or the question pipeline's row object. */
type ErrorEntry = string | { row?: number | null; message?: string } | unknown

/**
 * One entry of an `errors` array as a sentence. Handles both a bare string and the question
 * pipeline's `{ row, message }`, where `row` is the sheet row or the position in `questions[]`.
 */
export function describeError(entry: ErrorEntry): string {
  if (typeof entry === 'string') return entry
  if (!entry || typeof entry !== 'object') return String(entry ?? '')

  const obj = entry as { row?: number | null; message?: string }
  const message = obj.message || JSON.stringify(entry)
  return obj.row != null ? `#${obj.row}: ${message}` : message
}

/**
 * Turns any of the three failure shapes this API can return into one sentence.
 *
 * 1. Commerce / account: `{ code, messageKey, details }`
 * 2. Standard array: `{ errors: [...] }`
 * 3. ValidationProblemDetails (ASP.NET): `{ errors: { Key: ["msg…"] } }`
 */
export function describeFailure(data: unknown, fallback?: string): string {
  if (!data) return fallback || 'Request failed.'
  if (typeof data === 'string') return data || fallback || 'Request failed.'
  if (typeof data !== 'object') return fallback || 'Request failed.'

  const body = data as {
    code?: string
    messageKey?: string
    errors?: unknown
    title?: string
    detail?: string
  }

  if (body.code) return `${body.code} (${body.messageKey})`

  // Two kinds of array live here: plain sentences from auth/curriculum, and the question
  // pipeline's { row, message } objects. Joining the latter blindly renders "[object Object]",
  // which is what a rejected question sheet used to report.
  if (Array.isArray(body.errors)) return body.errors.map(describeError).join(' ')

  if (body.errors && typeof body.errors === 'object') {
    const messages = Object.entries(body.errors as Record<string, unknown>).flatMap(
      ([field, list]) =>
        (Array.isArray(list) ? list : [list]).map((m) =>
          field.startsWith('$') || field === 'request' ? String(m) : `${field}: ${m}`,
        ),
    )
    if (messages.length) return messages.join(' ')
  }

  return body.title || body.detail || fallback || 'Request failed.'
}

/** An HTTP failure carrying the parsed body and status alongside the formatted message. */
export class ApiError extends Error {
  readonly status: number
  readonly payload: unknown

  constructor(message: string, status: number, payload: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.payload = payload
  }
}
