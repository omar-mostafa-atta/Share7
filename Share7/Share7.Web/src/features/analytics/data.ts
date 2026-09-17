import { useCallback, useMemo, useState } from 'react'
import { api } from '../../lib/client'
import { useResource } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  AnalyticsOverviewDto,
  EconomyReportDto,
  EventCatalogueDto,
  EventCatalogueRowDto,
  EventDetailDto,
  FunnelReportDto,
  RetentionReportDto,
  TimeseriesDto,
  TimelineSourceKind,
  UpsertEventSchemaRequest,
  UserAnalyticsProfileDto,
  UserTimelineDto,
} from '../../types/api'

// ===========================================================================
// Analytics — data access
//
// Every hook here is a read of a rollup or a ledger. The one thing this module
// owns beyond fetching is THE DATE RANGE, because getting it wrong is the most
// common way an analytics console lies:
//
//   - Days are UTC and inclusive at both ends, matching the server.
//   - The default range excludes NOTHING but also assumes nothing: 30 days back
//     from today, which is the window every headline on the overview is sized
//     for.
//   - A range is never sent as a local date. `toISODay` formats from the UTC
//     parts, so an admin in Cairo asking for "yesterday" gets the same day the
//     server rolled up rather than one shifted by their offset.
// ===========================================================================

const ROOT = '/api/admin/analytics'

/** UTC calendar day, `YYYY-MM-DD`. Never the local date — see the note above. */
export function toISODay(date: Date): string {
  return date.toISOString().slice(0, 10)
}

export function daysAgo(days: number): string {
  const date = new Date()
  date.setUTCDate(date.getUTCDate() - days)
  return toISODay(date)
}

export function today(): string {
  return toISODay(new Date())
}

export interface DayRange {
  from: string
  to: string
}

export const DEFAULT_RANGE: DayRange = { from: daysAgo(29), to: today() }

/** Presets, so the common questions are one click and not four keystrokes. */
export const RANGE_PRESETS: { label: string; range: () => DayRange }[] = [
  { label: '7d', range: () => ({ from: daysAgo(6), to: today() }) },
  { label: '30d', range: () => ({ from: daysAgo(29), to: today() }) },
  { label: '90d', range: () => ({ from: daysAgo(89), to: today() }) },
]

function query(range: DayRange, extra?: Record<string, string | number | undefined>): string {
  const parts = new URLSearchParams({ from: range.from, to: range.to })

  for (const [key, value] of Object.entries(extra ?? {})) {
    if (value !== undefined && value !== '') parts.set(key, String(value))
  }

  return parts.toString()
}

const EMPTY_OVERVIEW: AnalyticsOverviewDto = {
  fromDayUtc: '',
  toDayUtc: '',
  dau: 0,
  wau: 0,
  mau: 0,
  stickiness: 0,
  newUsers: 0,
  sessions: 0,
  averageSessionSeconds: 0,
  sessionsPerActiveUser: 0,
  totalPlaySeconds: 0,
  totalEvents: 0,
  d1: null,
  d7: null,
  d30: null,
  d1CohortCount: 0,
  d7CohortCount: 0,
  d30CohortCount: 0,
  platforms: [],
  projectionLagSeconds: 0,
  pendingEvents: 0,
}

export function useAnalyticsOverview(range: DayRange) {
  return useResource<AnalyticsOverviewDto>(`${ROOT}/overview?${query(range)}`, EMPTY_OVERVIEW)
}

const EMPTY_RETENTION: RetentionReportDto = {
  fromCohortDayUtc: '',
  toCohortDayUtc: '',
  maxDayIndex: 0,
  cohorts: [],
  curve: [],
  computedAtUtc: null,
}

export function useRetention(range: DayRange, maxDayIndex: number) {
  return useResource<RetentionReportDto>(
    `${ROOT}/retention?${query(range, { maxDayIndex })}`,
    EMPTY_RETENTION,
  )
}

export function useTimeseries(metric: string, range: DayRange, dimension?: string) {
  const empty = useMemo<TimeseriesDto>(
    () => ({ metric, dimension: dimension ?? null, series: [] }),
    [metric, dimension],
  )

  return useResource<TimeseriesDto>(
    metric ? `${ROOT}/timeseries?${query(range, { metric, dimension })}` : null,
    empty,
  )
}

const EMPTY_CATALOGUE: EventCatalogueDto = { events: [], unregistered: [] }

export function useEventCatalogue(range: DayRange) {
  return useResource<EventCatalogueDto>(`${ROOT}/events?${query(range)}`, EMPTY_CATALOGUE)
}

export function useEventDetail(name: string | null, range: DayRange) {
  const empty = useMemo<EventDetailDto | null>(() => null, [])

  return useResource<EventDetailDto | null>(
    name ? `${ROOT}/events/${encodeURIComponent(name)}?${query(range)}` : null,
    empty,
  )
}

const EMPTY_ECONOMY: EconomyReportDto = { fromDayUtc: '', toDayUtc: '', currencies: [] }

