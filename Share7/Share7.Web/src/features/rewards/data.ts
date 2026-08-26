import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  CreateRewardRuleRequest,
  RewardRuleDto,
  UpdateRewardRuleRequest,
} from '../../types/api'

// ===========================================================================
// Reward rules — data access
//
// No `remove`, and that is the API's shape rather than an omission here:
// AdminRewardRulesController exposes GET, POST and PUT only. Every
// RewardTransaction a rule has ever produced points back at its id, so the rule
// row cannot go away without orphaning the ledger. Disabling is the retirement
// path.
// ===========================================================================

export function useRewardRules() {
  const resource = useResourceList<RewardRuleDto>('/api/admin/reward-rules')

  const create = useCallback(
    async (request: CreateRewardRuleRequest) => {
      await api.post<RewardRuleDto>('/api/admin/reward-rules', request)
      toast.success('Rule created', `"${request.name}" now pays on ${request.eventType}.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (ruleId: string, request: UpdateRewardRuleRequest) => {
      const updated = await api.put<RewardRuleDto>(`/api/admin/reward-rules/${ruleId}`, request)
      resource.set((rows) => rows.map((r) => (r.ruleId === ruleId ? updated : r)))

      toast.success(
        request.enabled ? 'Rule updated' : 'Rule disabled',
        request.enabled
          ? `"${updated.name}" saved.`
          : `"${updated.name}" no longer pays. Past payouts are untouched.`,
      )

      return updated
    },
    [resource],
  )

  return { ...resource, rules: resource.data, create, update }
}

/**
 * How often one player can be paid by the same rule.
 *
 * ONCE is per player for all time; ONCE_PER_DAY resets at UTC midnight; ALWAYS
 * pays every trigger and relies entirely on the cooldown and daily limit for
 * its bounds.
 */
export const REPEAT_POLICIES = [
  { value: 'ONCE', blurb: 'once per player, ever' },
  { value: 'ONCE_PER_DAY', blurb: 'once per player per UTC day' },
  { value: 'ALWAYS', blurb: 'every time, bounded only by the throttles' },
] as const

/**
 * Event types the platform raises. Suggestions only — the field is free text on
 * the wire and a new subsystem can raise anything.
 */
export const COMMON_EVENT_TYPES = [
  'LESSON_COMPLETED',
  'RUN_COMPLETED',
  'OBJECTIVE_CLAIMED',
  'LEADERBOARD_SETTLED',
  'LEVEL_UP',
  'FIRST_LOGIN',
] as const

export function blankRule(): CreateRewardRuleRequest {
  return {
    name: '',
    eventType: '',
    referenceKey: null,
    repeatPolicy: 'ONCE',
    cooldownSeconds: null,
    dailyLimit: null,
    transactionType: null,
    grants: [{ currencyId: '', amount: 1 }],
    entitlementProductIds: [],
    enabled: true,
  }
}
