import { useCallback, useEffect, useState } from 'react'
import { api } from '../../lib/client'
import type {
  CurriculumHealthDto,
  QuestionPoolFilter,
  QuestionSearchResultDto,
} from '../../types/api'

// ===========================================================================
// Curriculum insight — data access
//
// Two read-only views over the tree the rest of this feature authors: how
// complete it is, and what is inside any branch of it.
// ===========================================================================

export function useCurriculumHealth() {
  const [health, setHealth] = useState<CurriculumHealthDto | null>(null)
  const [loading, setLoading] = useState(true)

  const reload = useCallback(async () => {
    setLoading(true)
    try {
      setHealth(await api.get<CurriculumHealthDto>('/api/admin/curriculum/health'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void reload().catch(() => undefined)
  }, [reload])

  return { health, loading, reload }
}

export interface QuestionQuery {
  scopeLevel: string | null
  scopeId: string | null
  pool: QuestionPoolFilter
  search: string
  onlyUnpaired: boolean
  page: number
}

export function useQuestionSearch(query: QuestionQuery, enabled: boolean) {
  const [result, setResult] = useState<QuestionSearchResultDto | null>(null)
  const [loading, setLoading] = useState(false)

  const { scopeLevel, scopeId, pool, search, onlyUnpaired, page } = query

  useEffect(() => {
    if (!enabled) {
      setResult(null)
      return
    }

    // Typing in the search box fires this on every keystroke, and an unscoped search reads the whole
    // curriculum. The debounce is what keeps that from being one request per character; the abort is
    // what keeps a slow early response from landing after a fast later one and overwriting it.
    const controller = new AbortController()

    const timer = setTimeout(() => {
      const params = new URLSearchParams({
        pool,
        page: String(page),
        pageSize: '50',
      })

      if (scopeLevel && scopeId) {
        params.set('scopeLevel', scopeLevel)
        params.set('scopeId', scopeId)
      }
      if (search.trim()) params.set('search', search.trim())
      if (onlyUnpaired) params.set('onlyUnpaired', 'true')

      setLoading(true)

      api
        .get<QuestionSearchResultDto>(`/api/admin/curriculum/questions?${params}`, {
          signal: controller.signal,
        })
        .then(setResult)
        .catch(() => undefined)
        .finally(() => setLoading(false))
    }, 250)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [enabled, scopeLevel, scopeId, pool, search, onlyUnpaired, page])

  return { result, loading }
}
