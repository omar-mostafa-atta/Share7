import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  CreateObjectiveRequest,
  ObjectiveAdminDto,
  UpdateObjectiveRequest,
} from '../../types/api'

// ===========================================================================
// Objectives — data access
//
// Ports wwwroot/js/objectives.js.
//
// The asymmetry between create and update is the API's and is load-bearing:
// create sets key, kind, metric, scope and aggregation; update cannot touch
// any of them. Those five define what is being counted, and players already
// hold progress counted that way — changing the metric under a half-finished
// objective would silently re-interpret every stored value.
// ===========================================================================

export function useObjectives() {
  const resource = useResourceList<ObjectiveAdminDto>('/api/admin/objectives')

  const create = useCallback(
    async (request: CreateObjectiveRequest) => {
      await api.post<ObjectiveAdminDto>('/api/admin/objectives', request)
      toast.success('Objective created', `"${request.key}" is live.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (objectiveId: string, request: UpdateObjectiveRequest) => {
      const updated = await api.put<ObjectiveAdminDto>(
        `/api/admin/objectives/${objectiveId}`,
        request,
      )

      resource.set((rows) => rows.map((o) => (o.objectiveId === objectiveId ? updated : o)))
      toast.success('Objective updated', `"${updated.key}" saved.`)
      return updated
    },
    [resource],
  )

  const remove = useCallback(
    async (objective: ObjectiveAdminDto) => {
      await api.del(`/api/admin/objectives/${objective.objectiveId}`)
      resource.set((rows) => rows.filter((o) => o.objectiveId !== objective.objectiveId))
      toast.success('Objective deleted', `"${objective.key}" is gone.`)
    },
    [resource],
  )

  return { ...resource, objectives: resource.data, create, update, remove }
}

// ---------------------------------------------------------------------------
// Vocabulary
//
// These are the values the server accepts. They are strings on the wire with no
// enum endpoint to enumerate them, so the console has to carry the list — which
// means it can drift from the backend. Kept here, in one place, rather than
// inlined into a <select> so the drift is at least findable.
// ---------------------------------------------------------------------------

/** How often progress resets. */
export const KINDS = ['DAILY', 'WEEKLY', 'ACHIEVEMENT'] as const

/**
 * How a metric's reported values combine.
 * SUM adds every report; MAX keeps the largest single one; LAST overwrites.
 */
export const AGGREGATIONS = ['SUM', 'MAX', 'LAST'] as const

/**
 * Metrics the platform emits. Not exhaustive — the field is free text and a new
 * game can report anything — so the editor offers these and still allows typing.
 */
export const COMMON_METRICS = [
  'runs_completed',
  'lessons_completed',
  'questions_correct',
  'distance_travelled',
  'coins_collected',
  'xp_earned',
  'perfect_lessons',
  'multiplayer_wins',
] as const

/** Whether an objective is live right now, by the availability window. */
export function isLive(objective: ObjectiveAdminDto, now = Date.now()): boolean {
  if (!objective.isActive) return false

  const from = objective.availableFromUtc ? Date.parse(`${objective.availableFromUtc}Z`) : null
  const to = objective.availableToUtc ? Date.parse(`${objective.availableToUtc}Z`) : null

  if (from && now < from) return false
  if (to && now >= to) return false

  return true
}

export function blankObjective(): CreateObjectiveRequest {
  return {
    key: '',
    kind: 'DAILY',
    metric: '',
    scope: null,
    target: 1,
    aggregation: 'SUM',
    gameId: null,
    gradeId: null,
    langId: null,
    availableFromUtc: null,
    availableToUtc: null,
    iconKey: null,
    sortOrder: 0,
    isActive: true,
    translations: [],
  }
}