export function useEconomy(range: DayRange) {
  return useResource<EconomyReportDto>(`${ROOT}/economy?${query(range)}`, EMPTY_ECONOMY)
}

/**
 * A funnel is fetched on demand rather than on every keystroke.
 *
 * It is the one read here that opens raw event rows — bounded, but not free —
 * and re-running it while somebody is still typing the third step would cost
 * several full scans to answer a question nobody asked yet.
 */
export function useFunnel(range: DayRange) {
  const [report, setReport] = useState<FunnelReportDto | null>(null)
  const [loading, setLoading] = useState(false)

  const run = useCallback(
    async (steps: string[], windowHours: number) => {
      if (steps.length < 2) {
        toast.warn('Not a funnel', 'A funnel needs at least two steps.')
        return
      }

      setLoading(true)

      try {
        const result = await api.get<FunnelReportDto>(
          `${ROOT}/funnel?${query(range, { steps: steps.join(','), windowHours })}`,
        )
        setReport(result)
      } catch {
        // Already surfaced by the global handler. The previous report stays on
        // screen rather than the page blanking over one failed request.
      } finally {
        setLoading(false)
      }
    },
    [range],
  )

  return { report, loading, run }
}

export function useUserAnalytics(userId: string | null) {
  const empty = useMemo<UserAnalyticsProfileDto | null>(() => null, [])

  return useResource<UserAnalyticsProfileDto | null>(
    userId ? `${ROOT}/users/${userId}` : null,
    empty,
  )
}

/**
 * The trace, paged.
 *
 * Paging appends rather than replaces, because the whole point of this view is
 * reading a history in order — a page that swapped its contents would make the
 * reader lose their place on every "load more".
 */
export function useUserTimeline(userId: string | null, sources: TimelineSourceKind[]) {
  const [pages, setPages] = useState<UserTimelineDto[]>([])
  const [loading, setLoading] = useState(false)

  const filter = sources.length > 0 ? sources.join(',') : undefined

  const load = useCallback(
    async (before?: string | null) => {
      if (!userId) return

      setLoading(true)

      try {
        const parts = new URLSearchParams({ limit: '100' })
        if (before) parts.set('before', before)
        if (filter) parts.set('sources', filter)

        const page = await api.get<UserTimelineDto>(
          `${ROOT}/users/${userId}/timeline?${parts.toString()}`,
        )

        setPages((current) => (before ? [...current, page] : [page]))
      } catch {
        // Surfaced globally.
      } finally {
        setLoading(false)
      }
    },
    [userId, filter],
  )

  const entries = useMemo(() => pages.flatMap((page) => page.entries), [pages])
  const nextBefore = pages.length > 0 ? pages[pages.length - 1].nextBeforeUtc : null

  const reset = useCallback(() => setPages([]), [])

  return { entries, nextBefore, loading, load, reset }
}

export function useEventSchemas() {
  const save = useCallback(async (name: string, request: UpsertEventSchemaRequest) => {
    const row = await api.put<EventCatalogueRowDto>(
      `${ROOT}/schemas/${encodeURIComponent(name)}`,
      request,
    )

    toast.success(
      'Event registered',
      `"${name}" is ${row.enabled ? 'accepted' : 'refused'} and ${
        row.rollUpDaily ? 'rolled up daily' : 'not rolled up'
      }. Events already stored stay unfolded.`,
    )

    return row
  }, [])

  const seed = useCallback(async () => {
    const result = await api.post<{ added: number }>(`${ROOT}/schemas/seed`)

    toast.success(
      'Vocabulary seeded',
      result.added === 0
        ? 'Everything shipped is already registered.'
        : `${result.added} name(s) added. Existing rows were left exactly as authored.`,
    )

    return result.added
  }, [])

  return { save, seed }
}

// ---------------------------------------------------------------------------
// Formatting
//
// Kept here rather than in lib/format because these are analytics conventions,
// not general ones — and the null handling in particular is a rule, not a taste.
// ---------------------------------------------------------------------------

/**
 * A rate as a percentage, or an em dash when it is not yet known.
 *
 * NULL IS NOT ZERO. A D30 that has not matured and a D30 of nobody look
 * identical if both render "0%", and one of them sends a team to fix something
 * that was never broken.
 */
export function percent(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined) return '—'
  return `${(value * 100).toFixed(digits)}%`
}

export function duration(seconds: number): string {
  if (seconds <= 0) return '—'
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`

  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)

  return `${hours}h ${minutes}m`
}

export function compact(value: number): string {
  if (Math.abs(value) < 1000) return value.toLocaleString()
  return value.toLocaleString(undefined, { notation: 'compact', maximumFractionDigits: 1 })
}

/** Tone for a timeline row, so the eye can find money in a wall of behaviour. */
export function sourceTone(source: TimelineSourceKind): string {
  switch (source) {
    case 'CurrencyLedger':
      return 'brand'
    case 'Reward':
      return 'success'
    case 'Purchase':
      return 'warning'
    case 'Entitlement':
      return 'info'
    case 'Run':
      return 'cool'
    case 'Attempt':
      return 'info'
    default:
      return 'muted'
  }
}
