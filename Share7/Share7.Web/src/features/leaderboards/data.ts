import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  CreateLeaderboardCycleRequest,
  FlaggedResultDto,
  LeaderboardBoardAdminDto,
  LeaderboardCycleDto,
  MetricBoundDto,
  SaveLeaderboardBoardRequest,
  SaveMetricBoundRequest,
} from '../../types/api'

// ===========================================================================
// Leaderboards — data access
//
// Four related but independent surfaces:
//
//   boards   the definition: what is ranked, how, over what period
//   cycles   one competition window of a board; ranks only move while Open
//   bounds   per-metric sanity limits; results beyond them are flagged
//   flagged  results awaiting a human verdict
//
// They are separate hooks rather than one because the pages that use them do
// not need all four at once, and a single hook would fetch the flagged queue
// every time someone opened the board editor.
//
// All four go through useResourceList, which tolerates both response shapes —
// boards, bounds and flagged come back as bare arrays while most of the rest of
// the admin API wraps its collection in an envelope.
// ===========================================================================

// ---------------------------------------------------------------------------
// Boards
// ---------------------------------------------------------------------------

export function useBoards() {
  const resource = useResourceList<LeaderboardBoardAdminDto>('/api/admin/leaderboards/boards')

  const create = useCallback(
    async (request: SaveLeaderboardBoardRequest) => {
      await api.post<LeaderboardBoardAdminDto>('/api/admin/leaderboards/boards', request)
      toast.success('Board created', `"${request.boardKey}" is defined.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (boardId: string, request: SaveLeaderboardBoardRequest) => {
      const updated = await api.put<LeaderboardBoardAdminDto>(
        `/api/admin/leaderboards/boards/${boardId}`,
        request,
      )
      resource.set((rows) => rows.map((b) => (b.boardId === boardId ? updated : b)))
      toast.success('Board updated', `"${updated.boardKey}" saved.`)
      return updated
    },
    [resource],
  )

  return { ...resource, boards: resource.data, create, update }
}

// ---------------------------------------------------------------------------
// Cycles
// ---------------------------------------------------------------------------

export function useCycles(boardId: string | null) {
  const resource = useResourceList<LeaderboardCycleDto>(
    boardId ? `/api/admin/leaderboards/boards/${boardId}/cycles` : null,
  )

  const create = useCallback(
    async (request: CreateLeaderboardCycleRequest) => {
      if (!boardId) return
      await api.post(`/api/admin/leaderboards/boards/${boardId}/cycles`, request)
      toast.success('Cycle scheduled', 'It opens at the start time you set.')
      await resource.reload()
    },
    [boardId, resource],
  )

  /**
   * Recompute ranks from the underlying results.
   *
   * The expensive one. Needed after a flagged result is resolved, because the
   * projection that built the cycle's ranks ran while that result was excluded
   * — clearing it does not retroactively insert the player into the standings.
   */
  const rebuild = useCallback(
    async (cycleId: string) => {
      await api.post(`/api/admin/leaderboards/cycles/${cycleId}/rebuild`)
      toast.success('Rebuild started', 'Ranks are being recomputed from the stored results.')
      await resource.reload()
    },
    [resource],
  )

  /**
   * Close the cycle and pay out.
   *
   * Irreversible: settlement writes reward transactions against the final
   * standings, and there is no unsettle. Anything still flagged at this moment
   * is settled as-is.
   */
  const settle = useCallback(
    async (cycleId: string) => {
      await api.post(`/api/admin/leaderboards/cycles/${cycleId}/settle`)
      toast.success('Cycle settled', 'Final ranks are frozen and rewards have been paid.')
      await resource.reload()
    },
    [resource],
  )

  return { ...resource, cycles: resource.data, create, rebuild, settle }
}

// ---------------------------------------------------------------------------
// Metric bounds
// ---------------------------------------------------------------------------

export function useMetricBounds() {
  const resource = useResourceList<MetricBoundDto>('/api/admin/leaderboards/bounds')

  // One endpoint for both create and update — it is a PUT that upserts on
  // (gameId, metric), so there is no separate create path to call.
  const save = useCallback(
    async (request: SaveMetricBoundRequest) => {
      await api.put<MetricBoundDto>('/api/admin/leaderboards/bounds', request)
      toast.success('Bound saved', `Limits for "${request.metric}" are in effect.`)
      await resource.reload()
    },
    [resource],
  )

  return { ...resource, bounds: resource.data, save }
}

// ---------------------------------------------------------------------------
// Flagged results
// ---------------------------------------------------------------------------

export function useFlaggedResults() {
  const resource = useResourceList<FlaggedResultDto>('/api/admin/leaderboards/flagged')

  const resolve = useCallback(
    async (result: FlaggedResultDto, legitimate: boolean) => {
      await api.post(`/api/admin/leaderboards/flagged/${result.resultId}/resolve`, { legitimate })

      resource.set((rows) => rows.filter((r) => r.resultId !== result.resultId))

      toast.success(
        legitimate ? 'Result accepted' : 'Result rejected',
        legitimate
          ? `${result.displayName}'s score counts. Rebuild the cycle for it to appear in the ranks.`
          : `${result.displayName}'s score stays out of the boards.`,
      )
    },
    [resource],
  )

  return { ...resource, flagged: resource.data, resolve }
}

// ---------------------------------------------------------------------------
// Vocabulary
// ---------------------------------------------------------------------------

export const SORT_DIRECTIONS = ['Desc', 'Asc'] as const

/** How several results from one player combine into their standing. */
export const AGGREGATIONS = ['Best', 'Sum', 'Last'] as const

/** The competition window. AllTime never rolls over. */
export const PERIODS = ['AllTime', 'Daily', 'Weekly', 'Monthly'] as const

/** Which groupings the board can be sliced by. */
export const COHORTS = ['All', 'Grade', 'Language', 'GradeAndLanguage'] as const

export function blankBoard(): SaveLeaderboardBoardRequest {
  return {
    boardKey: '',
    metric: '',
    sortDirection: 'Desc',
    aggregation: 'Best',
    period: 'AllTime',
    supportedCohorts: 'All',
    gameId: null,
    gradeId: null,
    langId: null,
    visibleRankLimit: null,
    graceSeconds: 60,
    isActive: true,
    translations: [],
  }
}
