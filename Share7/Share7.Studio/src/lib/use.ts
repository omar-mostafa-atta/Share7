import { useCallback, useEffect, useRef, useState } from 'react'
import { useI18n } from '../i18n/i18n'
import type { ContentLanguage, NodeTitle, TrailStep } from './studio'

// ===========================================================================
// The small habits every screen shares
// ===========================================================================

export interface Loaded<T> {
  data: T | undefined
  loading: boolean
  error: unknown
  reload: () => void
  set: (next: T) => void
}

/**
 * Fetches once, and again whenever `keys` change. An answer that arrives after
 * the screen has moved on is dropped rather than painted over the new one.
 */
export function useLoad<T>(fetch: () => Promise<T>, keys: unknown[]): Loaded<T> {
  const [data, setData] = useState<T>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [turn, setTurn] = useState(0)
  const latest = useRef(0)

  useEffect(() => {
    const mine = ++latest.current
    setLoading(true)
    setError(null)

    fetch()
      .then((value) => {
        if (mine === latest.current) {
          setData(value)
          setLoading(false)
        }
      })
      .catch((problem) => {
        if (mine === latest.current) {
          setError(problem)
          setLoading(false)
        }
      })

    // The fetch closure changes on every render; the keys are what actually decide.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...keys, turn])

  return {
    data,
    loading,
    error,
    reload: useCallback(() => setTurn((n) => n + 1), []),
    set: useCallback((next: T) => setData(next), []),
  }
}

/** Runs something that changes data, and says whether it is still running. */
export function useDoing(): [boolean, <T>(work: () => Promise<T>) => Promise<T | undefined>] {
  const [busy, setBusy] = useState(false)
  const alive = useRef(true)

  useEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
    }
  }, [])

  const run = useCallback(async <T,>(work: () => Promise<T>) => {
    setBusy(true)
    try {
      return await work()
    } finally {
      if (alive.current) setBusy(false)
    }
  }, [])

  return [busy, run]
}

/** Waits for typing to stop before doing anything — autosave, search. */
export function useSettled<T>(value: T, after = 700): T {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const timer = window.setTimeout(() => setSettled(value), after)
    return () => window.clearTimeout(timer)
  }, [value, after])

  return settled
}

// ---------------------------------------------------------------------------
// Reading names out of the curriculum
// ---------------------------------------------------------------------------

/**
 * A node's name in the interface language, falling back to whatever it does
 * have — a chapter named only in English still has to be findable by somebody
 * reading the Studio in Arabic.
 */
export function useTitle() {
  const { language } = useI18n()

  return useCallback(
    (titles: NodeTitle[] | undefined, languages: ContentLanguage[] | undefined) => {
      if (!titles?.length) return ''
      const wanted = languages?.find((one) => one.code === language)
      const match = wanted && titles.find((title) => title.langId === wanted.id)
      return (match ?? titles[0]).title
    },
    [language],
  )
}

export function useTrail() {
  const title = useTitle()
  return useCallback(
    (trail: TrailStep[] | undefined, languages: ContentLanguage[] | undefined) =>
      (trail ?? []).map((step) => ({ id: step.id, label: title(step.titles, languages) })),
    [title],
  )
}

/** Copies to the clipboard and says whether it worked, for the few things worth copying. */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}
