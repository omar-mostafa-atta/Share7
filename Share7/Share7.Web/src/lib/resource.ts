import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from './client'

// ===========================================================================
// useResource
//
// Twelve feature pages fetch a list, show a skeleton, expose a refresh button
// and swallow the error because the global handler already toasted it. Written
// out per page that is the same twenty lines twelve times — which is what the
// vanilla console did, and three of its copies had drifted.
//
// Deliberately not a cache and not a query library. Nothing here dedupes
// across components or revalidates in the background: the console is a handful
// of pages behind an admin login, and a stale-while-revalidate layer would be
// more machinery than the problem has.
// ===========================================================================

export interface Resource<T> {
  data: T
  loading: boolean
  refreshing: boolean
  reload: () => Promise<void>

  /** Replace the local copy without a round-trip, after a write returns the new row. */
  set: (next: T | ((current: T) => T)) => void
}

export function useResource<T>(
  path: string | null,
  fallback: T,
  select?: (raw: unknown) => T,
): Resource<T> {
  const [data, setData] = useState<T>(fallback)
  const [loading, setLoading] = useState(path !== null)
  const [refreshing, setRefreshing] = useState(false)

  // `select` is almost always an inline arrow, so it is a new function on every
  // render. Holding it in a ref keeps it out of the load callback's dependency
  // list — otherwise every render rebuilds `load`, the effect below re-runs, and
  // the page fetches in an infinite loop.
  const selectRef = useRef(select)
  selectRef.current = select

  // Guards against a slow first response overwriting a newer one. Two loads can
  // be in flight after a fast reload, and without this the older reply wins
  // whenever it happens to land second.
  const generation = useRef(0)

  const load = useCallback(
    async (isRefresh: boolean) => {
      if (path === null) return

      const mine = ++generation.current
      if (isRefresh) setRefreshing(true)

      try {
        const raw = await api.get<unknown>(path)
        if (mine !== generation.current) return

        setData(selectRef.current ? selectRef.current(raw) : (raw as T))
      } catch {
        // Already surfaced by the global error handler in App.tsx. Swallowing
        // here keeps the previous data on screen rather than blanking the page
        // over one dropped request.
      } finally {
        if (mine === generation.current) {
          setLoading(false)
          setRefreshing(false)
        }
      }
    },
    [path],
  )

  useEffect(() => {
    if (path === null) {
      // A null path means "not applicable yet" — no board selected, no user
      // looked up. Reset to the fallback so the previous selection's rows do
      // not linger under the new empty state.
      setData(fallback)
      setLoading(false)
      return
    }

    setLoading(true)
    void load(false)
    // `fallback` is intentionally not a dependency: callers pass an inline `[]`,
    // which is a new array every render and would re-fetch forever.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, load])

  const set = useCallback((next: T | ((current: T) => T)) => {
    setData((current) => (typeof next === 'function' ? (next as (c: T) => T)(current) : next))
  }, [])

  return { data, loading, refreshing, reload: () => load(true), set }
}

/**
 * Coerce a list response to an array, whatever shape it arrived in.
 *
 * The admin API is not consistent about this, and it is not close. Measured
 * against the running server:
 *
 *   bare array      games · leaderboards/boards · bounds · flagged
 *   { objectives }  { products }  { productKinds }  { grants }  { offers }
 *   { rules }       { valuations }  { levels }  { runs }  { sessions }
 *
 * Matching a key name per call site is what produced the first version of this
 * console: some hooks guessed right, six guessed wrong, and the pages that
 * guessed wrong rendered nothing or threw `.map is not a function`.
 *
 * So this takes no key. It returns the response if it is already an array,
 * otherwise the first array-valued property of the envelope. Every one of these
 * endpoints carries a single collection, so "the first array" is unambiguous —
 * and a wrapper key renamed on the server stops being a client-side break.
 */
export function asList<T>(raw: unknown): T[] {
  if (Array.isArray(raw)) return raw as T[]

  if (raw && typeof raw === 'object') {
    for (const value of Object.values(raw as Record<string, unknown>)) {
      if (Array.isArray(value)) return value as T[]
    }
  }

  return []
}

/** A list endpoint, tolerant of both shapes. */
export function useResourceList<T>(path: string | null): Resource<T[]> {
  return useResource<T[]>(path, [], (raw) => asList<T>(raw))
}
